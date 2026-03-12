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
    private const string PageId = "LLMask";

    public LLMaskOptionsPage(
        Lifetime lifetime,
        OptionsPageContext optionsPageContext,
        OptionsSettingsSmartContext smartContext)
        : base(lifetime, optionsPageContext, smartContext)
    {
        AddHeader("String-Based Obfuscation");
        AddBoolOption((LLMaskSettings s) => s.UseStringObfuscation, "Enable string-based obfuscation");
        AddStringOption((LLMaskSettings s) => s.CustomWhitelist, "Additional preserved identifiers (comma-separated)");

        AddHeader("Base Whitelist");
        AddCommentText("Warning: Editing these identifiers can have unforeseen consequences and may result in less readable obfuscated output.");
        AddStringOption((LLMaskSettings s) => s.BaseWhitelist, "Base preserved identifiers (comma-separated)");
    }
}
