using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace GameplayEnhancements.Patches
{
    /// <summary>
    /// Pre-loads skill data for all characters when the Encyclopedia's Skill
    /// tab initializes. Runs synchronously during the Encyclopedia's own
    /// init hang so there's only one wait instead of two.
    /// </summary>
    [HarmonyPatch(typeof(SKillCollection), "Start")]
    internal static class SkillPreloadPatch
    {
        /// <summary>
        /// True while the background skill preload coroutine is running.
        /// </summary>
        internal static bool IsPreloading;

        /// <summary>
        /// Current preload progress for overlay display.
        /// </summary>
        internal static int PreloadCurrent;
        internal static int PreloadTotal;
        internal static string PreloadCurrentName;

        private static MethodInfo _skillAddMethod;

        static void Postfix(SKillCollection __instance)
        {
            if (!Plugin.CollectionsOptimizationEnabled) return;
            _skillAddMethod = AccessTools.Method(typeof(SKillCollection), "SkillAdd");
            if (_skillAddMethod == null)
            {
                Debug.LogWarning("[GameplayEnhancements] Could not find SKillCollection.SkillAdd method");
                return;
            }

            // Run on the parent Collections object which stays active
            // (SKillCollection deactivates itself at the end of Start).
            var collections = __instance.GetComponentInParent<Collections>();
            if (collections != null)
                collections.StartCoroutine(PreloadAllCharacters(__instance));
        }

        /// <summary>
        /// Maximum milliseconds of work per frame before yielding. Keeps
        /// the overlay responsive while minimizing total preload time.
        /// </summary>
        private const long FrameBudgetMs = 200;

        /// <summary>
        /// Iterates all characters and calls SkillAdd for each unloaded one.
        /// Uses adaptive batching: processes multiple characters per frame
        /// as long as the cumulative time stays under the frame budget.
        /// </summary>
        private static IEnumerator PreloadAllCharacters(SKillCollection instance)
        {
            IsPreloading = true;
            yield return null;

            var alignChar = Traverse.Create(instance).Field("Align_Char")
                .GetValue<List<GameObject>>();
            if (alignChar == null) yield break;

            // Count how many characters need preloading.
            int total = 0;
            foreach (var go in alignChar)
            {
                var cs = go.GetComponent<Skill_CharSelect>();
                if (cs != null && !cs.isLoad) total++;
            }
            PreloadTotal = total;
            PreloadCurrent = 0;

            var sw = new Stopwatch();
            var frameSw = new Stopwatch();
            long totalMs = 0;
            int preloaded = 0;

            frameSw.Start();
            foreach (var charGo in alignChar)
            {
                var charSelect = charGo.GetComponent<Skill_CharSelect>();
                if (charSelect == null || charSelect.isLoad)
                    continue;

                PreloadCurrent = preloaded + 1;
                PreloadCurrentName = charSelect.Key;

                sw.Restart();
                _skillAddMethod.Invoke(instance, new object[] { charSelect.Key });
                charSelect.isLoad = true;
                sw.Stop();
                totalMs += sw.ElapsedMilliseconds;
                preloaded++;

                // Yield when the frame budget is exceeded, then reset.
                if (frameSw.ElapsedMilliseconds >= FrameBudgetMs)
                {
                    yield return null;
                    frameSw.Restart();
                }
            }

            if (preloaded > 0)
                Debug.Log($"[GameplayEnhancements] Skill preload complete: {preloaded} characters in {totalMs}ms");

            IsPreloading = false;
        }
    }

    /// <summary>
    /// Wraps CharSelect_CampUI.OpenProfile() in a coroutine that shows a
    /// progress overlay before instantiating the Encyclopedia UI. This covers
    /// the hang from Addressable loading + Collections.Start() + skill preload.
    /// </summary>
    [HarmonyPatch(typeof(CharSelect_CampUI), nameof(CharSelect_CampUI.OpenProfile))]
    internal static class OpenProfileOverlayPatch
    {
        static bool Prefix(CharSelect_CampUI __instance)
        {
            if (!Plugin.CollectionsOptimizationEnabled) return true;
            __instance.StartCoroutine(OpenProfileWithOverlay(__instance));
            return false;
        }

        private static IEnumerator OpenProfileWithOverlay(CharSelect_CampUI instance)
        {
            Traverse.Create(instance).Field("CollectionOn").SetValue(true);

            // Setup the callbacks.
            System.Action deleteAction = () =>
            {
                AccessTools.Method(typeof(CharSelect_CampUI), "SelectUIOn")
                    ?.Invoke(instance, null);
            };
            System.Action hideAction = () =>
            {
                AccessTools.Method(typeof(CharSelect_CampUI), "SelectUIOff")
                    ?.Invoke(instance, null);
            };

            yield return OpenProfileSharedHelper.OpenProfileShared(
                instance.NowSelectedKey, true, deleteAction, hideAction);
        }
    }

    /// <summary>
    /// Wraps CharSelectMainUIV2.OpenProfile() in a coroutine with a progress
    /// overlay. This is the character selection screen path (Difficulty,
    /// Change Mode, etc.) which opens Collections with IsOnce=true.
    /// </summary>
    [HarmonyPatch(typeof(CharSelectMainUIV2), nameof(CharSelectMainUIV2.OpenProfile))]
    internal static class OpenProfileOverlayPatchV2
    {
        static bool Prefix(CharSelectMainUIV2 __instance)
        {
            if (!Plugin.CollectionsOptimizationEnabled) return true;
            __instance.StartCoroutine(OpenProfileWithOverlay(__instance));
            return false;
        }

        private static IEnumerator OpenProfileWithOverlay(CharSelectMainUIV2 instance)
        {
            // Early-out checks matching the original method.
            if (instance.NowSelectNum < 0) yield break;

            var isReturnProp = AccessTools.Property(typeof(CharSelectMainUIV2), "IsReturnToArkWindowOn");
            if (isReturnProp != null && (bool)isReturnProp.GetValue(instance, null))
                yield break;

            if (instance.PassiveOn)
                instance.RemovePassive();

            instance.IsMain = false;

            string charKey = instance.CharDatas != null
                ? instance.CharDatas[instance.NowSelectNum].Key
                : null;

            yield return OpenProfileSharedHelper.OpenProfileShared(
                charKey, true,
                () => instance.SelectUIOn(),
                () => instance.SelectUIOff());
        }
    }

    /// <summary>
    /// Shared coroutine for both OpenProfile paths. Creates or reuses a
    /// cached Collections instance, configures it for the selected character,
    /// and waits behind an overlay until fully loaded.
    /// </summary>
    internal static class OpenProfileSharedHelper
    {
        private const string Tag = "[GameplayEnhancements]";

        /// <summary>
        /// Creates or reuses a cached Collections, opens the given character,
        /// sets IsOnce + DeleteAction, and waits behind an overlay until ready.
        /// </summary>
        internal static IEnumerator OpenProfileShared(
            string charKey, bool isOnce,
            System.Action deleteAction, System.Action hideCallerAction)
        {
            var overlay = ProgressOverlayHelper.Show("Loading character info...");
            yield return null;

            // Hide the caller's UI.
            hideCallerAction?.Invoke();

            Collections collections;
            bool fromCache = CachedCollectionsPatch._cached != null;

            if (fromCache)
            {
                Debug.Log($"{Tag} Reusing cached Collections for OpenProfile");
                CachedCollectionsPatch.ReactivateCachedForProfile();
                collections = Object.FindObjectOfType<Collections>();
            }
            else
            {
                var obj = UIManager.InstantiateActiveAddressable(
                    UIManager.inst.AR_CollectionsUI,
                    AddressableLoadManager.ManageType.Collection);
                collections = obj.GetComponent<Collections>();
            }

            if (collections == null)
            {
                ProgressOverlayHelper.Hide(overlay);
                yield break;
            }

            // Hide Collections behind overlay immediately after creation.
            Canvas collectionsCanvas = collections.GetComponentInParent<Canvas>();
            if (collectionsCanvas == null)
                collectionsCanvas = collections.GetComponent<Canvas>();
            if (collectionsCanvas != null)
                collectionsCanvas.enabled = false;

            // Configure for profile view.
            collections.IsOnce = isOnce;
            collections.DeleteAction = deleteAction;

            // Switch to Character tab and navigate to the selected character.
            if (fromCache)
                collections.SelectCategory(0);
            if (charKey != null && collections.cc != null)
                collections.cc.CharacterInfoOnName(charKey);

            // Yield a frame so the preload coroutine can start.
            yield return null;

            Debug.Log($"{Tag} OpenProfile: IsPreloading={SkillPreloadPatch.IsPreloading}, " +
                      $"fromCache={fromCache}");

            // Wait for skill preload if running.
            while (SkillPreloadPatch.IsPreloading)
            {
                int cur = SkillPreloadPatch.PreloadCurrent;
                int tot = SkillPreloadPatch.PreloadTotal;
                string name = SkillPreloadPatch.PreloadCurrentName ?? "";
                ProgressOverlayHelper.UpdateMessage(overlay,
                    $"Preloading skill data...\n{name} ({cur}/{tot})");
                yield return null;
            }

            Debug.Log($"{Tag} OpenProfile: preload done, settling");

            // Wait for frames to settle.
            ProgressOverlayHelper.UpdateMessage(overlay, "Finalizing...");
            int smoothCount = 0;
            while (smoothCount < 5)
            {
                yield return null;
                if (Time.unscaledDeltaTime < 0.100f)
                    smoothCount++;
                else
                    smoothCount = 0;
            }

            if (collectionsCanvas != null)
                collectionsCanvas.enabled = true;

            ProgressOverlayHelper.Hide(overlay);
        }
    }

    /// <summary>
    /// Reusable progress overlay helper for full-screen loading indicators.
    /// </summary>
    internal static class ProgressOverlayHelper
    {
        /// <summary>
        /// Creates a full-screen dark overlay with a centered message.
        /// </summary>
        internal static GameObject Show(string message)
        {
            var overlayGo = new GameObject("ProgressOverlay");
            Object.DontDestroyOnLoad(overlayGo);

            var canvas = overlayGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10000;
            overlayGo.AddComponent<GraphicRaycaster>();

            // Dark background.
            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(overlayGo.transform, false);
            var bgImage = bgGo.AddComponent<Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.9f);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            // Message text.
            var textGo = new GameObject("Message");
            textGo.transform.SetParent(overlayGo.transform, false);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = message;
            tmp.fontSize = 32;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = new Vector2(0.2f, 0.4f);
            textRt.anchorMax = new Vector2(0.8f, 0.6f);
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            return overlayGo;
        }

        /// <summary>
        /// Updates the overlay message text.
        /// </summary>
        internal static void UpdateMessage(GameObject overlay, string message)
        {
            if (overlay == null) return;
            var tmp = overlay.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null) tmp.text = message;
        }

        /// <summary>
        /// Destroys the overlay.
        /// </summary>
        internal static void Hide(GameObject overlay)
        {
            if (overlay != null)
                Object.Destroy(overlay);
        }
    }
}
