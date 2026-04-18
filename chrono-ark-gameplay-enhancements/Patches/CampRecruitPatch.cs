using GameDataEditor;
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
    internal static class CampRecruitPatch
    {
        private const string Tag = "[GameplayEnhancements][CampRecruit]";

        /// <summary>
        /// Tracks whether we already logged the Update-frame decision this
        /// session to avoid per-frame spam.
        /// </summary>
        private static bool _loggedUpdateDecision;

        /// <summary>
        /// Snapshot of AddParty from the previous Update tick so we only log
        /// on transitions.
        /// </summary>
        private static bool? _prevAddParty;

        // ==================================================================
        //  FieldSystem.CampfireMap — camp scene setup (fresh entry & load)
        // ==================================================================

        /// <summary>
        /// Resets the stale AddParty flag after a save/load when no character
        /// was actually recruited, then logs the full CampSave state.
        /// </summary>
        [HarmonyPatch(typeof(FieldSystem), nameof(FieldSystem.CampfireMap))]
        internal static class CampfireMapPatch
        {
            [HarmonyPostfix]
            static void Postfix()
            {
                _loggedUpdateDecision = false;
                _prevAddParty = null;

                var camp = StageSystem.instance?.Map?.MainCamp;
                bool isLoaded = PlayData.TSavedata.IsLoaded;
                bool hasCampSave = PlayData.TSavedata.Data_StageMapData?.CampSaveData != null;

                // Fix: clear the stale AddParty UI guard on load when no
                // recruitment actually happened. The recruit screen is never
                // open after a load so this guard is meaningless.
                if (camp != null && camp.AddParty && !camp.PartyAdded)
                {
                    Debug.Log($"{Tag} FIX: Clearing stale AddParty flag (was True, PartyAdded=False)");
                    camp.AddParty = false;
                }

                Debug.Log($"{Tag} === CampfireMap finished ===");
                Debug.Log($"{Tag}   IsLoaded={isLoaded}, HasCampSaveData={hasCampSave}");
                Debug.Log($"{Tag}   StageNum={PlayData.TSavedata.StageNum}");
                Debug.Log($"{Tag}   Party.Count={PlayData.TSavedata.Party?.Count}");

                if (camp != null)
                {
                    Debug.Log($"{Tag}   Camp.AddParty={camp.AddParty}");
                    Debug.Log($"{Tag}   Camp.PartyAdded={camp.PartyAdded}");
                    Debug.Log($"{Tag}   Camp.CampEnd={camp.CampEnd}");
                    Debug.Log($"{Tag}   Camp.EnableCamp={camp.EnableCamp}");
                    Debug.Log($"{Tag}   Camp.CasualPartyAdd={camp.CasualPartyAdd}");
                    Debug.Log($"{Tag}   Camp.NewParty={camp.NewParty?.Key ?? "(null)"}");
                }
                else
                {
                    Debug.Log($"{Tag}   Camp instance is NULL");
                }

                if (hasCampSave)
                {
                    var cs = PlayData.TSavedata.Data_StageMapData.CampSaveData;
                    Debug.Log($"{Tag}   CampSaveData.AddParty={cs.AddParty}");
                    Debug.Log($"{Tag}   CampSaveData.PartyAdded={cs.PartyAdded}");
                    Debug.Log($"{Tag}   CampSaveData.CampEnd={cs.CampEnd}");
                    Debug.Log($"{Tag}   CampSaveData.CasualPartyAdd={cs.CasualPartyAdd}");
                }
            }
        }

        // ==================================================================
        //  Camp.CampEnable — player interacts with the campfire object
        // ==================================================================

        /// <summary>
        /// Logs which branch CampEnable takes and the full CampSave state.
        /// </summary>
        [HarmonyPatch(typeof(Camp), nameof(Camp.CampEnable))]
        internal static class CampEnablePatch
        {
            [HarmonyPrefix]
            static void Prefix(Camp __instance)
            {
                Debug.Log($"{Tag} === CampEnable called ===");
                Debug.Log($"{Tag}   CampEnd={__instance.CampEnd} -> path={(__instance.CampEnd ? "AfterEnable" : "_CampEnable")}");
                Debug.Log($"{Tag}   AddParty={__instance.AddParty}");
                Debug.Log($"{Tag}   PartyAdded={__instance.PartyAdded}");
                Debug.Log($"{Tag}   CasualPartyAdd={__instance.CasualPartyAdd}");
                Debug.Log($"{Tag}   NewParty={__instance.NewParty?.Key ?? "(null)"}");
                Debug.Log($"{Tag}   Party.Count={PlayData.TSavedata.Party?.Count}");
                Debug.Log($"{Tag}   StageNum={PlayData.TSavedata.StageNum}");

                var spRule = PlayData.TSavedata.SpRule;
                bool cantRecruit = spRule != null && spRule.RuleChange.CantNewPartymember;
                Debug.Log($"{Tag}   SpRule={spRule?.Key ?? "(null)"}, CantNewPartymember={cantRecruit}");
                Debug.Log($"{Tag}   SpalcialRule={PlayData.SpalcialRule ?? "(null)"}");
                Debug.Log($"{Tag}   CasualMode={SaveManager.NowData.GameOptions.CasualMode}");
            }
        }

        // ==================================================================
        //  CampUI.Init — camp UI buttons are set up
        // ==================================================================

        /// <summary>
        /// Logs the exact button visibility decision made during Init.
        /// </summary>
        [HarmonyPatch(typeof(CampUI), nameof(CampUI.Init))]
        internal static class CampUIInitPatch
        {
            [HarmonyPostfix]
            static void Postfix(CampUI __instance)
            {
                var camp = __instance.MainCampScript;
                bool btnActive = __instance.Button_AddParty != null
                    && __instance.Button_AddParty.gameObject.activeSelf;

                Debug.Log($"{Tag} === CampUI.Init finished ===");
                Debug.Log($"{Tag}   Button_AddParty active={btnActive}");
                Debug.Log($"{Tag}   Button_AddParty null={__instance.Button_AddParty == null}");
                Debug.Log($"{Tag}   Camp.AddParty={camp.AddParty}");
                Debug.Log($"{Tag}   Camp.CasualPartyAdd={camp.CasualPartyAdd}");
                Debug.Log($"{Tag}   StageNum={PlayData.TSavedata.StageNum}");
                Debug.Log($"{Tag}   Party.Count={PlayData.TSavedata.Party?.Count}");
            }
        }

        // ==================================================================
        //  CampUI.Update — per-frame button visibility override
        // ==================================================================

        /// <summary>
        /// Logs the first frame where Update decides the button state and
        /// any subsequent transition of the AddParty flag.
        /// </summary>
        [HarmonyPatch(typeof(CampUI), "Update")]
        internal static class CampUIUpdatePatch
        {
            [HarmonyPostfix]
            static void Postfix(CampUI __instance)
            {
                if (__instance.Button_AddParty == null)
                    return;

                var camp = __instance.MainCampScript;
                if (camp == null)
                    return;

                bool addParty = camp.AddParty;
                bool stageOk = PlayData.TSavedata.StageNum == 1
                            || PlayData.TSavedata.StageNum == 3;
                bool partyFull = PlayData.TSavedata.Party != null
                              && PlayData.TSavedata.Party.Count >= 4;
                bool soloRule = PlayData.SpalcialRule == GDEItemKeys.SpecialRule_SR_Solo
                            || PlayData.SpalcialRule == GDEItemKeys.SpecialRule_Story_AzarSolo;
                bool btnActive = __instance.Button_AddParty.gameObject.activeSelf;

                bool transition = _prevAddParty.HasValue && _prevAddParty.Value != addParty;
                if (!_loggedUpdateDecision || transition)
                {
                    string reason = transition ? "TRANSITION" : "FIRST_FRAME";
                    Debug.Log($"{Tag} === CampUI.Update [{reason}] ===");
                    Debug.Log($"{Tag}   AddParty={addParty}, StageOk={stageOk}, PartyFull={partyFull}, SoloRule={soloRule}");
                    Debug.Log($"{Tag}   Button active after Update={btnActive}");
                    Debug.Log($"{Tag}   Expected visibility={!addParty && stageOk && !partyFull && !soloRule}");
                    _loggedUpdateDecision = true;
                }

                _prevAddParty = addParty;
            }
        }

        // ==================================================================
        //  CampUI.PartyAdd — player clicks the Recruit button
        // ==================================================================

        /// <summary>
        /// Logs when the player clicks Recruit and the AddParty flag is set.
        /// </summary>
        [HarmonyPatch(typeof(CampUI), nameof(CampUI.PartyAdd))]
        internal static class PartyAddPatch
        {
            [HarmonyPrefix]
            static void Prefix(CampUI __instance)
            {
                Debug.Log($"{Tag} === PartyAdd clicked ===");
                Debug.Log($"{Tag}   AddParty before={__instance.MainCampScript.AddParty}");
            }

            [HarmonyPostfix]
            static void Postfix(CampUI __instance)
            {
                Debug.Log($"{Tag}   AddParty after={__instance.MainCampScript.AddParty}");
            }
        }

        // ==================================================================
        //  Camp.NewPartyadd — recruited character is actually added
        // ==================================================================

        /// <summary>
        /// Logs when the recruited party member is added to the party.
        /// </summary>
        [HarmonyPatch(typeof(Camp), nameof(Camp.NewPartyadd))]
        internal static class NewPartyaddPatch
        {
            [HarmonyPrefix]
            static void Prefix(Camp __instance)
            {
                Debug.Log($"{Tag} === NewPartyadd called ===");
                Debug.Log($"{Tag}   NewParty={__instance.NewParty?.Key ?? "(null)"}");
                Debug.Log($"{Tag}   PartyAdded={__instance.PartyAdded}");
                Debug.Log($"{Tag}   Party.Count={PlayData.TSavedata.Party?.Count}");
            }
        }

        // ==================================================================
        //  Data_Map.Save — camp state is serialized to disk
        // ==================================================================

        /// <summary>
        /// Logs the CampSave values being written when the game saves.
        /// </summary>
        [HarmonyPatch(typeof(Data_Map), nameof(Data_Map.Save))]
        internal static class DataMapSavePatch
        {
            [HarmonyPostfix]
            static void Postfix(Data_Map __instance)
            {
                if (__instance.CampSaveData != null)
                {
                    Debug.Log($"{Tag} === Data_Map.Save ===");
                    Debug.Log($"{Tag}   Saving CampSaveData.AddParty={__instance.CampSaveData.AddParty}");
                    Debug.Log($"{Tag}   Saving CampSaveData.PartyAdded={__instance.CampSaveData.PartyAdded}");
                    Debug.Log($"{Tag}   Saving CampSaveData.CampEnd={__instance.CampSaveData.CampEnd}");
                    Debug.Log($"{Tag}   Saving CampSaveData.CasualPartyAdd={__instance.CampSaveData.CasualPartyAdd}");
                }
                else
                {
                    Debug.Log($"{Tag} === Data_Map.Save === CampSaveData is NULL");
                }
            }
        }

        // ==================================================================
        //  CampSave.Load — camp state is deserialized from disk
        // ==================================================================

        /// <summary>
        /// Logs the CampSave values after deserialization.
        /// </summary>
        [HarmonyPatch(typeof(CampSave), nameof(CampSave.Load))]
        internal static class CampSaveLoadPatch
        {
            [HarmonyPostfix]
            static void Postfix(CampSave __instance)
            {
                Debug.Log($"{Tag} === CampSave.Load ===");
                Debug.Log($"{Tag}   Loaded AddParty={__instance.AddParty}");
                Debug.Log($"{Tag}   Loaded PartyAdded={__instance.PartyAdded}");
                Debug.Log($"{Tag}   Loaded CampEnd={__instance.CampEnd}");
                Debug.Log($"{Tag}   Loaded CasualPartyAdd={__instance.CasualPartyAdd}");
                Debug.Log($"{Tag}   Loaded EnableCamp={__instance.EnableCamp}");
            }
        }

        // ==================================================================
        //  Camp._CampEnd — campfire heal/leave sequence
        // ==================================================================

        /// <summary>
        /// Logs when the camp end sequence begins.
        /// </summary>
        [HarmonyPatch(typeof(Camp), nameof(Camp._CampEnd))]
        internal static class CampEndPatch
        {
            [HarmonyPrefix]
            static void Prefix(Camp __instance)
            {
                Debug.Log($"{Tag} === _CampEnd starting ===");
                Debug.Log($"{Tag}   AddParty={__instance.AddParty}");
                Debug.Log($"{Tag}   PartyAdded={__instance.PartyAdded}");
                Debug.Log($"{Tag}   NewParty={__instance.NewParty?.Key ?? "(null)"}");
            }
        }

        // ==================================================================
        //  CampUI.HealButton — the "leave camp" button
        // ==================================================================

        /// <summary>
        /// Logs the state when the player clicks the campfire/heal button.
        /// </summary>
        [HarmonyPatch(typeof(CampUI), nameof(CampUI.HealButton))]
        internal static class HealButtonPatch
        {
            [HarmonyPrefix]
            static void Prefix(CampUI __instance)
            {
                var camp = __instance.MainCampScript;
                Debug.Log($"{Tag} === HealButton clicked ===");
                Debug.Log($"{Tag}   CasualPartyAdd={camp.CasualPartyAdd}");
                Debug.Log($"{Tag}   CampEnd={camp.CampEnd}");
                Debug.Log($"{Tag}   AddParty={camp.AddParty}");
            }
        }
    }
}
