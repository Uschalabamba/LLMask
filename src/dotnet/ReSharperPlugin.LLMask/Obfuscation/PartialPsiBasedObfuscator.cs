#nullable enable
using System.Collections.Generic;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.Util;

namespace ReSharperPlugin.LLMask.Obfuscation;

/// <summary>
/// Obfuscates a user-selected region of a C# file using the same full-file PSI
/// analysis as <see cref="PsiBasedObfuscator"/>, then carves out only the portion
/// of the output that corresponds to the original selection.
///
/// Because all three passes (frequency counting, identifier registration, and
/// reference resolution) still operate on the entire file, identifiers inside the
/// selection receive the same placeholder numbers they would have in a full-file
/// obfuscation — there is no consistency gap at the selection boundaries.
/// </summary>
public static class PartialPsiBasedObfuscator
{
    /// <summary>
    /// Obfuscates the whole file, then returns only the portion of the output
    /// that corresponds to <paramref name="selectionRange"/> in the original document.
    /// Snaps to token boundaries (selections that start or end mid-token are expanded
    /// to the enclosing token — this never occurs in practice for code selections).
    /// </summary>
    public static string ObfuscateSelection(
        ICSharpFile file,
        TextRange selectionRange,
        IEnumerable<string>? extraPreservedWords     = null,
        IEnumerable<string>? basePreservedWords      = null,
        bool sortByFrequency                         = true,
        bool useAssemblyResolution                   = true,
        IEnumerable<string>? wellKnownNamespaceRoots = null)
    {
        var (fullOutput, tokenMap) = PsiBasedObfuscator.ObfuscateCore(
            file,
            extraPreservedWords,
            basePreservedWords,
            sortByFrequency,
            useAssemblyResolution,
            wellKnownNamespaceRoots,
            buildTokenMap: true);

        return tokenMap is { Count: > 0 }
            ? CarveSelection(fullOutput, tokenMap, selectionRange)
            : string.Empty;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Selection carving
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the portion of <paramref name="fullOutput"/> that corresponds to
    /// the original document range <paramref name="sel"/>.
    ///
    /// The algorithm works by scanning the token offset map (built during Pass 2)
    /// for the output positions of the tokens that bound the selection:
    /// <list type="bullet">
    ///   <item><term>outStart</term>
    ///     <description>Output offset of the last token whose original start ≤ sel.StartOffset
    ///     (i.e. the token containing or immediately preceding the selection start).</description>
    ///   </item>
    ///   <item><term>outEnd</term>
    ///     <description>Output offset of the first token whose original start ≥ sel.EndOffset
    ///     (i.e. the first token after the selection).  Defaults to end-of-output.</description>
    ///   </item>
    /// </list>
    /// </summary>
    private static string CarveSelection(
        string fullOutput,
        List<(int origOffset, int outOffset)> tokenMap,
        TextRange sel)
    {
        // Find the output start: last token with origOffset ≤ sel.StartOffset.
        var outStart = tokenMap[0].outOffset;
        for (var i = tokenMap.Count - 1; i >= 0; i--)
        {
            if (tokenMap[i].origOffset <= sel.StartOffset)
            {
                outStart = tokenMap[i].outOffset;
                break;
            }
        }

        // Find the output end: first token with origOffset ≥ sel.EndOffset.
        // Defaults to end-of-output when every token falls within the selection.
        var outEnd = fullOutput.Length;
        foreach (var (origOffset, outOffset) in tokenMap)
        {
            if (origOffset >= sel.EndOffset)
            {
                outEnd = outOffset;
                break;
            }
        }

        return outEnd > outStart
            ? fullOutput.Substring(outStart, outEnd - outStart)
            : string.Empty;
    }
}
