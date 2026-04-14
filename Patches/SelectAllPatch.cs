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
        private static GameObject _selectAllGo;
        private static Toggle _selectAllToggle;

        internal static bool UpdatingFromCode;

        // Cached sprites from the game's checkbox prefab.
        private static Sprite _boxSprite;
        private static Sprite _checkSprite;

        static void Postfix(ModUI __instance)
        {
            CacheSprites(__instance);
            CreateSelectAllToggle(__instance);
            RefreshSelectAllState(__instance);
        }

        private static void CacheSprites(ModUI instance)
        {
            if (_boxSprite != null) return;

            // Grab sprites from the mod scroll element prefab's CheckBox.
            var prefab = instance.ModScrollElementPrefab;
            if (prefab == null) return;

            var checkBoxTransform = prefab.transform.Find("CheckBox");
            if (checkBoxTransform != null)
            {
                var boxImage = checkBoxTransform.GetComponent<Image>();
                if (boxImage != null) _boxSprite = boxImage.sprite; // "Box_"

                var checkTransform = checkBoxTransform.Find("Check");
                if (checkTransform != null)
                {
                    var checkImage = checkTransform.GetComponent<Image>();
                    if (checkImage != null) _checkSprite = checkImage.sprite; // "Check"
                }
            }
        }

        private static void CreateSelectAllToggle(ModUI instance)
        {
            if (_selectAllGo != null)
            {
                RefreshSelectAllState(instance);
                return;
            }

            var modUiTransform = instance.transform;

            // --- Toggle (checkbox only, no inline label) ---
            _selectAllGo = new GameObject("SelectAllToggle");
            _selectAllGo.transform.SetParent(modUiTransform, false);

            var rt = _selectAllGo.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(-870f, 397f);
            rt.sizeDelta = new Vector2(34f, 34f);

            // Checkbox background using game's Box_ sprite.
            var checkboxBg = _selectAllGo.AddComponent<Image>();
            if (_boxSprite != null)
            {
                checkboxBg.sprite = _boxSprite;
                checkboxBg.type = Image.Type.Simple;
                checkboxBg.color = Color.white;
            }
            else
            {
                checkboxBg.color = new Color(0.3f, 0.3f, 0.3f, 1f);
            }

            // Checkmark using game's Check sprite.
            var checkmarkGo = new GameObject("Checkmark");
            checkmarkGo.transform.SetParent(_selectAllGo.transform, false);
            var checkmarkRt = checkmarkGo.AddComponent<RectTransform>();
            checkmarkRt.anchorMin = Vector2.zero;
            checkmarkRt.anchorMax = Vector2.one;
            checkmarkRt.offsetMin = Vector2.zero;
            checkmarkRt.offsetMax = Vector2.zero;
            var checkmarkImage = checkmarkGo.AddComponent<Image>();
            if (_checkSprite != null)
            {
                checkmarkImage.sprite = _checkSprite;
                checkmarkImage.type = Image.Type.Simple;
                checkmarkImage.color = Color.white;
            }
            else
            {
                checkmarkImage.color = Color.white;
            }

            // Toggle component.
            _selectAllToggle = _selectAllGo.AddComponent<Toggle>();
            _selectAllToggle.targetGraphic = checkboxBg;
            _selectAllToggle.graphic = checkmarkImage;
            _selectAllToggle.isOn = false;

            _selectAllToggle.onValueChanged.AddListener(delegate(bool isOn)
            {
                if (UpdatingFromCode) return;
                ToggleAllVisible(instance, isOn);
            });
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
