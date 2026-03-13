// Diagnostic file: used only to inspect PSI node types for object initialiser
// property assignments.  The class is defined in the same file so the PSI can
// resolve the properties without any external assembly references.

class DiagWidget
{
    public int Width  { get; set; }
    public int Height { get; set; }
    public string Label { get; set; }
}

class DiagUsage
{
    void Run()
    {
        var w = new DiagWidget
        {
            Width  = 100,
            Height = 200,
            Label  = "hello"
        };
    }
}
