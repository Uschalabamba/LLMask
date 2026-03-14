using JetBrains.Application.UI.Options;
using JetBrains.Application.UI.Options.OptionPages;
using JetBrains.Application.UI.Options.OptionsDialog;
using JetBrains.IDE.UI.Options;
using JetBrains.Lifetimes;

namespace ReSharperPlugin.LLMask.Settings;

[OptionsPage(
    PageId,
    "LLMask",
    null,
    ParentId = EnvironmentPage.Pid)]
public class LLMaskOptionsPage : BeSimpleOptionsPage
{
    public const string PageId = "LLMask";

    public LLMaskOptionsPage(
        Lifetime lifetime,
        OptionsPageContext optionsPageContext,
        OptionsSettingsSmartContext smartContext)
        : base(lifetime, optionsPageContext, smartContext)
    {
        AddHeader("PSI-Based Obfuscation (full file)");
        AddBoolOption((LLMaskSettings s) => s.UsePsiObfuscation, "Enable PSI-based obfuscation (right-click a .cs file → LLMask: Obfuscate file)");
        AddBoolOption((LLMaskSettings s) => s.UsePsiFrequencySorting, "Sort identifiers by frequency (most-used names get the lowest numbers, e.g. SomeMethod1)");
        AddBoolOption((LLMaskSettings s) => s.UseAssemblyResolution, "Auto-preserve identifiers from well-known assemblies (System.*, Serilog, Newtonsoft, …) without manual whitelist entries");

        AddHeader("Selection Obfuscation Mode");
        AddBoolOption(
            (LLMaskSettings s) => s.UsePsiForSelection,
            "Use PSI-based obfuscation for selections " +
            "(unchecked = fast string-based; checked = full PSI analysis with consistent identifier numbering)");
        AddStringOption((LLMaskSettings s) => s.CustomWhitelist,
            "Additional preserved identifiers (comma-separated) — used by both modes");

        AddHeader("Config File");
        AddCommentText(
            "The base whitelist and well-known namespace roots are loaded from llmask.json at " +
            "runtime. Place the file in your solution root to customize them. If absent, " +
            "built-in defaults are used. The file can be committed to source control for " +
            "per-team customization.");
        AddStringOption((LLMaskSettings s) => s.ConfigFilePath,
            "Custom config file path (absolute). Leave empty to use llmask.json in the solution root.");
    }
}
