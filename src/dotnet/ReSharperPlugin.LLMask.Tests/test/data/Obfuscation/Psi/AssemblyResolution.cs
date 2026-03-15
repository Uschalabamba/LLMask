using System;

class AssemblyResolutionCase
{
    // ProcessData is a proprietary method — always obfuscated regardless of the setting.
    string ProcessData(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;
        Console.WriteLine(input);
        return input.ToUpper();
    }
}
