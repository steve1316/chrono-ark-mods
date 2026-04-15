using ChronoArkMod;
using ChronoArkMod.ModData.Settings;
using HarmonyLib;
using UnityEngine;

namespace GameplayEnhancements.Patches
{
    /// <summary>
    /// Overrides the default starting Mana, Gold, and Soulstones for new runs
    /// based on configurable mod settings.
    /// </summary>
    [HarmonyPatch(typeof(PlayData), nameof(PlayData.init))]
    internal static class StartingStatsPatch
    {
        static void Postfix()
        {
            // Only apply to fresh new runs, not when loading an existing save.
            // After init(), a new run has StageNum=0 and GameStarted=false.
            if (PlayData.TSavedata.StageNum != 0 || PlayData.GameStarted)
                return;

            var modInfo = ModManager.getModInfo("GameplayEnhancements");
            if (modInfo == null) return;

            var manaSetting = modInfo.GetSetting<SliderSetting>("StartingMana");
            if (manaSetting != null)
                PlayData.AP = (int)manaSetting.Value;

            var goldSetting = modInfo.GetSetting<SliderSetting>("StartingGold");
            if (goldSetting != null)
                PlayData.Gold = (int)goldSetting.Value;

            var soulSetting = modInfo.GetSetting<SliderSetting>("StartingSoulstones");
            if (soulSetting != null)
                PlayData.Soul = (int)soulSetting.Value;

            Debug.Log($"[GameplayEnhancements] Starting stats: AP={PlayData.AP}, Gold={PlayData.Gold}, Soul={PlayData.Soul}");
        }
    }
}
