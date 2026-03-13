using JetBrains.Application.Settings;
using JetBrains.Application.Settings.WellKnownRootKeys;
using ReSharperPlugin.LLMask.Data;

namespace ReSharperPlugin.LLMask.Settings;

/// <summary>
/// Global settings key for LLMask.
/// Stored under the Environment section so they appear in
/// Settings → Tools → Environment → LLMask.
/// </summary>
[SettingsKey(typeof(EnvironmentSettings), "LLMask Plugin Settings")]
public class LLMaskSettings
{
    [SettingsEntry(true, "Obfuscate selected code using string-based analysis when triggering the context action")]
    public bool UseStringObfuscation;

    [SettingsEntry(true, "Obfuscate an entire .cs file using PSI-based analysis when triggering the file action")]
    public bool UsePsiObfuscation;

    [SettingsEntry(true, "Sort obfuscated identifiers by usage frequency so the most-used names get the lowest numbers")]
    public bool UsePsiFrequencySorting;

    [SettingsEntry(true, "Use PSI reference resolution to automatically preserve identifiers from well-known assemblies (System.*, Serilog, Newtonsoft, etc.) without manual whitelist maintenance")]
    public bool UseAssemblyResolution;

    [SettingsEntry("", "Comma-separated list of additional identifiers to preserve (not obfuscate)")]
    public string CustomWhitelist;

    [SettingsEntry(CSharpIdentifierData.DefaultBaseWhitelist, "Comma-separated base list of identifiers to preserve (C# keywords and BCL types)")]
    public string BaseWhitelist;

    [SettingsEntry(CSharpIdentifierData.DefaultWellKnownNamespaceRoots, "Comma-separated list of well-known namespace root segments used for assembly-resolution auto-preservation")]
    public string WellKnownNamespaceRoots;
}
