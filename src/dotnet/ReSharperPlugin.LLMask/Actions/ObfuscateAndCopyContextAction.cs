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
using ReSharperPlugin.LLMask.Data;
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
    {
        var element = provider.GetSelectedElement<ITreeNode>();
        if (element == null || provider.DocumentSelection.IsEmpty)
            return false;

        // Hide the context action entirely when string-based obfuscation is disabled.
        // We read settings via the selected element's solution so we don't inject
        // ISettingsStore in the constructor (which would silently break the factory).
        var settings = element.GetSolution()
            .GetComponent<ISettingsStore>()
            .BindToContextTransient(ContextRange.ApplicationWide)
            .GetKey<LLMaskSettings>(SettingsOptimization.DoMeSlowly);

        return settings.SelectionMode == SelectionObfuscatorMode.StringBased;
    }

    protected override Action<ITextControl> ExecutePsiTransaction(ISolution solution, IProgressIndicator progress)
    {
        var settings = solution.GetComponent<ISettingsStore>()
                               .BindToContextTransient(ContextRange.ApplicationWide)
                               .GetKey<LLMaskSettings>(SettingsOptimization.DoMeSlowly);

        var extraWords = string.IsNullOrWhiteSpace(settings.CustomWhitelist)
            ? null
            : settings.CustomWhitelist
                .Split(',')
                .Select(w => w.Trim())
                .Where(w => w.Length > 0);

        var solutionRoot = solution.SolutionFilePath.Directory.FullPath;
        var config = string.IsNullOrWhiteSpace(settings.ConfigFilePath)
            ? LLMaskDataProvider.Load(solutionRoot)
            : LLMaskDataProvider.LoadFromFile(settings.ConfigFilePath);

        var selectionRange = provider.DocumentSelection;

        return textControl =>
        {
            var selectedText = textControl.Document.GetText(selectionRange.TextRange);
            var (obfuscated, mapping) = StringBasedObfuscator.Obfuscate(selectedText, extraWords, config.BaseWhitelist);
            LLMaskMappingStore.AppendSession(solutionRoot, mapping);
            log.Info($"LLMask obfuscated selection: {selectedText.Length} → {obfuscated.Length} chars, session {mapping.SessionId}, copied to clipboard");
            System.Windows.Clipboard.SetText(mapping.MarkerLine + "\n" + obfuscated);
        };
    }
}
