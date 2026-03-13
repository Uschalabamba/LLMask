using System;

// Fix 2 — base class: ApplicationException is a BCL type in base-class position.
// This is an IUserTypeUsage node and is invisible to the IReferenceExpression walk.
// The IUserTypeUsage pass must resolve it and add it to resolvedSafeNames so it
// passes through verbatim.
class CustomExceptionCase : ApplicationException
{
    CustomExceptionCase(string message) : base(message) { }
}

// Fix 2 — return-type annotation and constructor call: FormatException appears both
// as a declared return type and in a 'new' expression. Both positions are IUserTypeUsage
// nodes, not IReferenceExpression nodes.
// Fix 1 — static qualifier: BitConverter is a well-known type used only as a static
// qualifier. When GetBytes is resolved its containing type (BitConverter) is also
// added to resolvedSafeNames.
class TypeUsageCase
{
    FormatException CreateException(string message)
    {
        return new FormatException(message);
    }

    byte[] ConvertInt(int value)
    {
        return BitConverter.GetBytes(value);
    }
}

// Proprietary types — must always be obfuscated regardless of the setting.
class ProprietaryCase
{
    void ProprietaryMethod() { }
}
