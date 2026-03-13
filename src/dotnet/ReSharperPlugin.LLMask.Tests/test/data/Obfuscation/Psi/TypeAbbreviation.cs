// Test data for type-abbreviation prefixes on local variable declarations.
// Variables of well-known BCL types with multi-component names should receive
// a prefix derived from the type's CamelCase initials rather than "localVar".
// Types whose initials collapse to a single letter (Int32→"i", List→"l") are
// excluded by design — they would produce prefixes indistinguishable from
// loop counters or single-char verbatim identifiers.

using System;
using System.Text;

class TypeAbbreviationCase
{
    void Run()
    {
        // StringBuilder → initials "SB" → "sb" → sb1
        var builder = new StringBuilder();

        // ArgumentException → initials "AE" → "ae" → ae1
        var err = new ArgumentException("test");

        // Second variable of the same type → ae2
        var err2 = new ArgumentException("other");

        // Second StringBuilder → sb2
        var builder2 = new StringBuilder();

        // Proprietary type: no well-known namespace → falls back to localVar
        var widget = new ProprietaryWidget();
    }
}

// A locally-declared class — not in a well-known namespace,
// so variables of this type must keep the generic "localVar" prefix.
class ProprietaryWidget { }
