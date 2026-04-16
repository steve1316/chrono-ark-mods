using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GameplayEnhancements.Patches
{
    /// <summary>
    /// Orchestrates Collections UI loading behind a progress overlay so the
    /// user never sees lag once the overlay dismisses. Each TAB press shows
    /// the overlay, creates Collections, initializes all tabs, waits for
    /// skill preloading and frame settling, then reveals the UI.
    /// </summary>
    internal static class CachedCollectionsPatch
    {
        private const string Tag = "[GameplayEnhancements]";
        private const float SmoothFrameThreshold = 0.060f;
        private const int RequiredSmoothFrames = 10;

        private static bool _bypassOpen;
        private static MonoBehaviour _coroutineHost;

        /// <summary>
        /// Sets the MonoBehaviour used to host coroutines (called from Plugin).
        /// </summary>
        internal static void SetCoroutineHost(MonoBehaviour host)
        {
            _coroutineHost = host;
        }

        /// <summary>
        /// Intercepts UIManager.CollectionsUIOpen to run the overlay-based
        /// loading flow instead of opening Collections directly.
        /// </summary>
        [HarmonyPatch(typeof(UIManager), nameof(UIManager.CollectionsUIOpen))]
        internal static class CollectionsUIOpenPatch
        {
            static bool Prefix()
            {
                if (_bypassOpen) return true;

                if (_coroutineHost != null)
                {
                    _coroutineHost.StartCoroutine(OpenWithOverlay());
                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Shows a progress overlay, creates Collections, initializes all
        /// tabs sequentially, waits for skill preloading and frame settling,
        /// then dismisses the overlay to reveal a fully ready UI.
        /// </summary>
        private static IEnumerator OpenWithOverlay()
        {
            var totalSw = Stopwatch.StartNew();

            var overlay = ProgressOverlayHelper.Show("Loading encyclopedia...");
            yield return null;

            // Create the Collections UI. CharacterCollection.Start runs
            // synchronously; other tabs are deferred by DeferredCollectionsPatch.
            ProgressOverlayHelper.UpdateMessage(overlay, "Loading characters...");
            yield return null;

            var sw = Stopwatch.StartNew();
            _bypassOpen = true;
            UIManager.CollectionsUIOpen();
            _bypassOpen = false;
            sw.Stop();
            Debug.Log($"{Tag} CollectionsUIOpen (Character tab): {sw.ElapsedMilliseconds}ms");

            // Hide Collections behind the overlay by disabling its Canvas.
            var collections = Object.FindObjectOfType<Collections>();
            Canvas collectionsCanvas = null;
            if (collections != null)
            {
                collectionsCanvas = collections.GetComponentInParent<Canvas>();
                if (collectionsCanvas == null)
                    collectionsCanvas = collections.GetComponent<Canvas>();
                if (collectionsCanvas != null)
                    collectionsCanvas.enabled = false;
            }

            // Initialize deferred tabs one at a time with overlay updates.
            yield return null;

            ProgressOverlayHelper.UpdateMessage(overlay, "Loading skills...");
            yield return null;
            sw.Restart();
            DeferredCollectionsPatch.InitializeDeferredSkill();
            sw.Stop();
            Debug.Log($"{Tag} SKillCollection init: {sw.ElapsedMilliseconds}ms");
            yield return null;

            ProgressOverlayHelper.UpdateMessage(overlay, "Loading items...");
            yield return null;
            sw.Restart();
            DeferredCollectionsPatch.InitializeDeferredItem();
            sw.Stop();
            Debug.Log($"{Tag} ItemCollection init: {sw.ElapsedMilliseconds}ms");
            yield return null;

            ProgressOverlayHelper.UpdateMessage(overlay, "Loading monsters...");
            yield return null;
            sw.Restart();
            DeferredCollectionsPatch.InitializeDeferredMonster();
            sw.Stop();
            Debug.Log($"{Tag} MonsterCollection init: {sw.ElapsedMilliseconds}ms");

            // Wait for the SkillPreloadPatch background coroutine to finish.
            while (SkillPreloadPatch.IsPreloading)
            {
                int cur = SkillPreloadPatch.PreloadCurrent;
                int tot = SkillPreloadPatch.PreloadTotal;
                string name = SkillPreloadPatch.PreloadCurrentName ?? "";
                ProgressOverlayHelper.UpdateMessage(overlay,
                    $"Preloading skill data...\n{name} ({cur}/{tot})");
                yield return null;
            }

            Debug.Log($"{Tag} Skill preload finished");

            // Wait for frames to settle.
            ProgressOverlayHelper.UpdateMessage(overlay, "Finalizing...");
            yield return null;

            int smoothCount = 0;
            while (smoothCount < RequiredSmoothFrames)
            {
                yield return null;
                if (Time.unscaledDeltaTime < SmoothFrameThreshold)
                    smoothCount++;
                else
                    smoothCount = 0;
            }

            totalSw.Stop();
            Debug.Log($"{Tag} Collections fully ready in {totalSw.ElapsedMilliseconds}ms");

            // Reveal Collections and dismiss overlay.
            if (collectionsCanvas != null)
                collectionsCanvas.enabled = true;

            ProgressOverlayHelper.Hide(overlay);
        }

        // --- ESC transpiler: keep Addressable cache warm across opens ---

        /// <summary>
        /// Removes Resources.UnloadUnusedAssets() and GC.Collect() from
        /// Collections.ESC() so that Addressable-loaded sprites stay in
        /// Unity's asset cache. Everything else (Destroy, flag resets,
        /// gamepad restore) runs unmodified.
        /// </summary>
        [HarmonyPatch(typeof(Collections), "ESC")]
        internal static class CollectionsESCTranspiler
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var unloadMethod = AccessTools.Method(typeof(Resources),
                    nameof(Resources.UnloadUnusedAssets));
                var gcCollect = AccessTools.Method(typeof(System.GC), "Collect",
                    System.Type.EmptyTypes);

                foreach (var inst in instructions)
                {
                    if (inst.Calls(unloadMethod))
                    {
                        // UnloadUnusedAssets returns AsyncOperation — replace
                        // with ldnull so the pop after it still works.
                        yield return new CodeInstruction(OpCodes.Ldnull);
                    }
                    else if (inst.Calls(gcCollect))
                    {
                        yield return new CodeInstruction(OpCodes.Nop);
                    }
                    else
                    {
                        yield return inst;
                    }
                }

                Debug.Log($"{Tag} ESC transpiler: removed UnloadUnusedAssets + GC.Collect");
            }
        }
    }
}
