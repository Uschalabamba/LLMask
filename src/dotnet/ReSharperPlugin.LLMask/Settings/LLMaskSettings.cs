using JetBrains.Application.Settings;
using JetBrains.Application.Settings.WellKnownRootKeys;

namespace ReSharperPlugin.LLMask.Settings;

public enum SelectionObfuscatorMode
{
    StringBased,
    PsiBased
}

/// <summary>
/// Global settings key for LLMask.
/// Stored under the Environment section so they appear in
/// Settings → Tools → Environment → LLMask.
/// </summary>
[SettingsKey(typeof(EnvironmentSettings), "LLMask Plugin Settings")]
public class LLMaskSettings
{
    [SettingsEntry(SelectionObfuscatorMode.StringBased, "Which obfuscator to use for code selections")]
    public SelectionObfuscatorMode SelectionMode;

    [SettingsEntry(true, "Obfuscate an entire .cs file using PSI-based analysis when triggering the file action")]
    public bool UsePsiObfuscation;

    [SettingsEntry(true, "Sort obfuscated identifiers by usage frequency so the most-used names get the lowest numbers")]
    public bool UsePsiFrequencySorting;

    [SettingsEntry(true, "Use PSI reference resolution to automatically preserve identifiers from well-known assemblies (System.*, Serilog, Newtonsoft, etc.) without manual whitelist maintenance")]
    public bool UseAssemblyResolution;

    [SettingsEntry("", "Comma-separated list of additional identifiers to preserve (not obfuscate)")]
    public string CustomWhitelist;

    [SettingsEntry("", "Absolute path to a custom llmask.json config file. Leave empty to use llmask.json in the solution root (falling back to built-in defaults if absent).")]
    public string ConfigFilePath;
}
