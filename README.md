# Chrono Ark Mods

A collection of quality-of-life and performance mods for [Chrono Ark](https://store.steampowered.com/app/1188310/Chrono_Ark/).

## Mods

### [Gameplay Enhancements](chrono-ark-gameplay-enhancements/)

1. Fixes the camp recruit button disappearing after save/load.
2. Optionally caches the Encyclopedia UI and defers tab loading to eliminate freezes.
3. Configurable starting Mana, Gold, and Soulstones for new runs.
4. Mod compatibility fixes for broken skill tooltips and outdated buffs.

### [Workshop Screen Overhaul](chrono-ark-workshop-overhaul/)

1. Smoother scrolling in the mod list.
2. Per-mod apply progress with timing.
3. Select All toggle for batch operations.
4. Shift-click range selection.
5. Editable load order numbers.

### [Mod Translation Injector](chrono-ark-mod-translation-injector/)

1. Reads keyed_overrides.json for localization key overrides.
2. Reads text_overrides.json for hardcoded CJK string replacements.

## Building

Each mod is an independent .NET 4.7.2 project. Set the `CHRONO_ARK_DIR` environment variable to your game installation path, or edit the default in each `.csproj`.

```bash
cd chrono-ark-gameplay-enhancements
dotnet build
```

Build output is automatically copied to the game's `Mod/` folder via a post-build target.

## Requirements

- Chrono Ark
- .NET Framework 4.7.2
- HarmonyLib (included with the game's mod framework)
