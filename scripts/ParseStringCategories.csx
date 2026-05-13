// C# Script to parse tor_strings.xml and extract category mappings from XML comments
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;

var filePath = @"C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\TOR_Core\ModuleData\tor_strings.xml";
var lines = File.ReadAllLines(filePath);

var categoryMappings = new Dictionary<string, List<string>>();
string currentCategory = "Uncategorized";
var categoryPattern = new Regex(@"^\s*<!--\s*(.+?)\s*-->$");
var stringPattern = new Regex(@"<string\s+id=""([^""]+)""");

// Comments to skip (not categories)
var skipPatterns = new[] {
    "TODO", "FIXME", "NOTE", "========", "Culture-", "These cultures",
    "Certain string ids", "Because localization", "is it", "unsure if",
    "disabled texts", "Omitted"
};

foreach (var line in lines)
{
    var commentMatch = categoryPattern.Match(line);
    if (commentMatch.Success)
    {
        var comment = commentMatch.Groups[1].Value.Trim();

        // Skip explanatory comments
        if (skipPatterns.Any(p => comment.Contains(p, StringComparison.OrdinalIgnoreCase)))
            continue;

        // Skip very short comments or inline comments
        if (comment.Length < 2 || comment.StartsWith("!"))
            continue;

        currentCategory = comment;
        if (!categoryMappings.ContainsKey(currentCategory))
            categoryMappings[currentCategory] = new List<string>();
    }

    var stringMatch = stringPattern.Match(line);
    if (stringMatch.Success)
    {
        var stringId = stringMatch.Groups[1].Value;
        if (!categoryMappings.ContainsKey(currentCategory))
            categoryMappings[currentCategory] = new List<string>();
        categoryMappings[currentCategory].Add(stringId);
    }
}

// Output summary
Console.WriteLine($"Found {categoryMappings.Count} categories with {categoryMappings.Values.Sum(l => l.Count)} total strings\n");

foreach (var kvp in categoryMappings.OrderBy(k => k.Key))
{
    Console.WriteLine($"## {kvp.Key} ({kvp.Value.Count} strings)");
    var samples = kvp.Value.Take(3);
    foreach (var s in samples)
        Console.WriteLine($"   - {s}");
    if (kvp.Value.Count > 3)
        Console.WriteLine($"   ... and {kvp.Value.Count - 3} more");
    Console.WriteLine();
}
