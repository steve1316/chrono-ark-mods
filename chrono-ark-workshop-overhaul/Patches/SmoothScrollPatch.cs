using ChronoArkMod;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WorkshopOverhaul.Patches
{
    /// <summary>
    /// Matches the scrollbar-area scroll speed to the list-area speed set by
    /// SmoothScrollPatch below. Without this, scrolling over the scrollbar
    /// track uses the native ScrollRect sensitivity which is much slower.
    /// </summary>
    [HarmonyPatch(typeof(ModUI), "OnEnable")]
    internal static class ScrollRectConfigPatch
    {
        private static bool _applied;

        static void Postfix(ModUI __instance)
        {
            if (_applied) return;
            _applied = true;

            var scrollRect = __instance.ModScrollAlign?.GetComponentInParent<ScrollRect>();
            if (scrollRect == null) return;

            // ~80px per scroll tick to match the list-area patch below.
            scrollRect.scrollSensitivity = 80f;
        }
    }

    /// <summary>
    /// Replaces the game's RecordViewScroll.Update scroll-wheel handling which
    /// applies large normalized jumps to the scrollbar. Our version uses smaller
    /// increments scaled to content size for smooth, proportional scrolling.
    /// Drag behavior is preserved unchanged.
    /// </summary>
    [HarmonyPatch(typeof(RecordViewScroll), "Update")]
    internal static class SmoothScrollPatch
    {
        static bool Prefix(RecordViewScroll __instance, bool ___On)
        {
            if (!___On) return false;

            // Smooth scroll: scale by visible fraction so the step feels
            // consistent regardless of how many mods are in the list.
            float axis = Input.GetAxis("Mouse ScrollWheel");
            if (axis != 0f && __instance.Main != null)
            {
                var content = __instance.Main.content;
                var viewport = __instance.Main.viewport;
                if (content != null && viewport != null)
                {
                    float contentHeight = content.rect.height;
                    float viewportHeight = viewport.rect.height;
                    if (contentHeight > viewportHeight)
                    {
                        // Move by a fixed pixel amount relative to content height.
                        float pixelStep = 80f;
                        float normalizedStep = pixelStep / (contentHeight - viewportHeight);
                        __instance.Main.verticalScrollbar.value =
                            Mathf.Clamp01(__instance.Main.verticalScrollbar.value + axis * normalizedStep * 10f);
                    }
                }
            }

            return false;
        }
    }
}
