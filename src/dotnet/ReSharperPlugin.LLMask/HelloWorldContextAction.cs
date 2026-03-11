using System;
using JetBrains.Application.Progress;
using JetBrains.Diagnostics;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.ContextActions;
using JetBrains.ReSharper.Feature.Services.CSharp.ContextActions;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.TextControl;
using JetBrains.Util;

namespace ReSharperPlugin.LLMask
{
    [ContextAction(
        Name = "LLMask.HelloWorldContextAction",
        Description = "Show Hello World (LLMask)",
        GroupType = typeof(CSharpContextActions),
        Disabled = false,
        Priority = 1)]
    public class HelloWorldContextAction : ContextActionBase
    {
        private static readonly ILog Log = JetBrains.Diagnostics.Log.GetLog<HelloWorldContextAction>();

        private readonly ICSharpContextActionDataProvider _provider;

        public HelloWorldContextAction(ICSharpContextActionDataProvider provider)
        {
            _provider = provider;
        }

        public override string Text => "LLMask: Copy selection to clipboard";

        public override bool IsAvailable(IUserDataHolder cache)
        {
            return _provider.GetSelectedElement<ITreeNode>() != null
                   && !_provider.DocumentSelection.IsEmpty;
        }

        protected override Action<ITextControl> ExecutePsiTransaction(ISolution solution, IProgressIndicator progress)
        {
            var selectionRange = _provider.DocumentSelection;
            return textControl =>
            {
                var selectedText = textControl.Document.GetText(selectionRange.TextRange);
                Log.Info($"LLMask copying {selectedText.Length} characters to clipboard");
                System.Windows.Clipboard.SetText(selectedText);
            };
        }
    }
}
