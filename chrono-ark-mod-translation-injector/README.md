# Mod Translation Injector

Injects English translations for mod content in Chrono Ark at runtime. Supports both the I2 Localization keyed term system and direct hardcoded CJK string replacement.

## Features

### Keyed Localization Overrides

Patches `LanguageSourceData.GetTermData()` (all overloads) to inject English text for localization keys defined in `keyed_overrides.json`. When the game looks up a term and finds a match in the override file, the English column of the returned `TermData` is set to the override value.

### Hardcoded Text Replacement

Patches text property setters on `TMP_Text`, `TextMeshProUGUI`, `TextMeshPro`, and `UnityEngine.UI.Text` to intercept string assignments. If the text contains CJK characters and matches an entry in `text_overrides.json`, it is replaced with the English translation. Also patches `BattleSystem.I_OtherSkillSelect` and `BattleSystem.System_SkillSelect` for skill selection prompt strings.

### CJK Detection

Uses fast Unicode range checks (CJK Unified Ideographs, Extension A, Compatibility Ideographs) to avoid processing strings that don't contain CJK characters.

## Data Files

Place these JSON files in the mod's folder alongside the DLL:

- **`keyed_overrides.json`** - Dictionary of localization key to English string.
- **`text_overrides.json`** - Dictionary of original CJK string to English replacement.

## Installation

1. Build with `dotnet build`.
2. The DLL and `ChronoArkMod.json` are copied to the game's `Mod/ModTranslationInjector/` folder automatically.
3. Add your translation JSON files to the same folder.
4. Enable the mod in the Workshop screen and restart.
