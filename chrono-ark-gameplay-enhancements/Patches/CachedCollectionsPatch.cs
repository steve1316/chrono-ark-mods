using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GameplayEnhancements.Patches
{
    /// <summary>
    /// Caches the Collections UI across open/close cycles. On close, reparents
    /// to a hidden DontDestroyOnLoad container and manually simulates OnDestroy
    /// so the game's UI tracking is fully cleaned up. On reopen, reparents back
    /// and re-registers with UIManager. First open uses an overlay for loading.
    /// </summary>
    internal static class CachedCollectionsPatch
    {
        private const string Tag = "[GameplayEnhancements]";
        private const float SmoothFrameThreshold = 0.060f;
        private const int RequiredSmoothFrames = 10;

        internal static Collections _cached;
        private static bool _bypassOpen;
        private static MonoBehaviour _coroutineHost;
        private static GameObject _cacheContainer;


        // Cached reflection — resolved once.
        private static bool _reflectionReady;
        private static MethodInfo _uiCheckMethod;
        private static MethodInfo _setActiveUIForceMethod;
        private static FieldInfo _allUIField;
        private static FieldInfo _noneUICheckListField;
        private static FieldInfo _prevLayoutsField;

        internal static void SetCoroutineHost(MonoBehaviour host)
        {
            _coroutineHost = host;
        }

        /// <summary>
        /// Returns (or creates) the hidden DontDestroyOnLoad container
        /// used to hold cached Collections between open/close cycles.
        /// </summary>
        private static GameObject GetCacheContainer()
        {
            if (_cacheContainer == null)
            {
                _cacheContainer = new GameObject("CollectionsCache_Hidden");
                UnityEngine.Object.DontDestroyOnLoad(_cacheContainer);
                _cacheContainer.SetActive(false);
            }
            return _cacheContainer;
        }

        private static void EnsureReflection()
        {
            if (_reflectionReady) return;
            _reflectionReady = true;

            _uiCheckMethod = AccessTools.Method(typeof(UIManager), "UICheck");
            _setActiveUIForceMethod = AccessTools.Method(typeof(UIManager),
                "SetActiveUIForce", new[] { typeof(UI) });
            _allUIField = AccessTools.Field(typeof(UIManager), "AllUI");
            _noneUICheckListField = AccessTools.Field(typeof(UIManager), "NoneUICheckLIst");
            _prevLayoutsField = AccessTools.Field(typeof(Collections), "PrevLayouts");
        }

        // ----------------------------------------------------------------
        // UIManager.CollectionsUIOpen — overlay on first open, cache reuse
        // ----------------------------------------------------------------

        [HarmonyPatch(typeof(UIManager), nameof(UIManager.CollectionsUIOpen))]
        internal static class CollectionsUIOpenPatch
        {
            static bool Prefix()
            {
                if (!Plugin.CollectionsOptimizationEnabled) return true;
                if (_bypassOpen) return true;

                if (_cached != null)
                {
                    Debug.Log($"{Tag} Reusing cached Collections UI (delayed 1 frame)");
                    _coroutineHost.StartCoroutine(DelayedReactivate());
                    return false;
                }

                if (_coroutineHost != null)
                {
                    _coroutineHost.StartCoroutine(FirstOpenWithOverlay());
                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Moves the cached Collections back from the hidden container,
        /// clears destroyed state, and re-registers with UIManager.
        /// </summary>
        private static void ReactivateCached()
        {
            EnsureReflection();
            var collections = _cached;

            // Undo the destroyed state.
            collections.Destoryed = false;

            // Reactivate the gameObject (triggers OnEnable).
            collections.gameObject.SetActive(true);

            // Re-register with the game's UI system. SetActiveUIForce
            // sets NowActiveUI, adds to AllUI, and parents under ActiveSlot.
            // Note: SetActiveUIForce uses SetParent with worldPositionStays=true,
            // which corrupts the RectTransform when moving between Canvas
            // hierarchies. We fix it immediately after.
            _setActiveUIForceMethod.Invoke(null, new object[] { collections });

            // Reset the RectTransform to match the original first-open state.
            var rt = collections.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.localPosition = new Vector3(0f, 0f, -0.1f);
                rt.localScale = Vector3.one;
            }

            // Clear cached reference now that it's active again.
            _cached = null;

            Debug.Log($"{Tag} Reactivated cached Collections at frame {Time.frameCount} " +
                      $"(NowActiveUI={UIManager.NowActiveUI?.GetType().Name})");
        }

        /// <summary>
        /// Reactivates cached Collections for the OpenProfile path.
        /// No delayed frame needed since profile opens don't have TAB toggle.
        /// </summary>
        internal static void ReactivateCachedForProfile()
        {
            if (_cached == null) return;
            ReactivateCached();
        }

        /// <summary>
        /// Waits one frame for the TAB key's GetKeyDown to expire, then
        /// reactivates. This prevents Collections.Update() from seeing
        /// TAB as pressed and immediately closing via BackButton.
        /// </summary>
        private static IEnumerator DelayedReactivate()
        {
            yield return null;
            if (_cached != null)
                ReactivateCached();
        }

        // ----------------------------------------------------------------
        // Collections.ESC — transpiler replaces Destroy with cache redirect
        // ----------------------------------------------------------------

        [HarmonyPatch(typeof(Collections), "ESC")]
        internal static class CollectionsESCPatch
        {
            static IEnumerable<CodeInstruction> Transpiler(
                IEnumerable<CodeInstruction> instructions)
            {
                var destroyMethod = AccessTools.Method(typeof(UnityEngine.Object),
                    "Destroy", new[] { typeof(UnityEngine.Object) });
                var redirectMethod = AccessTools.Method(
                    typeof(CachedCollectionsPatch), nameof(DestroyOrCache));
                var unloadMethod = AccessTools.Method(typeof(Resources),
                    nameof(Resources.UnloadUnusedAssets));
                var gcCollect = AccessTools.Method(typeof(GC), "Collect",
                    Type.EmptyTypes);

                foreach (var inst in instructions)
                {
                    if (inst.Calls(destroyMethod))
                        yield return new CodeInstruction(OpCodes.Call, redirectMethod);
                    else if (inst.Calls(unloadMethod))
                        yield return new CodeInstruction(OpCodes.Ldnull);
                    else if (inst.Calls(gcCollect))
                        yield return new CodeInstruction(OpCodes.Nop);
                    else
                        yield return inst;
                }
            }
        }

        /// <summary>
        /// Called instead of Object.Destroy during Collections.ESC().
        /// For the Collections gameObject (non-IsOnce): reparents to a hidden
        /// container, deactivates, and manually simulates OnDestroy so the
        /// game's UI tracking is fully cleaned up. For other objects on cached
        /// Collections: skips destroy. For everything else: destroys normally.
        /// </summary>
        public static void DestroyOrCache(UnityEngine.Object obj)
        {
            if (!Plugin.CollectionsOptimizationEnabled)
            {
                UnityEngine.Object.Destroy(obj);
                return;
            }

            if (obj is GameObject go)
            {
                var collections = go.GetComponent<Collections>();
                if (collections != null)
                {
                    EnsureReflection();

                    // 1. Reparent to hidden DontDestroyOnLoad container.
                    go.transform.SetParent(GetCacheContainer().transform, false);

                    // 2. Deactivate.
                    go.SetActive(false);

                    // 3. Simulate UI.OnDestroy chain:
                    collections.Destoryed = true;
                    _uiCheckMethod.Invoke(null, null);
                    var allUI = _allUIField.GetValue(null) as System.Collections.IList;
                    allUI?.Remove(collections);
                    var noneList = _noneUICheckListField.GetValue(null) as System.Collections.IList;
                    noneList?.Remove(collections);

                    // 4. Simulate Collections.OnDestroy: reset GamepadManager.
                    var gpmType = AccessTools.TypeByName("GamepadManager");
                    gpmType?.GetField("LayoutStop")?.SetValue(null, false);

                    // 5. Destroy any floating tooltips.
                    try { AccessTools.Method("ToolTipWindow:ToolTipDestroy")?.Invoke(null, null); }
                    catch { /* Non-critical. */ }

                    // 6. Reset preload flag — deactivation kills coroutines
                    //    so the preload will never reach IsPreloading = false.
                    SkillPreloadPatch.IsPreloading = false;

                    // 7. Clear IsOnce so the cache can be reused by any path.
                    collections.IsOnce = false;
                    collections.DeleteAction = null;

                    // 8. Cache.
                    _cached = collections;

                    Debug.Log($"{Tag} Cached Collections UI → " +
                              $"DontDestroyOnLoad (NowActiveUI=" +
                              $"{(UIManager.NowActiveUI != null ? UIManager.NowActiveUI.GetType().Name : "null")})");
                    return;
                }
            }
            else if (obj is Component comp)
            {
                var collections = comp.GetComponent<Collections>();
                if (collections != null)
                {
                    Debug.Log($"{Tag} Skipped destroying {comp.GetType().Name} (caching)");
                    return;
                }
            }

            // Not cacheable — destroy normally.
            UnityEngine.Object.Destroy(obj);
        }

        // ----------------------------------------------------------------
        // Collections.Start — detect conflicting new instances
        // ----------------------------------------------------------------

        [HarmonyPatch(typeof(Collections), "Start")]
        internal static class CollectionsStartPatch
        {
            static void Postfix(Collections __instance)
            {
                if (!Plugin.CollectionsOptimizationEnabled) return;
                if (_cached != null && _cached != __instance)
                {
                    Debug.Log($"{Tag} New Collections instance created, clearing stale cache");
                    UnityEngine.Object.Destroy(_cached.gameObject);
                    _cached = null;
                }
            }
        }

        // ----------------------------------------------------------------
        // First-open overlay flow
        // ----------------------------------------------------------------

        private static IEnumerator FirstOpenWithOverlay()
        {
            var totalSw = Stopwatch.StartNew();

            var overlay = ProgressOverlayHelper.Show("Loading encyclopedia...");
            yield return null;

            ProgressOverlayHelper.UpdateMessage(overlay, "Loading characters...");
            yield return null;

            var sw = Stopwatch.StartNew();
            _bypassOpen = true;
            UIManager.CollectionsUIOpen();
            _bypassOpen = false;
            sw.Stop();
            Debug.Log($"{Tag} CollectionsUIOpen (Character tab): {sw.ElapsedMilliseconds}ms");

            // Log initial transform for comparison with cached reopen.
            var collections = UnityEngine.Object.FindObjectOfType<Collections>();
            if (collections != null)
            {
                var rt0 = collections.GetComponent<RectTransform>();
                if (rt0 != null)
                    Debug.Log($"{Tag} [FirstOpen] RectTransform: pos={rt0.localPosition}, scale={rt0.localScale}, " +
                              $"size={rt0.sizeDelta}, anchors=({rt0.anchorMin},{rt0.anchorMax})");
            }
            Canvas collectionsCanvas = null;
            if (collections != null)
            {
                collectionsCanvas = collections.GetComponentInParent<Canvas>();
                if (collectionsCanvas == null)
                    collectionsCanvas = collections.GetComponent<Canvas>();
                if (collectionsCanvas != null)
                    collectionsCanvas.enabled = false;
            }

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

            if (collectionsCanvas != null)
                collectionsCanvas.enabled = true;

            ProgressOverlayHelper.Hide(overlay);
        }
    }
}
