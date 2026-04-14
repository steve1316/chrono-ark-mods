using ChronoArkMod.Plugin;
using HarmonyLib;
using UnityEngine;

namespace GameplayEnhancements
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
            HarmonyInstance = new Harmony("com.steve1316.gameplayenhancements");
            HarmonyInstance.PatchAll(typeof(Plugin).Assembly);
            Debug.Log("[GameplayEnhancements] Patches applied successfully");
        }

        /// <summary>
        /// Removes all Harmony patches when the mod is unloaded.
        /// </summary>
        public override void Dispose()
        {
            HarmonyInstance?.UnpatchSelf();
            HarmonyInstance = null;
            Instance = null;
            Debug.Log("[GameplayEnhancements] Patches removed");
        }
    }
}
