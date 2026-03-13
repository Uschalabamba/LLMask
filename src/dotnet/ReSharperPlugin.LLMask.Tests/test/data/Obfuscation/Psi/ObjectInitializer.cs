using System;
using System.Collections.Generic;

// Fix 3 — object initialiser properties: property names in object initialiser
// syntax (new Foo { Prop = value }) live in IPropertyInitializer nodes and are
// invisible to both the IReferenceExpression and IUserTypeUsage walks.
// IMemberInitializer.Reference must be used to resolve them and preserve the
// names verbatim when the declaring type is in a well-known namespace.

// A locally-declared class whose properties are set via an object initialiser.
// Both the class name and property names should be obfuscated because the class
// is proprietary (not in a well-known namespace).
class ProprietaryWidget
{
    public int Width  { get; set; }
    public int Height { get; set; }
    public string Label { get; set; }
}

// BCL type whose settable properties are set via object initialiser.
// ArgumentException inherits from Exception, which lives in System — a
// well-known namespace.  Its "Message"-like properties resolve to
// System.Exception/System.ArgumentException → nsRoot = "System".
class ObjectInitCase
{
    // List<T> is in the base whitelist so it passes through anyway, but we set
    // properties on it via an object initialiser to exercise the code path.
    // FormatException is not in the whitelist; its Capacity setter (from
    // List<T>) and Data (from Exception) let us test BCL property name preservation.
    void UseList()
    {
        var ex = new ArgumentException
        {
            // HelpLink and Source are properties of System.Exception → well-known
            HelpLink = "http://example.com",
            Source   = "MyAssembly"
        };

        // Proprietary class: all names obfuscated regardless.
        var widget = new ProprietaryWidget
        {
            Width  = 100,
            Height = 200,
            Label  = "hello"
        };
    }
}
