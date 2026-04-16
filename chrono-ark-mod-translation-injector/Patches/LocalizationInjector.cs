using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GameDataEditor;
using HarmonyLib;
using I2.Loc;
using Newtonsoft.Json;
using UnityEngine;

namespace ModTranslationInjector.Patches
{
    /// <summary>
    /// Intercepts I2.Loc translation lookups to inject English overrides.
    /// Patches LanguageSourceData.GetTranslation because the mod framework
    /// calls it directly on each mod's own LanguageSourceData instance,
    /// bypassing LocalizationManager entirely.
    /// </summary>
    internal static class LocalizationInjector
    {
        private const string OverridesFileName = "keyed_overrides.json";

        internal static Dictionary<string, string> Overrides = new Dictionary<string, string>();

        /// <summary>
        /// Loads keyed overrides from JSON into memory. The JSON is grouped
        /// by mod name as the parent key, with term keys as children:
        /// { "ModName": { "Term_Key": "English text", ... }, ... }
        /// </summary>
        internal static void LoadOverrides()
        {
            string jsonPath = GetModFilePath(OverridesFileName);
            if (jsonPath == null || !File.Exists(jsonPath))
            {
                Debug.Log($"[ModTranslationInjector] {OverridesFileName} not found");
                return;
            }

            try
            {
                string json = File.ReadAllText(jsonPath);
                var grouped = JsonConvert.DeserializeObject<
                    Dictionary<string, Dictionary<string, string>>>(json);
                if (grouped != null)
                {
                    Overrides.Clear();
                    foreach (var mod in grouped)
                    {
                        foreach (var entry in mod.Value)
                            Overrides[NormalizeKey(entry.Key)] = entry.Value;

                        Debug.Log($"[ModTranslationInjector] Loaded {mod.Value.Count} keyed overrides from '{mod.Key}'");
                    }
                    Debug.Log($"[ModTranslationInjector] Total keyed overrides: {Overrides.Count}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ModTranslationInjector] Failed to load {OverridesFileName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches LanguageSourceData.GetTranslation to intercept the
        /// per-instance translation calls the mod framework makes.
        /// </summary>
        internal static void ApplyPatches(Harmony harmony)
        {
            if (Overrides.Count == 0)
                return;

            // Patch GDEDataManager.Init to inject character names after mod data loads.
            // Character names aren't in the LocalizeDataPool, so they bypass
            // GetTranslation entirely and must be written to GDE data directly.
            try
            {
                var initMethod = AccessTools.Method(typeof(GDEDataManager), "Init", new[] { typeof(bool) });
                if (initMethod != null)
                {
                    var postfix = new HarmonyMethod(typeof(LocalizationInjector), nameof(GDEInitPostfix));
                    harmony.Patch(initMethod, postfix: postfix);
                    Debug.Log("[ModTranslationInjector] Patched GDEDataManager.Init for character name overrides");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModTranslationInjector] Failed to patch GDEDataManager.Init: {ex.Message}");
            }

            // Patch all GetTranslation overloads on LanguageSourceData.
            try
            {
                var methods = AccessTools.GetDeclaredMethods(typeof(LanguageSourceData))
                    .FindAll(m => m.Name == "GetTranslation");

                foreach (var method in methods)
                {
                    try
                    {
                        var prefix = new HarmonyMethod(typeof(LocalizationInjector),
                            nameof(GetTranslationPrefix));
                        harmony.Patch(method, prefix: prefix);
                        Debug.Log($"[ModTranslationInjector] Patched LanguageSourceData.GetTranslation ({method.GetParameters().Length} params)");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[ModTranslationInjector] Failed to patch GetTranslation: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModTranslationInjector] Could not find GetTranslation: {ex.Message}");
            }
        }

        /// <summary>
        /// Prefix for LanguageSourceData.GetTranslation: returns our
        /// override directly when the term matches a keyed override.
        /// </summary>
        static bool GetTranslationPrefix(string term, ref string __result)
        {
            if (string.IsNullOrEmpty(term))
                return true;

            if (Overrides.TryGetValue(term, out string english))
            {
                __result = english;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Postfix for GDEDataManager.Init: writes character name overrides
        /// directly into GDE data. The game's LocalizeDataPool excludes the
        /// name field from localization, so it must be set here instead.
        /// </summary>
        static void GDEInitPostfix()
        {
            int count = 0;
            foreach (var kvp in Overrides)
            {
                // Character name keys follow "Character/Key_Name" format.
                if (!kvp.Key.StartsWith("Character/") || !kvp.Key.EndsWith("_Name"))
                    continue;

                // Extract GDE key: "Character/Jefuty_Name" -> "Jefuty".
                string gdeKey = kvp.Key.Substring("Character/".Length,
                    kvp.Key.Length - "Character/".Length - "_Name".Length);

                try
                {
                    GDEDataManager.SetString(gdeKey, "name", kvp.Value);
                    count++;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ModTranslationInjector] Failed to set name for {gdeKey}: {ex.Message}");
                }
            }

            if (count > 0)
                Debug.Log($"[ModTranslationInjector] Injected {count} character name overrides via GDE");
        }

        /// <summary>
        /// Normalizes JSON keys to match the game's internal term format.
        /// The game truncates some suffixes (e.g. "PassiveDesc" -> "PassiveDes").
        /// </summary>
        private static string NormalizeKey(string key)
        {
            if (key.EndsWith("_PassiveDesc"))
                return key.Substring(0, key.Length - 1);

            return key;
        }

        /// <summary>
        /// Resolves a filename to an absolute path in the mod's root directory.
        /// </summary>
        internal static string GetModFilePath(string fileName)
        {
            try
            {
                string gameRoot = Path.GetDirectoryName(Application.dataPath);
                string modDir = Path.Combine(gameRoot, "Mod", "ModTranslationInjector");
                string candidate = Path.Combine(modDir, fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (Exception)
            {
                // Fall through.
            }

            try
            {
                string asmLocation = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(asmLocation))
                {
                    string asmDir = Path.GetDirectoryName(asmLocation);
                    string modDir = Path.GetDirectoryName(asmDir);
                    string candidate = Path.Combine(modDir, fileName);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
            catch (Exception)
            {
                // Assembly location not available.
            }

            Debug.LogWarning($"[ModTranslationInjector] Could not find {fileName} in any known location");
            return null;
        }
    }
}
