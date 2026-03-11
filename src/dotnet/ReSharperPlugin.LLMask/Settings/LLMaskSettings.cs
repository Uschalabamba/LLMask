using JetBrains.Application.Settings;
using JetBrains.Application.Settings.WellKnownRootKeys;

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
}
