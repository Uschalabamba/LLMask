using System;
using System.Linq;
using JetBrains.Application.Progress;
using JetBrains.Application.Settings;
using JetBrains.Diagnostics;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.ContextActions;
using JetBrains.ReSharper.Feature.Services.CSharp.ContextActions;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.TextControl;
using JetBrains.Util;
using ReSharperPlugin.LLMask.Obfuscation;
using ReSharperPlugin.LLMask.Settings;

namespace ReSharperPlugin.LLMask;

[ContextAction(
    Name = "LLMask.ObfuscateAndCopyContextAction",
    Description = "Obfuscate selected C# code and copy to clipboard (LLMask)",
    GroupType = typeof(CSharpContextActions),
    Disabled = false,
    Priority = 1)]
public class ObfuscateAndCopyContextAction(ICSharpContextActionDataProvider provider) : ContextActionBase
{
    private static readonly ILog log = Log.GetLog<ObfuscateAndCopyContextAction>();

    public override string Text => "LLMask: Obfuscate and copy to clipboard";

    public override bool IsAvailable(IUserDataHolder cache)
        => provider.GetSelectedElement<ITreeNode>() != null
           && !provider.DocumentSelection.IsEmpty;

    protected override Action<ITextControl> ExecutePsiTransaction(ISolution solution, IProgressIndicator progress)
    {
        var settings = solution.GetComponent<ISettingsStore>()
                               .BindToContextTransient(ContextRange.ApplicationWide)
                               .GetKey<LLMaskSettings>(SettingsOptimization.DoMeSlowly);
        
        if (!settings.UseStringObfuscation)
        {
            return null;
        }

        var extraWords = string.IsNullOrWhiteSpace(settings.CustomWhitelist)
            ? null
            : settings.CustomWhitelist
                .Split(',')
                .Select(w => w.Trim())
                .Where(w => w.Length > 0);

        var baseWords = string.IsNullOrWhiteSpace(settings.BaseWhitelist)
            ? null
            : settings.BaseWhitelist
                .Split(',')
                .Select(w => w.Trim())
                .Where(w => w.Length > 0);

        var selectionRange = provider.DocumentSelection;
        
        return textControl =>
        {
            var selectedText = textControl.Document.GetText(selectionRange.TextRange);
            var obfuscated   = StringBasedObfuscator.Obfuscate(selectedText, extraWords, baseWords);
            log.Info($"LLMask obfuscated selection: {selectedText.Length} → {obfuscated.Length} chars, copied to clipboard");
            System.Windows.Clipboard.SetText(obfuscated);
        };
    }
}
