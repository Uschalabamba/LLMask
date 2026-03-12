#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using JetBrains.ReSharper.Psi.CSharp.Parsing;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Tree;
using ReSharperPlugin.LLMask.Data;

namespace ReSharperPlugin.LLMask.Obfuscation;

/// <summary>
/// Obfuscates a C# file by walking its PSI tree and replacing identifiers,
/// string literals, and comments with generic placeholders.
///
/// Two-pass strategy:
///   Pass 1 — walks all ICSharpDeclaration nodes to register declared names
///             with human-friendly prefixes (MyClass, SomeMethod, _myField, …).
///   Pass 2 — walks all ITokenNode tokens and builds the output:
///             · Known identifiers look up their registered placeholder.
///             · Unknown identifiers (external types/methods) fall back to a
///               context-aware heuristic: invocation context wins over text shape.
///             · String literals are replaced with "<str_N>", "<path_N>", "<url_N>".
///             · Comments are replaced with // <comment> or /* <comment> */.
/// </summary>
public static class PsiBasedObfuscator
{
    private static readonly HashSet<string> DefaultPreservedWords =
        new(CSharpIdentifierData.DefaultBaseWhitelist.Split(','), StringComparer.Ordinal);

    public static string Obfuscate(
        ICSharpFile file,
        IEnumerable<string>? extraPreservedWords = null,
        IEnumerable<string>? basePreservedWords = null)
    {
        var baseWords = basePreservedWords != null
            ? new HashSet<string>(basePreservedWords, StringComparer.Ordinal)
            : DefaultPreservedWords;

        HashSet<string>? extra = null;
        if (extraPreservedWords != null)
        {
            extra = new HashSet<string>(extraPreservedWords, StringComparer.Ordinal);
        }

        // category prefix → next available number
        var counters    = new Dictionary<string, int>(StringComparer.Ordinal);
        var strCounters = new int[3]; // [0] str  [1] path  [2] url

        // identifier text → assigned placeholder
        var idMap  = new Dictionary<string, string>(StringComparer.Ordinal);
        var strMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var sb     = new StringBuilder();

        // ── Pass 1: register every declared name ─────────────────────────────
        foreach (var decl in file.Descendants<ICSharpDeclaration>())
        {
            var name = decl.DeclaredName;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (baseWords.Contains(name) || (extra != null && extra.Contains(name)))
            {
                continue;
            }

            if (idMap.ContainsKey(name))
            {
                continue;
            }

            var prefix = GetPrefixForDeclaration(decl);
            if (prefix == null)
            {
                continue;
            }

            counters.TryGetValue(prefix, out var n);
            idMap[name]    = $"{prefix}{n + 1}";
            counters[prefix] = n + 1;
        }

        // ── Pass 2: walk tokens and build output ─────────────────────────────
        foreach (var token in file.Descendants<ITokenNode>())
        {
            var tokenType = token.GetTokenType();
            var raw = token.GetText();

            if (tokenType == CSharpTokenType.END_OF_LINE_COMMENT)
            {
                sb.Append("// <comment>");
            }
            else if (tokenType == CSharpTokenType.C_STYLE_COMMENT)
            {
                sb.Append("/* <comment> */");
            }
            else if (IsStringLiteralToken(tokenType))
            {
                if (!strMap.TryGetValue(raw, out var strPlaceholder))
                {
                    strPlaceholder = StringBasedObfuscator.MakeStringPlaceholder(
                        StringBasedObfuscator.ExtractStringContent(raw), strCounters);
                    strMap[raw] = strPlaceholder;
                }
                sb.Append(strPlaceholder);
            }
            else if (tokenType == CSharpTokenType.CHARACTER_LITERAL)
            {
                sb.Append(raw);
            }
            else if (tokenType == CSharpTokenType.IDENTIFIER)
            {
                if (baseWords.Contains(raw) || (extra != null && extra.Contains(raw)))
                {
                    sb.Append(raw);
                }
                else if (idMap.TryGetValue(raw, out var placeholder))
                {
                    sb.Append(placeholder);
                }
                else
                {
                    // Unknown identifier (external type/method) — context heuristic.
                    var prefix = GetHeuristicPrefix(raw, token);
                    counters.TryGetValue(prefix, out var n);
                    var newPlaceholder = $"{prefix}{n + 1}";
                    idMap[raw]       = newPlaceholder; // cache for consistent reuse
                    counters[prefix] = n + 1;
                    sb.Append(newPlaceholder);
                }
            }
            else
            {
                sb.Append(raw);
            }
        }

        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Declaration prefix mapping
    // ─────────────────────────────────────────────────────────────────────────

    /// Returns a human-friendly prefix for the kind of symbol declared, or null to skip it.
    private static string? GetPrefixForDeclaration(ICSharpDeclaration decl) => decl switch
    {
        INamespaceDeclaration                                   => "MyNamespace",
        IClassDeclaration                                       => "MyClass",
        IInterfaceDeclaration                                   => "IMyInterface",
        IStructDeclaration                                      => "MyStruct",
        IEnumDeclaration                                        => "MyEnum",
        IDelegateDeclaration                                    => "MyDelegate",
        IMethodDeclaration                                      => "SomeMethod",
        IPropertyDeclaration or IIndexerDeclaration             => "MyProperty",
        IFieldDeclaration                                       => "_myField",
        IEventDeclaration                                       => "MyEvent",
        IParameterDeclaration                                   => "param",
        ILocalVariableDeclaration                               => "localVar",
        IConstantDeclaration or IEnumMemberDeclaration          => "CONST_",

        // Constructor/destructor names equal the class name, which is already
        // registered via IClassDeclaration — skip to avoid double-mapping.
        // Type parameters are also skipped; renaming them inside generic
        // constraints is error-prone at the token level.
        _ => null,
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Heuristic for external identifiers
    // ─────────────────────────────────────────────────────────────────────────

    /// Heuristic prefix for identifiers not declared in this file.
    /// Invocation context takes priority over text shape to avoid labelling
    /// external PascalCase method calls (e.g. Console.WriteLine) as types.
    private static string GetHeuristicPrefix(string identifier, ITokenNode token)
    {
        if (IsInvocationCallee(token))
        {
            return "SomeMethod";
        }

        if (identifier.Length >= 2 && identifier[0] == '_' && char.IsLower(identifier[1]))
        {
            return "_myField";
        }

        if (StringBasedObfuscator.IsAllUpperCase(identifier))
        {
            return "CONST_";
        }

        if (char.IsUpper(identifier[0]))
        {
            return "MyClass"; // PascalCase but not locally declared → external type reference
        }

        return "localVar";
    }

    /// Returns true when the identifier token is the direct callee of an invocation,
    /// i.e. the nearest IReferenceExpression ancestor's parent is an IInvocationExpression.
    /// This handles both simple calls (Foo()) and member calls (obj.Foo()),
    /// while excluding arguments inside the same invocation subtree.
    private static bool IsInvocationCallee(ITokenNode token)
    {
        for (var node = token.Parent; node != null; node = node.Parent)
        {
            if (node is IReferenceExpression)
            {
                return node.Parent is IInvocationExpression;
            }

            if (node is IExpression)
            {
                return false;
            }
        }
        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // String literal token detection
    // ─────────────────────────────────────────────────────────────────────────

    private static bool IsStringLiteralToken(JetBrains.ReSharper.Psi.Parsing.TokenNodeType tokenType) =>
        tokenType == CSharpTokenType.STRING_LITERAL_REGULAR
        || tokenType == CSharpTokenType.STRING_LITERAL_VERBATIM
        || tokenType == CSharpTokenType.SINGLE_LINE_RAW_STRING_LITERAL
        || tokenType == CSharpTokenType.MULTI_LINE_RAW_STRING_LITERAL
        || tokenType == CSharpTokenType.INTERPOLATED_STRING_REGULAR_START
        || tokenType == CSharpTokenType.INTERPOLATED_STRING_REGULAR_MIDDLE
        || tokenType == CSharpTokenType.INTERPOLATED_STRING_REGULAR_END
        || tokenType == CSharpTokenType.INTERPOLATED_STRING_VERBATIM_START
        || tokenType == CSharpTokenType.INTERPOLATED_STRING_VERBATIM_MIDDLE
        || tokenType == CSharpTokenType.INTERPOLATED_STRING_VERBATIM_END
        || tokenType == CSharpTokenType.INTERPOLATED_STRING_RAW_SINGLE_LINE_START
        || tokenType == CSharpTokenType.INTERPOLATED_STRING_RAW_MULTI_LINE_START
        || tokenType == CSharpTokenType.INTERPOLATED_STRING_RAW_TEXT
        || tokenType == CSharpTokenType.INTERPOLATED_STRING_RAW_END;
}
