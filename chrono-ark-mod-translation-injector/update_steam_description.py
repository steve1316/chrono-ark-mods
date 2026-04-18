"""Updates the Mod Translation Injector's STEAM_DESCRIPTION.txt with a
Supported Mods section generated from the override JSON files.

Usage:
    python update_steam_description.py [--json-dir DIR] [--workshop-dir DIR]

By default, reads JSON files from the game's mod folder and resolves
Steam Workshop IDs from the workshop content folder to generate linked
mod names in the output.
"""

import argparse
import json
from datetime import datetime
from pathlib import Path

SCRIPT_DIR = Path(__file__).parent
STEAM_DESC_PATH = SCRIPT_DIR / "STEAM_DESCRIPTION.txt"
DEFAULT_JSON_DIR = Path(
    "F:/SteamLibrary/steamapps/common/Chrono Ark/Mod/ModTranslationInjector"
)
DEFAULT_WORKSHOP_DIR = Path(
    "F:/SteamLibrary/steamapps/workshop/content/1188930"
)
WORKSHOP_URL = "https://steamcommunity.com/sharedfiles/filedetails/?id="

# Markers for the auto-generated section.
SECTION_START = "[h2]Supported Mods[/h2]"
SECTION_END_MARKER = "<!-- END SUPPORTED MODS -->"


def load_json(path: Path) -> dict:
    """Loads a JSON file, returning an empty dict if missing."""
    if not path.exists():
        return {}
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def build_workshop_map(workshop_dir: Path) -> dict:
    """Builds a mapping from mod ID to Steam Workshop numeric ID.

    Scans each subfolder in the workshop content directory for a
    ChronoArkMod.json and extracts the mod's ID field. The subfolder
    name is the Steam Workshop file ID.
    """
    mapping = {}
    if not workshop_dir.exists():
        return mapping

    for entry in workshop_dir.iterdir():
        if not entry.is_dir():
            continue
        config = entry / "ChronoArkMod.json"
        if not config.exists():
            continue
        try:
            data = load_json(config)
            mod_id = data.get("id", "")
            if mod_id:
                mapping[mod_id] = entry.name
        except (json.JSONDecodeError, OSError):
            continue

    return mapping


def build_supported_mods_section(
    keyed: dict, text: dict, workshop_map: dict, timestamp: str
) -> str:
    """Builds the Steam BBCode for the Supported Mods section."""
    all_mods = sorted(
        set(keyed.keys()) | set(text.keys()),
        key=lambda s: s.lower(),
    )

    lines = [
        SECTION_START,
        "",
        f"[i]Last updated: {timestamp}[/i]",
        "",
        f"{len(all_mods)} mods currently supported.",
        "",
        "[list]",
    ]

    for mod in all_mods:
        steam_id = workshop_map.get(mod)
        if steam_id:
            lines.append(f"[*] [url={WORKSHOP_URL}{steam_id}]{mod}[/url]")
        else:
            lines.append(f"[*] {mod}")

    lines.append("[/list]")
    lines.append(SECTION_END_MARKER)
    return "\n".join(lines)


def update_description(section_bbcode: str) -> None:
    """Replaces or appends the Supported Mods section in the description."""
    desc = STEAM_DESC_PATH.read_text(encoding="utf-8")

    start_idx = desc.find(SECTION_START)
    end_idx = desc.find(SECTION_END_MARKER)

    if start_idx != -1 and end_idx != -1:
        end_idx += len(SECTION_END_MARKER)
        desc = desc[:start_idx] + section_bbcode + desc[end_idx:]
    else:
        hr_idx = desc.rfind("[hr][/hr]")
        if hr_idx != -1:
            desc = desc[:hr_idx] + section_bbcode + "\n\n" + desc[hr_idx:]
        else:
            desc = desc.rstrip() + "\n\n" + section_bbcode + "\n"

    STEAM_DESC_PATH.write_text(desc, encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(
        description="Update STEAM_DESCRIPTION.txt with supported mod list."
    )
    parser.add_argument(
        "--json-dir",
        type=Path,
        default=DEFAULT_JSON_DIR,
        help="Directory containing keyed_overrides.json and text_overrides.json",
    )
    parser.add_argument(
        "--workshop-dir",
        type=Path,
        default=DEFAULT_WORKSHOP_DIR,
        help="Steam workshop content directory for Chrono Ark (app 1188930)",
    )
    args = parser.parse_args()

    keyed = load_json(args.json_dir / "keyed_overrides.json")
    text = load_json(args.json_dir / "text_overrides.json")

    if not keyed and not text:
        print(f"No override files found in {args.json_dir}")
        return

    workshop_map = build_workshop_map(args.workshop_dir)
    print(f"Resolved {len(workshop_map)} Workshop IDs from {args.workshop_dir}")

    all_mods = set(keyed.keys()) | set(text.keys())
    unlinked = [m for m in sorted(all_mods) if m not in workshop_map]
    if unlinked:
        print(f"Warning: {len(unlinked)} mods without Workshop links: "
              + ", ".join(unlinked))

    timestamp = datetime.now().strftime("%Y-%m-%d")
    section = build_supported_mods_section(keyed, text, workshop_map, timestamp)
    update_description(section)

    total_keyed = sum(len(v) for v in keyed.values())
    total_text = sum(len(v) for v in text.values())
    print(f"Updated STEAM_DESCRIPTION.txt: {len(all_mods)} mods, "
          f"{total_keyed} keyed + {total_text} text overrides")


if __name__ == "__main__":
    main()
