#nullable enable
using System.Linq;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Files;
using JetBrains.ReSharper.Resources.Shell;
using JetBrains.ReSharper.TestFramework;
using NUnit.Framework;
using ReSharperPlugin.LLMask.Obfuscation;

namespace ReSharperPlugin.LLMask.Tests.Obfuscation;

/// <summary>
/// PSI-based obfuscation tests.  Each test loads a real C# source file through
/// the ReSharper PSI stack and asserts properties of the obfuscated output.
///
/// Test data files live under test/data/Obfuscation/Psi/.
/// </summary>
[TestFixture]
public class PsiBasedObfuscatorTests : BaseTestWithSingleProject
{
    protected override string RelativeTestDataPath => "Obfuscation/Psi";

    // ── Helper ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens <paramref name="fileName"/> (relative to RelativeTestDataPath) as a
    /// single-project solution, obfuscates it, and returns the result string.
    /// </summary>
    private string ObfuscateFile(string fileName) =>
        ObfuscateFileWithOptions(fileName);

    private string ObfuscateFileWithOptions(
        string fileName,
        bool sortByFrequency = true,
        bool useAssemblyResolution = true)
    {
        var result = string.Empty;

        WithSingleProject(fileName, (_, _, project) =>
        {
            using (ReadLockCookie.Create())
            {
                var projectFile = project.GetSubItems()
                    .OfType<IProjectFile>()
                    .Single();

                var psiFile = projectFile
                    .ToSourceFile()!
                    .GetPrimaryPsiFile() as ICSharpFile;

                result = PsiBasedObfuscator.Obfuscate(
                    psiFile!,
                    sortByFrequency: sortByFrequency,
                    useAssemblyResolution: useAssemblyResolution);
            }
        });

        return result;
    }

    // ── Frequency ordering ────────────────────────────────────────────────────

    [Test]
    [Description("A method declared later in the file but called more often must get a lower placeholder number")]
    public void FrequencyOrdering_MoreFrequentMethod_GetsLowerNumber()
    {
        var output = ObfuscateFile("FrequencyOrdering.cs");
        // 'Common' is called 5 times, 'Rare' only once.
        // Despite 'Rare' being declared first, 'Common' must get SomeMethod1.
        Assert.That(output, Does.Contain("SomeMethod1()").And.Not.Contain("SomeMethod1\n"),
            "'Common' (5 calls) must be assigned SomeMethod1");
        Assert.That(output, Does.Contain("SomeMethod2()"),
            "'Rare' (1 call) must be assigned SomeMethod2");
    }

    [Test]
    [Description("The less-frequent method declared first must not 'steal' the SomeMethod1 slot")]
    public void FrequencyOrdering_LessFrequentMethod_DoesNotGetLowestNumber()
    {
        var output = ObfuscateFile("FrequencyOrdering.cs");
        // 'Rare' must not be SomeMethod1 — that belongs to the most-used method.
        var rareCount = output.Split(["SomeMethod1"], System.StringSplitOptions.None).Length - 1;
        // SomeMethod1 appears: 1 declaration + 5 call sites = 6 times (for 'Common')
        Assert.That(rareCount, Is.GreaterThan(1),
            "SomeMethod1 should appear multiple times because it maps to the heavily-used 'Common'");
    }

    // ── For loop ──────────────────────────────────────────────────────────────

    [Test]
    [Description("The for-loop counter 'i' must receive a semantic 'index' label, not pass through as-is")]
    public void ForLoop_Counter_GetsIndexLabel()
    {
        var output = ObfuscateFile("ForLoop.cs");
        Assert.That(output, Does.Contain("index1"),
            "for-loop counter 'i' should be labelled 'index1'");
    }

    [Test]
    [Description("A variable declared inside the for-loop body is not the counter; it must get 'localVar', not 'index'")]
    public void ForLoop_BodyVariable_GetsLocalVarLabel()
    {
        var output = ObfuscateFile("ForLoop.cs");
        Assert.That(output, Does.Contain("localVar1"),
            "'temp' inside the for-loop body should be labelled 'localVar1'");
    }

    [Test]
    [Description("Only one variable in ForLoop.cs should receive an 'index' label — the loop counter itself")]
    public void ForLoop_OnlyCounter_GetsIndexLabel_NotBodyVar()
    {
        var output = ObfuscateFile("ForLoop.cs");
        // 'index1' for 'i' is expected; 'index2' would mean 'temp' was mis-labelled
        Assert.That(output, Does.Not.Contain("index2"),
            "'temp' must not be labelled 'index2'; only the loop counter earns the 'index' prefix");
    }

    [Test]
    [Description("'i' appears in the initialiser, condition, increment and body — all occurrences must be the same placeholder")]
    public void ForLoop_CounterOccurrences_AllMapToSamePlaceholder()
    {
        var output = ObfuscateFile("ForLoop.cs");
        // ForLoop.cs: for (int i = 0; i < 10; i++) { int temp = i * 2; }
        // 'i' appears at least 4 times; each must become 'index1'
        var count = output.Split(["index1"], System.StringSplitOptions.None).Length - 1;
        Assert.That(count, Is.GreaterThanOrEqualTo(4),
            "All occurrences of 'i' must map to 'index1'");
    }

    // ── Foreach loop ──────────────────────────────────────────────────────────

    [Test]
    [Description("The foreach iteration variable 'item' must receive the 'element' semantic label")]
    public void ForeachLoop_IterationVariable_GetsElementLabel()
    {
        var output = ObfuscateFile("ForeachLoop.cs");
        Assert.That(output, Does.Contain("element1"),
            "foreach iteration variable 'item' should be labelled 'element1'");
    }

    [Test]
    [Description("A plain local variable declared before the foreach (not the iterator) must get 'localVar'")]
    public void ForeachLoop_CollectionVariable_GetsLocalVarLabel()
    {
        var output = ObfuscateFile("ForeachLoop.cs");
        Assert.That(output, Does.Contain("localVar1"),
            "'items' (the collection variable) should be labelled 'localVar1'");
    }

    [Test]
    [Description("A local variable declared inside the foreach body must get 'localVar', not 'element'")]
    public void ForeachLoop_BodyVariable_GetsLocalVarLabel()
    {
        var output = ObfuscateFile("ForeachLoop.cs");
        Assert.That(output, Does.Contain("localVar2"),
            "'result' inside the foreach body should be labelled 'localVar2'");
    }

    [Test]
    [Description("Well-known 'using System.Collections.Generic;' must survive verbatim in the foreach test")]
    public void ForeachLoop_WellKnownUsing_IsPreservedVerbatim()
    {
        var output = ObfuscateFile("ForeachLoop.cs");
        Assert.That(output, Does.Contain("using System.Collections.Generic;"),
            "Well-known namespace directives must be emitted verbatim");
    }

    // ── Lambda ────────────────────────────────────────────────────────────────

    [Test]
    [Description("Even a single-char lambda parameter must get the 'element' semantic label (not passed through as 'x')")]
    public void Lambda_SingleCharParameter_GetsElementLabel()
    {
        var output = ObfuscateFile("Lambda.cs");
        Assert.That(output, Does.Contain("element1"),
            "Lambda parameter 'x' should be labelled 'element1', not emitted verbatim");
    }

    [Test]
    [Description("Lambda parameter must NOT fall through to the verbatim single-char path")]
    public void Lambda_Parameter_IsNotPassedThroughVerbatim()
    {
        var output = ObfuscateFile("Lambda.cs");
        // 'x' in 'x => x.Length > 0' must be replaced; if it stayed 'x' the assertion fails
        Assert.That(output, Does.Not.Match(@"\bx\s*=>"),
            "'x' in the lambda must be replaced by its placeholder, not emitted as-is");
    }

    [Test]
    [Description("Multiple well-known usings at the top of Lambda.cs must all be preserved")]
    public void Lambda_WellKnownUsings_ArePreservedVerbatim()
    {
        var output = ObfuscateFile("Lambda.cs");
        Assert.That(output, Does.Contain("using System.Collections.Generic;"));
        Assert.That(output, Does.Contain("using System.Linq;"));
    }

    // ── Method parameters ─────────────────────────────────────────────────────

    [Test]
    [Description("Method parameters must receive the 'param' prefix, numbered in declaration order")]
    public void MethodParams_BothParams_GetParamLabels()
    {
        var output = ObfuscateFile("MethodParams.cs");
        Assert.That(output, Does.Contain("param1"),
            "First parameter 'count' should be labelled 'param1'");
        Assert.That(output, Does.Contain("param2"),
            "Second parameter 'name' should be labelled 'param2'");
    }

    [Test]
    [Description("A local variable computed from the parameters must get 'localVar', not 'param'")]
    public void MethodParams_LocalVariable_GetsLocalVarLabel()
    {
        var output = ObfuscateFile("MethodParams.cs");
        Assert.That(output, Does.Contain("localVar1"),
            "'result' (the computed local) should be labelled 'localVar1'");
    }

    [Test]
    [Description("Parameter references inside the method body must reuse the same placeholder assigned at the declaration site")]
    public void MethodParams_ParameterUsages_ConsistentWithDeclaration()
    {
        var output = ObfuscateFile("MethodParams.cs");
        // MethodParams.cs: var result = count + name.Length;
        // 'count' → param1, 'name' → param2; 'result' → localVar1
        // Check all three appear (which implies the usages in the body map correctly)
        Assert.That(output, Does.Contain("param1 + param2"),
            "Body usage of parameters must be the same placeholder as the declaration");
    }

    // ── Comments ──────────────────────────────────────────────────────────────

    [Test]
    [Description("Single-line // comments must be stripped entirely from the output")]
    public void Comments_LineComment_IsRemoved()
    {
        var output = ObfuscateFile("Comments.cs");
        Assert.That(output, Does.Not.Contain("This is a line comment"),
            "Comment text must not appear in the output");
        Assert.That(output, Does.Not.Contain("inline comment"),
            "Inline comment text must not appear in the output");
        Assert.That(output, Does.Not.Contain("//"),
            "The '//' token itself must be dropped — comments are removed, not replaced");
    }

    [Test]
    [Description("XML doc comments (///) must also be removed entirely")]
    public void Comments_XmlDocComment_IsRemoved()
    {
        var output = ObfuscateFile("Comments.cs");
        Assert.That(output, Does.Not.Contain("summary"),
            "XML doc comment contents must not appear in the output");
    }

    [Test]
    [Description("Block /* */ comments must be stripped entirely from the output")]
    public void Comments_BlockComment_IsRemoved()
    {
        var output = ObfuscateFile("Comments.cs");
        Assert.That(output, Does.Not.Contain("block comment"),
            "Block comment text must not appear in the output");
        Assert.That(output, Does.Not.Contain("/*"),
            "The '/*' token itself must be dropped — block comments are removed, not replaced");
    }

    // ── Single-character literals ─────────────────────────────────────────────

    [Test]
    [Description("Char literals must always pass through verbatim")]
    public void SingleCharLiterals_CharLiteral_IsPreservedVerbatim()
    {
        var output = ObfuscateFile("SingleCharLiterals.cs");
        Assert.That(output, Does.Contain("'x'"),
            "Char literal 'x' must appear verbatim in the output");
    }

    [Test]
    [Description("A single-character regular string literal must pass through verbatim")]
    public void SingleCharLiterals_SingleCharRegularString_IsPreservedVerbatim()
    {
        var output = ObfuscateFile("SingleCharLiterals.cs");
        Assert.That(output, Does.Contain("\"a\""),
            "Single-char string literal \"a\" must appear verbatim in the output");
    }

    [Test]
    [Description("A single-character verbatim string literal must pass through verbatim")]
    public void SingleCharLiterals_SingleCharVerbatimString_IsPreservedVerbatim()
    {
        var output = ObfuscateFile("SingleCharLiterals.cs");
        Assert.That(output, Does.Contain("@\"z\""),
            "Single-char verbatim string literal @\"z\" must appear verbatim in the output");
    }

    [Test]
    [Description("Multi-character string literals must still be replaced even when single-char ones are preserved")]
    public void SingleCharLiterals_MultiCharStrings_AreObfuscated()
    {
        var output = ObfuscateFile("SingleCharLiterals.cs");
        Assert.That(output, Does.Not.Contain("\"hello\""),
            "Multi-char string literal \"hello\" must be replaced");
        Assert.That(output, Does.Not.Contain("\"world\""),
            "Multi-char verbatim string @\"world\" must be replaced");
        Assert.That(output, Does.Contain("\"someString"),
            "Multi-char strings must produce a 'someString' placeholder");
    }

    // ── Using directives ──────────────────────────────────────────────────────

    [Test]
    [Description("Well-known namespace usings (System.*, Serilog, etc.) must be emitted verbatim")]
    public void Usings_WellKnown_ArePreservedVerbatim()
    {
        var output = ObfuscateFile("UnknownUsing.cs");
        Assert.That(output, Does.Contain("using System.Collections.Generic;"),
            "System.* usings must be preserved verbatim");
        Assert.That(output, Does.Contain("using System.Linq;"),
            "System.Linq must be preserved verbatim");
    }

    [Test]
    [Description("Proprietary/unknown namespace usings must be collapsed to 'using SomeNamespace;'")]
    public void Usings_Unknown_AreReplaced()
    {
        var output = ObfuscateFile("UnknownUsing.cs");
        Assert.That(output, Does.Not.Contain("MyCompany"),
            "Proprietary namespace 'MyCompany.*' must not appear in the output");
        Assert.That(output, Does.Contain("using SomeNamespace;"),
            "Unknown namespace usings must be replaced with 'using SomeNamespace;'");
    }

    [Test]
    [Description("Multiple well-known usings must each appear exactly once (no deduplication of distinct directives)")]
    public void Usings_MultipleWellKnown_EachAppearsOnce()
    {
        var output = ObfuscateFile("UnknownUsing.cs");
        var genericCount = output.Split(["using System.Collections.Generic;"],
            System.StringSplitOptions.None).Length - 1;
        var linqCount = output.Split(["using System.Linq;"],
            System.StringSplitOptions.None).Length - 1;
        Assert.That(genericCount, Is.EqualTo(1));
        Assert.That(linqCount, Is.EqualTo(1));
    }

    // ── Assembly resolution ───────────────────────────────────────────────────

    [Test]
    [Description("A proprietary method must always be obfuscated, regardless of the assembly-resolution setting")]
    public void AssemblyResolution_CustomMethod_IsAlwaysObfuscated()
    {
        var output = ObfuscateFile("AssemblyResolution.cs");
        Assert.That(output, Does.Not.Contain("ProcessItems"),
            "Proprietary method 'ProcessItems' must be replaced with a placeholder");
    }

    [Test]
    [Description("BCL method names must appear verbatim when assembly resolution is enabled (the default)")]
    public void AssemblyResolution_BclMethodNames_ArePreservedWhenEnabled()
    {
        var output = ObfuscateFile("AssemblyResolution.cs"); // useAssemblyResolution = true (default)
        Assert.That(output, Does.Contain("IsNullOrEmpty"),
            "BCL method 'string.IsNullOrEmpty' must be preserved verbatim");
        Assert.That(output, Does.Contain("WriteLine"),
            "BCL method 'Console.WriteLine' must be preserved verbatim");
        Assert.That(output, Does.Contain("ToUpper"),
            "BCL method 'string.ToUpper' must be preserved verbatim");
    }

    [Test]
    [Description("BCL method names must be obfuscated when assembly resolution is disabled")]
    public void AssemblyResolution_BclMethodNames_AreObfuscatedWhenDisabled()
    {
        var output = ObfuscateFileWithOptions("AssemblyResolution.cs", useAssemblyResolution: false);
        Assert.That(output, Does.Not.Contain("IsNullOrEmpty"),
            "BCL method 'IsNullOrEmpty' must be replaced when assembly resolution is off");
        Assert.That(output, Does.Not.Contain("WriteLine"),
            "BCL method 'WriteLine' must be replaced when assembly resolution is off");
        Assert.That(output, Does.Not.Contain("ToUpper"),
            "BCL method 'ToUpper' must be replaced when assembly resolution is off");
    }

    [Test]
    [Description("Assembly resolution must not affect how proprietary names are obfuscated — they must still get consistent placeholders")]
    public void AssemblyResolution_CustomMethod_GetsPlaceholderNotVerbatim()
    {
        var output = ObfuscateFile("AssemblyResolution.cs");
        Assert.That(output, Does.Contain("SomeMethod"),
            "Proprietary method must be replaced with a 'SomeMethod' placeholder");
    }
}
