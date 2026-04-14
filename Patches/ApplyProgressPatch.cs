using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using ChronoArkMod;
using HarmonyLib;
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

            // Count how many mods are changing state.
            var enabledBefore = new HashSet<string>(ModManager.LoadedMods);
            var enabledAfter = new HashSet<string>(ModManager.EnabledMods);
            int toLoad = 0, toUnload = 0;
            foreach (var id in enabledAfter)
                if (!enabledBefore.Contains(id)) toLoad++;
            foreach (var id in enabledBefore)
                if (!enabledAfter.Contains(id)) toUnload++;

            Debug.Log($"[WorkshopOverhaul] Apply started: {toLoad} to load, {toUnload} to unload, reloadAll={needReLoadAll}");

            // Show the overlay.
            var overlay = ProgressOverlay.Show(instance.transform,
                $"Applying mod changes...\n({toLoad} loading, {toUnload} unloading)");

            // Yield one frame so Unity renders the overlay before the
            // synchronous ModStatChanged call blocks the main thread.
            yield return null;

            // Time the SaveModSettings call.
            var sw = Stopwatch.StartNew();
            ModManager.SaveModSettings();
            sw.Stop();
            Debug.Log($"[WorkshopOverhaul] SaveModSettings: {sw.ElapsedMilliseconds}ms");

            // Time ModStatChanged (the main bottleneck).
            sw.Restart();
            var failedMods = ModManager.ModStatChanged(needReLoadAll);
            sw.Stop();
            long modStatMs = sw.ElapsedMilliseconds;
            Debug.Log($"[WorkshopOverhaul] ModStatChanged: {modStatMs}ms");

            totalSw.Stop();
            long totalMs = totalSw.ElapsedMilliseconds;
            Debug.Log($"[WorkshopOverhaul] Total apply time: {totalMs}ms (failed: {failedMods.Count})");

            // Update overlay with result before hiding.
            ProgressOverlay.UpdateMessage(overlay, $"Done in {totalMs}ms");
            yield return new WaitForSeconds(0.5f);

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
