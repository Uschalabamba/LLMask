#nullable enable
using System.Linq;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Files;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.ReSharper.Resources.Shell;
using JetBrains.ReSharper.TestFramework;
using JetBrains.Util;
using NUnit.Framework;
using ReSharperPlugin.LLMask.Obfuscation;
using System;

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
                    useAssemblyResolution: useAssemblyResolution).output;
            }
        });

        return result;
    }

    /// <summary>
    /// Opens <paramref name="fileName"/>, locates the first method named
    /// <paramref name="methodName"/> via PSI, and returns the result of
    /// <see cref="PartialPsiBasedObfuscator.ObfuscateSelection"/> scoped to
    /// that method's document range.
    /// </summary>
    private string ObfuscateMethodSelection(
        string fileName,
        string methodName,
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

                // JetBrains' Descendants<T>() returns a custom enumerable that
                // doesn't expose LINQ's First(predicate) — use a foreach loop.
                IMethodDeclaration? method = null;
                foreach (var m in psiFile!.Descendants<IMethodDeclaration>())
                    if (m.DeclaredName == methodName) { method = m; break; }
                Assert.That(method, Is.Not.Null, $"Method '{methodName}' not found in {fileName}");

                var range = method.GetDocumentRange().TextRange;

                result = PartialPsiBasedObfuscator.ObfuscateSelection(
                    psiFile!,
                    range,
                    sortByFrequency: sortByFrequency,
                    useAssemblyResolution: useAssemblyResolution).output;
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
        var rareCount = output.Split(["SomeMethod1"], StringSplitOptions.None).Length - 1;
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
        var count = output.Split(["index1"], StringSplitOptions.None).Length - 1;
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
            StringSplitOptions.None).Length - 1;
        var linqCount = output.Split(["using System.Linq;"],
            StringSplitOptions.None).Length - 1;
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

    // ── Assembly resolution: type names (Fix 1 & Fix 2) ──────────────────────

    [Test]
    [Description("BCL type in base-class position must be preserved verbatim (Fix 2: IUserTypeUsage walk)")]
    public void AssemblyResolution_BaseClassTypeName_IsPreservedWhenEnabled()
    {
        var output = ObfuscateFile("AssemblyResolutionTypeNames.cs");
        Assert.That(output, Does.Contain("ApplicationException"),
            "BCL type 'ApplicationException' in base-class position must appear verbatim");
    }

    [Test]
    [Description("BCL type in base-class position must be obfuscated when assembly resolution is disabled")]
    public void AssemblyResolution_BaseClassTypeName_IsObfuscatedWhenDisabled()
    {
        var output = ObfuscateFileWithOptions("AssemblyResolutionTypeNames.cs", useAssemblyResolution: false);
        Assert.That(output, Does.Not.Contain("ApplicationException"),
            "'ApplicationException' must be replaced when assembly resolution is off");
    }

    [Test]
    [Description("BCL type used as a return-type annotation and in a constructor call must be preserved verbatim (Fix 2: IUserTypeUsage walk)")]
    public void AssemblyResolution_ConstructorAndReturnTypeTypeName_IsPreservedWhenEnabled()
    {
        var output = ObfuscateFile("AssemblyResolutionTypeNames.cs");
        Assert.That(output, Does.Contain("FormatException"),
            "BCL type 'FormatException' in return-type and constructor positions must appear verbatim");
    }

    [Test]
    [Description("BCL type in constructor and return-type positions must be obfuscated when assembly resolution is disabled")]
    public void AssemblyResolution_ConstructorAndReturnTypeTypeName_IsObfuscatedWhenDisabled()
    {
        var output = ObfuscateFileWithOptions("AssemblyResolutionTypeNames.cs", useAssemblyResolution: false);
        Assert.That(output, Does.Not.Contain("FormatException"),
            "'FormatException' must be replaced when assembly resolution is off");
    }

    [Test]
    [Description("BCL type used only as a static qualifier must be preserved verbatim (Fix 1: containing-type enrichment when resolving a member)")]
    public void AssemblyResolution_StaticQualifierTypeName_IsPreservedWhenEnabled()
    {
        var output = ObfuscateFile("AssemblyResolutionTypeNames.cs");
        Assert.That(output, Does.Contain("BitConverter"),
            "BCL type 'BitConverter' used as a static qualifier must appear verbatim");
        Assert.That(output, Does.Contain("GetBytes"),
            "BCL member 'GetBytes' on 'BitConverter' must also appear verbatim");
    }

    [Test]
    [Description("BCL static-qualifier type must be obfuscated when assembly resolution is disabled")]
    public void AssemblyResolution_StaticQualifierTypeName_IsObfuscatedWhenDisabled()
    {
        var output = ObfuscateFileWithOptions("AssemblyResolutionTypeNames.cs", useAssemblyResolution: false);
        Assert.That(output, Does.Not.Contain("BitConverter"),
            "'BitConverter' must be replaced when assembly resolution is off");
    }

    [Test]
    [Description("Proprietary class and method names in the type-names test file must always be obfuscated")]
    public void AssemblyResolution_ProprietaryNamesInTypeNamesFile_AreAlwaysObfuscated()
    {
        var output = ObfuscateFile("AssemblyResolutionTypeNames.cs");
        Assert.That(output, Does.Not.Contain("ProprietaryCase"),
            "Proprietary class 'ProprietaryCase' must be obfuscated");
        Assert.That(output, Does.Not.Contain("ProprietaryMethod"),
            "Proprietary method 'ProprietaryMethod' must be obfuscated");
    }

    // ── Object initialiser property names (Fix 3) ────────────────────────────

    [Test]
    [Description("BCL property names set in an object initialiser must be preserved verbatim (Fix 3: IPropertyInitializer walk)")]
    public void ObjectInitializer_BclPropertyNames_PreservedWhenEnabled()
    {
        var output = ObfuscateFile("ObjectInitializer.cs");
        // HelpLink and Source are properties of System.Exception (mscorlib).
        // They are NOT in the base whitelist; only Fix 3 can preserve them.
        Assert.That(output, Does.Contain("HelpLink"),
            "BCL property 'HelpLink' (System.Exception) in an object initialiser must appear verbatim");
        Assert.That(output, Does.Contain("Source"),
            "BCL property 'Source' (System.Exception) in an object initialiser must appear verbatim");
    }

    [Test]
    [Description("BCL property names in object initialisers must be obfuscated when assembly resolution is disabled")]
    public void ObjectInitializer_BclPropertyNames_ObfuscatedWhenDisabled()
    {
        var output = ObfuscateFileWithOptions("ObjectInitializer.cs", useAssemblyResolution: false);
        Assert.That(output, Does.Not.Contain("HelpLink"),
            "'HelpLink' must be replaced when assembly resolution is off");
        Assert.That(output, Does.Not.Contain("Source"),
            "'Source' must be replaced when assembly resolution is off");
    }

    [Test]
    [Description("Proprietary property names in an object initialiser must always be obfuscated, even with assembly resolution enabled")]
    public void ObjectInitializer_ProprietaryPropertyNames_AlwaysObfuscated()
    {
        var output = ObfuscateFile("ObjectInitializer.cs");
        // Width, Height, Label are declared on ProprietaryWidget — a local class with
        // no well-known namespace. Fix 3 must not accidentally preserve them.
        Assert.That(output, Does.Not.Contain("Width"),
            "Proprietary property 'Width' must be obfuscated");
        Assert.That(output, Does.Not.Contain("Height"),
            "Proprietary property 'Height' must be obfuscated");
        Assert.That(output, Does.Not.Contain("Label"),
            "Proprietary property 'Label' must be obfuscated");
    }

    // ── Type-abbreviation prefixes for local variables ───────────────────────

    [Test]
    public void TypeAbbreviation_StringBuilder_GetsInitialsPrefix()
    {
        var output = ObfuscateFile("TypeAbbreviation.cs");
        Assert.That(output, Does.Contain("sb1"),
            "First StringBuilder variable should receive prefix 'sb1'");
    }

    [Test]
    public void TypeAbbreviation_ArgumentException_GetsInitialsPrefix()
    {
        var output = ObfuscateFile("TypeAbbreviation.cs");
        Assert.That(output, Does.Contain("ae1"),
            "First ArgumentException variable should receive prefix 'ae1'");
    }

    [Test]
    public void TypeAbbreviation_SameType_SharesPrefixPool()
    {
        var output = ObfuscateFile("TypeAbbreviation.cs");
        Assert.That(output, Does.Contain("ae2"),
            "Second ArgumentException variable should receive 'ae2' (same prefix pool)");
        Assert.That(output, Does.Contain("sb2"),
            "Second StringBuilder variable should receive 'sb2' (same prefix pool)");
    }

    [Test]
    public void TypeAbbreviation_ProprietaryType_KeepsLocalVarPrefix()
    {
        var output = ObfuscateFile("TypeAbbreviation.cs");
        Assert.That(output, Does.Contain("localVar"),
            "Variable of proprietary type must keep the generic 'localVar' prefix");
    }

    [Test]
    public void TypeAbbreviation_WhenDisabled_AllLocalsGetLocalVarPrefix()
    {
        var output = ObfuscateFileWithOptions("TypeAbbreviation.cs", useAssemblyResolution: false);
        Assert.That(output, Does.Not.Match(@"\bsb1\b"),
            "With assembly resolution disabled no 'sb' prefix should appear");
        Assert.That(output, Does.Not.Match(@"\bae1\b"),
            "With assembly resolution disabled no 'ae' prefix should appear");
        Assert.That(output, Does.Contain("localVar"),
            "With assembly resolution disabled all locals should get the generic localVar prefix");
    }

    // ── Interpolated string obfuscation ──────────────────────────────────────

    [Test]
    public void InterpolatedString_StructuralDelimiters_Preserved()
    {
        // The output must start with "$" and be syntactically valid C#.
        var output = ObfuscateFile("InterpolatedString.cs");
        Assert.That(output, Does.Contain("$\""),
            "Interpolated string prefix $\" must be preserved");
    }

    [Test]
    public void InterpolatedString_ShortTextFragments_KeptVerbatim()
    {
        // "r" and "c" are single-char text fragments → not replaced.
        // "x=" and "y=" are two chars — the "=" is punctuation, but "x" and "y"
        // are the only letter, so non-ws count = 2 → they ARE replaced.
        // The single chars "r" and "c" between $" and { must survive.
        var output = ObfuscateFile("InterpolatedString.cs");
        // $"r{…} c{…}" should preserve the "r" and " c" fragments
        Assert.That(output, Does.Contain("$\"r{"),
            "Single-char text fragment 'r' at the start must be kept verbatim");
        Assert.That(output, Does.Contain("} c{"),
            "Single-char text fragment ' c' in the middle must be kept verbatim");
    }

    [Test]
    public void InterpolatedString_LongTextFragments_Obfuscated()
    {
        // "hello " is a multi-char text fragment → replaced with someStringN.
        var output = ObfuscateFile("InterpolatedString.cs");
        Assert.That(output, Does.Not.Contain("hello"),
            "Multi-char text fragment 'hello' must be obfuscated");
        Assert.That(output, Does.Contain("someString"),
            "Obfuscated text fragment must produce a someStringN placeholder");
    }

    [Test]
    public void InterpolatedString_EmptyTextFragment_ProducesNoPlaceholder()
    {
        // $"{row}" has an empty text fragment → no someStringN generated there.
        var output = ObfuscateFile("InterpolatedString.cs");
        // The obfuscated row variable will appear; the string should just be $"{…}"
        Assert.That(output, Does.Contain("$\"{"),
            "Empty-text interpolated string must keep $\"{ delimiter");
    }

    [Test]
    public void InterpolatedString_OutputIsSyntacticallyCoherent()
    {
        // In the old broken output, interpolated string parts were each replaced
        // wholesale with "someStringN", producing sequences like:
        //   "someString4"localVar3 + 1"someString5"
        // where closing quote of one placeholder is immediately followed by an
        // identifier — syntactically a string literal concatenated with nothing.
        // After the fix each part is emitted as $"<text>{ … }<text>{ … }<text>"
        // so we should never see a plain string-placeholder followed immediately
        // by a lowercase identifier (the telltale broken-interpolation signature).
        var output = ObfuscateFile("InterpolatedString.cs");
        Assert.That(output, Does.Not.Match(@"someString\d+""\w"),
            "Old broken pattern: someStringN\"identifier must not appear in output");
    }

    // ── Selection carving (PartialPsiBasedObfuscator) ─────────────────────────

    [Test]
    public void SelectionCarving_DoesNotContainOuterFieldPlaceholder()
    {
        // The outer field "outerSecret" is declared outside InnerMethod.
        // When we carve only InnerMethod, its placeholder must not appear.
        var fullOutput = ObfuscateFile("SelectionObfuscation.cs");
        var carved     = ObfuscateMethodSelection("SelectionObfuscation.cs", "InnerMethod");

        // Find whatever placeholder outerSecret received in the full output
        // and verify it is absent from the carved selection.
        Assert.That(fullOutput, Does.Contain("_myField"),
            "Full-file output should contain the outer field placeholder");
        Assert.That(carved, Does.Not.Contain("_myField"),
            "Carved selection must not contain the outer field placeholder");
    }

    [Test]
    public void SelectionCarving_ContainsMethodBody()
    {
        // The carved output must contain tokens that appear inside InnerMethod.
        var carved = ObfuscateMethodSelection("SelectionObfuscation.cs", "InnerMethod");
        Assert.That(carved, Does.Contain("InnerMethod").Or.Contain("SomeMethod"),
            "Carved output must contain the method declaration tokens");
    }

    [Test]
    public void SelectionCarving_IdentifiersConsistentWithFullFile()
    {
        // "ex" is an ArgumentException variable → abbreviated prefix "ae" → ae1.
        // The carved selection must use the same placeholder as the full-file output.
        var fullOutput = ObfuscateFile("SelectionObfuscation.cs");
        var carved     = ObfuscateMethodSelection("SelectionObfuscation.cs", "InnerMethod");

        Assert.That(fullOutput, Does.Contain("ae1"),
            "Full-file output should assign ae1 to the ArgumentException variable");
        Assert.That(carved, Does.Contain("ae1"),
            "Carved selection must use the same ae1 placeholder as the full-file output");
    }

    [Test]
    public void SelectionCarving_BclNamesPreservedVerbatim()
    {
        // HelpLink is a BCL property (System.Exception) — resolved by Pass 1c.
        // It must appear verbatim in the carved output when assembly resolution is on.
        var carved = ObfuscateMethodSelection("SelectionObfuscation.cs", "InnerMethod");
        Assert.That(carved, Does.Contain("HelpLink"),
            "BCL property name HelpLink must be preserved verbatim inside the carved selection");
    }

    [Test]
    public void SelectionCarving_BclNamesObfuscatedWhenResolutionDisabled()
    {
        // With assembly resolution off, HelpLink is no longer preserved.
        var carved = ObfuscateMethodSelection("SelectionObfuscation.cs", "InnerMethod",
            useAssemblyResolution: false);
        Assert.That(carved, Does.Not.Contain("HelpLink"),
            "With assembly resolution disabled, HelpLink must be obfuscated");
    }

    // ── Diagnostic ────────────────────────────────────────────────────────────

    /// <summary>
    /// Dumps the full PSI tree for ObjectInitializerDiag.cs to the test output.
    /// Run this test and inspect the console output to discover the concrete and
    /// interface node types that represent property names inside object initialisers
    /// (e.g. the "Width" in <c>new DiagWidget { Width = 100 }</c>).
    /// This test always passes — it is a one-off inspection aid.
    /// </summary>
    [Test]
    [Explicit("Diagnostic only — run manually to inspect PSI node types")]
    public void Diagnostic_ObjectInitializer_PsiTreeDump()
    {
        WithSingleProject("ObjectInitializerDiag.cs", (_, _, project) =>
        {
            using (ReadLockCookie.Create())
            {
                var psiFile = project.GetSubItems()
                    .OfType<IProjectFile>()
                    .Single()
                    .ToSourceFile()!
                    .GetPrimaryPsiFile() as ICSharpFile;

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=== Full PSI tree ===");
                sb.AppendLine();

                foreach (var node in psiFile!.Descendants())
                {
                    // Compute indentation depth.
                    var depth = 0;
                    for (var p = node.Parent; p != null; p = p.Parent) depth++;

                    var indent   = new string(' ', depth * 2);
                    var concrete = node.GetType().Name;

                    // Collect implemented PSI interfaces (the ones we pattern-match on).
                    var ifaces = string.Join(", ", node.GetType()
                        .GetInterfaces()
                        .Where(i => i.Namespace?.StartsWith("JetBrains.ReSharper.Psi") == true
                                 || i.Namespace?.StartsWith("JetBrains.ReSharper.Psi.CSharp") == true)
                        .Select(i => i.Name)
                        .OrderBy(n => n));

                    var tokenText = node is ITokenNode tk
                        ? $"  \"{tk.GetText().Trim()}\""
                        : string.Empty;

                    sb.AppendLine($"{indent}[{concrete}]{tokenText}");
                    if (!string.IsNullOrEmpty(ifaces))
                    {
                        sb.AppendLine($"{indent}  implements: {ifaces}");
                    }
                }

                // ── Reflection: show all gettable properties of the first IPropertyInitializer ──
        sb.AppendLine();
        sb.AppendLine("=== IPropertyInitializer reflection (first instance) ===");
        var firstPropInit = psiFile!.Descendants<IPropertyInitializer>().FirstOrDefault();
        if (firstPropInit != null)
        {
            foreach (var iface in firstPropInit.GetType().GetInterfaces()
                         .OrderBy(i => i.Name))
            {
                foreach (var prop in iface.GetProperties()
                             .OrderBy(p => p.Name))
                {
                    string valStr;
                    try { valStr = prop.GetValue(firstPropInit)?.ToString() ?? "<null>"; }
                    catch (Exception ex) { valStr = $"<{ex.GetType().Name}>"; }
                    sb.AppendLine($"  [{iface.Name}].{prop.Name} = {valStr}");
                }
            }
        }

        Console.WriteLine(sb.ToString());
            }
        });

        Assert.Pass("Diagnostic — inspect the test output for PSI node types.");
    }
}
