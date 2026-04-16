using System;
using System.Diagnostics;
using System.Reflection;
using ChronoArkMod;
using ChronoArkMod.ModData;
using GameDataEditor;
using HarmonyLib;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GameplayEnhancements.PerfDebug
{
    /// <summary>
    /// Targeted Stopwatch-based timing patches on known game methods.
    /// Applied manually (not via PatchAll) so each patch can fail
    /// independently without crashing the entire mod.
    /// </summary>
    internal static class KnownMethodTimingPatches
    {
        private const string Tag = "[GameplayEnhancements][PerfDebug][Timing]";

        // Shared stopwatch per patch — safe because Unity is single-threaded.
        private static readonly Stopwatch[] _stopwatches = new Stopwatch[8];

        /// <summary>
        /// Applies all known-method timing patches individually with error handling.
        /// </summary>
        internal static void ApplyAll(Harmony harmony)
        {
            var targets = new[]
            {
                new PatchTarget("PlayData", "init", typeof(PlayData), nameof(PlayData.init), 0),
                new PatchTarget("PlayData", "DataBaseInit", typeof(PlayData), nameof(PlayData.DataBaseInit), 1),
                new PatchTarget("GDEDataManager", "BuildDataKeysBySchemaList", typeof(GDEDataManager), nameof(GDEDataManager.BuildDataKeysBySchemaList), 2),
                new PatchTarget("Collections", "Start", typeof(Collections), "Start", 3),
                new PatchTarget("SKillCollection", "Start", typeof(SKillCollection), "Start", 4),
                new PatchTarget("UIManager", "InstantiateActiveAddressable", typeof(UIManager), nameof(UIManager.InstantiateActiveAddressable), 5),
                new PatchTarget("ModManager", "LoadMod", typeof(ModManager), nameof(ModManager.LoadMod), 6),
                new PatchTarget("ModManager", "MakeSaveType", typeof(ModManager), nameof(ModManager.MakeSaveType), 7),
            };

            for (int i = 0; i < _stopwatches.Length; i++)
                _stopwatches[i] = new Stopwatch();

            var prefixMethod = typeof(KnownMethodTimingPatches).GetMethod(
                nameof(TimingPrefix), BindingFlags.Static | BindingFlags.NonPublic);
            var postfixMethod = typeof(KnownMethodTimingPatches).GetMethod(
                nameof(TimingPostfix), BindingFlags.Static | BindingFlags.NonPublic);

            int applied = 0;
            foreach (var target in targets)
            {
                try
                {
                    var original = AccessTools.Method(target.Type, target.MethodName);
                    if (original == null)
                    {
                        Debug.LogWarning($"{Tag} Method not found: {target.DisplayName}");
                        continue;
                    }

                    // Store the index and display name in __state via prefix return.
                    harmony.Patch(original,
                        prefix: new HarmonyMethod(prefixMethod),
                        postfix: new HarmonyMethod(postfixMethod));
                    applied++;
                    Debug.Log($"{Tag} Patched {target.DisplayName}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"{Tag} Failed to patch {target.DisplayName}: {ex.Message}");
                }
            }

            Debug.Log($"{Tag} Applied {applied}/{targets.Length} known method patches");
        }

        /// <summary>
        /// Generic prefix that records the method entry and starts timing.
        /// </summary>
        private static void TimingPrefix(MethodBase __originalMethod)
        {
            string name = $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}";
            FrameTimeMonitor.RecordMethodEntry(name);

            int idx = GetStopwatchIndex(__originalMethod);
            if (idx >= 0)
                _stopwatches[idx].Restart();
        }

        /// <summary>
        /// Generic postfix that logs elapsed time if above 1ms.
        /// </summary>
        private static void TimingPostfix(MethodBase __originalMethod)
        {
            int idx = GetStopwatchIndex(__originalMethod);
            if (idx < 0) return;

            _stopwatches[idx].Stop();
            long ms = _stopwatches[idx].ElapsedMilliseconds;
            if (ms > 1)
            {
                string name = $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}";
                Debug.Log($"{Tag} {name}: {ms}ms");
            }
        }

        /// <summary>
        /// Maps a method to its stopwatch index by name. Not performance-critical
        /// since these patches only fire on specific known methods.
        /// </summary>
        private static int GetStopwatchIndex(MethodBase method)
        {
            string key = $"{method.DeclaringType?.Name}.{method.Name}";
            switch (key)
            {
                case "PlayData.init": return 0;
                case "PlayData.DataBaseInit": return 1;
                case "GDEDataManager.BuildDataKeysBySchemaList": return 2;
                case "Collections.Start": return 3;
                case "SKillCollection.Start": return 4;
                case "UIManager.InstantiateActiveAddressable": return 5;
                case "ModManager.LoadMod": return 6;
                case "ModManager.MakeSaveType": return 7;
                default: return -1;
            }
        }

        /// <summary>
        /// Holds metadata for a single patch target.
        /// </summary>
        private struct PatchTarget
        {
            internal readonly string ClassName;
            internal readonly string MethodName;
            internal readonly Type Type;
            internal readonly string DisplayName;
            internal readonly int Index;

            internal PatchTarget(string className, string methodName, Type type, string resolvedName, int index)
            {
                ClassName = className;
                MethodName = resolvedName;
                Type = type;
                DisplayName = $"{className}.{methodName}";
                Index = index;
            }
        }
    }
}
