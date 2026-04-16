# Mod Translation Injector

Injects English translations for mod content in Chrono Ark at runtime. Supports both the I2 Localization keyed term system and direct hardcoded CJK string replacement.

## Features

### Keyed Localization Overrides

Patches `LanguageSourceData.GetTranslation()` to intercept the per-instance translation calls the mod framework makes. The Chrono Ark mod framework (`ModLocalizationInfo.LocalizeUpdate`) calls `GetTranslation` directly on each mod's own `LanguageSourceData` instance, bypassing `LocalizationManager` entirely. Our prefix checks the override dictionary first and returns the English text directly when matched.

Character names are handled separately because the game's `LocalizeDataPool` excludes the `name` field from localization. A postfix on `GDEDataManager.Init` writes character name overrides directly into GDE data via `GDEDataManager.SetString` after all mod data loads.

Key normalization is applied at load time to correct common mismatches between JSON keys and the game's internal term format (e.g. `PassiveDesc` -> `PassiveDes`).

### Hardcoded Text Replacement

Patches text property setters on `TMP_Text`, `TextMeshProUGUI`, `TextMeshPro`, and `UnityEngine.UI.Text` to intercept string assignments. If the text contains CJK characters and matches an entry in `text_overrides.json`, it is replaced with the English translation. Also patches `BattleSystem.I_OtherSkillSelect` and `BattleSystem.System_SkillSelect` for skill selection prompt strings.

### CJK Detection

Uses fast Unicode range checks (CJK Unified Ideographs, Extension A, Compatibility Ideographs) to avoid processing strings that don't contain CJK characters.

## Data Files

Place these JSON files in the mod's folder alongside the DLL. Both use a nested structure grouped by mod name:

**`keyed_overrides.json`** - Localization key overrides grouped by mod:

```json
{
  "SomeMod": {
    "Skill_FireBall_Name": "Fireball",
    "Skill_FireBall_Desc": "Deals fire damage to a single enemy."
  },
  "AnotherMod": {
    "Item_Potion_Name": "Healing Potion"
  }
}
```

**`text_overrides.json`** - Hardcoded CJK string replacements grouped by mod:

```json
{
  "SomeMod": {
    "\u706b\u7403": "Fireball",
    "\u653b\u51fb": "Attack"
  }
}
```

The mod name keys are for organization only — all entries are flattened into a single lookup dictionary at load time.

## Installation

1. Build with `dotnet build`.
2. The DLL and `ChronoArkMod.json` are copied to the game's `Mod/ModTranslationInjector/` folder automatically.
3. Add your translation JSON files to the same folder.
4. Enable the mod in the Workshop screen and restart.
