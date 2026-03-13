#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Application.Progress;
using JetBrains.Application.Settings;
using JetBrains.Diagnostics;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.ContextActions;
using JetBrains.ReSharper.Feature.Services.CSharp.ContextActions;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.Parsing;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.TextControl;
using JetBrains.Util;
using ReSharperPlugin.LLMask.Settings;

namespace ReSharperPlugin.LLMask;

[ContextAction(
    Name = "LLMask.AddSelectionToWhitelistContextAction",
    Description = "Add all identifiers in the current selection to the LLMask custom whitelist",
    GroupType = typeof(CSharpContextActions),
    Disabled = false,
    Priority = 1)]
public class AddSelectionToWhitelistContextAction(ICSharpContextActionDataProvider provider) : ContextActionBase
{
    private static readonly ILog Log = JetBrains.Diagnostics.Log.GetLog<AddSelectionToWhitelistContextAction>();

    // Identifiers to add, computed in IsAvailable.
    private List<string>? _newWords;

    public override string Text =>
        _newWords is { Count: > 0 }
            ? $"LLMask: Add {_newWords.Count} identifier{(_newWords.Count == 1 ? "" : "s")} to whitelist"
            : "LLMask: Add selection identifiers to whitelist";

    public override bool IsAvailable(IUserDataHolder cache)
    {
        // Require a real selection.
        if (provider.DocumentSelection.IsEmpty)
            return false;

        var psiFile = provider.PsiFile as ICSharpFile;
        if (psiFile == null)
            return false;

        var element = provider.GetSelectedElement<ITreeNode>();
        if (element == null)
            return false;

        // Only show when at least one obfuscation mode is active.
        var settings = element.GetSolution()
            .GetComponent<ISettingsStore>()
            .BindToContextTransient(ContextRange.ApplicationWide)
            .GetKey<LLMaskSettings>(SettingsOptimization.DoMeSlowly);

        if (!settings.UseStringObfuscation && !settings.UsePsiObfuscation)
            return false;

        var existing  = ParseWhitelist(settings.CustomWhitelist);
        var baseWords = ParseWhitelist(settings.BaseWhitelist);
        var excluded  = new HashSet<string>(existing.Concat(baseWords), StringComparer.Ordinal);

        _newWords = CollectNewIdentifiers(psiFile, provider.DocumentSelection.TextRange, excluded);
        return _newWords.Count > 0;
    }

    protected override Action<ITextControl> ExecutePsiTransaction(ISolution solution, IProgressIndicator progress)
    {
        var toAdd = _newWords;

        return _ =>
        {
            if (toAdd == null || toAdd.Count == 0) return;

            var store = solution.GetComponent<ISettingsStore>()
                .BindToContextTransient(ContextRange.ApplicationWide);

            var current = store.GetKey<LLMaskSettings>(SettingsOptimization.DoMeSlowly).CustomWhitelist;
            var existing = new HashSet<string>(ParseWhitelist(current), StringComparer.Ordinal);

            // Re-check at write time in case another action ran concurrently.
            var actuallyNew = toAdd.Where(w => !existing.Contains(w)).ToList();
            if (actuallyNew.Count == 0) return;

            existing.UnionWith(actuallyNew);
            store.SetValue((LLMaskSettings s) => s.CustomWhitelist,
                string.Join(", ", existing.OrderBy(w => w, StringComparer.Ordinal)));

            Log.Info($"LLMask: added {actuallyNew.Count} identifiers to whitelist: {string.Join(", ", actuallyNew)}");
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// Walks PSI identifier tokens whose start offset falls within
    /// <paramref name="selRange"/>, excludes single-char tokens and anything
    /// already in <paramref name="excluded"/>, and returns deduplicated results.
    private static List<string> CollectNewIdentifiers(
        ICSharpFile psiFile,
        TextRange selRange,
        HashSet<string> excluded)
    {
        var seen   = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        foreach (var token in psiFile.Descendants<ITokenNode>())
        {
            if (token.GetTokenType() != CSharpTokenType.IDENTIFIER)
                continue;

            // Only tokens that start inside the selection.
            var tokenRange = token.GetDocumentRange().TextRange;
            if (!selRange.Contains(tokenRange.StartOffset))
                continue;

            var name = token.GetText();

            // Skip single-character identifiers — they carry no proprietary information.
            if (name.Length <= 1)
                continue;

            if (excluded.Contains(name))
                continue;

            if (seen.Add(name))
                result.Add(name);
        }

        return result;
    }

    private static IEnumerable<string> ParseWhitelist(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? Enumerable.Empty<string>()
            : raw!.Split(',').Select(w => w.Trim()).Where(w => w.Length > 0);
}
