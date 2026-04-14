using System.Collections.Generic;
using ChronoArkMod;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WorkshopOverhaul.Patches
{
    /// <summary>
    /// Adds a "Select All" toggle and reorganizes the top bar of the Workshop screen.
    /// </summary>
    [HarmonyPatch(typeof(ModUI), "OnEnable")]
    internal static class SelectAllPatch
    {
        private static Toggle _selectAllToggle;

        internal static bool UpdatingFromCode;

        static void Postfix(ModUI __instance)
        {
            RefreshSelectAllState(__instance);
        }

        private static void ToggleAllVisible(ModUI instance, bool enable)
        {
            UpdatingFromCode = true;

            try
            {
                var visibleMods = new List<string>(instance.ModscrolLElementsList_NowShow);

                foreach (string modId in visibleMods)
                {
                    if (!instance.ModscrolLElements.TryGetValue(modId, out var element))
                        continue;

                    bool currentlyEnabled = ModManager.IsModEnabled(modId);
                    if (currentlyEnabled == enable)
                        continue;

                    ModManager.SetModEnabled(modId, enable);

                    // Suppress the toggle's listener, set value, re-register.
                    element.isEnabledTog.onValueChanged.RemoveAllListeners();
                    element.isEnabledTog.isOn = enable;
                    element.CheckObj.SetActive(enable);

                    string capturedModId = modId;
                    element.isEnabledTog.onValueChanged.AddListener(delegate(bool isOn)
                    {
                        Debug.Log(capturedModId + isOn);
                        instance.SetModEnabled(capturedModId, isOn);
                        instance.OnModsScrollItemClicked(element.modInfo.id);
                        element.CheckObj.SetActive(isOn);
                    });
                }

                bool selectedEnabled = ModManager.EnabledMods.Contains(instance._curModId);
                instance.EnableBtn.gameObject.SetActive(!selectedEnabled);
                instance.DisableBtn.gameObject.SetActive(selectedEnabled);

                instance.AlignUpdate();
                instance.ApplyBtn.gameObject.SetActive(true);
            }
            finally
            {
                UpdatingFromCode = false;
            }
        }

        public static void RefreshSelectAllState(ModUI instance)
        {
            if (_selectAllToggle == null) return;

            var visibleMods = instance.ModscrolLElementsList_NowShow;
            bool allEnabled = visibleMods.Count > 0;
            foreach (string modId in visibleMods)
            {
                if (!ModManager.IsModEnabled(modId))
                {
                    allEnabled = false;
                    break;
                }
            }

            UpdatingFromCode = true;
            _selectAllToggle.isOn = allEnabled;
            UpdatingFromCode = false;
        }
    }

    [HarmonyPatch(typeof(ModUI), nameof(ModUI.SetModEnabled))]
    internal static class RefreshSelectAllOnToggle
    {
        static void Postfix(ModUI __instance)
        {
            if (SelectAllPatch.UpdatingFromCode) return;
            SelectAllPatch.RefreshSelectAllState(__instance);
        }
    }
}
