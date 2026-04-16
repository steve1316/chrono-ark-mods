using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModTranslationInjector.Patches
{
    /// <summary>
    /// Replaces hardcoded CJK strings in TMPro text assignments at runtime.
    /// Patches are applied manually to handle missing methods gracefully.
    /// </summary>
    internal static class TextOverridePatch
    {
        internal static Dictionary<string, string> Overrides = new Dictionary<string, string>();

        private const string OverridesFileName = "text_overrides.json";

        /// <summary>
        /// Loads the text override mapping from the mod's directory.
        /// </summary>
        internal static void LoadOverrides()
        {
            string jsonPath = LocalizationInjector.GetModFilePath(OverridesFileName);
            if (jsonPath == null || !File.Exists(jsonPath))
            {
                Debug.Log($"[ModTranslationInjector] {OverridesFileName} not found, skipping");
                return;
            }

            try
            {
                string json = File.ReadAllText(jsonPath);
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                if (loaded != null)
                {
                    Overrides = loaded;
                    Debug.Log($"[ModTranslationInjector] Loaded {Overrides.Count} text overrides");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ModTranslationInjector] Failed to load {OverridesFileName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Manually patches TMPro text setters, skipping any that can't be resolved.
        /// </summary>
        internal static void ApplyPatches(Harmony harmony)
        {
            var prefix = new HarmonyMethod(typeof(TextOverridePatch), nameof(Prefix));

            // Try each TMPro class that might have a text setter.
            Type[] targets = { typeof(TMP_Text), typeof(TextMeshProUGUI), typeof(TextMeshPro) };
            foreach (var type in targets)
            {
                try
                {
                    var setter = AccessTools.PropertySetter(type, "text");
                    if (setter != null)
                    {
                        harmony.Patch(setter, prefix: prefix);
                        Debug.Log($"[ModTranslationInjector] Patched {type.Name}.text setter");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ModTranslationInjector] Could not patch {type.Name}.text: {ex.Message}");
                }
            }

            // Also try SetText(string) as an alternative code path.
            try
            {
                var setTextMethod = AccessTools.Method(typeof(TMP_Text), "SetText", new[] { typeof(string) });
                if (setTextMethod != null)
                {
                    var setTextPrefix = new HarmonyMethod(typeof(TextOverridePatch), nameof(PrefixSetText));
                    harmony.Patch(setTextMethod, prefix: setTextPrefix);
                    Debug.Log("[ModTranslationInjector] Patched TMP_Text.SetText(string)");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModTranslationInjector] Could not patch SetText: {ex.Message}");
            }

            // Patch Unity's legacy UI.Text setter in case the game uses that.
            try
            {
                var uiTextSetter = AccessTools.PropertySetter(typeof(UnityEngine.UI.Text), "text");
                if (uiTextSetter != null)
                {
                    harmony.Patch(uiTextSetter, prefix: prefix);
                    Debug.Log("[ModTranslationInjector] Patched UnityEngine.UI.Text.text setter");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModTranslationInjector] Could not patch UI.Text: {ex.Message}");
            }

            // Patch BattleSystem.I_OtherSkillSelect which takes a string prompt
            // for the skill selection UI. Some mods pass hardcoded CJK strings here.
            PatchSkillSelectMethods(harmony);
        }

        /// <summary>
        /// Prefix for the text property setter.
        /// </summary>
        static void Prefix(ref string value)
        {
            ApplyOverride(ref value);
        }

        /// <summary>
        /// Prefix for SetText(string) method.
        /// </summary>
        static void PrefixSetText(ref string text)
        {
            ApplyOverride(ref text);
        }

        /// <summary>
        /// Patches BattleSystem methods that accept string prompts for skill selection UI.
        /// </summary>
        private static void PatchSkillSelectMethods(Harmony harmony)
        {
            var prefix = new HarmonyMethod(typeof(TextOverridePatch), nameof(Prefix));

            // Find all overloads of I_OtherSkillSelect and System_SkillSelect
            // and patch any string parameter they have.
            string[] methodNames = { "I_OtherSkillSelect", "System_SkillSelect" };
            var battleSystemType = AccessTools.TypeByName("BattleSystem");
            if (battleSystemType == null)
            {
                Debug.LogWarning("[ModTranslationInjector] BattleSystem type not found");
                return;
            }

            foreach (string methodName in methodNames)
            {
                try
                {
                    var methods = AccessTools.GetDeclaredMethods(battleSystemType)
                        .FindAll(m => m.Name == methodName);
                    foreach (var method in methods)
                    {
                        // Patch with a generic prefix that replaces any string arg.
                        var stringPrefix = new HarmonyMethod(
                            typeof(TextOverridePatch), nameof(PrefixStringArgs));
                        harmony.Patch(method, prefix: stringPrefix);
                        Debug.Log($"[ModTranslationInjector] Patched BattleSystem.{methodName}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"[ModTranslationInjector] Could not patch {methodName}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Generic prefix that scans all string parameters via __args and replaces CJK matches.
        /// </summary>
        static void PrefixStringArgs(object[] __args)
        {
            if (__args == null || Overrides.Count == 0)
                return;

            for (int i = 0; i < __args.Length; i++)
            {
                if (__args[i] is string s && !string.IsNullOrEmpty(s) && ContainsCjk(s))
                {
                    if (Overrides.TryGetValue(s, out string replacement))
                    {
                        __args[i] = replacement;
                    }
                }
            }
        }

        /// <summary>
        /// Replaces the text value if it matches a known CJK override.
        /// </summary>
        private static void ApplyOverride(ref string value)
        {
            if (Overrides.Count == 0 || string.IsNullOrEmpty(value))
                return;

            if (!ContainsCjk(value))
                return;

            if (Overrides.TryGetValue(value, out string replacement))
            {
                value = replacement;
            }
        }

        /// <summary>
        /// Fast check for CJK unified ideographs in a string.
        /// </summary>
        private static bool ContainsCjk(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= '\u4E00' && c <= '\u9FFF')
                    return true;
                if (c >= '\u3400' && c <= '\u4DBF')
                    return true;
                if (c >= '\uF900' && c <= '\uFAFF')
                    return true;
            }
            return false;
        }
    }
}
