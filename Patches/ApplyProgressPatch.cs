using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using ChronoArkMod;
using ChronoArkMod.ModData;
using GameDataEditor;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using WorkshopOverhaul.UI;
using Debug = UnityEngine.Debug;

namespace WorkshopOverhaul.Patches
{
    /// <summary>
    /// Wraps ModUI.OnApplyBtnClick() in a coroutine that shows a progress overlay
    /// before the synchronous mod loading begins. This gives Unity one frame to
    /// render the overlay so the screen doesn't appear frozen.
    /// Also logs detailed timing to Player.log for benchmarking.
    /// </summary>
    [HarmonyPatch(typeof(ModUI), nameof(ModUI.OnApplyBtnClick))]
    internal static class ApplyProgressPatch
    {
        static bool Prefix(ModUI __instance)
        {
            __instance.StartCoroutine(ApplyWithOverlay(__instance));
            return false; // Skip original method.
        }

        /// <summary>
        /// Coroutine that shows a progress overlay, yields a frame for it to render,
        /// then runs the synchronous mod load/unload and logs timing.
        /// </summary>
        private static IEnumerator ApplyWithOverlay(ModUI instance)
        {
            var totalSw = Stopwatch.StartNew();

            // Check for invalid dependencies first (same as original).
            bool needReLoadAll = false;
            bool needRestart = false;
            bool invalid = false;

            instance.AlignSort();
            ModManager.CheckEnabled(out needReLoadAll, out needRestart, out invalid);

            if (invalid)
            {
                instance.ShowInvalidDenendency();
                yield break;
            }

            if (GetNeedRestart(instance) || needRestart)
            {
                instance.confirmUI.Init("Need Restart", "", new UnityEngine.Events.UnityAction(() =>
                {
                    ModManager.SaveModSettings();
                    Debug.Log("Restart");
                    Application.Quit();
                }));
                yield break;
            }

            // Snapshot loaded/enabled lists before mutation.
            var loadedBefore = new List<string>(ModManager.LoadedMods);
            var enabledSnapshot = new List<string>(ModManager.EnabledMods);
            var loadedSet = new HashSet<string>(loadedBefore);
            var enabledSet = new HashSet<string>(enabledSnapshot);

            int toLoad = 0, toUnload = 0;
            if (needReLoadAll)
            {
                toUnload = loadedBefore.Count;
                toLoad = enabledSnapshot.Count;
            }
            else
            {
                foreach (var id in enabledSnapshot)
                    if (!loadedSet.Contains(id)) toLoad++;
                foreach (var id in loadedBefore)
                    if (!enabledSet.Contains(id)) toUnload++;
            }

            Debug.Log($"[WorkshopOverhaul] Apply started: {toLoad} to load, {toUnload} to unload, reloadAll={needReLoadAll}");

            var overlay = ProgressOverlay.Show(instance.transform,
                $"Applying mod changes...\n({toLoad} loading, {toUnload} unloading)");
            yield return null;

            var sw = Stopwatch.StartNew();
            ModManager.SaveModSettings();
            sw.Stop();
            Debug.Log($"[WorkshopOverhaul] SaveModSettings: {sw.ElapsedMilliseconds}ms");

            // Enable deferred audio loading for the duration of this apply.
            DeferredAudioLoadPatch.IsApplyActive = true;

            // --- Phase A: Unload mods no longer enabled (or all if reloading). ---
            var failedMods = new List<string>();
            int unloadIndex = 0;
            foreach (string modId in loadedBefore)
            {
                if (!enabledSet.Contains(modId) || needReLoadAll)
                {
                    unloadIndex++;
                    string title = ModManager.getModInfo(modId)?.Title ?? modId;
                    ProgressOverlay.UpdateMessage(overlay,
                        $"Unloading {unloadIndex}/{toUnload}: {title}");
                    yield return null;

                    sw.Restart();
                    ModManager.UnLoadMod(ModManager.getModInfo(modId));
                    sw.Stop();
                    Debug.Log($"[WorkshopOverhaul] UnLoadMod '{title}': {sw.ElapsedMilliseconds}ms");
                }
            }

            // --- Phase B: Load newly enabled mods (or all if reloading). ---
            int loadIndex = 0;
            foreach (string modId in enabledSnapshot)
            {
                if (!loadedSet.Contains(modId) || needReLoadAll)
                {
                    loadIndex++;
                    string title = ModManager.getModInfo(modId)?.Title ?? modId;
                    ProgressOverlay.UpdateMessage(overlay,
                        $"Loading {loadIndex}/{toLoad}: {title}");
                    yield return null;

                    sw.Restart();
                    bool success = ModManager.LoadMod(ModManager.getModInfo(modId));
                    sw.Stop();
                    Debug.Log($"[WorkshopOverhaul] LoadMod '{title}': {sw.ElapsedMilliseconds}ms (success={success})");

                    if (!success)
                        failedMods.Add(modId);
                }
            }

            // --- Phase B2: Deferred audio init (yield between mods). ---
            DeferredAudioLoadPatch.IsApplyActive = false;
            var pendingAudio = new List<ModInfo>(DeferredAudioLoadPatch.PendingAudioInit);
            DeferredAudioLoadPatch.PendingAudioInit.Clear();
            int audioIndex = 0;
            foreach (var modInfo in pendingAudio)
            {
                audioIndex++;
                ProgressOverlay.UpdateMessage(overlay,
                    $"Loading audio {audioIndex}/{pendingAudio.Count}: {modInfo.Title ?? modInfo.id}");
                yield return null;

                sw.Restart();
                modInfo.audioinfo.init();
                sw.Stop();
                if (sw.ElapsedMilliseconds > 5)
                    Debug.Log($"[WorkshopOverhaul] Audio init '{modInfo.id}': {sw.ElapsedMilliseconds}ms");
            }

            // --- Phase C: Finalize (inlined UpdateModInfo with per-step timing). ---

            // C1: Per-mod schema + localization updates.
            ProgressOverlay.UpdateMessage(overlay, "Updating mod data schemas...");
            yield return null;

            sw.Restart();
            foreach (string modId in new List<string>(ModManager.LoadedMods))
            {
                var info = ModManager.getModInfo(modId);
                if (info != null)
                    info.UpdateModInfo();
            }
            sw.Stop();
            Debug.Log($"[WorkshopOverhaul] Per-mod UpdateModInfo: {sw.ElapsedMilliseconds}ms");

            // C2: Rebuild save types (reflection scan of all assemblies).
            ProgressOverlay.UpdateMessage(overlay, "Building save types...");
            yield return null;

            sw.Restart();
            ModManager.MakeSaveType();
            sw.Stop();
            Debug.Log($"[WorkshopOverhaul] MakeSaveType: {sw.ElapsedMilliseconds}ms");

            // C3: Rebuild the GDE data-key-to-schema index.
            ProgressOverlay.UpdateMessage(overlay, "Rebuilding data index...");
            yield return null;

            sw.Restart();
            GDEDataManager.BuildDataKeysBySchemaList();
            sw.Stop();
            Debug.Log($"[WorkshopOverhaul] BuildDataKeysBySchemaList: {sw.ElapsedMilliseconds}ms");

            // C4: Reinitialize skill/item databases.
            ProgressOverlay.UpdateMessage(overlay, "Initializing database...");
            yield return null;

            sw.Restart();
            PlayData.DataBaseInit();
            sw.Stop();
            Debug.Log($"[WorkshopOverhaul] DataBaseInit: {sw.ElapsedMilliseconds}ms");

            // Update version label (UIManager may not exist on all screens).
            try
            {
                if (UIManager.inst != null)
                {
                    UIManager.inst.Version.text = Application.version;
                    if (ModManager.LoadedMods.Count > 0)
                        UIManager.inst.Version.text += $"\nwith mod({ModManager.LoadedMods.Count})";
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[WorkshopOverhaul] Version text error: {ex.Message}");
            }

            totalSw.Stop();
            long totalMs = totalSw.ElapsedMilliseconds;
            Debug.Log($"[WorkshopOverhaul] Total apply time: {totalMs}ms (failed: {failedMods.Count})");

            // Update overlay with result before hiding.
            ProgressOverlay.UpdateMessage(overlay, $"Done in {totalMs}ms");
            yield return new WaitForSeconds(1.0f);

            // Hide the overlay.
            ProgressOverlay.Hide(overlay);

            // Clear change state (replicate original behavior).
            ClearChangeState(instance);

            // Show failure hint or close.
            if (failedMods.Count > 0)
            {
                string text = "";
                foreach (string modId in failedMods)
                {
                    string title = ModManager.getModInfo(modId)?.Title;
                    text += title + "[" + modId + "]\n";
                }
                instance.confirmUI.Init("Fail to Load", text, null);
            }
            else
            {
                instance.Main.CloseUserModeUI();
                instance.gameObject.SetActive(false);
            }
        }

        // _needRestart is private; Traverse is the Harmony way to read it
        // without reflection boilerplate.
        private static bool GetNeedRestart(ModUI instance)
        {
            return Traverse.Create(instance).Field("_needRestart").GetValue<bool>();
        }

        // Mirrors the private ClearChangeState() method. These three flags
        // control whether the Apply button shows and whether a restart is prompted.
        private static void ClearChangeState(ModUI instance)
        {
            var traverse = Traverse.Create(instance);
            traverse.Field("_hasChangedEnable").SetValue(false);
            traverse.Field("_hasChangedSetting").SetValue(false);
            traverse.Field("_needRestart").SetValue(false);
        }
    }
}
