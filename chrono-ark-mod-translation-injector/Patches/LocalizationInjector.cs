using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using I2.Loc;
using Newtonsoft.Json;
using UnityEngine;

namespace ModTranslationInjector.Patches
{
    /// <summary>
    /// Intercepts I2.Loc term data lookups to inject English overrides.
    /// Patches LanguageSourceData.GetTermData so that whenever the game
    /// retrieves a term we have an override for, the English column is
    /// set before the caller reads it.
    /// </summary>
    internal static class LocalizationInjector
    {
        private const string OverridesFileName = "keyed_overrides.json";

        internal static Dictionary<string, string> Overrides = new Dictionary<string, string>();
        private static int _englishIndex = -1;

        /// <summary>
        /// Loads keyed overrides from JSON into memory.
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
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                if (loaded != null)
                {
                    Overrides = loaded;
                    Debug.Log($"[ModTranslationInjector] Loaded {Overrides.Count} keyed overrides");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ModTranslationInjector] Failed to load {OverridesFileName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches LanguageSourceData.GetTermData to intercept term lookups.
        /// </summary>
        internal static void ApplyPatches(Harmony harmony)
        {
            if (Overrides.Count == 0)
                return;

            // Cache the English language index.
            try
            {
                if (LocalizationManager.Sources != null && LocalizationManager.Sources.Count > 0)
                {
                    _englishIndex = LocalizationManager.Sources[0].GetLanguageIndex("English");
                    Debug.Log($"[ModTranslationInjector] English language index: {_englishIndex}");
                }
            }
            catch (Exception)
            {
                // Will try to resolve lazily in the postfix.
            }

            var postfix = new HarmonyMethod(typeof(LocalizationInjector), nameof(GetTermDataPostfix));

            // Patch all GetTermData overloads on LanguageSourceData.
            try
            {
                var methods = AccessTools.GetDeclaredMethods(typeof(LanguageSourceData))
                    .FindAll(m => m.Name == "GetTermData");

                foreach (var method in methods)
                {
                    try
                    {
                        harmony.Patch(method, postfix: postfix);
                        Debug.Log($"[ModTranslationInjector] Patched LanguageSourceData.GetTermData ({method.GetParameters().Length} params)");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[ModTranslationInjector] Failed to patch GetTermData: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModTranslationInjector] Could not find GetTermData: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for GetTermData: when the game retrieves a term we have
        /// an override for, set the English column on the returned TermData.
        /// </summary>
        static void GetTermDataPostfix(string term, ref TermData __result, LanguageSourceData __instance)
        {
            if (__result == null || string.IsNullOrEmpty(term))
                return;

            if (!Overrides.TryGetValue(term, out string english))
                return;

            // Lazily resolve the English index if not cached yet.
            if (_englishIndex < 0)
            {
                _englishIndex = __instance.GetLanguageIndex("English");
                if (_englishIndex < 0)
                    return;
            }

            if (_englishIndex < __result.Languages.Length)
            {
                __result.Languages[_englishIndex] = english;
            }
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
