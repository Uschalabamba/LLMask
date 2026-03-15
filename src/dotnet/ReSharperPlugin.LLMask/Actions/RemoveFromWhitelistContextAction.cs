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
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.TextControl;
using JetBrains.Util;
using ReSharperPlugin.LLMask.Settings;

namespace ReSharperPlugin.LLMask;

[ContextAction(
    Name = "LLMask.RemoveFromWhitelistContextAction",
    Description = "Remove the identifier under caret from the LLMask custom whitelist",
    GroupType = typeof(CSharpContextActions),
    Disabled = false,
    Priority = 1)]
public class RemoveFromWhitelistContextAction(ICSharpContextActionDataProvider provider) : ContextActionBase
{
    private static readonly ILog log = Log.GetLog<RemoveFromWhitelistContextAction>();

    private string? tokenName;

    public override string Text => this.tokenName != null ? $"LLMask: Remove \"{this.tokenName}\" from whitelist" : "LLMask: Remove from whitelist";

    public override bool IsAvailable(IUserDataHolder cache)
    {
        var identifier = provider.GetSelectedElement<IIdentifier>();
        if (identifier == null)
        {
            return false;
        }

        var name = identifier.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var settings = identifier.GetSolution()
            .GetComponent<ISettingsStore>()
            .BindToContextTransient(ContextRange.ApplicationWide)
            .GetKey<LLMaskSettings>(SettingsOptimization.DoMeSlowly);

        // Only show when the token is actually in the custom whitelist.
        if (!ParseWhitelist(settings.CustomWhitelist).Contains(name, StringComparer.Ordinal))
        {
            return false;
        }

        this.tokenName = name;
        return true;
    }

    protected override Action<ITextControl> ExecutePsiTransaction(ISolution solution, IProgressIndicator progress)
    {
        var name = this.tokenName;

        return _ =>
        {
            if (name == null)
            {
                return;
            }

            var store = solution.GetComponent<ISettingsStore>()
                .BindToContextTransient(ContextRange.ApplicationWide);

            var current = store.GetKey<LLMaskSettings>(SettingsOptimization.DoMeSlowly).CustomWhitelist;
            var words = ParseWhitelist(current)
                .Where(w => !string.Equals(w, name, StringComparison.Ordinal))
                .ToList();

            store.SetValue((LLMaskSettings s) => s.CustomWhitelist, string.Join(", ", words));
            log.Info($"LLMask: removed \"{name}\" from custom whitelist");
        };
    }

    private static IEnumerable<string> ParseWhitelist(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? Enumerable.Empty<string>()
            : raw!.Split(',').Select(w => w.Trim()).Where(w => w.Length > 0);
}
