# Gameplay Enhancements - Technical Documentation

## Architecture

The mod has three functional areas: **Collections UI performance** (4 patch files), **mod compatibility fixes** (1 patch file), and **starting stats** (1 patch file), plus a **PerfDebug** instrumentation framework (5 files).

## Problems and Solutions

### Problem: 5-second freeze when pressing TAB

The Collections UI (Encyclopedia) has 4 tabs: Character, Skill, Item, Monster. When TAB is pressed, the game creates a new Collections instance via Addressables and all 4 tabs run their `Start()` → `Init()` methods synchronously on the same frame. Profiling showed:

- `CharacterCollection.Start`: 1,721ms (57 prefab instantiations + 57 Addressable sprite loads)
- `SKillCollection.Start`: 2,439ms (68 face sprite loads via `LoadAsyncCompletion` + 2,210 `GDESkillData` constructions)
- `ItemCollection.Start`: 504ms
- `MonsterCollection.Start`: 328ms

Total: ~5,000ms of synchronous work on one frame.

### Solution: Deferred tab initialization (DeferredCollectionsPatch)

Only the default Character tab (index 0) initializes on open. The other 3 tabs have their `Start()` intercepted by a high-priority Harmony Prefix that returns false and stores the instance. When the user switches tabs or the overlay triggers initialization, the stored instance's `Start()` is invoked via cached `MethodInfo` with a bypass flag to prevent re-interception.

**Why a bypass flag instead of calling Init() directly:** `Start()` is the entry point that other Harmony patches (timing probes, SkillPreloadPatch) hook into. Calling `Init()` directly would skip those patches. The bypass flag lets the full Harmony chain run.

**Why check IsOnce:** Collections opened from character profile views (`CharSelect_CampUI.OpenProfile`, `CharSelectMainUIV2.OpenProfile`) set `IsOnce=true`. These are one-shot views that must have all tabs ready immediately since the user navigates directly to a character info page. Deferring tabs in this path caused jumbled/broken UI.

### Problem: 20 seconds of intermittent lag after opening

After the initial 5s freeze, the game stuttered with 300-400ms hitches for ~20 seconds. The FrameTimeMonitor ring buffer showed `_2dxFX_GrayScale.OnEnable` and shader effect activations flooding the frame. This was caused by hundreds of newly instantiated prefabs triggering deferred shader compilation and layout rebuilds.

### Solution: Progress overlay (CachedCollectionsPatch + SkillPreloadPatch)

A full-screen overlay blocks interaction while all loading happens behind it. The overlay only dismisses after the skill preload coroutine completes AND 10 consecutive frames run under 60ms (indicating shader compilation has settled). The user never sees any lag after the overlay dismisses.

**Why hide the Canvas instead of SetActive(false):** Deactivating the Collections gameObject would kill running coroutines (like the skill preload). Disabling the parent Canvas hides rendering while keeping all MonoBehaviour lifecycle active.

### Problem: Every TAB press re-created Collections from scratch

The game destroys Collections on ESC via `Object.Destroy(gameObject)`. Next TAB press creates a fresh instance, paying the full initialization cost again.

### Solution: UI caching via DontDestroyOnLoad (CachedCollectionsPatch)

A Harmony Transpiler on `Collections.ESC()` replaces `Object.Destroy` calls with `DestroyOrCache`. Instead of destroying, the method:

1. **Reparents to a hidden DontDestroyOnLoad container.** This is critical because `UIManager.UICheck()` scans `ActiveSlot.transform` children every frame looking for UIs to activate. If the cached Collections stayed parented under ActiveSlot, UICheck would find it and restore it as `NowActiveUI`, consuming ESC input and breaking the main menu.

2. **Manually simulates the OnDestroy chain.** Unity's `OnDestroy` normally runs `UI.OnDestroy()` which sets `Destoryed=true`, calls `UICheck()`, and removes from `AllUI`. Since we skip actual destruction, we must replicate this exactly or the game's UI tracking gets corrupted. Specifically, `UIManager.UICheck()` is called to clean `BeforeUI` and null `NowActiveUI`, then the instance is explicitly removed from `AllUI` and `NoneUICheckLIst`.

3. **Resets `SkillPreloadPatch.IsPreloading`.** Deactivation (`SetActive(false)`) kills all coroutines on the gameObject, including the skill preload coroutine. If `IsPreloading` stayed true, the next overlay would wait forever for a coroutine that no longer exists.

4. **Removes `Resources.UnloadUnusedAssets()` and `GC.Collect()`.** The original ESC calls these to free memory. Removing them keeps Unity's Addressable asset cache warm — subsequent opens skip expensive sprite loads because the assets are still in memory. This cut subsequent open times from 25s to 7s.

**Why delayed reactivation (1 frame):** The game's TAB key is a toggle. `Collections.Update()` checks `Input.GetKeyDown(PlayerMainKey)` and calls `UIManager.BackButton()` → `ESC()` if Collections is active. If we reactivated on the same frame as the TAB press, `GetKeyDown` would still return true and immediately close Collections. Delaying by one frame ensures `GetKeyDown` has expired.

**Why reset RectTransform after reactivation:** `UIManager.SetActiveUIForce()` calls `transform.SetParent(ActiveSlot)` with Unity's default `worldPositionStays=true`. Moving from DontDestroyOnLoad (no Canvas) back to the game's Canvas hierarchy causes Unity to compute insane local coordinates (position=-2.5M, scale=108x) to preserve the "world" position. Resetting to `(0,0,-0.1)` and scale `(1,1,1)` restores correct rendering.

### Problem: ESC for main menu broken after caching

After closing Collections (cached), pressing ESC didn't open the main menu.

**Root cause discovered via F9 debug hotkey:** `UIManager.UICheck()` runs every frame and scans `BeforeUI` and `AllUI` lists. Even though we set `Destoryed=true` and nulled `NowActiveUI`, the cached Collections reference was still in `AllUI` (deactivated but not Unity-null). `UICheck` kept restoring it as `NowActiveUI`, and `UIManager.BackButton()` consumed the ESC key before `PauseWindowOpenCheck` could run.

**Fix:** Explicitly remove from `AllUI`, `BeforeUI`, and `NoneUICheckLIst` after simulating OnDestroy. Also, `PauseWindowOpenCheck` has a condition on `UIManager.NowActiveUI` — Unity's `Object.Destroy` makes references evaluate as null via operator overloading, but deactivation doesn't. Manually nulling `NowActiveUI` after cache was required.

### Problem: Floating tooltip persists after close

The original `Collections.ESC()` has conditional tooltip cleanup for Character and Monster info views, but not all tooltip scenarios. With caching, `ToolTipWindow.ToolTipDestroy()` might not be reached because our `DestroyOrCache` intercepts before the full ESC code path completes for tooltip edge cases.

**Fix:** Explicitly call `ToolTipWindow.ToolTipDestroy()` in `DestroyOrCache` after caching.

### Problem: Mod skill tooltip permanently stuck on screen

Hovering over certain mod skills (e.g., Jefuty's Miracle Declaration / 神迹宣言) caused a broken tooltip to appear and never dismiss, requiring a game restart.

**Root cause:** The game's `ToolTipWindow.SkillToolTip()` instantiates a tooltip GameObject, calls `SkillToolTip.Input()` to populate it, and only then assigns it to the static `ToolTip` field. If `Input()` throws, the GameObject is orphaned on screen and `ToolTipDestroy()` can never find it because `ToolTip` is still null.

The specific crash was a `MissingFieldException: Field 'Stat.PlusMPUse' not found` thrown during `Buff.DataToBuff()` → `B_Jefuty_R1.Init()`. The Jefuty mod was compiled when `Stat.PlusMPUse` was an `int`, but the field was changed to a `PlusMP` class in a game update. This is a JIT-time failure — the runtime can't compile the method at all, so the exception is thrown at the virtual dispatch site in `DataToBuff`, not inside `Init()`.

**Fix (two layers):**

1. **`BuffDataToBuffPatch`** — Harmony Finalizer on `Buff.DataToBuff`. When a `MissingFieldException` or `MissingMethodException` is caught, creates a fallback plain `Buff` (not the broken mod subclass) with the same data fields. The tooltip renders with correct buff name, description, and duration, just without the subclass's custom stat modifications.

2. **`SkillTooltipPatch`** — Harmony Finalizers on `ToolTipWindow.SkillToolTip` and `SkillToolTip_Collection`. If any unhandled exception escapes, the orphaned tooltip is found via `FindObjectOfType<SkillToolTip>()` and destroyed. This is a generic safety net for any tooltip crash, not just the Jefuty case.

## Patch Files

### CachedCollectionsPatch.cs

**Harmony patches:**
- `UIManager.CollectionsUIOpen` (Prefix) — On cache hit, delays reactivation by one frame. On first open, runs `FirstOpenWithOverlay` coroutine.
- `Collections.ESC` (Transpiler) — Replaces `Object.Destroy` with `DestroyOrCache`, removes `UnloadUnusedAssets` and `GC.Collect`.
- `Collections.Start` (Postfix) — If a new Collections instance is created while a stale cache exists (e.g., opened via character profile while a TAB cache exists), destroys the stale cache to avoid conflicts.

### DeferredCollectionsPatch.cs

**Harmony patches:**
- `SKillCollection.Start`, `ItemCollection.Start`, `MonsterCollection.Start` (Prefix, Priority.First) — Skips Start unless bypass flag is set or `IsOnce=true`.
- `Collections.Start` (Postfix) — Caches `MethodInfo` for deferred Start invocation.
- `Collections.SelectCategory` (Prefix) — Triggers deferred initialization when the user clicks a tab.

### SkillPreloadPatch.cs

**Harmony patches:**
- `SKillCollection.Start` (Postfix) — Starts `PreloadAllCharacters` coroutine with adaptive batching (200ms frame budget).
- `CharSelect_CampUI.OpenProfile` (Prefix) — Wraps camp profile open in overlay coroutine via shared helper.
- `CharSelectMainUIV2.OpenProfile` (Prefix) — Wraps character select profile open in overlay coroutine via shared helper.

**OpenProfileSharedHelper.OpenProfileShared:** Shared coroutine for both OpenProfile paths. If cache exists, reactivates, switches to Character tab (`SelectCategory(0)`), and navigates to the selected character (`CharacterInfoOnName`). Sets `IsOnce=true` and `DeleteAction` for proper close behavior. Waits for preload and frame settling behind overlay.

### SkillTooltipPatch.cs

**Harmony patches:**
- `ToolTipWindow.SkillToolTip` (Finalizer) — Catches exceptions, destroys orphaned tooltip via `FindObjectOfType<SkillToolTip>()`, returns null.
- `ToolTipWindow.SkillToolTip_Collection` (Finalizer) — Same safety net for the collection/encyclopedia variant.
- `Buff.DataToBuff` (Finalizer) — Catches `MissingFieldException`/`MissingMethodException` from mod subclass JIT failures, creates a fallback plain `Buff` with the same data fields.

### StartingStatsPatch.cs

**Harmony patch:** `PlayData.init` (Postfix) — Checks `StageNum==0 && !GameStarted` to detect fresh runs (not loaded saves), then reads slider settings and overrides starting stats.

## PerfDebug Framework

### FrameTimeMonitor.cs

Logs hitches at three tiers (100ms/500ms/2000ms). Maintains a static ring buffer of recent method entries populated by other patches. Dumps active Canvases on freeze events for identifying which UI is open. F9 hotkey dumps full UIManager state.

### KnownMethodTimingPatches.cs

Manual Harmony patches on 8 known game methods with per-method try-catch so failures don't crash the mod. Each patch records to the ring buffer and logs via Stopwatch.

### CollectionInitProbes.cs

Sub-method instrumentation tracking call counts and aggregate time for operations inside `CharacterCollection.Init` and `SKillCollection.Init`. Identified that `LoadAsyncCompletion` (68 calls, 1,112ms) and prefab instantiation (~1,200ms) were the dominant costs.

**Note:** The `LoadAsyncCompletion<Sprite>` generic method patch was removed after it caused Unity crashes by corrupting the Mono JIT's generic dispatch for other instantiations of the same generic method.

## Key Game Types

| Type | Role |
|------|------|
| `Collections` | Parent UI for the 4-tab encyclopedia. Extends `UI`. `ESC()` handles close. `Update()` handles TAB toggle. |
| `UI` | Base class. `Destoryed` (sic) flag. `OnDestroy` deregisters from UIManager. `DisableBack` prevents BackButton. |
| `UIManager` | Singleton. `NowActiveUI`, `AllUI`, `BeforeUI` lists. `SetActiveUIForce` registers. `UICheck` validates state every frame. `PauseWindowOpenCheck` gates ESC→main menu. |
| `PlayData` | Game state. `init()` loads saves. `DataBaseInit()` rebuilds skill/item databases. |
| `AddressableLoadManager` | Wraps Unity Addressables. Assets stay cached if `UnloadUnusedAssets` is skipped. |
| `GamepadManager` | Input state. `LayoutStop` and `IsLayoutMode` must be reset on Collections close or ESC is consumed. |
