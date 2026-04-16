using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GameplayEnhancements.PerfDebug
{
    /// <summary>
    /// Discovery-oriented instrumentation for finding unknown performance
    /// bottlenecks. Scans Assembly-CSharp for interesting types and patches
    /// all MonoBehaviour lifecycle methods with timing.
    /// </summary>
    internal static class DiscoveryPatches
    {
        private const string Tag = "[GameplayEnhancements][PerfDebug]";

        // Per-method stopwatch stored via method token to avoid collisions.
        private static readonly Dictionary<int, Stopwatch> _stopwatches = new Dictionary<int, Stopwatch>();

        /// <summary>
        /// Scans Assembly-CSharp for types whose names match keywords related
        /// to the UI screens and systems we want to identify.
        /// </summary>
        internal static void ScanAssemblyForInterestingTypes()
        {
            var asm = typeof(PlayData).Assembly;
            var keywords = new[]
            {
                "Tab", "Collection", "Encyclo", "Camp", "CharInfo",
                "Monster", "Bestiary", "Profile", "Status", "Skill",
                "Item", "Save", "Load", "Inventory", "Menu", "Battle",
                "Party", "Record", "Info"
            };

            var matches = new List<string>();
            foreach (var type in asm.GetTypes())
            {
                foreach (var keyword in keywords)
                {
                    if (type.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // Check if it's a MonoBehaviour to flag UI-relevant types.
                        bool isMono = typeof(MonoBehaviour).IsAssignableFrom(type);
                        matches.Add($"{type.FullName}{(isMono ? " [MonoBehaviour]" : "")}");
                        break;
                    }
                }
            }

            matches.Sort();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{Tag}[Discovery] Types matching keywords ({matches.Count}):");
            foreach (var m in matches)
                sb.AppendLine($"  {m}");
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Patches Start(), Awake(), and OnEnable() on all MonoBehaviour subclasses
        /// in Assembly-CSharp with timing instrumentation. Only logs calls > 5ms.
        /// </summary>
        internal static void PatchAllMonoBehaviourLifecycles(Harmony harmony)
        {
            var sw = Stopwatch.StartNew();
            var asm = typeof(PlayData).Assembly;
            int patchedMethods = 0;
            int skippedTypes = 0;
            var lifecycleMethods = new[] { "Start", "Awake", "OnEnable" };

            var prefixMethod = new HarmonyMethod(
                typeof(DiscoveryPatches).GetMethod(nameof(LifecyclePrefix),
                    BindingFlags.Static | BindingFlags.NonPublic));
            var postfixMethod = new HarmonyMethod(
                typeof(DiscoveryPatches).GetMethod(nameof(LifecyclePostfix),
                    BindingFlags.Static | BindingFlags.NonPublic));

            foreach (var type in asm.GetTypes())
            {
                if (!typeof(MonoBehaviour).IsAssignableFrom(type) || type.IsAbstract)
                    continue;

                foreach (var methodName in lifecycleMethods)
                {
                    try
                    {
                        var method = AccessTools.Method(type, methodName);
                        if (method == null || method.DeclaringType != type)
                            continue;

                        harmony.Patch(method, prefix: prefixMethod, postfix: postfixMethod);
                        patchedMethods++;
                    }
                    catch (Exception ex)
                    {
                        skippedTypes++;
                        Debug.LogWarning($"{Tag}[Discovery] Failed to patch {type.Name}.{methodName}: {ex.Message}");
                    }
                }
            }

            sw.Stop();
            Debug.Log($"{Tag}[Discovery] Broad lifecycle patching complete: " +
                      $"{patchedMethods} methods patched, {skippedTypes} skipped, " +
                      $"took {sw.ElapsedMilliseconds}ms");
        }

        /// <summary>
        /// Generic prefix for all broad lifecycle patches. Records the method
        /// entry and starts a Stopwatch keyed by the method's metadata token.
        /// </summary>
        private static void LifecyclePrefix(MethodBase __originalMethod)
        {
            string name = $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}";
            FrameTimeMonitor.RecordMethodEntry(name);

            int token = __originalMethod.MetadataToken;
            if (!_stopwatches.TryGetValue(token, out var methodSw))
            {
                methodSw = new Stopwatch();
                _stopwatches[token] = methodSw;
            }
            methodSw.Restart();
        }

        /// <summary>
        /// Generic postfix for all broad lifecycle patches. Logs if > 5ms.
        /// </summary>
        private static void LifecyclePostfix(MethodBase __originalMethod)
        {
            int token = __originalMethod.MetadataToken;
            if (!_stopwatches.TryGetValue(token, out var methodSw))
                return;

            methodSw.Stop();
            if (methodSw.ElapsedMilliseconds > 5)
            {
                string name = $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}";
                Debug.Log($"{Tag}[Lifecycle] {name}: {methodSw.ElapsedMilliseconds}ms");
            }
        }
    }
}
