# Chrono Ark Mods

A collection of quality-of-life and performance mods for [Chrono Ark](https://store.steampowered.com/app/1188310/Chrono_Ark/).

## Mods

### [Gameplay Enhancements](chrono-ark-gameplay-enhancements/)

Performance and quality-of-life improvements for gameplay. Eliminates Encyclopedia freezes by pre-loading skill data behind a progress overlay, caching the Collections UI for instant reopens, and deferring non-visible tab initialization. Includes configurable starting Mana, Gold, and Soulstones.

### [Workshop Screen Overhaul](chrono-ark-workshop-overhaul/)

Overhauls the Workshop mod management screen with smoother scrolling, per-mod apply progress with timing, Select All toggle, shift-click range selection, and editable load order numbers.

### [Mod English Translations](chrono-ark-mod-english-translations/)

Injects English translations for mod content at runtime. Patches the I2 Localization system for keyed overrides and replaces hardcoded CJK strings in UI text components.

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
