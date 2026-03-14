#nullable enable
using JetBrains.Collections;
using System;
using System.Collections.Generic;

namespace ReSharperPlugin.LLMask.Obfuscation;

/// <summary>
/// Holds the inverse mapping produced by an obfuscation run, keyed by a short
/// session ID so multiple runs can coexist in the mapping store.
/// </summary>
public sealed class LLMaskMapping
{
    /// <summary>6-char lowercase hex string taken from a fresh Guid.</summary>
    public string SessionId { get; }

    /// <summary>ISO-8601 timestamp of the obfuscation run.</summary>
    public string Timestamp { get; }

    /// <summary>obfuscated identifier → original identifier</summary>
    public IReadOnlyDictionary<string, string> Identifiers { get; }

    /// <summary>
    /// obfuscated full string token (including surrounding quotes, e.g. "someString1")
    ///   → original full string token (including surrounding quotes, e.g. "hello world")
    /// </summary>
    public IReadOnlyDictionary<string, string> Strings { get; }

    public LLMaskMapping(
        string sessionId,
        string timestamp,
        Dictionary<string, string> identifiers,
        Dictionary<string, string> strings)
    {
        SessionId   = sessionId;
        Timestamp   = timestamp;
        Identifiers = identifiers;
        Strings     = strings;
    }

    /// <summary>
    /// Builds a <see cref="LLMaskMapping"/> from the forward maps maintained
    /// internally by the obfuscators:
    /// <list type="bullet">
    ///   <item><paramref name="idMap"/>  — original identifier → obfuscated identifier</item>
    ///   <item><paramref name="strMap"/> — original full string token → obfuscated full string token</item>
    /// </list>
    /// Both maps are inverted so the result can be used directly for deobfuscation.
    /// </summary>
    public static LLMaskMapping FromForwardMaps(
        Dictionary<string, string> idMap,
        Dictionary<string, string> strMap)
    {
        var sessionId = Guid.NewGuid().ToString("N").Substring(0, 6);
        var timestamp = DateTime.UtcNow.ToString("o");

        var identifiers = new Dictionary<string, string>(idMap.Count, StringComparer.Ordinal);
        foreach (var (original, obfuscated) in idMap)
        {
            identifiers[obfuscated] = original;
        }

        var strings = new Dictionary<string, string>(strMap.Count, StringComparer.Ordinal);
        foreach (var (original, obfuscated) in strMap)
        {
            strings[obfuscated] = original;
        }

        return new LLMaskMapping(sessionId, timestamp, identifiers, strings);
    }

    public const string MarkerPrefix = "// LLMask-Session: ";
    public string MarkerLine => MarkerPrefix + SessionId;
}
