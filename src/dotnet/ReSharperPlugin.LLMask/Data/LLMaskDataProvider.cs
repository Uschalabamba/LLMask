#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ReSharperPlugin.LLMask.Data;

/// <summary>
/// Loads the LLMask configuration from a llmask.json file in the solution root,
/// falling back to the embedded default when no file is found.
/// When the file is present each non-empty key replaces the embedded default for that key.
/// Keys absent from the file retain their embedded defaults.
/// </summary>
public static class LLMaskDataProvider
{
    private const string ResourceName = "ReSharperPlugin.LLMask.Resources.llmask.json";

    private static readonly Lazy<LLMaskConfig> embeddedConfig = new(LoadEmbedded);

    public static LLMaskConfig GetEmbedded() => embeddedConfig.Value;

    public static LLMaskConfig Load(string? solutionRootPath)
    {
        if (string.IsNullOrEmpty(solutionRootPath))
        {
            return embeddedConfig.Value;
        }

        var filePath = Path.Combine(solutionRootPath, "llmask.json");
        if (!File.Exists(filePath))
        {
            return embeddedConfig.Value;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            return Merge(Parse(json));
        }
        catch
        {
            return embeddedConfig.Value;
        }
    }

    public static LLMaskConfig LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return embeddedConfig.Value;
        }

        try
        {
            return Merge(Parse(File.ReadAllText(filePath)));
        }
        catch
        {
            return embeddedConfig.Value;
        }
    }
    
    private static LLMaskConfig LoadEmbedded()
    {
        var asm = typeof(LLMaskDataProvider).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found. Ensure the file is marked as EmbeddedResource.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private static LLMaskConfig Merge(LLMaskConfig fromFile)
    {
        var embedded = embeddedConfig.Value;
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
public sealed class LLMaskConfig(
    IReadOnlyList<string> baseWhitelist,
    IReadOnlyList<string> wellKnownNamespaceRoots)
{
    public IReadOnlyList<string> BaseWhitelist { get; } = baseWhitelist;

    public IReadOnlyList<string> WellKnownNamespaceRoots { get; } = wellKnownNamespaceRoots;
}
