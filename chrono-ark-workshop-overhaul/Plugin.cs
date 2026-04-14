using ChronoArkMod.Plugin;
using HarmonyLib;
using UnityEngine;

namespace WorkshopOverhaul
{
    public class Plugin : ChronoArkPlugin
    {
        internal static Plugin Instance;
        internal static Harmony HarmonyInstance;

        /// <summary>
        /// Applies all Harmony patches when the mod is loaded.
        /// </summary>
        public override void Initialize()
        {
            Instance = this;
            HarmonyInstance = new Harmony("com.steve1316.workshopoverhaul");
            HarmonyInstance.PatchAll(typeof(Plugin).Assembly);
            Debug.Log("[WorkshopOverhaul] Patches applied successfully");
        }

        /// <summary>
        /// Removes all Harmony patches when the mod is unloaded.
        /// </summary>
        public override void Dispose()
        {
            HarmonyInstance?.UnpatchSelf();
            HarmonyInstance = null;
            Instance = null;
            Debug.Log("[WorkshopOverhaul] Patches removed");
        }
    }
}
