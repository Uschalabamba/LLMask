#nullable enable
using System.Linq;
using JetBrains.Application.Components;
using JetBrains.Application.Parts;
using JetBrains.Application.Settings;
using JetBrains.Application.Threading;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.Rd.Tasks;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Files;
using JetBrains.ReSharper.Resources.Shell;
using JetBrains.Util;
using ReSharperPlugin.LLMask.Obfuscation;
using ReSharperPlugin.LLMask.Settings;

#if RIDER
using JetBrains.Rider.Model;
#endif

namespace ReSharperPlugin.LLMask;

[SolutionComponent(Instantiation.DemandAnyThreadSafe)]
public class LLMaskHost : IStartupActivity
{
    public LLMaskHost(
        Lifetime lifetime,
        ISolution solution,
        ISettingsStore settingsStore,
        IShellLocks shellLocks
#if RIDER
        , LLMaskModel model
#endif
    )
    {
#if RIDER
        // RdProperty.Value must be set on the Shell Rd Dispatcher (main thread).
        // The constructor may run on a pool thread, so we always dispatch.
        shellLocks.ExecuteOrQueue(lifetime, "LLMask: init PSI enabled state",
            () => PushPsiEnabled(model, settingsStore));

        // Keep the property in sync: re-push whenever any setting changes.
        settingsStore.Changed.Advise(lifetime, _ =>
            shellLocks.ExecuteOrQueue(lifetime, "LLMask: update PSI enabled state",
                () => PushPsiEnabled(model, settingsStore)));

        // SetSync handler runs on the RD wire thread (background); PSI read lock
        // can be acquired directly from there.
        model.ObfuscateFile.SetSync((lt, filePath) =>
        {
            var settings = settingsStore
                .BindToContextTransient(ContextRange.ApplicationWide)
                .GetKey<LLMaskSettings>(SettingsOptimization.DoMeSlowly);

            if (!settings.UsePsiObfuscation)
                return string.Empty;

            var extraWords = string.IsNullOrWhiteSpace(settings.CustomWhitelist) ? null
                : settings.CustomWhitelist.Split(',').Select(w => w.Trim()).Where(w => w.Length > 0);
            var baseWords = string.IsNullOrWhiteSpace(settings.BaseWhitelist) ? null
                : settings.BaseWhitelist.Split(',').Select(w => w.Trim()).Where(w => w.Length > 0);

            using (ReadLockCookie.Create())
            {
                var path = VirtualFileSystemPath.TryParse(
                    filePath, InteractionContext.SolutionContext, FileSystemPathInternStrategy.TRY_GET_INTERNED_BUT_DO_NOT_INTERN);
                if (path.IsNullOrEmpty()) return string.Empty;

                var projectFile = solution.FindProjectItemsByLocation(path)
                    .OfType<IProjectFile>()
                    .FirstOrDefault();

                var psiFile = projectFile?.ToSourceFile()?.GetPrimaryPsiFile() as ICSharpFile;
                if (psiFile == null) return string.Empty;

                return PsiBasedObfuscator.Obfuscate(psiFile, extraWords, baseWords, settings.UsePsiFrequencySorting);
            }
        });
#endif
    }

#if RIDER
    private static void PushPsiEnabled(LLMaskModel model, ISettingsStore settingsStore)
    {
        var enabled = settingsStore
            .BindToContextTransient(ContextRange.ApplicationWide)
            .GetKey<LLMaskSettings>(SettingsOptimization.DoMeSlowly)
            .UsePsiObfuscation;
        model.IsPsiObfuscationEnabled.Value = enabled;
    }
#endif
}
