# Gameplay Enhancements

Performance and quality-of-life improvements for Chrono Ark gameplay.

## Features

### Collections UI Performance

The Encyclopedia / Collections screen (opened via TAB or character profile) normally freezes for several seconds while loading all tab data synchronously. This mod eliminates that freeze with three techniques:

- **Deferred tab initialization.** Only the default Character tab loads on open. The Skill, Item, and Monster tabs defer their `Start()` until selected or background-preloaded.
- **Progress overlay.** A full-screen overlay with status messages blocks interaction while the UI loads behind it. Shows per-character progress during skill preloading.
- **UI caching.** After the first open, the Collections gameObject is reparented to a hidden `DontDestroyOnLoad` container instead of being destroyed. Subsequent opens reactivate the cached instance instantly. The Addressable asset cache is kept warm by skipping `Resources.UnloadUnusedAssets` on close.

All three entry points are covered:
- TAB key in the Field scene (general encyclopedia).
- Character profile from the camp screen (`CharSelect_CampUI.OpenProfile`).
- Character profile from the character selection screen (`CharSelectMainUIV2.OpenProfile`).

### Configurable Starting Stats

Override the default starting Mana, Gold, and Soulstones for new runs via mod settings. Only applies to fresh runs, not loaded saves.

| Setting | Range | Default |
|---------|-------|---------|
| Starting Mana | 1 - 10 | 3 |
| Starting Gold | 0 - 1,000 | 0 |
| Starting Soulstones | 0 - 25 | 0 |

### Performance Debug Instrumentation

Built-in debug tooling for profiling game performance (always active but low overhead):

- **Frame-time monitor.** Logs hitches above 100ms with severity tiers, a ring buffer of recent method calls, TAB key correlation, and canvas snapshots on freezes.
- **Known method timing.** Stopwatch hooks on `PlayData.init`, `PlayData.DataBaseInit`, `Collections.Start`, `SKillCollection.Start`, and other key methods.
- **Scene transition tracker.** Logs scene load and active-scene-change events.
- **F9 debug hotkey.** Dumps the full `UIManager` state (NowActiveUI, AllUI, BeforeUI, GamepadManager flags, cached Collections reference) to `Player.log` on demand.

## Installation

1. Build with `dotnet build`.
2. The DLL and `ChronoArkMod.json` are copied to the game's `Mod/GameplayEnhancements/` folder automatically.
3. Enable the mod in the Workshop screen and restart.
