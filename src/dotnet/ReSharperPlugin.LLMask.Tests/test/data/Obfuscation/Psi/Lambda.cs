using System.Collections.Generic;
using System.Linq;

class LambdaCase
{
    void Method()
    {
        var items = new List<string>();
        var filtered = items.Where(x => x.Length > 0).ToList();
    }
}
