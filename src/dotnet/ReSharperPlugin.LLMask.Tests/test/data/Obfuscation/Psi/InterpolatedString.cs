// Test data for interpolated string obfuscation.
// The obfuscator must preserve the $"..." structure and emit syntactically
// valid output; only the text fragments between structural delimiters are
// candidates for replacement.

class InterpolatedStringCase
{
    void Run(int row, int column, string name, string path)
    {
        // Single-char text fragments on each side of the interpolation hole.
        // "r" and "c" are 1 non-ws char → kept verbatim.
        var a = $"r{row + 1} c{column + 1}";

        // Multi-char text fragment: "hello " → replaced with someStringN.
        var b = $"hello {name}!";

        // Empty text fragment (start/end only delimiters) — no content to replace.
        var c = $"{row}";

        // Verbatim interpolated string.
        var d = $@"path: {path}\file";

        // Multiple holes with short fragments.
        var e = $"x={row} y={column}";
    }
}
