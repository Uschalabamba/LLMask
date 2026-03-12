#nullable enable
using System.Linq;
using JetBrains.Application.Components;
using JetBrains.Application.Parts;
using JetBrains.Application.Settings;
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
        ISettingsStore settingsStore
#if RIDER
        , LLMaskModel model
#endif
    )
    {
#if RIDER
        // SetSync handler runs on the RD wire thread (background); PSI read lock
        // can be acquired directly from there.
        model.ObfuscateFile.SetSync((lt, filePath) =>
        {
            var settings = settingsStore
                .BindToContextTransient(ContextRange.ApplicationWide)
                .GetKey<LLMaskSettings>(SettingsOptimization.DoMeSlowly);

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

                return PsiBasedObfuscator.Obfuscate(psiFile); //, extraWords, baseWords);
            }
        });
#endif
    }
}
