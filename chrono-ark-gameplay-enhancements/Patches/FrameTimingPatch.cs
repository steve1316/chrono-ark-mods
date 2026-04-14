using System.Diagnostics;
using HarmonyLib;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GameplayEnhancements.Patches
{
    /// <summary>
    /// Logs timing for known slow UI operations: Encyclopedia open,
    /// character View Info, and Skill tab character selection.
    /// </summary>
    [HarmonyPatch(typeof(CharacterCollection), nameof(CharacterCollection.CharacterInfoOn))]
    internal static class ViewInfoTimingPatch
    {
        static void Prefix(int n)
        {
            TimingHelper.Start("CharacterInfoOn");
        }

        static void Postfix(int n)
        {
            TimingHelper.Stop("CharacterInfoOn", $"character index={n}");
        }
    }

    [HarmonyPatch(typeof(SKillCollection), nameof(SKillCollection.ChangeCharacterKey))]
    internal static class SkillTabTimingPatch
    {
        static void Prefix(string Name)
        {
            TimingHelper.Start("ChangeCharacterKey");
        }

        static void Postfix(string Name)
        {
            TimingHelper.Stop("ChangeCharacterKey", $"char='{Name}'");
        }
    }

    [HarmonyPatch(typeof(Collections), "Start")]
    internal static class EncyclopediaStartTimingPatch
    {
        static void Prefix()
        {
            TimingHelper.Start("Collections.Start");
        }

        static void Postfix()
        {
            TimingHelper.Stop("Collections.Start");
        }
    }

    [HarmonyPatch(typeof(Collections), nameof(Collections.Init))]
    internal static class EncyclopediaInitTimingPatch
    {
        static void Prefix()
        {
            TimingHelper.Start("Collections.Init");
        }

        static void Postfix()
        {
            TimingHelper.Stop("Collections.Init");
        }
    }

    /// <summary>
    /// Simple timing helper to avoid repeating Stopwatch boilerplate.
    /// </summary>
    internal static class TimingHelper
    {
        private static readonly Stopwatch Sw = new Stopwatch();
        private static string _currentLabel;

        internal static void Start(string label)
        {
            _currentLabel = label;
            Sw.Restart();
        }

        internal static void Stop(string label, string detail = null)
        {
            Sw.Stop();
            string msg = $"[WorkshopOverhaul] {label}: {Sw.ElapsedMilliseconds}ms";
            if (detail != null)
                msg += $" ({detail})";
            Debug.Log(msg);
        }
    }
}
