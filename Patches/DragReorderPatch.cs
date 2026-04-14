using System.Collections;
using ChronoArkMod;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WorkshopOverhaul.Patches
{
    /// <summary>
    /// Adds an editable order number to each mod scroll element. Typing a new
    /// number moves the mod to that position and shifts others down. Numbers
    /// reflect position in the visible (filtered) mod list.
    /// </summary>
    [HarmonyPatch(typeof(ModUI), "Init")]
    internal static class OrderNumberPatch
    {
        // Prevents recursive processing during reorder operations.
        private static bool _updating;

        static void Postfix(ModUI __instance)
        {
            _updating = true;
            try
            {
                foreach (var kvp in __instance.ModscrolLElements)
                {
                    var element = kvp.Value;
                    if (element.transform.Find("OrderInput") != null)
                        continue;

                    CreateOrderInput(element, __instance);
                }
            }
            finally
            {
                _updating = false;
            }

            // Delay initial number assignment by one frame so InputField
            // components finish their Start() initialization.
            __instance.StartCoroutine(DelayedRefresh(__instance));
        }

        private static IEnumerator DelayedRefresh(ModUI modUI)
        {
            yield return null;
            RefreshAllNumbers(modUI);
        }

        /// <summary>
        /// Creates an order number input field on the right side of a mod element.
        /// </summary>
        private static void CreateOrderInput(ModScrollElementScript element, ModUI modUI)
        {
            var inputGo = new GameObject("OrderInput");
            inputGo.transform.SetParent(element.transform, false);

            var rt = inputGo.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(295f, 0f);
            rt.sizeDelta = new Vector2(44f, 32f);

            var bgImage = inputGo.AddComponent<Image>();
            bgImage.color = new Color(0.12f, 0.12f, 0.12f, 0.9f);
            var outline = inputGo.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.3f);
            outline.effectDistance = new Vector2(1f, 1f);

            // Text child.
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(inputGo.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(4f, 2f);
            textRt.offsetMax = new Vector2(-4f, -2f);
            var text = textGo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 16;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(1f, 1f, 1f, 0.8f);
            text.supportRichText = false;

            // Placeholder child.
            var placeholderGo = new GameObject("Placeholder");
            placeholderGo.transform.SetParent(inputGo.transform, false);
            var phRt = placeholderGo.AddComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(4f, 2f);
            phRt.offsetMax = new Vector2(-4f, -2f);
            var phText = placeholderGo.AddComponent<Text>();
            phText.font = text.font;
            phText.fontSize = 16;
            phText.fontStyle = FontStyle.Italic;
            phText.alignment = TextAnchor.MiddleCenter;
            phText.color = new Color(1f, 1f, 1f, 0.2f);
            phText.text = "#";

            var inputField = inputGo.AddComponent<InputField>();
            inputField.textComponent = text;
            inputField.placeholder = phText;
            inputField.contentType = InputField.ContentType.IntegerNumber;
            inputField.characterLimit = 3;
            inputField.caretWidth = 2;
            inputField.caretColor = Color.white;
            inputField.selectionColor = new Color(0.3f, 0.5f, 0.8f, 0.5f);
            inputField.customCaretColor = true;
            inputField.caretBlinkRate = 0.85f;

            string modId = element.modInfo.id;

            // Refresh the displayed number when clicked so it always reflects
            // the current visible position before the user starts editing.
            var trigger = inputGo.AddComponent<EventTrigger>();
            var pointerDown = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            pointerDown.callback.AddListener(delegate
            {
                if (_updating) return;
                int visiblePos = GetVisiblePosition(modUI, modId);
                if (visiblePos > 0)
                    inputField.text = visiblePos.ToString();
            });
            trigger.triggers.Add(pointerDown);

            inputField.onEndEdit.AddListener(delegate(string value)
            {
                if (_updating) return;

                int currentPos = GetVisiblePosition(modUI, modId);
                if (currentPos <= 0) return;

                if (!int.TryParse(value, out int target) || target < 1)
                {
                    inputField.text = currentPos.ToString();
                    return;
                }

                if (target == currentPos)
                {
                    inputField.text = currentPos.ToString();
                    return;
                }

                _updating = true;
                try
                {
                    // Map target visible position to a full list index.
                    var visibleList = modUI.ModscrolLElementsList_NowShow;
                    int clampedTarget = Mathf.Clamp(target - 1, 0, visibleList.Count - 1);
                    string targetModId = visibleList[clampedTarget];
                    int listIndex = modUI.ModscrolLElementsList.IndexOf(targetModId);

                    modUI.ModsScrollItemChangeIndex(modId, listIndex);
                    modUI.AlignUpdate();
                    modUI.ApplyBtn.gameObject.SetActive(true);
                    RefreshAllNumbers(modUI);
                }
                finally
                {
                    _updating = false;
                }
            });
        }

        /// <summary>
        /// Gets the 1-based position of a mod in the visible (filtered) list.
        /// </summary>
        private static int GetVisiblePosition(ModUI modUI, string modId)
        {
            var visibleList = modUI.ModscrolLElementsList_NowShow;
            int index = visibleList.IndexOf(modId);
            return index >= 0 ? index + 1 : 0;
        }

        /// <summary>
        /// Updates all order number fields based on the visible list order.
        /// </summary>
        internal static void RefreshAllNumbers(ModUI modUI)
        {
            var visibleList = modUI.ModscrolLElementsList_NowShow;
            _updating = true;
            try
            {
                // Clear all numbers first (hidden mods get no number).
                foreach (var kvp in modUI.ModscrolLElements)
                {
                    var inputTransform = kvp.Value.transform.Find("OrderInput");
                    if (inputTransform == null) continue;
                    var field = inputTransform.GetComponent<InputField>();
                    if (field != null)
                        field.text = "";
                }

                // Set numbers for visible mods.
                for (int i = 0; i < visibleList.Count; i++)
                {
                    if (!modUI.ModscrolLElements.TryGetValue(visibleList[i], out var element))
                        continue;

                    var inputTransform = element.transform.Find("OrderInput");
                    if (inputTransform == null) continue;

                    var inputField = inputTransform.GetComponent<InputField>();
                    if (inputField != null)
                        inputField.text = (i + 1).ToString();
                }
            }
            finally
            {
                _updating = false;
            }
        }
    }
}
