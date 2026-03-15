#nullable enable
using JetBrains.ReSharper.Psi.CSharp.Parsing;
using JetBrains.ReSharper.Psi.Parsing;

namespace ReSharperPlugin.LLMask.Obfuscation;

/// <summary>
/// Extension methods on <see cref="TokenNodeType"/> for token-type predicates
/// shared by the PSI-based obfuscators.
/// </summary>
internal static class ITokenTypeExtensions
{
    // Regular/verbatim/raw non-interpolated string literals — these are complete
    // tokens that can be replaced wholesale with a "someStringN" placeholder.
    internal static bool IsStringLiteralToken(this TokenNodeType tokenType) =>
        tokenType == CSharpTokenType.STRING_LITERAL_REGULAR
        || tokenType == CSharpTokenType.STRING_LITERAL_VERBATIM
        || tokenType == CSharpTokenType.SINGLE_LINE_RAW_STRING_LITERAL
        || tokenType == CSharpTokenType.MULTI_LINE_RAW_STRING_LITERAL;

    // Interpolated string parts that contain a user-visible text fragment between
    // structural delimiters ($", {, }, ").  Raw interpolated string tokens
    // ($"""…""") are intentionally excluded and fall through to the verbatim
    // else branch — their delimiter nesting is too complex to parse safely here.
    internal static bool IsInterpolatedStringPart(this TokenNodeType tokenType) =>
        tokenType == CSharpTokenType.INTERPOLATED_STRING_REGULAR_START
        || tokenType == CSharpTokenType.INTERPOLATED_STRING_REGULAR_MIDDLE
        || tokenType == CSharpTokenType.INTERPOLATED_STRING_REGULAR_END
        || tokenType == CSharpTokenType.INTERPOLATED_STRING_VERBATIM_START
        || tokenType == CSharpTokenType.INTERPOLATED_STRING_VERBATIM_MIDDLE
        || tokenType == CSharpTokenType.INTERPOLATED_STRING_VERBATIM_END;
}
