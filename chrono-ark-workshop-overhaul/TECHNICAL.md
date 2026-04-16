# Workshop Screen Overhaul - Technical Documentation

## Architecture

All patches target the `ModUI` class and related Workshop screen components. The mod uses HarmonyLib for runtime patching and applies all patches automatically via `PatchAll`.

## Problems and Solutions

### Problem: UI freezes during mod apply with no feedback

Clicking Apply in the Workshop triggers synchronous mod loading/unloading that freezes the UI for several seconds with no progress indication. Users can't tell if the game is working or crashed.

### Solution: Progress overlay coroutine (ApplyProgressPatch)

Replaces `ModUI.OnApplyBtnClick()` with a coroutine that shows a progress overlay. Each phase (unload, load, audio init, schema update, database rebuild) yields between steps so the overlay message updates and the UI stays responsive. Per-mod timing is logged to `Player.log` for benchmarking.

### Problem: Audio initialization blocks the apply flow

`ModInfo.Load()` calls `audioinfo.init()` synchronously for each mod. Some mods have large audio assets that cause significant delays during the apply flow, compounding the UI freeze.

### Solution: Deferred audio loading (DeferredAudioLoadPatch)

Replaces `ModInfo.Load()` with a version that defers `audioinfo.init()` when called during the apply flow (detected via `IsApplyActive` flag). Assembly and GDE loading run normally. The deferred audio inits are processed later in ApplyProgressPatch's Phase B2, yielding a frame between each mod so the overlay stays responsive.

**Why only defer during Apply:** At game startup, all mods load before the first frame renders. Deferring audio there would leave a stale queue that never gets processed (no coroutine drives it). The `IsApplyActive` flag ensures deferral only happens when the ApplyProgressPatch coroutine is actively driving the flow.

### Problem: No way to enable/disable many mods at once

Users with 50+ mods had to toggle each one individually. No batch operations existed.

### Solution: Select All toggle + shift-click range selection (SelectAllPatch)

Adds a Select All checkbox that enables/disables all visible (filtered) mods. A guard flag prevents recursive callbacks when batch-toggling (each `SetModEnabled` call triggers `RefreshSelectAllOnToggle`, which would re-trigger Select All).

Shift-click range selection uses an anchor pattern: the first click sets the anchor mod, then shift-clicking another mod applies the anchor's enable/disable state across the range. The range is calculated over `ModscrolLElementsList_NowShow` (the filtered visible list), not the full mod list, so it respects the current search/tag filter.

### Problem: No way to reorder mods without drag-and-drop

The game's mod list has no visible position numbers and reordering requires imprecise drag-and-drop.

### Solution: Editable order number fields (DragReorderPatch/OrderNumberPatch)

Adds a `TMP_InputField` to each mod entry showing its 1-based position. Editing the number triggers `ModsScrollItemChangeIndex(fromPos, toPos)` via reflection to reorder. A guard flag (`_isUpdatingNumbers`) prevents the `onEndEdit` callback from firing during programmatic number updates.

### Problem: Scroll wheel feels inconsistent with many mods

The default scroll implementation moves a fixed fraction of the total list, so scrolling feels faster with more mods and slower with fewer.

### Solution: Proportional scrolling (SmoothScrollPatch)

Replaces `RecordViewScroll.Update()` with custom scroll logic that calculates a fixed pixel step (80px) normalized to the content height. Each scroll tick moves the same visual distance regardless of list length. The formula: `normalizedStep = 80 / contentHeight`, applied as `scrollbar.value += axis * normalizedStep * 10`.

## Patch Files

### ApplyProgressPatch.cs

**Harmony patch:** `ModUI.OnApplyBtnClick` (Prefix, returns false).

**Coroutine phases:**
1. Validation (`ModManager.CheckEnabled`), restart/dependency prompts.
2. Snapshot `LoadedMods`/`EnabledMods` before mutation.
3. Phase A: Unload disabled mods with per-mod progress.
4. Phase B: Load enabled mods with per-mod progress and failure tracking.
5. Phase B2: Process deferred audio inits from `DeferredAudioLoadPatch.PendingAudioInit`.
6. Phase C: `UpdateModInfo`, `MakeSaveType`, `BuildDataKeysBySchemaList`, `DataBaseInit` with per-step timing.
7. Result display and UI cleanup.

### LoadTimingPatch.cs (DeferredAudioLoadPatch)

**Harmony patch:** `ModInfo.Load` (Prefix, returns false).

Runs `LoadAtVeryBegining()` and `LoadGDE()` synchronously. If `IsApplyActive`: queues to `PendingAudioInit`. Otherwise: runs `audioinfo.init()` inline.

### SelectAllPatch.cs

**Harmony patches:**
- `ModUI.OnEnable` (Postfix) — Creates Select All toggle, repositions search/tag UI.
- `ModUI.SetModEnabled` (Postfix, `RefreshSelectAllOnToggle`) — Syncs checkbox.
- `ModUI.SetModEnabled` (Prefix, `ShiftClickTogglePatch`) — Range toggle on shift-click.
- `ModScrollElementScript.OnPointerClick` (Prefix, `ShiftClickBodyPatch`) — Range toggle on shift-click body.

### DragReorderPatch.cs (OrderNumberPatch)

**Harmony patch:** `ModUI.Init` (Postfix). Creates `TMP_InputField` per mod with `onEndEdit` callback.

### SmoothScrollPatch.cs

**Harmony patches:**
- `ModUI.OnEnable` (Postfix, `ScrollRectConfigPatch`) — Sets `scrollSensitivity = 80`.
- `RecordViewScroll.Update` (Prefix, `SmoothScrollPatch`) — Proportional scroll calculation.

## Key Game Types

| Type | Role |
|------|------|
| `ModUI` | Workshop screen controller. `OnApplyBtnClick()` triggers loading. `Init()` populates the list. |
| `ModInfo` | Per-mod metadata. `Load()` loads assembly + GDE + audio. |
| `ModManager` | Static manager. `LoadMod()`, `UnLoadMod()`, `CheckEnabled()`, `MakeSaveType()`. |
| `ModScrollElementScript` | Individual mod list entry. `OnPointerClick()` handles clicks. |
| `RecordViewScroll` | Scroll container. `Update()` handles wheel input. |
