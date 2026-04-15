using ChronoArkMod.Plugin;
using HarmonyLib;
using ModEnglishTranslations.Patches;
using UnityEngine;

namespace ModEnglishTranslations
{
    public class Plugin : ChronoArkPlugin
    {
        internal static Plugin Instance;
        internal static Harmony HarmonyInstance;

        /// <summary>
        /// Loads translation overrides and applies all Harmony patches.
        /// </summary>
        public override void Initialize()
        {
            Instance = this;
            HarmonyInstance = new Harmony("com.steve1316.modenglishtranslations");

            LocalizationInjector.InjectTranslations();
            TextOverridePatch.LoadOverrides();

            // Apply TMPro patches manually to handle missing methods gracefully.
            TextOverridePatch.ApplyPatches(HarmonyInstance);

            Debug.Log("[ModEnglishTranslations] Patches applied successfully");
        }

        /// <summary>
        /// Removes all Harmony patches when the mod is unloaded.
        /// </summary>
        public override void Dispose()
        {
            HarmonyInstance?.UnpatchSelf();
            HarmonyInstance = null;
            Instance = null;
            Debug.Log("[ModEnglishTranslations] Patches removed");
        }
    }
}
