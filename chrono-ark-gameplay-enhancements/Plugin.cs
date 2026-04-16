using ChronoArkMod.Plugin;
using GameplayEnhancements.Patches;
using GameplayEnhancements.PerfDebug;
using HarmonyLib;
using UnityEngine;

namespace GameplayEnhancements
{
    public class Plugin : ChronoArkPlugin
    {
        internal static Plugin Instance;
        internal static Harmony HarmonyInstance;

        private GameObject _perfDebugGo;

        /// <summary>
        /// Applies all Harmony patches when the mod is loaded.
        /// </summary>
        public override void Initialize()
        {
            Instance = this;
            HarmonyInstance = new Harmony("com.steve1316.gameplayenhancements");
            HarmonyInstance.PatchAll(typeof(Plugin).Assembly);
            Debug.Log("[GameplayEnhancements] Patches applied successfully");

            // PerfDebug: persistent frame-time monitor and scene tracker.
            _perfDebugGo = new GameObject("PerfDebug_FrameMonitor");
            Object.DontDestroyOnLoad(_perfDebugGo);
            _perfDebugGo.AddComponent<FrameTimeMonitor>();
            _perfDebugGo.AddComponent<SceneTransitionTracker>();

            // Provide the cached collections overlay flow a coroutine host.
            CachedCollectionsPatch.SetCoroutineHost(
                _perfDebugGo.GetComponent<FrameTimeMonitor>());

            // PerfDebug: all instrumentation is wrapped in try-catch so
            // failures here never prevent the core mod from loading.
            try
            {
                KnownMethodTimingPatches.ApplyAll(HarmonyInstance);
                CollectionInitProbes.ApplyAll(HarmonyInstance);
                // Discovery patching disabled — added 16s to startup and we've
                // already identified all bottlenecks. Type scan kept for reference.
                DiscoveryPatches.ScanAssemblyForInterestingTypes();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GameplayEnhancements][PerfDebug] Init failed: {ex}");
            }
        }

        /// <summary>
        /// Removes all Harmony patches when the mod is unloaded.
        /// </summary>
        public override void Dispose()
        {
            if (_perfDebugGo != null)
                Object.Destroy(_perfDebugGo);

            HarmonyInstance?.UnpatchSelf();
            HarmonyInstance = null;
            Instance = null;
            Debug.Log("[GameplayEnhancements] Patches removed");
        }
    }
}
