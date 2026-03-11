using System.Windows;
using System.Windows.Controls;
using JetBrains.Application.DataContext;
using JetBrains.Application.UI.Actions;
using JetBrains.Application.UI.ActionsRevised.Menu;
using JetBrains.Diagnostics;

namespace ReSharperPlugin.LLMask
{
    [Action("LLMask.HelloWorld", "LLMask: Hello World")]
    public class HelloWorldAction : IExecutableAction
    {
        private static readonly ILog Log = JetBrains.Diagnostics.Log.GetLog<HelloWorldAction>();

        public bool Update(IDataContext context, ActionPresentation presentation, DelegateUpdate nextUpdate)
        {
            return true;
        }

        public void Execute(IDataContext context, DelegateExecute nextExecute)
        {
            Log.Info("LLMask HelloWorldAction executed");
            var window = new Window
            {
                Title = "LLMask",
                Width = 320,
                Height = 120,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize
            };

            var textBox = new TextBox
            {
                Text = "Hello World",
                IsReadOnly = true,
                Margin = new Thickness(10),
                FontSize = 14,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            window.Content = textBox;
            window.ShowDialog();
        }
    }
}
