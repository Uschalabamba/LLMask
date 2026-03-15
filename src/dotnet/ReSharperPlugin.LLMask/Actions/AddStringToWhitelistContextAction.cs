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
using JetBrains.ReSharper.Psi.CSharp.Parsing;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.TextControl;
using JetBrains.Util;
using ReSharperPlugin.LLMask.Obfuscation;
using ReSharperPlugin.LLMask.Settings;

namespace ReSharperPlugin.LLMask;

[ContextAction(
    Name = "LLMask.AddStringToWhitelistContextAction",
    Description = "Add the string literal under caret to the LLMask custom string whitelist",
    GroupType = typeof(CSharpContextActions),
    Disabled = false,
    Priority = 1)]
public class AddStringToWhitelistContextAction(ICSharpContextActionDataProvider provider) : ContextActionBase
{
    private static readonly ILog log = Log.GetLog<AddStringToWhitelistContextAction>();

    // String content (without quotes) captured in IsAvailable, consumed in ExecutePsiTransaction.
    private string? stringContent;

    public override string Text =>
        this.stringContent != null
            ? $"LLMask: Add \"{Truncate(this.stringContent, 30)}\" to string whitelist"
            : "LLMask: Add string to whitelist";

    public override bool IsAvailable(IUserDataHolder cache)
    {
        // Must be on a string literal token.
        var literal = provider.GetSelectedElement<ILiteralExpression>();
        if (literal == null)
            return false;

        var tokenType = literal.Literal.GetTokenType();
        if (!tokenType.IsStringLiteralToken())
            return false;

        var content = StringBasedObfuscator.ExtractStringContent(literal.Literal.GetText());

        // Single-char strings are never obfuscated — no need to whitelist them.
        if (content.Length <= 1)
            return false;

        // Don't show if already in the custom string whitelist.
        var settings = literal.GetSolution()
            .GetComponent<ISettingsStore>()
            .BindToContextTransient(ContextRange.ApplicationWide)
            .GetKey<LLMaskSettings>(SettingsOptimization.DoMeSlowly);

        if (ParseStringWhitelist(settings.CustomStringWhitelist).Contains(content, StringComparer.Ordinal))
            return false;

        this.stringContent = content;
        return true;
    }

    protected override Action<ITextControl> ExecutePsiTransaction(ISolution solution, IProgressIndicator progress)
    {
        var content = this.stringContent;

        return _ =>
        {
            if (content == null)
                return;

            var store = solution.GetComponent<ISettingsStore>()
                .BindToContextTransient(ContextRange.ApplicationWide);

            var current = store.GetKey<LLMaskSettings>(SettingsOptimization.DoMeSlowly).CustomStringWhitelist;
            var entries = ParseStringWhitelist(current).ToList();

            if (!entries.Contains(content, StringComparer.Ordinal))
            {
                entries.Add(content);
                store.SetValue((LLMaskSettings s) => s.CustomStringWhitelist, string.Join(", ", entries));
                log.Info($"LLMask: added string \"{content}\" to custom string whitelist");
            }
        };
    }

    private static IEnumerable<string> ParseStringWhitelist(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? Enumerable.Empty<string>()
            : raw!.Split(',').Select(w => w.Trim()).Where(w => w.Length > 0);

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s.Substring(0, maxLen) + "…";
}
