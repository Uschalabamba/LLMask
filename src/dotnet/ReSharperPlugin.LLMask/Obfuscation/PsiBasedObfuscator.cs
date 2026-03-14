#nullable enable
using JetBrains.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.Parsing;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Modules;
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
    private static readonly Lazy<HashSet<string>> DefaultPreservedWords =
        new(() => new HashSet<string>(LLMaskDataProvider.GetEmbedded().BaseWhitelist, StringComparer.Ordinal));

    private static readonly Lazy<HashSet<string>> DefaultWellKnownRoots =
        new(() => new HashSet<string>(LLMaskDataProvider.GetEmbedded().WellKnownNamespaceRoots, StringComparer.Ordinal));

    /// <summary>
    /// Obfuscates the entire file and returns the result and the session mapping.
    /// Delegates to <see cref="ObfuscateCore"/> without building the token-offset map.
    /// </summary>
    public static (string output, LLMaskMapping mapping) Obfuscate(
        ICSharpFile file,
        IEnumerable<string>? extraPreservedWords = null,
        IEnumerable<string>? basePreservedWords = null,
        bool sortByFrequency = true,
        bool useAssemblyResolution = true,
        IEnumerable<string>? wellKnownRoots = null)
    {
        var (fullOutput, mapping, _) = ObfuscateCore(file, extraPreservedWords, basePreservedWords,
            sortByFrequency, useAssemblyResolution, wellKnownRoots,
            buildTokenMap: false);
        return (fullOutput, mapping);
    }

    /// <summary>
    /// Core obfuscation implementation.  When <paramref name="buildTokenMap"/> is
    /// <see langword="true"/>, also returns a parallel list mapping each token's
    /// original document offset to its start position in the output string.
    /// <see cref="PartialPsiBasedObfuscator"/> uses the map to carve a selection
    /// from the full output without duplicating any pass logic.
    /// </summary>
    internal static (string fullOutput, LLMaskMapping mapping, List<(int origOffset, int outOffset)>? tokenMap)
        ObfuscateCore(
        ICSharpFile file,
        IEnumerable<string>? extraPreservedWords = null,
        IEnumerable<string>? basePreservedWords = null,
        bool sortByFrequency = true,
        bool useAssemblyResolution = true,
        IEnumerable<string>? wellKnownRoots = null,
        bool buildTokenMap = false)
    {
        var baseWords = basePreservedWords != null
            ? new HashSet<string>(basePreservedWords, StringComparer.Ordinal)
            : DefaultPreservedWords.Value;

        HashSet<string>? extra = null;
        if (extraPreservedWords != null)
        {
            extra = new HashSet<string>(extraPreservedWords, StringComparer.Ordinal);
        }

        var wellKnownRootsSet = wellKnownRoots != null
            ? new HashSet<string>(wellKnownRoots, StringComparer.Ordinal)
            : DefaultWellKnownRoots.Value;

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
        // Tracks which abbreviation (e.g. "tb") has been claimed by which type short
        // name (e.g. "TextBox") for two-level collision resolution.
        var abbrToType = new Dictionary<string, string>(StringComparer.Ordinal);
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

            // Single-character identifiers carry no proprietary information and pass
            // through verbatim in Pass 2.  This covers generic local variables (a, b)
            // and conventional short method-parameter names (e for EventArgs, s for
            // string, etc.).  For-loop counters (index prefix) and lambda/foreach
            // variables (element prefix) are intentionally excluded so they still
            // receive a semantic label (index1, element1) that documents their role.
            if (name.Length == 1 && prefix is "localVar" or "param")
            {
                continue;
            }

            // For local variables of well-known BCL/framework types, derive a short
            // prefix from the type's CamelCase initials (e.g. TextBox → "tb",
            // Random → "r") so the output reads "tb1" instead of "localVar17".
            // Proprietary-type locals are intentionally excluded — revealing initials
            // would partially leak the type name.
            if (prefix == "localVar" && useAssemblyResolution && decl is ILocalVariableDeclaration localDecl)
            {
                var abbr = TryGetTypeAbbreviationPrefix(localDecl, wellKnownRootsSet, abbrToType);
                if (abbr != null)
                    prefix = abbr;
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
                    // Also preserve the containing type's own short name so that type
                    // names used as static qualifiers are kept verbatim too.
                    // e.g. when processing "Black" from "Brushes.Black", containingType
                    // is Brushes → adds "Brushes" as well.
                    var typeShortName = containingType.GetClrName().ShortName;
                    if (!string.IsNullOrEmpty(typeShortName) && typeShortName.Length > 1)
                        resolvedSafeNames.Add(typeShortName);
                }
            }

            // ── Pass 1c (type usages): walk IUserTypeUsage nodes ─────────────────
            // IReferenceExpression only covers expression-context references.
            // Type names in type positions — base classes, new expressions, variable
            // type annotations, parameter types, cast targets, etc. — live inside
            // IUserTypeUsage nodes and are completely invisible to the expression walk.
            foreach (var typeUsage in file.Descendants<IUserTypeUsage>())
            {
                var typeName = typeUsage.ScalarTypeName;
                if (typeName == null)
                    continue;

                var name = typeName.ShortName;
                if (string.IsNullOrEmpty(name) || name.Length <= 1)
                    continue;

                if (resolvedSafeNames.Contains(name))
                    continue; // already resolved

                if (baseWords.Contains(name) || (extra != null && extra.Contains(name)))
                    continue;

                var element = typeName.Reference?.Resolve().DeclaredElement as ITypeElement;
                if (element == null)
                    continue;

                var fullTypeName = element.GetClrName().FullName;
                var firstDot = fullTypeName.IndexOf('.');
                if (firstDot <= 0)
                    continue;

                var nsRoot = fullTypeName.Substring(0, firstDot);
                if (wellKnownRootsSet.Contains(nsRoot))
                    resolvedSafeNames.Add(name);
            }

            // ── Pass 1c (object initialiser properties) ───────────────────────────
            // Property names in object initialisers (e.g. "Width" in
            // new TextBox { Width = 100 }) live inside IPropertyInitializer nodes
            // as bare Identifier tokens.  They are completely invisible to the
            // IReferenceExpression walk (no qualifier) and to the IUserTypeUsage
            // walk (they are not type references).
            // IMemberInitializer.Reference resolves to the IProperty/IField element,
            // from which we can navigate to the declaring type's namespace.
            foreach (var propInit in file.Descendants<IPropertyInitializer>())
            {
                var name = propInit.MemberName;
                if (string.IsNullOrEmpty(name) || name.Length <= 1)
                    continue;

                if (resolvedSafeNames.Contains(name))
                    continue;

                if (baseWords.Contains(name) || (extra != null && extra.Contains(name)))
                    continue;

                var element = propInit.Reference?.Resolve().DeclaredElement as ITypeMember;
                if (element == null)
                    continue;

                var containingType = element.ContainingType;
                if (containingType == null)
                    continue;

                var fullTypeName = containingType.GetClrName().FullName;
                var firstDot = fullTypeName.IndexOf('.');
                if (firstDot <= 0)
                    continue;

                var nsRoot = fullTypeName.Substring(0, firstDot);
                if (wellKnownRootsSet.Contains(nsRoot))
                {
                    resolvedSafeNames.Add(name);
                    // Also preserve the declaring type's own short name (same
                    // enrichment as the IReferenceExpression path above).
                    var typeShortName = containingType.GetClrName().ShortName;
                    if (!string.IsNullOrEmpty(typeShortName) && typeShortName.Length > 1)
                        resolvedSafeNames.Add(typeShortName);
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
            if (IsWellKnownUsing(directive, wellKnownRootsSet, extra))
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
        // When buildTokenMap is requested (used by PartialPsiBasedObfuscator for
        // selection carving), we record each token's original document start offset
        // alongside its start position in the output buffer.  Using-directive tokens
        // are excluded from the map because they are never part of a code selection.
        List<(int origOffset, int outOffset)>? tokenMap =
            buildTokenMap ? new List<(int origOffset, int outOffset)>() : null;

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

            // Track offset mapping for selection carving (no-op when map is null).
            tokenMap?.Add((token.GetDocumentRange().StartOffset.Offset, sb.Length));

            var tokenType = token.GetTokenType();
            var raw = token.GetText();

            if (tokenType == CSharpTokenType.END_OF_LINE_COMMENT ||
                tokenType == CSharpTokenType.C_STYLE_COMMENT)
            {
                // Drop comments entirely — they add noise without leaking proprietary
                // information. Covers //, /// XML-doc, and /* */ block comments.
            }
            else if (tokenType.IsInterpolatedStringPart())
            {
                // Emit structural delimiters verbatim; obfuscate only the text fragment
                // between them (e.g. the "r" in $"r{…}" or " c" in }· c{).
                // Short fragments (≤ 1 non-whitespace char) are kept verbatim because
                // they carry no proprietary information.
                sb.Append(ObfuscateInterpolatedStringPart(raw, tokenType, strCounters));
            }
            else if (tokenType.IsStringLiteralToken())
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

        return (sb.ToString(), LLMaskMapping.FromForwardMaps(idMap, strMap), tokenMap);
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
    private static bool IsWellKnownUsing(IUsingDirective directive, HashSet<string> roots, HashSet<string>? extra)
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

            return roots.Contains(root) || (extra != null && extra.Contains(root));
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
    // Type-abbreviation prefix derivation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to derive a short prefix from the declared type of a local variable.
    /// Returns <c>null</c> when the type is not from a well-known namespace (i.e. the
    /// variable should keep the generic <c>localVar</c> prefix).
    /// </summary>
    private static string? TryGetTypeAbbreviationPrefix(
        ILocalVariableDeclaration decl,
        HashSet<string> wellKnownRoots,
        Dictionary<string, string> abbrToType)
    {
        // Works for both explicit types and var-inferred types.
        if (decl.DeclaredElement is not ILocalVariable localVar) return null;
        // Skip non-named types (arrays, pointers, type parameters, …).
        if (localVar.Type is not IDeclaredType declaredType) return null;

        // GetTypeElement() requires an active CompilationContextCookie to resolve
        // the module reference context.  Without one the PSI layer logs a
        // "Implicit UniversalModuleReferenceContext" warning and may throw.
        // We create the cookie from the declaration's own module so the call is
        // always explicit and no log noise is produced.
        ITypeElement? typeElement;
        using (CompilationContextCookie.GetOrCreate(
                   decl.GetPsiModule().GetContextFromModule()))
        {
            typeElement = declaredType.GetTypeElement();
        }
        if (typeElement == null) return null;

        var fullName = typeElement.GetClrName().FullName;
        var firstDot = fullName.IndexOf('.');
        if (firstDot <= 0) return null;                                        // no namespace
        if (!wellKnownRoots.Contains(fullName.Substring(0, firstDot))) return null; // proprietary

        // ShortName strips generic arity suffix: "List`1" → "List", "Dictionary`2" → "Dictionary"
        var shortName = typeElement.GetClrName().ShortName;
        if (string.IsNullOrEmpty(shortName)) return null;

        return ResolveAbbreviation(shortName, abbrToType);
    }

    /// <summary>
    /// Returns the abbreviation to use as a placeholder prefix for variables of the
    /// given type short name, handling collisions via a two-level escalation:
    /// <list type="bullet">
    ///   <item>Level 1 — uppercase letters only: <c>TextBox</c> → <c>tb</c></item>
    ///   <item>Level 2 — uppercase + next char (if level 1 is taken by a different type):
    ///         <c>TextBox</c> → <c>tebo</c>, <c>TableBrowser</c> → <c>tabr</c></item>
    /// </list>
    /// </summary>
    private static string ResolveAbbreviation(string shortName, Dictionary<string, string> abbrToType)
    {
        var level1 = ExtractInitials(shortName);
        // Require at least 2 characters so single-component type names (Int32 → "i",
        // List → "l", Random → "r") don't get abbreviated — they'd produce noisy
        // single-letter prefixes indistinguishable from loop counters or local chars.
        if (level1.Length < 2) return "localVar";

        if (!abbrToType.TryGetValue(level1, out var owner) || owner == shortName)
        {
            abbrToType[level1] = shortName;
            return level1;
        }

        // Level 1 already claimed by a different type — escalate.
        var level2 = ExtractInitialsWithNext(shortName);
        if (level2.Length == 0) return "localVar";
        abbrToType[level2] = shortName; // no further collision handling needed
        return level2;
    }

    /// <summary>Extracts lowercase uppercase-letter initials: <c>TextBox</c> → <c>tb</c>.</summary>
    private static string ExtractInitials(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsUpper(c))
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Extracts initials with the following character for collision resolution:
    /// <c>TextBox</c> → <c>tebo</c>, <c>TableBrowser</c> → <c>tabr</c>.
    /// </summary>
    private static string ExtractInitialsWithNext(string name)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (!char.IsUpper(name[i])) continue;
            sb.Append(char.ToLowerInvariant(name[i]));
            if (i + 1 < name.Length && !char.IsUpper(name[i + 1]))
                sb.Append(name[i + 1]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Processes one fragment of a (non-raw) interpolated string token, keeping
    /// structural delimiters intact and obfuscating only the text content between them.
    /// <list type="bullet">
    ///   <item>START  ($"text{  or  @$"text{): prefix = up-to-and-including '"',  suffix = "{"</item>
    ///   <item>MIDDLE (}text{):               prefix = "}",                        suffix = "{"</item>
    ///   <item>END    (}text"  or  }")):      prefix = "}",                        suffix = '"'</item>
    /// </list>
    /// Text fragments whose non-whitespace character count is ≤ 1 are kept verbatim;
    /// longer fragments are replaced with a sequentially-numbered inline placeholder
    /// (no surrounding quotes — the delimiter quotes already delimit the string).
    /// </summary>
    private static string ObfuscateInterpolatedStringPart(
        string raw,
        JetBrains.ReSharper.Psi.Parsing.TokenNodeType tokenType,
        int[] strCounters)
    {
        bool isStart = tokenType == CSharpTokenType.INTERPOLATED_STRING_REGULAR_START
                    || tokenType == CSharpTokenType.INTERPOLATED_STRING_VERBATIM_START;
        bool isEnd   = tokenType == CSharpTokenType.INTERPOLATED_STRING_REGULAR_END
                    || tokenType == CSharpTokenType.INTERPOLATED_STRING_VERBATIM_END;

        string prefix, suffix, textContent;

        if (isStart)
        {
            // Format: $"<text>{   or   @$"<text>{   or   $@"<text>{
            // The opening quote is the last '"' before the text fragment.
            var quotePos = raw.IndexOf('"');
            prefix      = quotePos >= 0 ? raw.Substring(0, quotePos + 1) : "$\"";
            suffix      = "{";
            var textStart = quotePos >= 0 ? quotePos + 1 : prefix.Length;
            // text runs from after the quote to before the trailing '{'
            textContent = raw.Length > textStart + 1
                ? raw.Substring(textStart, raw.Length - textStart - 1)
                : string.Empty;
        }
        else if (isEnd)
        {
            // Format: }<text>"
            prefix      = "}";
            suffix      = "\"";
            textContent = raw.Length > 2 ? raw.Substring(1, raw.Length - 2) : string.Empty;
        }
        else // MIDDLE: }<text>{
        {
            prefix      = "}";
            suffix      = "{";
            textContent = raw.Length > 2 ? raw.Substring(1, raw.Length - 2) : string.Empty;
        }

        var nonWsCount = 0;
        foreach (var c in textContent)
        {
            if (!char.IsWhiteSpace(c))
            {
                nonWsCount++;
            }
        }

        var obfuscatedText = nonWsCount <= 1
            ? textContent
            : $"someString{++strCounters[0]}";

        return prefix + obfuscatedText + suffix;
    }
}
