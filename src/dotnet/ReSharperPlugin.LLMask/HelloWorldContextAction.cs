using System;
using JetBrains.Application.Progress;
using JetBrains.Diagnostics;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.ContextActions;
using JetBrains.ReSharper.Feature.Services.CSharp.ContextActions;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.TextControl;
using JetBrains.Util;
using ReSharperPlugin.LLMask.Obfuscation;

namespace ReSharperPlugin.LLMask;

[ContextAction(
    Name = "LLMask.ObfuscateAndCopyContextAction",
    Description = "Obfuscate selected C# code and copy to clipboard (LLMask)",
    GroupType = typeof(CSharpContextActions),
    Disabled = false,
    Priority = 1)]
public class HelloWorldContextAction(ICSharpContextActionDataProvider provider) : ContextActionBase
{
    private static readonly ILog Log = JetBrains.Diagnostics.Log.GetLog<HelloWorldContextAction>();

    public override string Text => "LLMask: Obfuscate and copy to clipboard";

    public override bool IsAvailable(IUserDataHolder cache)
    {
        return provider.GetSelectedElement<ITreeNode>() != null
               && !provider.DocumentSelection.IsEmpty;
    }

    protected override Action<ITextControl> ExecutePsiTransaction(ISolution solution, IProgressIndicator progress)
    {
        var selectionRange = provider.DocumentSelection;
        return textControl =>
        {
            var selectedText = textControl.Document.GetText(selectionRange.TextRange);
            var obfuscated   = CodeObfuscator.Obfuscate(selectedText);
            Log.Info($"LLMask obfuscated selection: {selectedText.Length} → {obfuscated.Length} chars, copied to clipboard");
            System.Windows.Clipboard.SetText(obfuscated);
        };
    }
}