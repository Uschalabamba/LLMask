using System.Collections.Generic;

class ForeachCase
{
    void Method()
    {
        var items = new List<string>();
        foreach (var item in items)
        {
            var result = item.Length;
        }
    }
}
