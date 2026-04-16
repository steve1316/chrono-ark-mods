# Workshop Screen Overhaul

Overhauls the Chrono Ark Workshop mod management screen with usability and performance improvements.

## Features

### Apply Progress Overlay

Wraps the mod apply flow in a coroutine with a full-screen progress overlay. Shows per-mod progress for unloading, loading, audio initialization, schema updates, and database rebuilds. Logs detailed timing for each phase.

### Deferred Audio Loading

Defers `ModInfo.audioinfo.init()` during the apply flow so audio loading is spread across frames instead of blocking in one large batch. Outside the apply flow (e.g., game startup), audio loads normally.

### Select All Toggle

Adds a Select All checkbox to the mod list. Enables or disables all currently visible (filtered) mods in one click. Stays synced when individual mods are toggled.

### Shift-Click Range Selection

Hold Shift and click a mod to select a range from the last-clicked mod. The range inherits the toggle state of the anchor mod, making it easy to enable or disable groups of mods.

### Editable Load Order Numbers

Adds an editable input field to each mod in the list showing its position number. Change the number to reorder mods by calling the game's `ModsScrollItemChangeIndex` method.

### Smooth Scrolling

Replaces the default scroll-wheel handling with proportional scrolling. Each scroll tick moves a fixed 80px regardless of list length, providing consistent feel.

## Installation

1. Build with `dotnet build`.
2. The DLL and `ChronoArkMod.json` are copied to the game's `Mod/WorkshopOverhaul/` folder automatically.
3. Enable the mod in the Workshop screen and restart.
