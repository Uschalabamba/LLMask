// Test data for PartialPsiBasedObfuscator selection-carving tests.
//
// SelectionOuter declares an outer field and an inner method.
// Tests carve just InnerMethod and verify that:
//   - outerSecret (_myField1) does NOT appear in the carved output
//   - InnerMethod's body identifiers get the same placeholders as in full-file mode
//   - BCL-resolved names (HelpLink) are preserved verbatim inside the carved output

using System;

class SelectionOuter
{
    string outerSecret = "external";

    void InnerMethod()
    {
        var ex = new ArgumentException("inner message");
        var link = ex.HelpLink;
    }
}
