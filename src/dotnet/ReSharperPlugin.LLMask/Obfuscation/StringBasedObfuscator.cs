#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using ReSharperPlugin.LLMask.Data;

namespace ReSharperPlugin.LLMask.Obfuscation;

/// <summary>
/// Tokenises a C# code snippet and replaces any identifier, string literal, or
/// comment that could reveal proprietary information with a stable, generic placeholder.
///
/// Preservation rules:
///   - C# language keywords and contextual keywords are kept verbatim.
///   - Well-known BCL / framework type names are kept verbatim.
///   - Everything else is replaced with a deterministic placeholder that encodes
///     the naming convention of the original identifier:
///       _camelCase  →  _field1, _field2, …
///       ALL_UPPER   →  CONST_1, CONST_2, …
///       PascalCase  →  TypeName1, TypeName2, …
///       camelCase   →  localVar1, localVar2, …
///   - String literals are replaced with "<str_N>", "<path_N>", or "<url_N>"
///     depending on whether the content looks like a plain string, a file path,
///     or a URL.  The same literal value always maps to the same placeholder.
///   - Block/line/doc comments are replaced with /* <comment> */ or // <comment>.
///   - Char literals and single-character string literals are kept verbatim
///     (single characters carry no proprietary information).
///   - Numbers, operators, and whitespace are kept verbatim.
/// </summary>
public static class StringBasedObfuscator
{
    private static readonly Lazy<HashSet<string>> preservedWords =
        new(() => new HashSet<string>(LLMaskDataProvider.GetEmbedded().BaseWhitelist, StringComparer.Ordinal));

    internal static readonly Regex tokenizer = new (
        // 1. Block comment (may span lines)
        @"(?<BlockComment>/\*[\s\S]*?\*/)" +
        // 2. Line / doc comment
        @"|(?<LineComment>//[^\r\n]*)" +
        // 3. Verbatim interpolated string  ($@ or @$ prefix)
        """|(?<VerbatimInterpString>(?:\$@|@\$)"(?:[^"]|"")*")""" +
        // 4. Interpolated string
        """|(?<InterpString>\$"(?:[^"\\]|\\.)*")""" +
        // 5. Verbatim string
        """|(?<VerbatimString>@"(?:[^"]|"")*")""" +
        // 6. Regular string
        """|(?<RegularString>"(?:[^"\\]|\\.)*")""" +
        // 7. Char literal (kept verbatim – single chars are not proprietary)
        @"|(?<CharLiteral>'(?:[^'\\]|\\.)*')" +
        // 8. Identifier or keyword
        @"|(?<Identifier>[a-zA-Z_]\w*)" +
        // 9. Catch-all: one character at a time (numbers, ops, whitespace, etc.)
        @"|(?<Other>[\s\S])",
        RegexOptions.Compiled
    );

    public static (string output, LLMaskMapping mapping) Obfuscate(
        string code,
        IEnumerable<string>? extraPreservedWords = null,
        IEnumerable<string>? basePreservedWords = null)
    {
        var baseWords = basePreservedWords != null
            ? new HashSet<string>(basePreservedWords, StringComparer.Ordinal)
            : preservedWords.Value;

        HashSet<string>? extra = null;
        if (extraPreservedWords != null)
        {
            extra = new HashSet<string>(extraPreservedWords, StringComparer.Ordinal);
            extra.ExceptWith(baseWords); // skip words already in the base list
        }

        // id:  [0] TypeName  [1] localVar  [2] _field  [3] CONST_
        var idCounters  = new int[4];
        // str: [0] str      [1] path       [2] url
        var strCounters = new int[3];

        var idMap  = new Dictionary<string, string>(StringComparer.Ordinal);
        var strMap = new Dictionary<string, string>(StringComparer.Ordinal);

        var sb = new StringBuilder(code.Length);

        foreach (Match m in tokenizer.Matches(code))
        {
            var raw = m.Value;

            if (m.Groups["BlockComment"].Success)
            {
                sb.Append("/* <comment> */");
            }
            else if (m.Groups["LineComment"].Success)
            {
                sb.Append("// <comment>");
            }
            else if (m.Groups["VerbatimInterpString"].Success
                     || m.Groups["InterpString"].Success
                     || m.Groups["VerbatimString"].Success
                     || m.Groups["RegularString"].Success)
            {
                var content = ExtractStringContent(raw);
                if (content.Length == 1)
                {
                    sb.Append(raw); // single-char strings carry no proprietary information
                }
                else
                {
                    if (!strMap.TryGetValue(raw, out var strPlaceholder))
                    {
                        strPlaceholder = MakeStringPlaceholder(content, strCounters);
                        strMap[raw] = strPlaceholder;
                    }
                    sb.Append(strPlaceholder);
                }
            }
            else if (m.Groups["CharLiteral"].Success)
            {
                sb.Append(raw); // single characters are not proprietary
            }
            else if (m.Groups["Identifier"].Success)
            {
                if (baseWords.Contains(raw) || (extra != null && extra.Contains(raw)))
                {
                    sb.Append(raw);
                }
                else
                {
                    if (!idMap.TryGetValue(raw, out var idPlaceholder))
                    {
                        idPlaceholder = MakeIdentifierPlaceholder(raw, idCounters);
                        idMap[raw]    = idPlaceholder;
                    }
                    sb.Append(idPlaceholder);
                }
            }
            else // Other: whitespace, operators, numbers, punctuation
            {
                sb.Append(raw);
            }
        }

        return (sb.ToString(), LLMaskMapping.FromForwardMaps(idMap, strMap));
    }

    internal static string MakeIdentifierPlaceholder(string id, int[] counters)
    {
        if (id.Length > 1 && id[0] == '_')
        {
            return "_field"   + ++counters[2];
        }

        if (IsAllUpperCase(id))
        {
            return "CONST_"   + ++counters[3];
        }

        if (char.IsUpper(id[0]))
        {
            return "TypeName" + ++counters[0];
        }

        return "localVar"     + ++counters[1];
    }

    internal static bool IsAllUpperCase(string s)
    {
        var hasLetter = false;
        foreach (var c in s)
        {
            if (!char.IsLetter(c))
            {
                continue;
            }

            if (char.IsLower(c))
            {
                return false;
            }

            hasLetter = true;
        }
        return hasLetter;
    }

    /// <summary>Strips string prefix characters ($, @) and surrounding quotes.</summary>
    internal static string ExtractStringContent(string token)
    {
        var i = 0;
        while (i < token.Length && (token[i] == '$' || token[i] == '@'))
        {
            i++;
        }

        if (i < token.Length && token[i] == '"')
        {
            i++;
        }

        var end = token.Length;
        if (end > i && token[end - 1] == '"')
        {
            end--;
        }

        return i <= end ? token.Substring(i, end - i) : string.Empty;
    }

    internal static bool IsUrl(string s) =>
        s.IndexOf("://", StringComparison.Ordinal) >= 0;

    internal static bool IsFilePath(string s) =>
        s.IndexOf('\\') >= 0 ||
        (s.Length >= 3 && char.IsLetter(s[0]) && s[1] == ':');

    /// <summary>Creates a string placeholder based on content heuristics.</summary>
    internal static string MakeStringPlaceholder(string content, int[] strCounters) =>
        IsUrl(content)      ? $"\"url{++strCounters[2]}\""
        : IsFilePath(content) ? $"\"path{++strCounters[1]}\""
        : $"\"someString{++strCounters[0]}\"";

}
