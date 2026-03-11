using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

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
///   - Char literals are kept verbatim (single characters are not proprietary).
///   - Numbers, operators, and whitespace are kept verbatim.
/// </summary>
public static class CodeObfuscator
{
    private static readonly HashSet<string> PreservedWords =
        new(StringComparer.Ordinal)
        {
            // C# keywords
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
            "checked", "class", "const", "continue", "decimal", "default", "delegate", "do",
            "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
            "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
            "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
            "object", "operator", "out", "override", "params", "private", "protected",
            "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
            "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
            "virtual", "void", "volatile", "while",

            // Contextual keywords
            "add", "alias", "ascending", "async", "await", "by", "descending", "dynamic",
            "equals", "from", "get", "global", "group", "into", "join", "let", "nameof",
            "notnull", "on", "orderby", "partial", "remove", "select", "set", "unmanaged",
            "value", "var", "when", "where", "with", "yield",

            // Preprocessor identifiers (appear as plain identifiers after '#')
            "define", "elif", "endif", "endregion", "error", "line", "nullable",
            "pragma", "region", "undef", "warning",

            // Well-known BCL / framework types
            "Action", "Activator", "AppDomain",
            "ArgumentException", "ArgumentNullException", "ArgumentOutOfRangeException",
            "Array", "ArrayList", "Attribute",
            "Boolean", "Byte",
            "CancellationToken", "CancellationTokenSource", "Char",
            "Console", "Convert",
            "DateTime", "DateTimeOffset", "Decimal", "Dictionary",
            "Double",
            "Enum", "Environment", "EventArgs", "EventHandler", "Exception",
            "Func",
            "Guid",
            "HashSet",
            "ICollection", "IComparable", "IDisposable",
            "IEnumerable", "IEnumerator", "IEquatable",
            "IList", "IReadOnlyCollection", "IReadOnlyDictionary", "IReadOnlyList",
            "Int16", "Int32", "Int64", "IntPtr",
            "InvalidOperationException",
            "KeyValuePair",
            "Lazy", "LinkedList", "List",
            "Math", "MemoryStream", "Monitor", "Mutex",
            "NotImplementedException", "NotSupportedException",
            "Nullable",
            "Object", "ObjectDisposedException",
            "OperationCanceledException", "OutOfMemoryException",
            "Parallel", "Path", "Queue",
            "Random", "Regex",
            "SByte", "Semaphore", "SemaphoreSlim", "Single",
            "SortedDictionary", "SortedList", "Stack", "StackOverflowException",
            "Stream", "StreamReader", "StreamWriter", "String", "StringBuilder",
            "Task", "Thread", "ThreadPool", "TimeSpan", "Timer", "Tuple",
            "Type",
            "UInt16", "UInt32", "UInt64", "UIntPtr", "Uri",
            "ValueTask", "ValueTuple", "Version",
            "WeakReference",

            // Common attribute names used without the 'Attribute' suffix
            "Flags", "NonSerialized", "Obsolete", "Serializable", "ThreadStatic",
        };

    private static readonly Regex Tokenizer = new Regex(
        // 1. Block comment (may span lines)
        @"(?<BlockComment>/\*[\s\S]*?\*/)" +
        // 2. Line / doc comment
        @"|(?<LineComment>//[^\r\n]*)" +
        // 3. Verbatim interpolated string  ($@ or @$ prefix)
        @"|(?<VerbatimInterpString>(?:\$@|@\$)""(?:[^""]|"""")*"")" +
        // 4. Interpolated string
        @"|(?<InterpString>\$""(?:[^""\\]|\\.)*"")" +
        // 5. Verbatim string
        @"|(?<VerbatimString>@""(?:[^""]|"""")*"")" +
        // 6. Regular string
        @"|(?<RegularString>""(?:[^""\\]|\\.)*"")" +
        // 7. Char literal (kept verbatim – single chars are not proprietary)
        @"|(?<CharLiteral>'(?:[^'\\]|\\.)*')" +
        // 8. Identifier or keyword
        @"|(?<Identifier>[a-zA-Z_]\w*)" +
        // 9. Catch-all: one character at a time (numbers, ops, whitespace, etc.)
        @"|(?<Other>[\s\S])",
        RegexOptions.Compiled
    );

    public static string Obfuscate(string code)
    {
        // id:  [0] TypeName  [1] localVar  [2] _field  [3] CONST_
        var idCounters  = new int[4];
        // str: [0] str      [1] path       [2] url
        var strCounters = new int[3];

        var idMap  = new Dictionary<string, string>(StringComparer.Ordinal);
        var strMap = new Dictionary<string, string>(StringComparer.Ordinal);

        var sb = new StringBuilder(code.Length);

        foreach (Match m in Tokenizer.Matches(code))
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
                if (!strMap.TryGetValue(raw, out var strPlaceholder))
                {
                    var content  = ExtractStringContent(raw);
                    strPlaceholder  = IsUrl(content)      ? $"\"<url_{++strCounters[2]}>\""
                        : IsFilePath(content) ? $"\"<path_{++strCounters[1]}>\""
                        : $"\"<str_{++strCounters[0]}>\"";
                    strMap[raw] = strPlaceholder;
                }
                sb.Append(strPlaceholder);
            }
            else if (m.Groups["CharLiteral"].Success)
            {
                sb.Append(raw); // single characters are not proprietary
            }
            else if (m.Groups["Identifier"].Success)
            {
                if (PreservedWords.Contains(raw))
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

        return sb.ToString();
    }

    private static string MakeIdentifierPlaceholder(string id, int[] counters)
    {
        if (id.Length > 1 && id[0] == '_')
            return "_field"   + (++counters[2]);
        if (IsAllUpperCase(id))
            return "CONST_"   + (++counters[3]);
        if (char.IsUpper(id[0]))
            return "TypeName" + (++counters[0]);
        return "localVar"     + (++counters[1]);
    }

    private static bool IsAllUpperCase(string s)
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
    private static string ExtractStringContent(string token)
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

    private static bool IsUrl(string s) =>
        s.IndexOf("://", StringComparison.Ordinal) >= 0;

    private static bool IsFilePath(string s) =>
        s.IndexOf('\\') >= 0 ||
        (s.Length >= 3 && char.IsLetter(s[0]) && s[1] == ':');
}