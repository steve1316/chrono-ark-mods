using System.Collections.Generic;
using System.Diagnostics;
using ChronoArkMod.ModData;
using HarmonyLib;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace WorkshopOverhaul.Patches
{
    /// <summary>
    /// Replaces ModInfo.Load() to defer audio initialization when triggered from
    /// our Apply flow. Assembly and GDE loading run normally, but audioinfo.init()
    /// is queued for ApplyProgressPatch to spread across frames. Outside the Apply
    /// flow (e.g. game startup), audio loads normally to avoid a stale queue.
    /// </summary>
    [HarmonyPatch(typeof(ModInfo), nameof(ModInfo.Load))]
    internal static class DeferredAudioLoadPatch
    {
        /// <summary>
        /// Set by ApplyProgressPatch to enable deferral only during Apply.
        /// </summary>
        internal static bool IsApplyActive;

        /// <summary>
        /// Mods whose audio init was deferred during the current apply cycle.
        /// Cleared by ApplyProgressPatch after processing.
        /// </summary>
        internal static readonly List<ModInfo> PendingAudioInit = new List<ModInfo>();

        static bool Prefix(ModInfo __instance)
        {
            var sw = Stopwatch.StartNew();

            bool needsAssemblyLoad = __instance.assemblyInfo.Assemblies.Count == 0;
            if (needsAssemblyLoad)
            {
                __instance.LoadAtVeryBegining();
            }
            sw.Stop();
            long assemblyMs = sw.ElapsedMilliseconds;

            sw.Restart();
            __instance.LoadGDE();
            sw.Stop();
            long gdeMs = sw.ElapsedMilliseconds;

            if (IsApplyActive)
            {
                PendingAudioInit.Add(__instance);
                Debug.Log($"[WorkshopOverhaul]   Load breakdown '{__instance.id}': " +
                          $"assembly={assemblyMs}ms, gde={gdeMs}ms, audio=deferred");
            }
            else
            {
                sw.Restart();
                __instance.audioinfo.init();
                sw.Stop();
                Debug.Log($"[WorkshopOverhaul]   Load breakdown '{__instance.id}': " +
                          $"assembly={assemblyMs}ms, gde={gdeMs}ms, audio={sw.ElapsedMilliseconds}ms");
            }

            return false; // Skip original method.
        }
    }
}
