using System.Collections.Generic;
using ChronoArkMod;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WorkshopOverhaul.Patches
{
    /// <summary>
    /// Adds a "Select All" toggle and reorganizes the top bar of the Workshop screen.
    ///
    /// Original layout (all on ModUI(Clone), 1920x1080 canvas, center pivot):
    ///   SearchInputField: (-652.4, 397) size 345.5x34, child "Name" label at relative (-190, -0.3)
    ///   TagDropDown:      (-294.3, 397) size 217.3x34, child "Name" label at relative (-119, -0.5)
    ///   ModeScrollView:   (-550.1, -53.5) size 699.4x807.4 (left edge ~-900, right edge ~-200)
    ///
    /// New layout: move labels above their fields, add Select All toggle on the left.
    ///   Row Y = 397 (fields), Label Y = 397 + 22 = 419 (above)
    ///   [Select All toggle] [Search field] [Tag dropdown]
    ///    x: -890 to -810     x: -800 to -470   x: -460 to -200
    /// </summary>
    [HarmonyPatch(typeof(ModUI), "OnEnable")]
    internal static class SelectAllPatch
    {
        private static GameObject _selectAllGo;
        private static Toggle _selectAllToggle;
        private static bool _repositioned;

        internal static bool UpdatingFromCode;

        // Cached sprites from the game's checkbox prefab.
        private static Sprite _boxSprite;
        private static Sprite _checkSprite;

        static void Postfix(ModUI __instance)
        {
            CacheSprites(__instance);
            RepositionExistingElements(__instance);
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

        private static void RepositionExistingElements(ModUI instance)
        {
            if (_repositioned) return;
            _repositioned = true;

            // New positions: fields on Y=397, labels above at Y=419.
            const float rowY = 397f;
            const float labelY = 22f; // offset above field

            // --- Search field ---
            // Move to x center = -635, width = 330.
            var searchRt = instance.SearchInputField?.GetComponent<RectTransform>();
            if (searchRt != null)
            {
                searchRt.anchoredPosition = new Vector2(-635f, rowY);
                searchRt.sizeDelta = new Vector2(330f, 34f);

                // Move the "Search" label above the field.
                var searchLabel = searchRt.Find("Name");
                if (searchLabel != null)
                {
                    var labelRt = searchLabel.GetComponent<RectTransform>();
                    // Detach from input field, reparent to ModUI so it sits above.
                    searchLabel.SetParent(instance.transform, true);
                    var slRt = searchLabel.GetComponent<RectTransform>();
                    slRt.anchorMin = new Vector2(0.5f, 0.5f);
                    slRt.anchorMax = new Vector2(0.5f, 0.5f);
                    // Position above the left edge of the search field.
                    // Search left edge = -635 - 330/2 = -800.
                    slRt.pivot = new Vector2(0f, 0f);
                    slRt.anchoredPosition = new Vector2(-800f, rowY + labelY);
                    slRt.sizeDelta = new Vector2(80f, 20f);
                    var slTmp = searchLabel.GetComponent<TextMeshProUGUI>();
                    if (slTmp != null)
                    {
                        slTmp.alignment = TextAlignmentOptions.BottomLeft;
                        slTmp.fontSize = 16;
                    }
                }
            }

            // --- Tag dropdown ---
            // Move to x center = -330, width = 260.
            var tagRt = instance.TagDropDown?.GetComponent<RectTransform>();
            if (tagRt != null)
            {
                tagRt.anchoredPosition = new Vector2(-330f, rowY);
                tagRt.sizeDelta = new Vector2(260f, 34f);

                // Move the "Tag" label above the dropdown.
                var tagLabel = tagRt.Find("Name");
                if (tagLabel != null)
                {
                    tagLabel.SetParent(instance.transform, true);
                    var tlRt = tagLabel.GetComponent<RectTransform>();
                    tlRt.anchorMin = new Vector2(0.5f, 0.5f);
                    tlRt.anchorMax = new Vector2(0.5f, 0.5f);
                    // Tag left edge = -330 - 260/2 = -460.
                    tlRt.pivot = new Vector2(0f, 0f);
                    tlRt.anchoredPosition = new Vector2(-460f, rowY + labelY);
                    tlRt.sizeDelta = new Vector2(50f, 20f);
                    var tlTmp = tagLabel.GetComponent<TextMeshProUGUI>();
                    if (tlTmp != null)
                    {
                        tlTmp.alignment = TextAlignmentOptions.BottomLeft;
                        tlTmp.fontSize = 16;
                    }
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

            const float rowY = 397f;
            const float labelY = 22f;
            var modUiTransform = instance.transform;

            // --- "Select All" label above the toggle ---
            var labelAboveGo = new GameObject("SelectAllLabel");
            labelAboveGo.transform.SetParent(modUiTransform, false);
            var labelAboveRt = labelAboveGo.AddComponent<RectTransform>();
            labelAboveRt.anchorMin = new Vector2(0.5f, 0.5f);
            labelAboveRt.anchorMax = new Vector2(0.5f, 0.5f);
            labelAboveRt.pivot = new Vector2(0f, 0f);
            // Toggle will be at x=-890, so label starts there too.
            labelAboveRt.anchoredPosition = new Vector2(-900f, rowY + labelY);
            labelAboveRt.sizeDelta = new Vector2(90f, 20f);
            var labelAboveTmp = labelAboveGo.AddComponent<TextMeshProUGUI>();
            labelAboveTmp.text = "Select All";
            labelAboveTmp.fontSize = 16;
            labelAboveTmp.alignment = TextAlignmentOptions.BottomLeft;
            labelAboveTmp.color = Color.white;
            if (instance.NameValue != null)
            {
                labelAboveTmp.font = instance.NameValue.font;
                labelAboveTmp.fontMaterial = instance.NameValue.fontMaterial;
            }

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
