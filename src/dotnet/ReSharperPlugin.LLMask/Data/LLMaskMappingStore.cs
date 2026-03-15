#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ReSharperPlugin.LLMask.Obfuscation;

namespace ReSharperPlugin.LLMask.Data;

/// <summary>
/// Persists obfuscation session mappings to <c>.llmask-map.json</c> in the solution root.
/// Sessions are append-only — existing sessions are never mutated.
/// The same placeholder may exist in multiple sessions mapping to different originals.
/// </summary>
public static class LLMaskMappingStore
{
    public const string FileName = ".llmask-map.json";

    private static string FilePath(string solutionRoot) =>
        Path.Combine(solutionRoot, FileName);

    public static bool HasMapping(string solutionRoot) =>
        File.Exists(FilePath(solutionRoot));

    /// <summary>
    /// Loads the session matching <paramref name="sessionId"/>, or the most-recent
    /// session when <paramref name="sessionId"/> is null or not found.
    /// Returns null when the file does not exist or contains no sessions.
    /// </summary>
    public static LLMaskMapping? Load(string solutionRoot, string? sessionId = null)
    {
        var path = FilePath(solutionRoot);
        if (!File.Exists(path))
        {
            return null;
        }

        var text = File.ReadAllText(path, Encoding.UTF8);
        var sessions = ParseSessions(text);
        if (sessions.Count == 0)
        {
            return null;
        }

        if (sessionId != null)
        {
            foreach (var s in sessions)
            {
                if (string.Equals(s.SessionId, sessionId, StringComparison.Ordinal))
                {
                    return s;
                }
            }
        }

        // Fall back to most-recent (last in the file).
        return sessions[sessions.Count - 1];
    }

    /// <summary>
    /// Appends <paramref name="mapping"/> as a new session to the mapping file.
    /// Creates the file if it does not exist.
    /// </summary>
    public static void AppendSession(string solutionRoot, LLMaskMapping mapping)
    {
        var path = FilePath(solutionRoot);

        List<LLMaskMapping> sessions;
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path, Encoding.UTF8);
            sessions = ParseSessions(existing);
        }
        else
        {
            sessions = new List<LLMaskMapping>();
        }

        sessions.Add(mapping);
        File.WriteAllText(path, Serialize(sessions), Encoding.UTF8);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Hand-rolled JSON serialization
    // ─────────────────────────────────────────────────────────────────────────

    private static string Serialize(List<LLMaskMapping> sessions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"sessions\": [");
        for (var i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            sb.AppendLine("    {");
            sb.Append("      \"id\": "); AppendJsonString(sb, s.SessionId); sb.AppendLine(",");
            sb.Append("      \"timestamp\": "); AppendJsonString(sb, s.Timestamp); sb.AppendLine(",");
            sb.AppendLine("      \"id_map\": {");
            SerializeMap(sb, s.Identifiers);
            sb.AppendLine("      },");
            sb.AppendLine("      \"str_map\": {");
            SerializeMap(sb, s.Strings);
            sb.AppendLine("      }");
            sb.Append("    }");
            sb.AppendLine(i < sessions.Count - 1 ? "," : string.Empty);
        }
        sb.AppendLine("  ]");
        sb.Append("}");
        return sb.ToString();
    }

    private static void SerializeMap(StringBuilder sb, IReadOnlyDictionary<string, string> map)
    {
        var entries = new List<KeyValuePair<string, string>>(map);
        for (var i = 0; i < entries.Count; i++)
        {
            sb.Append("        ");
            AppendJsonString(sb, entries[i].Key);
            sb.Append(": ");
            AppendJsonString(sb, entries[i].Value);
            sb.AppendLine(i < entries.Count - 1 ? "," : string.Empty);
        }
    }

    private static void AppendJsonString(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append($"\\u{(int)c:x4}");
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }
        sb.Append('"');
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Hand-rolled JSON parsing (minimal — handles only the format we write)
    // ─────────────────────────────────────────────────────────────────────────

    private static List<LLMaskMapping> ParseSessions(string json)
    {
        var result = new List<LLMaskMapping>();
        // Find the "sessions" array
        var sessionsStart = json.IndexOf("\"sessions\"", StringComparison.Ordinal);
        if (sessionsStart < 0)
        {
            return result;
        }

        var arrayStart = json.IndexOf('[', sessionsStart);
        if (arrayStart < 0)
        {
            return result;
        }

        var pos = arrayStart + 1;
        while (pos < json.Length)
        {
            // Skip whitespace
            while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;
            if (pos >= json.Length)
            {
                break;
            }

            if (json[pos] == ']')
            {
                break;
            }

            if (json[pos] != '{') { pos++; continue; }

            var sessionEnd = FindMatchingBrace(json, pos);
            if (sessionEnd < 0)
            {
                break;
            }

            var sessionJson = json.Substring(pos, sessionEnd - pos + 1);
            var session = ParseSession(sessionJson);
            if (session != null)
            {
                result.Add(session);
            }

            pos = sessionEnd + 1;
            // Skip comma
            while (pos < json.Length && (char.IsWhiteSpace(json[pos]) || json[pos] == ',')) pos++;
        }

        return result;
    }

    private static LLMaskMapping? ParseSession(string json)
    {
        var id        = ReadStringField(json, "id");
        var timestamp = ReadStringField(json, "timestamp");
        if (id == null || timestamp == null)
        {
            return null;
        }

        var idMap  = ReadMapField(json, "id_map");
        var strMap = ReadMapField(json, "str_map");

        return new LLMaskMapping(id, timestamp, idMap, strMap);
    }

    private static string? ReadStringField(string json, string fieldName)
    {
        var key = $"\"{fieldName}\"";
        var idx = json.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }

        var colon = json.IndexOf(':', idx + key.Length);
        if (colon < 0)
        {
            return null;
        }

        var strStart = json.IndexOf('"', colon + 1);
        if (strStart < 0)
        {
            return null;
        }

        return ReadJsonString(json, strStart, out _);
    }

    private static Dictionary<string, string> ReadMapField(string json, string fieldName)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var key = $"\"{fieldName}\"";
        var idx = json.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0)
        {
            return result;
        }

        var colon = json.IndexOf(':', idx + key.Length);
        if (colon < 0)
        {
            return result;
        }

        var braceStart = json.IndexOf('{', colon + 1);
        if (braceStart < 0)
        {
            return result;
        }

        var braceEnd = FindMatchingBrace(json, braceStart);
        if (braceEnd < 0)
        {
            return result;
        }

        var pos = braceStart + 1;
        while (pos < braceEnd)
        {
            while (pos < braceEnd && char.IsWhiteSpace(json[pos])) pos++;
            if (pos >= braceEnd || json[pos] != '"') { pos++; continue; }

            var keyStr = ReadJsonString(json, pos, out var afterKey);
            if (keyStr == null)
            {
                break;
            }

            pos = afterKey;

            var colonPos = json.IndexOf(':', pos);
            if (colonPos < 0 || colonPos > braceEnd)
            {
                break;
            }

            pos = colonPos + 1;

            while (pos < braceEnd && char.IsWhiteSpace(json[pos])) pos++;
            if (pos >= braceEnd || json[pos] != '"')
            {
                break;
            }

            var valStr = ReadJsonString(json, pos, out var afterVal);
            if (valStr == null)
            {
                break;
            }

            pos = afterVal;

            result[keyStr] = valStr;

            while (pos < braceEnd && (char.IsWhiteSpace(json[pos]) || json[pos] == ',')) pos++;
        }

        return result;
    }

    private static string? ReadJsonString(string json, int quoteStart, out int afterEnd)
    {
        afterEnd = quoteStart;
        if (quoteStart >= json.Length || json[quoteStart] != '"')
        {
            return null;
        }

        var sb = new StringBuilder();
        var i = quoteStart + 1;
        while (i < json.Length)
        {
            var c = json[i];
            if (c == '"') { afterEnd = i + 1; return sb.ToString(); }
            if (c == '\\' && i + 1 < json.Length)
            {
                i++;
                switch (json[i])
                {
                    case '"':  sb.Append('"');  break;
                    case '\\': sb.Append('\\'); break;
                    case 'n':  sb.Append('\n'); break;
                    case 'r':  sb.Append('\r'); break;
                    case 't':  sb.Append('\t'); break;
                    case 'u' when i + 4 < json.Length:
                        var hex = json.Substring(i + 1, 4);
                        if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var code))
                        {
                            sb.Append((char)code);
                        }

                        i += 4;
                        break;
                    default: sb.Append(json[i]); break;
                }
            }
            else
            {
                sb.Append(c);
            }
            i++;
        }
        return null; // unterminated string
    }

    private static int FindMatchingBrace(string json, int openPos)
    {
        var depth = 0;
        var inString = false;
        for (var i = openPos; i < json.Length; i++)
        {
            if (inString)
            {
                if (json[i] == '\\') { i++; continue; }
                if (json[i] == '"')
                {
                    inString = false;
                }

                continue;
            }
            if (json[i] == '"') { inString = true; continue; }
            if (json[i] == '{')
            {
                depth++;
            }
            else if (json[i] == '}') { depth--; if (depth == 0)
                {
                    return i;
                }
            }
        }
        return -1;
    }
}
