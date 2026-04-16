# Mod Translation Injector - Technical Documentation

## Architecture

The mod uses two complementary approaches to inject English translations: **keyed localization overrides** via the I2.Loc system, and **hardcoded text replacement** via UI text component patching. Patches are applied manually (not via `PatchAll`) so each can fail independently with graceful error handling.

## Problems and Solutions

### Problem: Modded content has no English translations

Community mods for Chrono Ark are primarily developed in Korean/Chinese. The game uses the I2 Localization system for translatable strings, but mod authors often only provide CJK translations. Some strings are hardcoded directly in UI text assignments, bypassing the localization system entirely.

### Solution: Two-layer translation injection

**Layer 1 — Keyed overrides (LocalizationInjector):** Patches the I2.Loc `GetTermData()` method to inject English text for specific localization keys. This handles all strings that go through the proper localization pipeline.

**Layer 2 — Hardcoded text replacement (TextOverridePatch):** Patches UI text property setters (`TMP_Text.text`, `TextMeshProUGUI.text`, `Text.text`, etc.) to intercept string assignments. If the string contains CJK characters and matches an override entry, it's replaced before the UI renders it. This catches strings that bypass the localization system.

**Why two layers:** Some mod content goes through I2.Loc (dialogue, item names) while other content is set directly on text components (UI labels, battle prompts). Neither approach alone catches everything.

### Problem: Load-order dependency for localization injection

The game loads localization data before mods initialize. If the mod tries to add terms to the localization source during `Initialize()`, they might be overwritten or not found because the source hasn't loaded the mod's terms yet.

### Solution: Postfix on GetTermData instead of pre-loading

Instead of adding terms to the localization database at startup (which has ordering issues), the mod patches `GetTermData()` with a Postfix that modifies the returned `TermData` object. When the game looks up any term, the Postfix checks if an override exists and sets the English column on the fly. This is load-order safe because it runs at lookup time, not initialization time.

**Why Postfix instead of Prefix:** The original method must run first to return the `TermData` object. The Postfix then modifies the English language column of the returned object. A Prefix couldn't modify a return value that doesn't exist yet.

### Problem: BattleSystem skill prompts use hardcoded strings

`BattleSystem.I_OtherSkillSelect` and `BattleSystem.System_SkillSelect` pass CJK strings as method parameters for skill selection prompts. These never touch a text component setter, so the text-component patches don't catch them.

### Solution: Patch all overloads of skill selection methods

`PatchSkillSelectMethods` uses reflection to find all overloads of both methods, identifies which parameters are strings, and patches each with `PrefixStringArgs` — a generic prefix that applies `ApplyOverride` to every `ref string` parameter. Wrapped in try-catch because different game versions may have different overloads.

### Problem: Performance impact of checking every text assignment

Every `TMP_Text.text` set in the game passes through the Prefix. With hundreds of text assignments per frame during UI-heavy scenes, this could cause performance issues.

### Solution: Fast CJK detection as early-out

`ContainsCjk()` scans each character against three Unicode ranges and returns on first match. Strings that contain no CJK characters (the vast majority in an English game session) are rejected immediately without a dictionary lookup. Only strings containing CJK characters trigger the override dictionary check.

## Patch Files

### LocalizationInjector.cs

**Data source:** `keyed_overrides.json` — `Dictionary<string, string>` mapping term keys to English strings.

**Harmony patches (manual):**
- `LanguageSourceData.GetTermData(string)` (Postfix)
- `LanguageSourceData.GetTermData(string, bool)` (Postfix)

**GetTermDataPostfix:** If the returned `TermData` is non-null and the term key is in the override dictionary, sets `termData.Languages[englishIndex]` to the override value. The English language index is resolved lazily via `LocalizationManager.Sources[0].GetLanguageIndex("English")` and cached.

**File resolution:** `GetModFilePath()` checks the game's `Mod/ModTranslationInjector/` directory first, falling back to the assembly's directory. This supports both deployed installs and development builds.

### TextOverridePatch.cs

**Data source:** `text_overrides.json` — `Dictionary<string, string>` mapping original CJK strings to English replacements.

**Harmony patches (manual):**
- `TMP_Text.text` setter, `TextMeshProUGUI.text` setter, `TextMeshPro.text` setter — Prefix
- `TMP_Text.SetText(string)` — Prefix
- `UnityEngine.UI.Text.text` setter — Prefix
- `BattleSystem.I_OtherSkillSelect` (all overloads) — Prefix on string params
- `BattleSystem.System_SkillSelect` (all overloads) — Prefix on string params

**ApplyOverride:** Checks `ContainsCjk()` first for fast rejection, then does a dictionary lookup. Modifies the string by ref before the original setter runs.

**ContainsCjk ranges:**
- `\u4E00-\u9FFF` — CJK Unified Ideographs
- `\u3400-\u4DBF` — CJK Extension A
- `\uF900-\uFAFF` — CJK Compatibility Ideographs

## Key Game Types

| Type | Role |
|------|------|
| `LanguageSourceData` | I2.Loc data source. `GetTermData()` returns `TermData` for a localization key. |
| `TermData` | Contains `Languages[]` array indexed by language. The English index is resolved via `GetLanguageIndex("English")`. |
| `LocalizationManager` | I2.Loc manager. `Sources[0]` is the primary language source. |
| `TMP_Text` | TextMeshPro base class. `text` property setter is the main text injection point. |
| `BattleSystem` | Combat manager. Skill selection methods pass hardcoded CJK prompt strings. |
