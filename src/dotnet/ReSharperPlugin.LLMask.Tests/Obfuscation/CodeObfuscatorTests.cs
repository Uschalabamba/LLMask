using NUnit.Framework;
using ReSharperPlugin.LLMask.Obfuscation;

namespace ReSharperPlugin.LLMask.Tests.Obfuscation;

[TestFixture]
public class CodeObfuscatorTests
{
    // -----------------------------------------------------------------------
    // Preservation — keywords and BCL types must pass through unchanged
    // -----------------------------------------------------------------------

    [TestCase("public")]
    [TestCase("private")]
    [TestCase("protected")]
    [TestCase("internal")]
    [TestCase("class")]
    [TestCase("interface")]
    [TestCase("struct")]
    [TestCase("enum")]
    [TestCase("void")]
    [TestCase("return")]
    [TestCase("if")]
    [TestCase("else")]
    [TestCase("for")]
    [TestCase("foreach")]
    [TestCase("while")]
    [TestCase("new")]
    [TestCase("this")]
    [TestCase("null")]
    [TestCase("true")]
    [TestCase("false")]
    [TestCase("var")]
    [TestCase("async")]
    [TestCase("await")]
    [TestCase("namespace")]
    [TestCase("using")]
    [TestCase("static")]
    [TestCase("readonly")]
    [TestCase("override")]
    [TestCase("virtual")]
    [TestCase("abstract")]
    [TestCase("sealed")]
    [TestCase("partial")]
    [TestCase("get")]
    [TestCase("set")]
    public void Obfuscate_CSharpKeyword_IsPreserved(string keyword)
        => Assert.That(CodeObfuscator.Obfuscate(keyword), Is.EqualTo(keyword));

    [TestCase("List")]
    [TestCase("Dictionary")]
    [TestCase("HashSet")]
    [TestCase("Task")]
    [TestCase("Exception")]
    [TestCase("Guid")]
    [TestCase("StringBuilder")]
    [TestCase("CancellationToken")]
    [TestCase("IEnumerable")]
    [TestCase("IDisposable")]
    [TestCase("IList")]
    [TestCase("IReadOnlyList")]
    [TestCase("DateTime")]
    [TestCase("TimeSpan")]
    [TestCase("Uri")]
    [TestCase("Type")]
    [TestCase("Action")]
    [TestCase("Func")]
    [TestCase("ValueTask")]
    [TestCase("String")]
    [TestCase("Int32")]
    [TestCase("Boolean")]
    public void Obfuscate_BclType_IsPreserved(string type)
        => Assert.That(CodeObfuscator.Obfuscate(type), Is.EqualTo(type));

    // -----------------------------------------------------------------------
    // Identifier categorisation — naming convention drives placeholder prefix
    // -----------------------------------------------------------------------

    [Test]
    public void Obfuscate_PascalCase_ProducesTypeName()
        => Assert.That(CodeObfuscator.Obfuscate("CustomerService"), Is.EqualTo("TypeName1"));

    [Test]
    public void Obfuscate_CamelCase_ProducesLocalVar()
        => Assert.That(CodeObfuscator.Obfuscate("customerId"), Is.EqualTo("localVar1"));

    [Test]
    public void Obfuscate_UnderscorePrefix_ProducesField()
        => Assert.That(CodeObfuscator.Obfuscate("_repository"), Is.EqualTo("_field1"));

    [Test]
    public void Obfuscate_AllUpperCase_ProducesConst()
        => Assert.That(CodeObfuscator.Obfuscate("MAX_RETRY_COUNT"), Is.EqualTo("CONST_1"));

    [Test]
    public void Obfuscate_SingleUnderscoreAlone_IsNotTreatedAsField()
    {
        // '_' on its own is the C# discard — length 1, so the _field check is skipped
        var result = CodeObfuscator.Obfuscate("_ = Foo()");
        Assert.That(result, Does.StartWith("localVar1"));
    }

    [Test]
    public void Obfuscate_EachCategory_UsesItsOwnCounter()
    {
        // TypeName, localVar, and _field counters are independent
        var result = CodeObfuscator.Obfuscate("Foo foo _foo");
        Assert.That(result, Is.EqualTo("TypeName1 localVar1 _field1"));
    }

    [Test]
    public void Obfuscate_MultiplePascalCaseIdentifiers_CountsUp()
    {
        var result = CodeObfuscator.Obfuscate("Alpha Bravo Charlie");
        Assert.That(result, Is.EqualTo("TypeName1 TypeName2 TypeName3"));
    }

    [Test]
    public void Obfuscate_MultipleCamelCaseIdentifiers_CountsUp()
    {
        var result = CodeObfuscator.Obfuscate("alpha bravo charlie");
        Assert.That(result, Is.EqualTo("localVar1 localVar2 localVar3"));
    }

    // -----------------------------------------------------------------------
    // Consistency — the same identifier always maps to the same placeholder
    //               within a single Obfuscate call
    // -----------------------------------------------------------------------

    [Test]
    public void Obfuscate_RepeatedPascalIdentifier_GetsSamePlaceholder()
    {
        // Foo appears at positions 0 and 2; both must map to TypeName1
        var result = CodeObfuscator.Obfuscate("Foo.Bar(Foo.Baz)");
        Assert.That(result, Is.EqualTo("TypeName1.TypeName2(TypeName1.TypeName3)"));
    }

    [Test]
    public void Obfuscate_RepeatedCamelIdentifier_GetsSamePlaceholder()
    {
        var result = CodeObfuscator.Obfuscate("foo + foo");
        Assert.That(result, Is.EqualTo("localVar1 + localVar1"));
    }

    [Test]
    public void Obfuscate_SameIdentifierAcrossMultipleExpressions_GetsSamePlaceholder()
    {
        var result = CodeObfuscator.Obfuscate("Widget.Create(widget, widgetId)");
        Assert.That(result, Is.EqualTo("TypeName1.TypeName2(localVar1, localVar2)"));
    }

    // -----------------------------------------------------------------------
    // String literals
    // -----------------------------------------------------------------------

    [Test]
    public void Obfuscate_PlainString_BecomesStrPlaceholder()
        => Assert.That(CodeObfuscator.Obfuscate("\"hello world\""), Is.EqualTo("\"<str_1>\""));

    [Test]
    public void Obfuscate_UrlString_BecomesUrlPlaceholder()
        => Assert.That(CodeObfuscator.Obfuscate("\"https://example.com/api\""), Is.EqualTo("\"<url_1>\""));

    [Test]
    public void Obfuscate_WindowsPathString_BecomesPathPlaceholder()
        // Simulates the C# source token: "C:\\Users\\me\\file.txt"
        // (two literal backslashes per separator, as they appear in source)
        => Assert.That(CodeObfuscator.Obfuscate(@"""C:\\Users\\me\\file.txt"""), Is.EqualTo("\"<path_1>\""));

    [Test]
    public void Obfuscate_DrivePrefixString_BecomesPathPlaceholder()
        // Simulates: "D:\data" (any string starting with X:)
        => Assert.That(CodeObfuscator.Obfuscate(@"""D:\data"""), Is.EqualTo("\"<path_1>\""));

    [Test]
    public void Obfuscate_VerbatimString_BecomesStrPlaceholder()
        // @"some text" — verbatim string, no path/url content
        => Assert.That(CodeObfuscator.Obfuscate(@"@""some text"""), Is.EqualTo("\"<str_1>\""));

    [Test]
    public void Obfuscate_InterpolatedString_BecomesStrPlaceholder()
        // $"hello world" — interpolated, no path/url content
        => Assert.That(CodeObfuscator.Obfuscate(@"$""hello world"""), Is.EqualTo("\"<str_1>\""));

    [Test]
    public void Obfuscate_SameStringLiteral_MapsToSamePlaceholder()
    {
        var result = CodeObfuscator.Obfuscate("\"hello\" + \"hello\"");
        Assert.That(result, Is.EqualTo("\"<str_1>\" + \"<str_1>\""));
    }

    [Test]
    public void Obfuscate_DifferentStringLiterals_GetDistinctPlaceholders()
    {
        var result = CodeObfuscator.Obfuscate("\"alpha\" + \"beta\"");
        Assert.That(result, Is.EqualTo("\"<str_1>\" + \"<str_2>\""));
    }

    [Test]
    public void Obfuscate_UrlAndPathInSameSnippet_UseDistinctCategories()
    {
        var result = CodeObfuscator.Obfuscate("\"https://api.example.com\" + \"C:\\\\logs\\\\app.log\"");
        Assert.That(result, Is.EqualTo("\"<url_1>\" + \"<path_1>\""));
    }

    // -----------------------------------------------------------------------
    // Comments
    // -----------------------------------------------------------------------

    [Test]
    public void Obfuscate_LineComment_IsRedacted()
        => Assert.That(CodeObfuscator.Obfuscate("// proprietary detail"), Is.EqualTo("// <comment>"));

    [Test]
    public void Obfuscate_DocComment_IsRedacted()
        => Assert.That(CodeObfuscator.Obfuscate("/// <summary>internal</summary>"), Is.EqualTo("// <comment>"));

    [Test]
    public void Obfuscate_BlockComment_IsRedacted()
        => Assert.That(CodeObfuscator.Obfuscate("/* line1\nline2 */"), Is.EqualTo("/* <comment> */"));

    [Test]
    public void Obfuscate_CodeAfterLineComment_IsStillObfuscated()
    {
        // The comment should not "swallow" the identifier on the next line
        var result = CodeObfuscator.Obfuscate("// comment\nMyClass obj");
        Assert.That(result, Is.EqualTo("// <comment>\nTypeName1 localVar1"));
    }

    // -----------------------------------------------------------------------
    // Char literals — kept verbatim (single characters are not proprietary)
    // -----------------------------------------------------------------------

    [TestCase("'a'")]
    [TestCase("'Z'")]
    [TestCase("'0'")]
    [TestCase("'\\n'")]
    [TestCase("'\\t'")]
    [TestCase("'\\''")]
    public void Obfuscate_CharLiteral_IsPreserved(string charLiteral)
        => Assert.That(CodeObfuscator.Obfuscate(charLiteral), Is.EqualTo(charLiteral));

    // -----------------------------------------------------------------------
    // Structural tokens — numbers, operators, whitespace, punctuation
    // -----------------------------------------------------------------------

    [Test]
    public void Obfuscate_IntegerLiteral_IsPreserved()
        => Assert.That(CodeObfuscator.Obfuscate("42"), Is.EqualTo("42"));

    [Test]
    public void Obfuscate_FloatLiteral_IsPreserved()
        => Assert.That(CodeObfuscator.Obfuscate("3.14"), Is.EqualTo("3.14"));

    [Test]
    public void Obfuscate_Punctuation_IsPreserved()
        => Assert.That(CodeObfuscator.Obfuscate("{ }"), Is.EqualTo("{ }"));

    [Test]
    public void Obfuscate_ArithmeticOperators_ArePreserved()
    {
        // Only operators survive; the single-letter identifiers get obfuscated
        var result = CodeObfuscator.Obfuscate("a + b");
        Assert.That(result, Is.EqualTo("localVar1 + localVar2"));
    }

    [Test]
    public void Obfuscate_Semicolons_ArePreserved()
        => Assert.That(CodeObfuscator.Obfuscate("return;"), Is.EqualTo("return;"));

    [Test]
    public void Obfuscate_MemberAccessDot_IsPreserved()
        => Assert.That(CodeObfuscator.Obfuscate("a.b"), Is.EqualTo("localVar1.localVar2"));

    // -----------------------------------------------------------------------
    // Integration — a realistic method body
    // -----------------------------------------------------------------------

    [Test]
    public void Obfuscate_FullMethod_StructureAndKeywordsPreservedIdentifiersObfuscated()
    {
        const string input =
            @"public string GetCustomerName(int customerId)
            {
                var customer = _repository.GetById(customerId);
                return customer.FullName;
            }";

        const string expected =
            @"public string TypeName1(int localVar1)
            {
                var localVar2 = _field1.TypeName2(localVar1);
                return localVar2.TypeName3;
            }";

        Assert.That(CodeObfuscator.Obfuscate(input), Is.EqualTo(expected));
    }

    [Test]
    public void Obfuscate_MethodWithStringAndComment_AllThreeRulesApply()
    {
        const string input =
            @"public void Connect()
            {
                // connect to the internal API
                var url = ""https://internal.corp/api"";
                _client.Open(url);
            }";

        const string expected =
            @"public void TypeName1()
            {
                // <comment>
                var localVar1 = ""<url_1>"";
                _field1.TypeName2(localVar1);
            }";

        Assert.That(CodeObfuscator.Obfuscate(input), Is.EqualTo(expected));
    }

    // -----------------------------------------------------------------------
    // Extra preserved words (CustomWhitelist)
    // -----------------------------------------------------------------------

    [Test]
    public void Obfuscate_ExtraWord_IsObfuscatedWithoutCustomList()
    {
        // Baseline: without extra words, a proprietary identifier is replaced
        var result = CodeObfuscator.Obfuscate("CustomerService");
        Assert.That(result, Is.EqualTo("TypeName1"));
    }

    [Test]
    public void Obfuscate_ExtraWord_IsPreservedWhenInCustomList()
    {
        var result = CodeObfuscator.Obfuscate("CustomerService", extraPreservedWords: ["CustomerService"]);
        Assert.That(result, Is.EqualTo("CustomerService"));
    }

    [Test]
    public void Obfuscate_MultipleExtraWords_AllPreserved()
    {
        var result = CodeObfuscator.Obfuscate(
            "OrderService PaymentService",
            extraPreservedWords: ["OrderService", "PaymentService"]);
        Assert.That(result, Is.EqualTo("OrderService PaymentService"));
    }

    [Test]
    public void Obfuscate_ExtraWords_DoNotAffectCountersForOtherIdentifiers()
    {
        // CustomerService is preserved; TypeName counter should start at 1 for ProductRepo
        var result = CodeObfuscator.Obfuscate(
            "CustomerService ProductRepo",
            extraPreservedWords: ["CustomerService"]);
        Assert.That(result, Is.EqualTo("CustomerService TypeName1"));
    }

    [Test]
    public void Obfuscate_ExtraWordAlreadyInBaseList_IsStillPreserved()
    {
        // "List" is in the default base list; adding it to extra should not cause issues
        var result = CodeObfuscator.Obfuscate("List", extraPreservedWords: ["List"]);
        Assert.That(result, Is.EqualTo("List"));
    }

    [Test]
    public void Obfuscate_EmptyExtraList_BehavesLikeDefault()
    {
        var withEmpty  = CodeObfuscator.Obfuscate("CustomerService", extraPreservedWords: []);
        var withoutAny = CodeObfuscator.Obfuscate("CustomerService");
        Assert.That(withEmpty, Is.EqualTo(withoutAny));
    }

    // -----------------------------------------------------------------------
    // Custom base whitelist (BaseWhitelist)
    // -----------------------------------------------------------------------

    [Test]
    public void Obfuscate_CustomBaseList_OnlyPreservesListedWords()
    {
        // With a minimal base list, even BCL types like "List" get obfuscated
        var result = CodeObfuscator.Obfuscate(
            "List CustomerService",
            basePreservedWords: ["CustomerService"]);
        Assert.That(result, Is.EqualTo("TypeName1 CustomerService"));
    }

    [Test]
    public void Obfuscate_CustomBaseList_ReplacesDefaultKeywords()
    {
        // Passing a custom base list replaces the built-in list entirely,
        // so C# keywords are no longer automatically preserved
        var result = CodeObfuscator.Obfuscate(
            "public void",
            basePreservedWords: []);
        Assert.That(result, Is.EqualTo("localVar1 localVar2"));
    }

    [Test]
    public void Obfuscate_NullBaseList_FallsBackToDefaultPreservedWords()
    {
        // null → built-in PreservedWords; keywords must still pass through
        var result = CodeObfuscator.Obfuscate("public void", basePreservedWords: null);
        Assert.That(result, Is.EqualTo("public void"));
    }
}
