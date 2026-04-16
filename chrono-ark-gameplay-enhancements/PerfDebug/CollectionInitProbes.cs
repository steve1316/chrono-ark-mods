using System;
using System.Diagnostics;
using System.Reflection;
using GameDataEditor;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace GameplayEnhancements.PerfDebug
{
    /// <summary>
    /// Sub-method instrumentation for CharacterCollection.Init() and
    /// SKillCollection.Init() to identify where time is spent.
    /// Tracks call counts and aggregate time for key operations,
    /// then reports a summary when each Init completes.
    /// </summary>
    internal static class CollectionInitProbes
    {
        private const string Tag = "[GameplayEnhancements][PerfDebug][InitBreakdown]";

        // Aggregate counters reset at Init entry, reported at Init exit.
        private static int _gdeCharCtorCount;
        private static long _gdeCharCtorMs;
        private static int _gdeSkillCtorCount;
        private static long _gdeSkillCtorMs;
        private static int _loadAsyncActionCount;
        private static long _loadAsyncActionMs;
        private static int _loadAsyncCompletionCount;
        private static long _loadAsyncCompletionMs;
        private static int _instantiateCount;
        private static long _instantiateMs;
        private static bool _tracking;
        private static readonly Stopwatch _probeSw = new Stopwatch();

        /// <summary>
        /// Applies all sub-method probes manually with error handling.
        /// </summary>
        internal static void ApplyAll(Harmony harmony)
        {
            var prefix = typeof(CollectionInitProbes);
            int applied = 0;

            applied += TryPatch(harmony, typeof(CharacterCollection), "Init",
                prefix.GetMethod(nameof(CharCollectionInitPrefix), BindingFlags.Static | BindingFlags.NonPublic),
                prefix.GetMethod(nameof(CharCollectionInitPostfix), BindingFlags.Static | BindingFlags.NonPublic));

            applied += TryPatch(harmony, typeof(SKillCollection), "Init",
                prefix.GetMethod(nameof(SkillCollectionInitPrefix), BindingFlags.Static | BindingFlags.NonPublic),
                prefix.GetMethod(nameof(SkillCollectionInitPostfix), BindingFlags.Static | BindingFlags.NonPublic));

            applied += TryPatch(harmony, typeof(GDECharacterData), ".ctor", new[] { typeof(string) },
                prefix.GetMethod(nameof(GDECharCtorPrefix), BindingFlags.Static | BindingFlags.NonPublic),
                prefix.GetMethod(nameof(GDECharCtorPostfix), BindingFlags.Static | BindingFlags.NonPublic));

            applied += TryPatch(harmony, typeof(GDESkillData), ".ctor", new[] { typeof(string) },
                prefix.GetMethod(nameof(GDESkillCtorPrefix), BindingFlags.Static | BindingFlags.NonPublic),
                prefix.GetMethod(nameof(GDESkillCtorPostfix), BindingFlags.Static | BindingFlags.NonPublic));

            // LoadAsyncAction(String, ManageType, Image) — used by CharacterCollection.
            applied += TryPatch(harmony,
                typeof(AddressableLoadManager), "LoadAsyncAction",
                new[] { typeof(string), typeof(AddressableLoadManager.ManageType), typeof(Image) },
                prefix.GetMethod(nameof(LoadAsyncActionPrefix), BindingFlags.Static | BindingFlags.NonPublic),
                prefix.GetMethod(nameof(LoadAsyncActionPostfix), BindingFlags.Static | BindingFlags.NonPublic));

            // LoadAsyncCompletion<Sprite> patch removed — patching this generic
            // method on Mono breaks the JIT for other instantiations and causes
            // crashes when static constructors trigger Addressable loads.

            // SKillCollection sub-methods.
            applied += TryPatch(harmony, typeof(SKillCollection), "Init_CharSelect",
                prefix.GetMethod(nameof(SubMethodPrefix), BindingFlags.Static | BindingFlags.NonPublic),
                prefix.GetMethod(nameof(SubMethodPostfix), BindingFlags.Static | BindingFlags.NonPublic));

            applied += TryPatch(harmony, typeof(SKillCollection), "ChangeCharacterKey",
                prefix.GetMethod(nameof(SubMethodPrefix), BindingFlags.Static | BindingFlags.NonPublic),
                prefix.GetMethod(nameof(SubMethodPostfix), BindingFlags.Static | BindingFlags.NonPublic));

            applied += TryPatch(harmony, typeof(SKillCollection), "PageNumInit",
                prefix.GetMethod(nameof(SubMethodPrefix), BindingFlags.Static | BindingFlags.NonPublic),
                prefix.GetMethod(nameof(SubMethodPostfix), BindingFlags.Static | BindingFlags.NonPublic));

            Debug.Log($"{Tag} Applied {applied} collection init probes");
        }

        // --- Init entry/exit ---

        private static void CharCollectionInitPrefix()
        {
            ResetCounters();
            _tracking = true;
        }

        private static void CharCollectionInitPostfix()
        {
            _tracking = false;
            Debug.Log($"{Tag} CharacterCollection.Init breakdown:" +
                      $"\n  GDECharacterData ctor: {_gdeCharCtorCount}x, {_gdeCharCtorMs}ms" +
                      $"\n  Instantiate (prefab): {_instantiateCount}x, {_instantiateMs}ms" +
                      $"\n  LoadAsyncAction: {_loadAsyncActionCount}x, {_loadAsyncActionMs}ms" +
                      $"\n  LoadAsyncCompletion: {_loadAsyncCompletionCount}x, {_loadAsyncCompletionMs}ms");
        }

        private static void SkillCollectionInitPrefix()
        {
            ResetCounters();
            _tracking = true;
        }

        private static void SkillCollectionInitPostfix()
        {
            _tracking = false;
            Debug.Log($"{Tag} SKillCollection.Init breakdown:" +
                      $"\n  GDECharacterData ctor: {_gdeCharCtorCount}x, {_gdeCharCtorMs}ms" +
                      $"\n  GDESkillData ctor: {_gdeSkillCtorCount}x, {_gdeSkillCtorMs}ms" +
                      $"\n  LoadAsyncCompletion: {_loadAsyncCompletionCount}x, {_loadAsyncCompletionMs}ms" +
                      $"\n  LoadAsyncAction: {_loadAsyncActionCount}x, {_loadAsyncActionMs}ms");
        }

        // --- Sub-operation probes ---

        private static void GDECharCtorPrefix()
        {
            if (_tracking) _probeSw.Restart();
        }

        private static void GDECharCtorPostfix()
        {
            if (!_tracking) return;
            _probeSw.Stop();
            _gdeCharCtorCount++;
            _gdeCharCtorMs += _probeSw.ElapsedMilliseconds;
        }

        private static void GDESkillCtorPrefix()
        {
            if (_tracking) _probeSw.Restart();
        }

        private static void GDESkillCtorPostfix()
        {
            if (!_tracking) return;
            _probeSw.Stop();
            _gdeSkillCtorCount++;
            _gdeSkillCtorMs += _probeSw.ElapsedMilliseconds;
        }

        private static void LoadAsyncActionPrefix()
        {
            if (_tracking) _probeSw.Restart();
        }

        private static void LoadAsyncActionPostfix()
        {
            if (!_tracking) return;
            _probeSw.Stop();
            _loadAsyncActionCount++;
            _loadAsyncActionMs += _probeSw.ElapsedMilliseconds;
        }

        private static void LoadAsyncCompletionPrefix()
        {
            if (_tracking) _probeSw.Restart();
        }

        private static void LoadAsyncCompletionPostfix()
        {
            if (!_tracking) return;
            _probeSw.Stop();
            _loadAsyncCompletionCount++;
            _loadAsyncCompletionMs += _probeSw.ElapsedMilliseconds;
        }

        // --- Generic sub-method timing (Init_CharSelect, ChangeCharacterKey, etc.) ---

        private static readonly Stopwatch _subMethodSw = new Stopwatch();

        private static void SubMethodPrefix(MethodBase __originalMethod)
        {
            _subMethodSw.Restart();
            FrameTimeMonitor.RecordMethodEntry(
                $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}");
        }

        private static void SubMethodPostfix(MethodBase __originalMethod)
        {
            _subMethodSw.Stop();
            if (_subMethodSw.ElapsedMilliseconds > 1)
            {
                Debug.Log($"{Tag} {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}: " +
                          $"{_subMethodSw.ElapsedMilliseconds}ms");
            }
        }

        // --- Helpers ---

        private static void ResetCounters()
        {
            _gdeCharCtorCount = 0;
            _gdeCharCtorMs = 0;
            _gdeSkillCtorCount = 0;
            _gdeSkillCtorMs = 0;
            _loadAsyncActionCount = 0;
            _loadAsyncActionMs = 0;
            _loadAsyncCompletionCount = 0;
            _loadAsyncCompletionMs = 0;
            _instantiateCount = 0;
            _instantiateMs = 0;
        }

        private static int TryPatch(Harmony harmony, Type type, string methodName,
            MethodInfo prefix, MethodInfo postfix)
        {
            try
            {
                var original = AccessTools.Method(type, methodName);
                if (original == null)
                {
                    Debug.LogWarning($"{Tag} Method not found: {type.Name}.{methodName}");
                    return 0;
                }
                harmony.Patch(original,
                    prefix: prefix != null ? new HarmonyMethod(prefix) : null,
                    postfix: postfix != null ? new HarmonyMethod(postfix) : null);
                return 1;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{Tag} Failed to patch {type.Name}.{methodName}: {ex.Message}");
                return 0;
            }
        }

        private static int TryPatch(Harmony harmony, Type type, string methodName,
            Type[] paramTypes, MethodInfo prefix, MethodInfo postfix)
        {
            try
            {
                MethodBase original = methodName == ".ctor"
                    ? (MethodBase)AccessTools.Constructor(type, paramTypes)
                    : AccessTools.Method(type, methodName, paramTypes);
                if (original == null)
                {
                    Debug.LogWarning($"{Tag} Method not found: {type.Name}.{methodName}");
                    return 0;
                }
                harmony.Patch(original,
                    prefix: prefix != null ? new HarmonyMethod(prefix) : null,
                    postfix: postfix != null ? new HarmonyMethod(postfix) : null);
                return 1;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{Tag} Failed to patch {type.Name}.{methodName}: {ex.Message}");
                return 0;
            }
        }
    }
}
