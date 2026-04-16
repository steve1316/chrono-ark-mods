using ChronoArkMod.Plugin;
using HarmonyLib;
using ModTranslationInjector.Patches;
using UnityEngine;

namespace ModTranslationInjector
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
            HarmonyInstance = new Harmony("com.steve1316.modtranslationinjector");

            LocalizationInjector.LoadOverrides();
            TextOverridePatch.LoadOverrides();

            // Apply all patches manually to handle missing methods gracefully.
            LocalizationInjector.ApplyPatches(HarmonyInstance);
            TextOverridePatch.ApplyPatches(HarmonyInstance);

            Debug.Log("[ModTranslationInjector] Patches applied successfully");
        }

        /// <summary>
        /// Removes all Harmony patches when the mod is unloaded.
        /// </summary>
        public override void Dispose()
        {
            HarmonyInstance?.UnpatchSelf();
            HarmonyInstance = null;
            Instance = null;
            Debug.Log("[ModTranslationInjector] Patches removed");
        }
    }
}
