using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WorkshopOverhaul.UI
{
    /// <summary>
    /// A simple full-screen overlay that displays a message during mod loading.
    /// Created and destroyed programmatically -- no prefab needed.
    /// </summary>
    public static class ProgressOverlay
    {
        private const string TextChildName = "Message";

        /// <summary>
        /// Creates and returns the overlay GameObject. Caller is responsible
        /// for calling Hide() to destroy it when done.
        /// </summary>
        public static GameObject Show(Transform parent, string message)
        {
            var overlayGo = new GameObject("WorkshopOverhaul_ProgressOverlay");
            overlayGo.transform.SetParent(parent, false);

            // Separate canvas with high sort order so the overlay renders above
            // all existing UI, including the mod list and detail panel.
            var canvas = overlayGo.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 9999;

            // Blocks clicks from reaching elements underneath.
            overlayGo.AddComponent<GraphicRaycaster>();

            // Full-screen RectTransform.
            var rt = overlayGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // Semi-transparent dark background.
            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(overlayGo.transform, false);
            var bgImage = bgGo.AddComponent<Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.75f);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            // Message text.
            var textGo = new GameObject(TextChildName);
            textGo.transform.SetParent(overlayGo.transform, false);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = message;
            tmp.fontSize = 36;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = new Vector2(0.2f, 0.4f);
            textRt.anchorMax = new Vector2(0.8f, 0.6f);
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            return overlayGo;
        }

        /// <summary>
        /// Updates the overlay text, e.g. to show elapsed time after loading completes.
        /// </summary>
        public static void UpdateMessage(GameObject overlay, string message)
        {
            if (overlay == null) return;

            var textTransform = overlay.transform.Find(TextChildName);
            if (textTransform != null)
            {
                var tmp = textTransform.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                    tmp.text = message;
            }
        }

        /// <summary>
        /// Destroys the overlay GameObject.
        /// </summary>
        public static void Hide(GameObject overlay)
        {
            if (overlay != null)
            {
                Object.Destroy(overlay);
            }
        }
    }
}
