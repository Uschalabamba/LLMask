#nullable enable
using System.Text;

namespace ReSharperPlugin.LLMask.Obfuscation;

/// <summary>
/// Reverses an obfuscation run by substituting placeholder tokens back to
/// their original identifiers and string literals, using a <see cref="LLMaskMapping"/>.
///
/// Identifiers or string literals introduced by the LLM (not present in the mapping)
/// are left untouched.
/// </summary>
public static class Deobfuscator
{
    /// <summary>
    /// Extracts the session ID from the first line of <paramref name="text"/> if it
    /// begins with the LLMask session marker comment, e.g. "// LLMask-Session: a1b2c3".
    /// Returns null when the marker is absent.
    /// </summary>
    public static string? ExtractSessionId(string text)
    {
        if (!text.StartsWith(LLMaskMapping.MarkerPrefix, System.StringComparison.Ordinal))
        {
            return null;
        }

        var lineEnd = text.IndexOf('\n');
        var line = lineEnd >= 0
            ? text.Substring(0, lineEnd).TrimEnd('\r')
            : text.TrimEnd('\r');

        var id = line.Substring(LLMaskMapping.MarkerPrefix.Length).Trim();
        return id.Length > 0 ? id : null;
    }

    /// <summary>
    /// Deobfuscates <paramref name="obfuscatedCode"/> using <paramref name="mapping"/>:
    /// <list type="bullet">
    ///   <item>Strips the leading session marker line if present.</item>
    ///   <item>For each string token (with surrounding quotes), looks it up in
    ///     <see cref="LLMaskMapping.Strings"/> and substitutes the original.</item>
    ///   <item>For each identifier token, looks it up in
    ///     <see cref="LLMaskMapping.Identifiers"/> and substitutes the original.</item>
    ///   <item>Everything else is emitted verbatim.</item>
    /// </list>
    /// </summary>
    public static string Deobfuscate(string obfuscatedCode, LLMaskMapping mapping)
    {
        // Strip session marker line.
        var code = obfuscatedCode;
        if (code.StartsWith(LLMaskMapping.MarkerPrefix, System.StringComparison.Ordinal))
        {
            var lineEnd = code.IndexOf('\n');
            code = lineEnd >= 0 ? code.Substring(lineEnd + 1) : string.Empty;
        }

        var sb = new StringBuilder(code.Length);

        foreach (System.Text.RegularExpressions.Match m in StringBasedObfuscator.tokenizer.Matches(code))
        {
            var raw = m.Value;

            // String token: look up the full token (including quotes) in the strings map.
            if (m.Groups["RegularString"].Success
                || m.Groups["VerbatimString"].Success
                || m.Groups["InterpString"].Success
                || m.Groups["VerbatimInterpString"].Success)
            {
                sb.Append(mapping.Strings.TryGetValue(raw, out var origStr) ? origStr : raw);
            }
            // Identifier token: look up in the identifiers map.
            else if (m.Groups["Identifier"].Success)
            {
                sb.Append(mapping.Identifiers.TryGetValue(raw, out var origId) ? origId : raw);
            }
            // Everything else verbatim.
            else
            {
                sb.Append(raw);
            }
        }

        return sb.ToString();
    }
}
