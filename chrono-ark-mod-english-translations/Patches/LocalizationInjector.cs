using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using I2.Loc;
using Newtonsoft.Json;
using UnityEngine;


namespace ModEnglishTranslations.Patches
{
    /// <summary>
    /// Injects English translations into I2.Loc's language source at runtime.
    /// </summary>
    internal static class LocalizationInjector
    {
        private const string OverridesFileName = "keyed_overrides.json";

        /// <summary>
        /// Loads keyed overrides from JSON and injects them into the active language source.
        /// </summary>
        internal static void InjectTranslations()
        {
            var overrides = LoadKeyedOverrides();
            if (overrides == null || overrides.Count == 0)
            {
                Debug.Log("[ModEnglishTranslations] No keyed overrides to inject");
                return;
            }

            var source = GetPrimaryLanguageSource();
            if (source == null)
            {
                Debug.LogWarning("[ModEnglishTranslations] No language source found, cannot inject");
                return;
            }

            int englishIndex = source.GetLanguageIndex("English");
            if (englishIndex < 0)
            {
                Debug.LogWarning("[ModEnglishTranslations] English language not found in source");
                return;
            }

            int injected = 0;
            int added = 0;

            foreach (var kvp in overrides)
            {
                string key = kvp.Key;
                string english = kvp.Value;

                var termData = source.GetTermData(key);
                if (termData != null)
                {
                    // Term exists, just set the English translation.
                    termData.Languages[englishIndex] = english;
                    injected++;
                }
                else
                {
                    // Term doesn't exist yet, add it.
                    termData = source.AddTerm(key, eTermType.Text);
                    if (termData != null)
                    {
                        termData.Languages[englishIndex] = english;
                        added++;
                    }
                }
            }

            Debug.Log($"[ModEnglishTranslations] Injected {injected} overrides, added {added} new terms");
        }

        /// <summary>
        /// Gets the first available language source from the localization manager.
        /// </summary>
        private static LanguageSourceData GetPrimaryLanguageSource()
        {
            if (LocalizationManager.Sources == null || LocalizationManager.Sources.Count == 0)
                return null;

            return LocalizationManager.Sources[0];
        }

        /// <summary>
        /// Loads the keyed overrides JSON from the mod directory.
        /// </summary>
        private static Dictionary<string, string> LoadKeyedOverrides()
        {
            string jsonPath = GetModFilePath(OverridesFileName);
            if (jsonPath == null || !File.Exists(jsonPath))
            {
                Debug.Log($"[ModEnglishTranslations] {OverridesFileName} not found");
                return null;
            }

            try
            {
                string json = File.ReadAllText(jsonPath);
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ModEnglishTranslations] Failed to load {OverridesFileName}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Resolves a filename to an absolute path in the mod's root directory.
        /// Uses the game's StreamingAssets/Mod path, then falls back to assembly location.
        /// </summary>
        internal static string GetModFilePath(string fileName)
        {
            // Primary: game's local Mod directory (one level above Application.dataPath).
            try
            {
                string gameRoot = Path.GetDirectoryName(Application.dataPath);
                string modDir = Path.Combine(gameRoot, "Mod", "ModEnglishTranslations");
                string candidate = Path.Combine(modDir, fileName);
                if (File.Exists(candidate))
                {
                    Debug.Log($"[ModEnglishTranslations] Found {fileName} at {candidate}");
                    return candidate;
                }
            }
            catch (Exception)
            {
                // StreamingAssets not available, fall through.
            }

            // Fallback: derive from DLL location (Assemblies/ -> mod root).
            try
            {
                string asmLocation = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(asmLocation))
                {
                    string asmDir = Path.GetDirectoryName(asmLocation);
                    string modDir = Path.GetDirectoryName(asmDir);
                    string candidate = Path.Combine(modDir, fileName);
                    if (File.Exists(candidate))
                    {
                        Debug.Log($"[ModEnglishTranslations] Found {fileName} at {candidate}");
                        return candidate;
                    }
                }
            }
            catch (Exception)
            {
                // Assembly location not available.
            }

            Debug.LogWarning($"[ModEnglishTranslations] Could not find {fileName} in any known location");
            return null;
        }
    }
}
