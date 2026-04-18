using HarmonyLib;
using UnityEngine;

namespace GameplayEnhancements.Patches
{
    /// <summary>
    /// Fixes a rare bug where the camp recruit button disappears after
    /// save/load. The base game's AddParty flag can become stale in the
    /// CampSave, persisting as true across save/load even when no character
    /// was actually recruited (PartyAdded=false). After a load the recruit
    /// UI is never open, so the guard is meaningless and must be cleared.
    /// </summary>
    [HarmonyPatch(typeof(FieldSystem), nameof(FieldSystem.CampfireMap))]
    internal static class CampRecruitPatch
    {
        /// <summary>
        /// Clears the stale AddParty UI guard after camp setup when no
        /// recruitment actually happened.
        /// </summary>
        [HarmonyPostfix]
        static void Postfix()
        {
            var camp = StageSystem.instance?.Map?.MainCamp;
            if (camp != null && camp.AddParty && !camp.PartyAdded)
            {
                Debug.Log("[GameplayEnhancements] Clearing stale AddParty flag (PartyAdded=False)");
                camp.AddParty = false;
            }
        }
    }
}
