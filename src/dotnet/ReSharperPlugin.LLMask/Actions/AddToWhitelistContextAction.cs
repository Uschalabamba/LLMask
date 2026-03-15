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
    Name = "LLMask.AddToWhitelistContextAction",
    Description = "Add the identifier under caret to the LLMask custom whitelist",
    GroupType = typeof(CSharpContextActions),
    Disabled = false,
    Priority = 1)]
public class AddToWhitelistContextAction(ICSharpContextActionDataProvider provider) : ContextActionBase
{
    private static readonly ILog log = Log.GetLog<AddToWhitelistContextAction>();

    // Captured in IsAvailable, consumed in ExecutePsiTransaction.
    private string? tokenName;

    public override string Text =>
        this.tokenName != null ? $"LLMask: Add \"{this.tokenName}\" to whitelist" : "LLMask: Add to whitelist";

    public override bool IsAvailable(IUserDataHolder cache)
    {
        // Must be on an identifier node.
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

        // Only show when at least one obfuscation mode is active.
        var settings = identifier.GetSolution()
            .GetComponent<ISettingsStore>()
            .BindToContextTransient(ContextRange.ApplicationWide)
            .GetKey<LLMaskSettings>(SettingsOptimization.DoMeSlowly);

        // Don't show if already in the custom whitelist.
        if (ParseWhitelist(settings.CustomWhitelist).Contains(name, StringComparer.Ordinal))
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
            var words = ParseWhitelist(current).ToList();

            if (!words.Contains(name, StringComparer.Ordinal))
            {
                words.Add(name);
                store.SetValue((LLMaskSettings s) => s.CustomWhitelist, string.Join(", ", words));
                log.Info($"LLMask: added \"{name}\" to custom whitelist");
            }
        };
    }

    private static IEnumerable<string> ParseWhitelist(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? Enumerable.Empty<string>()
            : raw!.Split(',').Select(w => w.Trim()).Where(w => w.Length > 0);
}
