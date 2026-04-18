using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GameplayEnhancements.Patches
{
    /// <summary>
    /// Defers initialization of non-visible Collection tabs so only the
    /// default tab (Character, index 0) pays the Start() cost up front.
    /// The overlay flow in CachedCollectionsPatch triggers each deferred
    /// tab explicitly via the public Initialize methods.
    /// </summary>
    internal static class DeferredCollectionsPatch
    {
        private const string Tag = "[GameplayEnhancements]";

        // Bypass flags — when true, let the original Start() run through.
        private static bool _bypassSkill;
        private static bool _bypassItem;
        private static bool _bypassMonster;

        // Track which tabs still need deferred initialization.
        private static SKillCollection _deferredSkill;
        private static ItemCollection _deferredItem;
        private static MonsterCollection _deferredMonster;

        // Original Start() methods cached for deferred invocation.
        private static MethodInfo _skillStart;
        private static MethodInfo _itemStart;
        private static MethodInfo _monsterStart;

        /// <summary>
        /// Returns true if any deferred tabs are still pending.
        /// </summary>
        internal static bool HasPending =>
            _deferredSkill != null || _deferredItem != null || _deferredMonster != null;

        /// <summary>
        /// Skips SKillCollection.Start() unless the bypass flag is set.
        /// </summary>
        [HarmonyPatch(typeof(SKillCollection), "Start")]
        [HarmonyPriority(Priority.First)]
        internal static class DeferSkillStart
        {
            static bool Prefix(SKillCollection __instance)
            {
                if (!Plugin.CollectionsOptimizationEnabled) return true;
                if (_bypassSkill) return true;
                if (IsOnceCollections(__instance)) return true;

                _deferredSkill = __instance;
                Debug.Log($"{Tag} Deferred SKillCollection.Start()");
                return false;
            }
        }

        /// <summary>
        /// Skips ItemCollection.Start() unless the bypass flag is set.
        /// </summary>
        [HarmonyPatch(typeof(ItemCollection), "Start")]
        [HarmonyPriority(Priority.First)]
        internal static class DeferItemStart
        {
            static bool Prefix(ItemCollection __instance)
            {
                if (!Plugin.CollectionsOptimizationEnabled) return true;
                if (_bypassItem) return true;
                if (IsOnceCollections(__instance)) return true;

                _deferredItem = __instance;
                Debug.Log($"{Tag} Deferred ItemCollection.Start()");
                return false;
            }
        }

        /// <summary>
        /// Skips MonsterCollection.Start() unless the bypass flag is set.
        /// </summary>
        [HarmonyPatch(typeof(MonsterCollection), "Start")]
        [HarmonyPriority(Priority.First)]
        internal static class DeferMonsterStart
        {
            static bool Prefix(MonsterCollection __instance)
            {
                if (!Plugin.CollectionsOptimizationEnabled) return true;
                if (_bypassMonster) return true;
                if (IsOnceCollections(__instance)) return true;

                _deferredMonster = __instance;
                Debug.Log($"{Tag} Deferred MonsterCollection.Start()");
                return false;
            }
        }

        /// <summary>
        /// Checks if the parent Collections was opened with IsOnce=true
        /// (character profile view). These should not be deferred.
        /// </summary>
        private static bool IsOnceCollections(MonoBehaviour tabComponent)
        {
            var collections = tabComponent.GetComponentInParent<Collections>();
            return collections != null && collections.IsOnce;
        }

        /// <summary>
        /// Caches the Start() MethodInfo references after Collections
        /// initializes so deferred invocation can call them later.
        /// </summary>
        [HarmonyPatch(typeof(Collections), "Start")]
        internal static class CollectionsStartPostfix
        {
            static void Postfix()
            {
                if (!Plugin.CollectionsOptimizationEnabled) return;
                CacheStartMethods();
            }
        }

        /// <summary>
        /// Before a tab switch, ensures the target tab is initialized.
        /// </summary>
        [HarmonyPatch(typeof(Collections), nameof(Collections.SelectCategory))]
        internal static class SelectCategoryPrefix
        {
            static void Prefix(int __0)
            {
                if (!Plugin.CollectionsOptimizationEnabled) return;
                switch (__0)
                {
                    case 1: InitializeDeferredSkill(); break;
                    case 2: InitializeDeferredItem(); break;
                    case 3: InitializeDeferredMonster(); break;
                }
            }
        }

        /// <summary>
        /// Initializes the deferred SKillCollection tab.
        /// </summary>
        internal static void InitializeDeferredSkill()
        {
            if (_deferredSkill == null) return;
            var instance = _deferredSkill;
            _deferredSkill = null;

            _bypassSkill = true;
            _skillStart.Invoke(instance, null);
            _bypassSkill = false;
        }

        /// <summary>
        /// Initializes the deferred ItemCollection tab.
        /// </summary>
        internal static void InitializeDeferredItem()
        {
            if (_deferredItem == null) return;
            var instance = _deferredItem;
            _deferredItem = null;

            _bypassItem = true;
            _itemStart.Invoke(instance, null);
            _bypassItem = false;
        }

        /// <summary>
        /// Initializes the deferred MonsterCollection tab.
        /// </summary>
        internal static void InitializeDeferredMonster()
        {
            if (_deferredMonster == null) return;
            var instance = _deferredMonster;
            _deferredMonster = null;

            _bypassMonster = true;
            _monsterStart.Invoke(instance, null);
            _bypassMonster = false;
        }

        private static void CacheStartMethods()
        {
            if (_skillStart != null) return;
            _skillStart = AccessTools.Method(typeof(SKillCollection), "Start");
            _itemStart = AccessTools.Method(typeof(ItemCollection), "Start");
            _monsterStart = AccessTools.Method(typeof(MonsterCollection), "Start");
        }
    }
}
