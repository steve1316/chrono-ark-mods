using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GameplayEnhancements.Patches
{
    /// <summary>
    /// Pre-loads skill data for all characters in the background when the
    /// Encyclopedia's Skill tab initializes. Each character's SkillAdd runs
    /// on a separate frame so the UI stays responsive. By the time the user
    /// clicks a modded character, its skills are already instantiated.
    /// </summary>
    [HarmonyPatch(typeof(SKillCollection), "Start")]
    internal static class SkillPreloadPatch
    {
        private static MethodInfo _skillAddMethod;

        static void Postfix(SKillCollection __instance)
        {
            _skillAddMethod = AccessTools.Method(typeof(SKillCollection), "SkillAdd");
            if (_skillAddMethod == null)
            {
                Debug.LogWarning("[WorkshopOverhaul] Could not find SKillCollection.SkillAdd method");
                return;
            }

            // Start on Collections.Main (the parent UI) which stays active,
            // because SKillCollection deactivates itself at the end of Start().
            var collections = __instance.GetComponentInParent<Collections>();
            if (collections != null)
                collections.StartCoroutine(PreloadAllCharacters(__instance));
            else
                Debug.LogWarning("[WorkshopOverhaul] Could not find Collections parent for preload coroutine");
        }

        /// <summary>
        /// Iterates all characters and calls SkillAdd for each unloaded one,
        /// yielding a frame between each to keep the UI responsive.
        /// </summary>
        private static IEnumerator PreloadAllCharacters(SKillCollection instance)
        {
            // Wait a frame for Init/Start to fully complete.
            yield return null;

            var alignChar = Traverse.Create(instance).Field("Align_Char")
                .GetValue<List<GameObject>>();
            if (alignChar == null) yield break;

            var sw = new Stopwatch();
            int preloaded = 0;

            foreach (var charGo in alignChar)
            {
                var charSelect = charGo.GetComponent<Skill_CharSelect>();
                if (charSelect == null || charSelect.isLoad)
                    continue;

                sw.Restart();
                _skillAddMethod.Invoke(instance, new object[] { charSelect.Key });
                charSelect.isLoad = true;
                sw.Stop();

                if (sw.ElapsedMilliseconds > 5)
                    Debug.Log($"[WorkshopOverhaul] Preloaded skills for '{charSelect.Key}': {sw.ElapsedMilliseconds}ms");

                preloaded++;
                yield return null;
            }

            if (preloaded > 0)
                Debug.Log($"[WorkshopOverhaul] Skill preload complete: {preloaded} characters");
        }
    }
}
