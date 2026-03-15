#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ReSharperPlugin.LLMask.Data;

/// <summary>
/// Loads the LLMask configuration from a <c>llmask.json</c> file in the solution root,
/// falling back to the embedded default when no file is found.
///
/// The JSON format is a simple object with two string-array keys:
/// <code>
/// {
///   "baseWhitelist": [ "var", "string", ... ],
///   "wellKnownNamespaceRoots": [ "System", "Microsoft", ... ]
/// }
/// </code>
///
/// When the file is present each non-empty key replaces the embedded default for that key.
/// Keys absent from the file retain their embedded defaults.
/// </summary>
public static class LLMaskDataProvider
{
    private const string ResourceName = "ReSharperPlugin.LLMask.Resources.llmask.json";

    private static readonly Lazy<LLMaskConfig> EmbeddedConfig =
        new Lazy<LLMaskConfig>(LoadEmbedded);

    /// <summary>
    /// Returns the embedded default configuration (never null).
    /// Loaded once and cached for the lifetime of the process.
    /// </summary>
    public static LLMaskConfig GetEmbedded() => EmbeddedConfig.Value;

    /// <summary>
    /// Loads configuration from <c>llmask.json</c> at <paramref name="solutionRootPath"/>.
    /// If the file does not exist or cannot be parsed the embedded defaults are returned.
    /// Per-key semantics: a non-empty array in the file replaces the embedded default for
    /// that key; an absent or empty array retains the embedded default.
    /// </summary>
    public static LLMaskConfig Load(string? solutionRootPath)
    {
        if (string.IsNullOrEmpty(solutionRootPath))
        {
            return EmbeddedConfig.Value;
        }

        var filePath = Path.Combine(solutionRootPath, "llmask.json");
        if (!File.Exists(filePath))
        {
            return EmbeddedConfig.Value;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            return Merge(Parse(json));
        }
        catch
        {
            // Corrupted or unreadable file — silently fall back to defaults.
            return EmbeddedConfig.Value;
        }
    }

    /// <summary>
    /// Loads configuration from an explicit absolute file path.
    /// Falls back to embedded defaults if the file does not exist or cannot be parsed.
    /// </summary>
    public static LLMaskConfig LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return EmbeddedConfig.Value;
        }

        try
        {
            return Merge(Parse(File.ReadAllText(filePath)));
        }
        catch
        {
            return EmbeddedConfig.Value;
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static LLMaskConfig LoadEmbedded()
    {
        var asm = typeof(LLMaskDataProvider).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found. Ensure the file is marked as EmbeddedResource.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// Per-key merge: prefer values from the file; fall back to embedded for missing/empty keys.
    private static LLMaskConfig Merge(LLMaskConfig fromFile)
    {
        var embedded = EmbeddedConfig.Value;
        return new LLMaskConfig(
            fromFile.BaseWhitelist.Count > 0 ? fromFile.BaseWhitelist : embedded.BaseWhitelist,
            fromFile.WellKnownNamespaceRoots.Count > 0 ? fromFile.WellKnownNamespaceRoots : embedded.WellKnownNamespaceRoots);
    }

    /// <summary>
    /// Minimal JSON parser for our specific two-key structure.
    /// Handles only flat string arrays (no nesting, no escape sequences beyond standard JSON).
    /// </summary>
    private static LLMaskConfig Parse(string json)
    {
        return new LLMaskConfig(
            ExtractStringArray(json, "baseWhitelist"),
            ExtractStringArray(json, "wellKnownNamespaceRoots"));
    }

    private static IReadOnlyList<string> ExtractStringArray(string json, string key)
    {
        // Match the content between [ and ] for the given top-level key.
        // RegexOptions.Singleline lets . match newlines inside the array.
        var match = Regex.Match(
            json,
            @"""" + Regex.Escape(key) + @"""\s*:\s*\[([^\[\]]*)\]",
            RegexOptions.Singleline);

        if (!match.Success)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        var arrayContent = match.Groups[1].Value;

        // Extract each quoted string value.
        foreach (Match item in Regex.Matches(arrayContent, @"""((?:[^""\\]|\\.)*)"""))
        {
            var value = item.Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                result.Add(value);
            }
        }

        return result;
    }
}

/// <summary>
/// Immutable snapshot of the LLMask configuration.
/// </summary>
public sealed class LLMaskConfig
{
    /// <summary>Identifiers that are always preserved verbatim (C# keywords, BCL types, …).</summary>
    public IReadOnlyList<string> BaseWhitelist { get; }

    /// <summary>Root namespace segments used by the assembly-resolution pass.</summary>
    public IReadOnlyList<string> WellKnownNamespaceRoots { get; }

    public LLMaskConfig(
        IReadOnlyList<string> baseWhitelist,
        IReadOnlyList<string> wellKnownNamespaceRoots)
    {
        BaseWhitelist = baseWhitelist;
        WellKnownNamespaceRoots = wellKnownNamespaceRoots;
    }
}
