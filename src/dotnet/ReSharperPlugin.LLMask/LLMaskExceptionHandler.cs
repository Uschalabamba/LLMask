using System;
using System.Threading.Tasks;
using JetBrains.Application;
using JetBrains.Diagnostics;
using JetBrains.Lifetimes;

namespace ReSharperPlugin.LLMask;

[ShellComponent]
public class LLMaskExceptionHandler
{
    private static readonly ILog Log = JetBrains.Diagnostics.Log.GetLog<LLMaskExceptionHandler>();

    private readonly Lifetime _lifetime;

    public LLMaskExceptionHandler(Lifetime lifetime)
    {
        _lifetime = lifetime;

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        lifetime.OnTermination(() =>
        {
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        });

        Log.Info("LLMask exception handler registered");
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (!_lifetime.IsAlive) return;
        var ex = e.ExceptionObject as Exception;
        try
        {
            Log.Error($"LLMask: Unhandled exception (IsTerminating={e.IsTerminating})", ex);
        }
        catch
        {
            // Suppress - log infrastructure may be disposed during shutdown
        }
    }

    private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved(); // Mark observed first so the process cannot crash regardless of what follows
        if (!_lifetime.IsAlive) return;
        try
        {
            Log.Error("LLMask: Unobserved task exception", e.Exception);
        }
        catch
        {
            // Suppress - log infrastructure may be disposed during shutdown
        }
    }
}