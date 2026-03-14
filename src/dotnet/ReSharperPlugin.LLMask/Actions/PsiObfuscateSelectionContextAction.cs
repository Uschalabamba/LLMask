using System;
using System.Linq;
using JetBrains.Application.Progress;
using JetBrains.Application.Settings;
using JetBrains.Diagnostics;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.ContextActions;
using JetBrains.ReSharper.Feature.Services.CSharp.ContextActions;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.TextControl;
using JetBrains.Util;
using ReSharperPlugin.LLMask.Data;
using ReSharperPlugin.LLMask.Obfuscation;
using ReSharperPlugin.LLMask.Settings;

namespace ReSharperPlugin.LLMask;

[ContextAction(
    Name        = "LLMask.PsiObfuscateSelectionContextAction",
    Description = "Obfuscate selected C# code using full PSI analysis and copy to clipboard (LLMask)",
    GroupType   = typeof(CSharpContextActions),
    Disabled    = false,
    Priority    = 1)]
public class PsiObfuscateSelectionContextAction(ICSharpContextActionDataProvider provider)
    : ContextActionBase
{
    private static readonly ILog log = Log.GetLog<PsiObfuscateSelectionContextAction>();

    public override string Text => "LLMask: Obfuscate selection (PSI)";

    public override bool IsAvailable(IUserDataHolder cache)
    {
        var element = provider.GetSelectedElement<ITreeNode>();
        if (element == null || provider.DocumentSelection.IsEmpty)
            return false;

        // Don't inject ISettingsStore in the constructor — that silently breaks
        // the context-action factory (see memory note).
        var settings = element.GetSolution()
            .GetComponent<ISettingsStore>()
            .BindToContextTransient(ContextRange.ApplicationWide)
            .GetKey<LLMaskSettings>(SettingsOptimization.DoMeSlowly);

        return settings.SelectionMode == SelectionObfuscatorMode.PsiBased;
    }

    protected override Action<ITextControl> ExecutePsiTransaction(
        ISolution solution, IProgressIndicator progress)
    {
        var psiFile = provider.GetSelectedElement<ITreeNode>()
            ?.GetContainingFile() as ICSharpFile;
        if (psiFile == null)
            return _ => { };

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

        var selectionRange = provider.DocumentSelection.TextRange;

        // All PSI work happens inside ExecutePsiTransaction where a read lock is held.
        // The returned lambda only touches the clipboard (UI-safe, no PSI access).
        var result = PartialPsiBasedObfuscator.ObfuscateSelection(
            psiFile,
            selectionRange,
            extraWords,
            config.BaseWhitelist,
            settings.UsePsiFrequencySorting,
            settings.UseAssemblyResolution,
            config.WellKnownNamespaceRoots);

        log.Info($"LLMask PSI-obfuscated selection ({selectionRange.Length} chars), copied to clipboard");
        return _ => System.Windows.Clipboard.SetText(result);
    }
}
