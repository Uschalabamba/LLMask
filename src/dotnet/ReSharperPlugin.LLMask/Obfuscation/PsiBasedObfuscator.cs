#nullable enable
using JetBrains.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.Parsing;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Tree;
using ReSharperPlugin.LLMask.Data;

namespace ReSharperPlugin.LLMask.Obfuscation;

/// <summary>
/// Obfuscates a C# file by walking its PSI tree and replacing identifiers,
/// string literals, and comments with generic placeholders.
///
/// Three-pass strategy:
///   Pass 0 — counts every IDENTIFIER token occurrence so that frequently-used
///             names receive lower placeholder numbers (SomeMethod1 for the most
///             common, SomeMethod2 for the next, etc.).
///   Pass 1 — walks all ICSharpDeclaration nodes to register declared names with
///             human-friendly prefixes (MyClass, SomeMethod, _myField, …).
///             Also scans all IDENTIFIER tokens to collect external (undeclared)
///             identifiers and their heuristic prefixes.
///             Both pools are then sorted by descending frequency and assigned
///             sequential numbers within each prefix group.
///   Pass 2 — walks all ITokenNode tokens and builds the output:
///             · Known identifiers look up their pre-assigned placeholder.
///             · Unknown identifiers (missed by the pre-scan) fall back to lazy
///               assignment using the same counter, so output is always consistent.
///             · String literals are replaced with "someStringN", "pathN", "urlN".
///             · Comments are stripped.
/// </summary>
public static class PsiBasedObfuscator
{
    private static readonly HashSet<string> DefaultPreservedWords =
        new(CSharpIdentifierData.DefaultBaseWhitelist.Split(','), StringComparer.Ordinal);

    public static string Obfuscate(
        ICSharpFile file,
        IEnumerable<string>? extraPreservedWords = null,
        IEnumerable<string>? basePreservedWords = null,
        bool sortByFrequency = true,
        bool useAssemblyResolution = true,
        IEnumerable<string>? wellKnownRoots = null)
    {
        var baseWords = basePreservedWords != null
            ? new HashSet<string>(basePreservedWords, StringComparer.Ordinal)
            : DefaultPreservedWords;

        HashSet<string>? extra = null;
        if (extraPreservedWords != null)
        {
            extra = new HashSet<string>(extraPreservedWords, StringComparer.Ordinal);
        }

        var wellKnownRootsSet = wellKnownRoots != null
            ? new HashSet<string>(wellKnownRoots, StringComparer.Ordinal)
            : CSharpIdentifierData.WellKnownNamespaceRoots;

        var strCounters = new int[3]; // [0] str  [1] path  [2] url
        var strMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var sb     = new StringBuilder();

        // ── Pass 0: count identifier token frequencies ────────────────────────
        // Only needed when frequency-sorted numbering is enabled.
        var freq = new Dictionary<string, int>(StringComparer.Ordinal);
        if (sortByFrequency)
        {
            foreach (var token in file.Descendants<ITokenNode>())
            {
                if (token.GetTokenType() != CSharpTokenType.IDENTIFIER)
                {
                    continue;
                }

                var t = token.GetText();
                freq[t] = freq.GetValueOrDefault(t, 0) + 1;
            }
        }

        // ── Pass 1a: collect declared identifiers (name → prefix) ─────────────
        // Only the first occurrence per name is kept (partial classes etc. may
        // declare the same name more than once).
        var declaredPrefix = new Dictionary<string, string>(StringComparer.Ordinal);
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

            if (declaredPrefix.ContainsKey(name))
            {
                continue;
            }

            var prefix = GetPrefixForDeclaration(decl);
            if (prefix == null)
            {
                continue;
            }

            // Single-character generic locals (e.g. int a = …) don't leak proprietary
            // information and just create noise — skip so they pass through verbatim.
            if (name.Length == 1 && prefix == "localVar")
            {
                continue;
            }

            declaredPrefix[name] = prefix;
        }

        // ── Pass 1b: collect external (undeclared) identifiers ───────────────
        // Only needed for frequency-sorted mode, where all identifiers must be
        // pre-assigned before Pass 2.  In tree-order mode, external identifiers
        // are assigned lazily in Pass 2 (same as the original behaviour).
        var externalPrefix = new Dictionary<string, string>(StringComparer.Ordinal);
        if (sortByFrequency)
        {
            foreach (var token in file.Descendants<ITokenNode>())
            {
                if (GetContainingUsingDirective(token) != null)
                {
                    continue;
                }

                if (token.GetTokenType() != CSharpTokenType.IDENTIFIER)
                {
                    continue;
                }

                var raw = token.GetText();
                if (baseWords.Contains(raw) || (extra != null && extra.Contains(raw)))
                {
                    continue;
                }

                if (declaredPrefix.ContainsKey(raw))
                {
                    continue;
                }

                if (externalPrefix.ContainsKey(raw))
                {
                    continue;
                }

                if (raw.Length == 1)
                {
                    continue;
                }

                externalPrefix[raw] = GetHeuristicPrefix(raw, token);
            }
        }

        // ── Pass 1c: resolve references to well-known assemblies ─────────────
        // Walk every IReferenceExpression; resolve it; if the declared element's
        // owning module is an IAssemblyPsiModule whose name starts with a
        // well-known namespace root, add the identifier text to resolvedSafeNames
        // so it will pass through verbatim in Pass 2 (and be excluded from the
        // placeholder assignment pools above).
        var resolvedSafeNames = new HashSet<string>(StringComparer.Ordinal);
        if (useAssemblyResolution)
        {
            foreach (var refExpr in file.Descendants<IReferenceExpression>())
            {
                var nameToken = refExpr.NameIdentifier;
                if (nameToken == null)
                {
                    continue;
                }

                var name = nameToken.Name;
                if (string.IsNullOrEmpty(name) || name.Length <= 1)
                {
                    continue;
                }

                // Already preserved by whitelist — no need to pay the resolve cost.
                if (baseWords.Contains(name) || (extra != null && extra.Contains(name)))
                {
                    continue;
                }

                var resolveResult = refExpr.Reference.Resolve();
                var element = resolveResult.DeclaredElement;

                // Primary path: element resolved cleanly — navigate to its containing type.
                ITypeElement? containingType = null;
                if (element != null)
                {
                    containingType = element is ITypeMember tm
                        ? tm.ContainingType
                        : element as ITypeElement;
                }

                // Fallback A: qualifier is a type reference (static call, e.g. Console.WriteLine).
                // Handles overloaded methods where DeclaredElement is null due to ambiguity.
                if (containingType == null &&
                    refExpr.QualifierExpression is IReferenceExpression qualRefExpr)
                {
                    containingType = qualRefExpr.Reference.Resolve().DeclaredElement as ITypeElement;
                }

                // Fallback B: qualifier is a value expression (instance / extension call).
                // Get the expression's declared type to find the receiver's namespace.
                if (containingType == null && refExpr.QualifierExpression is { } qualExpr)
                {
                    containingType = (qualExpr.GetExpressionType() as IDeclaredType)?.GetTypeElement();
                }

                if (containingType == null)
                {
                    continue;
                }

                // GetClrName().FullName gives "System.String", "System.Linq.Enumerable", etc.
                // Splitting on the first dot gives us the root namespace segment.
                var fullTypeName = containingType.GetClrName().FullName;
                var firstDot = fullTypeName.IndexOf('.');
                if (firstDot <= 0)
                {
                    continue;
                }

                var nsRoot = fullTypeName.Substring(0, firstDot);
                if (wellKnownRootsSet.Contains(nsRoot))
                {
                    resolvedSafeNames.Add(name);
                }
            }
        }

        // Remove assembly-resolved names from the placeholder pools so they are
        // never assigned a placeholder (they will pass through verbatim in Pass 2).
        foreach (var name in resolvedSafeNames)
        {
            declaredPrefix.Remove(name);
            externalPrefix.Remove(name);
        }

        // ── Assign numbers ────────────────────────────────────────────────────
        // When sortByFrequency: merge declared + external, sort each prefix group
        // by descending frequency, then assign 1, 2, 3 …
        // When !sortByFrequency: assign declared identifiers in tree-walk order;
        // external identifiers are left for lazy assignment in Pass 2.
        var idMap    = new Dictionary<string, string>(StringComparer.Ordinal);
        var counters = new Dictionary<string, int>(StringComparer.Ordinal);

        if (sortByFrequency)
        {
            var allCandidates = new Dictionary<string, string>(declaredPrefix, StringComparer.Ordinal);
            foreach (var (name, prefix) in externalPrefix)
            {
                allCandidates[name] = prefix;
            }

            foreach (var group in allCandidates.GroupBy(kv => kv.Value))
            {
                var n = 0;
                foreach (var kv in group.OrderByDescending(kv => freq.GetValueOrDefault(kv.Key, 0)))
                {
                    idMap[kv.Key] = $"{kv.Value}{++n}";
                }
                counters[group.Key] = n;
            }
        }
        else
        {
            // Tree-order: assign declared names sequentially as they were collected.
            foreach (var (name, prefix) in declaredPrefix)
            {
                counters.TryGetValue(prefix, out var n);
                idMap[name]      = $"{prefix}{n + 1}";
                counters[prefix] = n + 1;
            }
        }

        // ── Pre-pass: build directive replacements ────────────────────────────
        // Calling GetText() on a composite IUsingDirective node can trigger internal
        // declared-element resolution for resolvable namespaces (System, Microsoft, …),
        // which conflicts with concurrent code completion and causes "declaredElement
        // should be valid" exceptions. Using only ITokenNode.GetText() (leaf nodes that
        // return raw characters) is safe.
        var directiveReplacements = new Dictionary<IUsingDirective, string>();
        foreach (var directive in file.Descendants<IUsingDirective>())
        {
            string replacement;
            if (IsWellKnownUsing(directive, wellKnownRootsSet))
            {
                var buf = new StringBuilder();
                foreach (var t in directive.Descendants<ITokenNode>())
                {
                    buf.Append(t.GetText());
                }

                replacement = buf.ToString().TrimEnd();
            }
            else
            {
                replacement = "using SomeNamespace;";
            }
            directiveReplacements[directive] = replacement;
        }

        // ── Pass 2: walk tokens and build output ──────────────────────────────
        var emittedUsings = new HashSet<IUsingDirective>();
        foreach (var token in file.Descendants<ITokenNode>())
        {
            // Collapse each using directive to a single placeholder line.
            var containingUsing = GetContainingUsingDirective(token);
            if (containingUsing != null)
            {
                if (emittedUsings.Add(containingUsing))
                {
                    sb.Append(directiveReplacements.TryGetValue(containingUsing, out var rep)
                        ? rep
                        : "using SomeNamespace;");
                }
                continue;
            }

            var tokenType = token.GetTokenType();
            var raw = token.GetText();

            if (tokenType == CSharpTokenType.END_OF_LINE_COMMENT ||
                tokenType == CSharpTokenType.C_STYLE_COMMENT)
            {
                // Drop comments entirely — they add noise without leaking proprietary
                // information. Covers //, /// XML-doc, and /* */ block comments.
            }
            else if (IsStringLiteralToken(tokenType))
            {
                var content = StringBasedObfuscator.ExtractStringContent(raw);
                if (content.Length == 1)
                {
                    sb.Append(raw); // single-char strings carry no proprietary information
                }
                else
                {
                    if (!strMap.TryGetValue(raw, out var strPlaceholder))
                    {
                        strPlaceholder = StringBasedObfuscator.MakeStringPlaceholder(content, strCounters);
                        strMap[raw] = strPlaceholder;
                    }
                    sb.Append(strPlaceholder);
                }
            }
            else if (tokenType == CSharpTokenType.CHARACTER_LITERAL)
            {
                sb.Append(raw);
            }
            else if (tokenType == CSharpTokenType.IDENTIFIER)
            {
                if (resolvedSafeNames.Contains(raw)
                    || baseWords.Contains(raw) || (extra != null && extra.Contains(raw)))
                {
                    sb.Append(raw);
                }
                else if (idMap.TryGetValue(raw, out var placeholder))
                {
                    sb.Append(placeholder);
                }
                else if (raw.Length == 1)
                {
                    // Unregistered single-character identifiers carry no proprietary information.
                    sb.Append(raw);
                }
                else
                {
                    // Fallback: identifier missed by the pre-scans (should be rare).
                    // Assign lazily, continuing from the pre-assigned counters.
                    var prefix = GetHeuristicPrefix(raw, token);
                    counters.TryGetValue(prefix, out var n);
                    var newPlaceholder = $"{prefix}{n + 1}";
                    idMap[raw]       = newPlaceholder;
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
        // Lambda parameters (x => …) are semantically "the current element" just
        // like a foreach iteration variable, so they share the "element" prefix.
        // Regular method/constructor parameters keep the "param" label.
        // IParameterDeclaration is the same PSI type for both; the when-guard
        // distinguishes them by checking for an enclosing ILambdaExpression.
        IParameterDeclaration p when IsLambdaParameter(p)      => "element",
        IParameterDeclaration                                   => "param",
        // Local variables are routed through a helper that inspects the parent
        // chain to assign a more descriptive prefix for loop/foreach contexts.
        ILocalVariableDeclaration localVar                      => GetLocalVarPrefix(localVar),
        // In modern C# (7+), a foreach iteration variable declared with 'var'
        // is represented as ISingleVariableDesignation inside a
        // DeclarationExpression, NOT as ILocalVariableDeclaration.
        // We detect the foreach context by walking up to IForeachStatement.
        ISingleVariableDesignation designation                  => GetDesignationPrefix(designation),
        IConstantDeclaration or IEnumMemberDeclaration          => "CONST_",

        // Constructor/destructor names equal the class name, which is already
        // registered via IClassDeclaration — skip to avoid double-mapping.
        // Type parameters are also skipped; renaming them inside generic
        // constraints is error-prone at the token level.
        _ => null,
    };

    /// <summary>
    /// Returns <c>true</c> when <paramref name="param"/> is a lambda parameter
    /// (i.e. an <c>ILambdaExpression</c> appears in its parent chain before any
    /// method or class boundary).
    /// </summary>
    private static bool IsLambdaParameter(IParameterDeclaration param)
    {
        for (var node = param.Parent; node != null; node = node.Parent)
        {
            if (node is ILambdaExpression)
            {
                return true;
            }

            if (node is ICSharpFunctionDeclaration || node is IClassDeclaration)
            {
                return false;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns a semantic prefix for a local variable based on its syntactic role:
    /// <list type="bullet">
    ///   <item><c>idx</c>  – declared in a <c>for</c>-loop initialiser</item>
    ///   <item><c>elem</c> – declared in a <c>foreach</c> statement</item>
    ///   <item><c>localVar</c> – any other local variable</item>
    /// </list>
    /// The walk stops at the nearest enclosing <c>IBlock</c> or function boundary
    /// so that locals inside a loop body are not mislabelled as loop indices.
    /// </summary>
    private static string GetLocalVarPrefix(ILocalVariableDeclaration decl)
    {
        for (var node = decl.Parent; node != null; node = node.Parent)
        {
            // IForeachStatement must be checked before IForStatement because it is
            // a subtype in the ReSharper PSI hierarchy — checking the more general
            // type first would match foreach and wrongly return "index".
            if (node is IForeachStatement)
            {
                return "element";
            }

            if (node is IForStatement)
            {
                return "index";
            }

            // Stop at block / function boundaries so a variable declared
            // *inside* a loop body is not treated as the loop counter.
            if (node is IBlock || node is ICSharpFunctionDeclaration)
            {
                break;
            }
        }
        return "localVar";
    }

    /// <summary>
    /// Returns a semantic prefix for a <c>var</c>-pattern designation (C# 7+)
    /// such as the iteration variable in <c>foreach (var item in ...)</c>.
    /// Walks the parent chain to detect the foreach context.
    /// </summary>
    private static string GetDesignationPrefix(ISingleVariableDesignation designation)
    {
        for (var node = designation.Parent; node != null; node = node.Parent)
        {
            if (node is IForeachStatement)
            {
                return "element";
            }

            if (node is IBlock || node is ICSharpFunctionDeclaration)
            {
                break;
            }
        }
        return "localVar";
    }

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

    /// Returns true when the directive's root namespace segment is well-known
    /// (e.g. System, Serilog, Xunit) and its text can be emitted verbatim.
    /// Only plain namespace directives are checked; alias directives are always replaced.
    private static bool IsWellKnownUsing(IUsingDirective directive, HashSet<string> roots)
    {
        // Alias directives (using Alias = Foo.Bar) are always replaced.
        if (directive is IUsingAliasDirective)
        {
            return false;
        }

        // Walk the directive's leaf tokens to find the first IDENTIFIER — that is
        // always the root namespace segment (e.g. "System", "Serilog").
        // Keyword tokens (using, global, static) and punctuation are never IDENTIFIER
        // type, so we skip them without needing to parse text at all.
        // Critically, we never call GetText() on the composite IUsingDirective node,
        // which can trigger internal declared-element resolution for resolvable
        // namespaces and cause "declaredElement should be valid" exceptions in
        // concurrent code-completion.
        foreach (var token in directive.Descendants<ITokenNode>())
        {
            if (token.GetTokenType() != CSharpTokenType.IDENTIFIER)
            {
                continue;
            }

            var root = token.GetText();

            // "global" and "static" can appear as contextual identifiers in some
            // edge cases; skip them and take the next identifier instead.
            if (root is "global" or "static")
            {
                continue;
            }

            return roots.Contains(root);
        }

        return false;
    }

    private static IUsingDirective? GetContainingUsingDirective(ITokenNode token)
    {
        for (var node = token.Parent; node != null; node = node.Parent)
        {
            if (node is IUsingDirective d)
            {
                return d;
            }

            if (node is ICSharpFile)
            {
                return null;
            }
        }
        return null;
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
