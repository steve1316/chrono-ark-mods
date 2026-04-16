using System;
using GameDataEditor;
using HarmonyLib;
using UnityEngine;

namespace GameplayEnhancements.Patches
{
    /// <summary>
    /// Prevents skill tooltips from getting permanently stuck on screen when a
    /// mod skill's data causes an exception during tooltip rendering. The game
    /// instantiates the tooltip GameObject before populating it via Input(),
    /// and only assigns it to the static ToolTip field afterward. If Input()
    /// throws, the GameObject is orphaned and ToolTipDestroy() can never find
    /// it because ToolTip is still null.
    /// </summary>
    [HarmonyPatch(typeof(ToolTipWindow))]
    internal static class SkillTooltipPatch
    {
        private const string Tag = "[GameplayEnhancements]";

        /// <summary>
        /// Catches exceptions from SkillToolTip, destroys the orphaned tooltip
        /// GameObject, and suppresses the exception so the game continues.
        /// </summary>
        [HarmonyPatch(nameof(ToolTipWindow.SkillToolTip))]
        [HarmonyFinalizer]
        static Exception SkillToolTipFinalizer(
            Exception __exception, ref GameObject __result)
        {
            if (__exception == null)
                return null;

            Debug.LogWarning(
                $"{Tag} SkillToolTip threw (tooltip cleaned up): {__exception}");
            DestroyOrphanedTooltip();
            __result = null;
            return null;
        }

        /// <summary>
        /// Catches exceptions from SkillToolTip_Collection with the same
        /// safety net.
        /// </summary>
        [HarmonyPatch(nameof(ToolTipWindow.SkillToolTip_Collection))]
        [HarmonyFinalizer]
        static Exception SkillToolTipCollectionFinalizer(
            Exception __exception, ref GameObject __result)
        {
            if (__exception == null)
                return null;

            Debug.LogWarning(
                $"{Tag} SkillToolTip_Collection threw (tooltip cleaned up): " +
                __exception);
            DestroyOrphanedTooltip();
            __result = null;
            return null;
        }

        /// <summary>
        /// Finds and destroys any SkillToolTip component left in the scene
        /// that was never assigned to the static ToolTip field. Safe to call
        /// because ToolTipDestroy() already ran at the start of the method,
        /// so any SkillToolTip in the scene is the orphan from the failed call.
        /// </summary>
        private static void DestroyOrphanedTooltip()
        {
            ToolTipWindow.ToolTipDestroy();

            var orphan = UnityEngine.Object.FindObjectOfType<SkillToolTip>();
            if (orphan != null)
            {
                UnityEngine.Object.Destroy(orphan.gameObject);
                Debug.Log($"{Tag} Destroyed orphaned skill tooltip.");
            }
        }
    }

    /// <summary>
    /// Catches MissingFieldException/MissingMethodException from Buff.DataToBuff
    /// when a mod buff subclass references fields that were changed in a game
    /// update (e.g. Jefuty's B_Jefuty_R1 sets Stat.PlusMPUse as an int, but
    /// the field is now a PlusMP class). Creates a fallback plain Buff so the
    /// tooltip still renders.
    /// </summary>
    [HarmonyPatch(typeof(Buff), nameof(Buff.DataToBuff))]
    internal static class BuffDataToBuffPatch
    {
        private const string Tag = "[GameplayEnhancements]";

        /// <summary>
        /// When DataToBuff throws because a mod subclass can't JIT-compile,
        /// creates a plain Buff with the same data fields so callers still
        /// get a valid object.
        /// </summary>
        [HarmonyFinalizer]
        static Exception Finalizer(
            Exception __exception,
            ref Buff __result,
            GDEBuffData _BuffData,
            BattleChar Char,
            BattleChar Use,
            int LifeTime,
            bool view)
        {
            if (__exception == null)
                return null;

            if (!(__exception is MissingFieldException)
                && !(__exception is MissingMethodException))
                return __exception;

            Debug.LogWarning(
                $"{Tag} Buff.DataToBuff failed for " +
                $"'{_BuffData?.Key ?? "?"}': {__exception.Message} — " +
                "using fallback Buff");

            // Build a plain Buff (not the mod subclass) with the same data.
            var buff = new Buff();
            buff.BuffData = _BuffData;

            if (Char != null)
            {
                buff.BChar = Char;
                buff.MyChar = Char.Info;
            }
            else
            {
                buff.BChar = Use;
                buff.MyChar = Use.Info;
            }

            var stack = new StackBuff();
            stack.UseState = Use;

            if (_BuffData.LifeTime != 0f)
            {
                stack.RemainTime = LifeTime != -1
                    ? LifeTime
                    : (int)_BuffData.LifeTime;
                buff.LifeTime = (int)_BuffData.LifeTime;
            }
            else
            {
                buff.TimeUseless = true;
            }

            buff.SkillCautionView = _BuffData.UseSkillDebuff;
            if (_BuffData.Barrier >= 1)
                buff.BarrierHP = _BuffData.Barrier;

            buff.StackInfo.Add(stack);
            buff.CantDisable = _BuffData.Cantdisable;
            buff.View = view;

            // Plain Buff.Init() is safe — no mod field references.
            try { buff.Init(); }
            catch (Exception ex)
            {
                Debug.LogWarning($"{Tag} Fallback Buff.Init() also failed: {ex.Message}");
            }

            __result = buff;
            return null;
        }
    }
}
