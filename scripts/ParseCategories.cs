using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;

class Program
{
    static void Main()
    {
        var filePath = @"C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\TOR_Core\ModuleData\tor_strings.xml";
        var lines = File.ReadAllLines(filePath);

        var categoryMappings = new Dictionary<string, List<string>>();
        string currentCategory = "Uncategorized";
        var categoryPattern = new Regex(@"^\s*<!--\s*(.+?)\s*-->$");
        var stringPattern = new Regex(@"<string\s+id=""([^""]+)""");

        // Comments to skip (not categories)
        string[] skipPatterns = {
            "TODO", "FIXME", "NOTE", "========", "Culture-", "These cultures",
            "Certain string ids", "Because localization", "is it", "unsure if",
            "disabled texts", "Omitted", "Enchanter NPCs", "General Enchantment"
        };

        foreach (var line in lines)
        {
            var commentMatch = categoryPattern.Match(line);
            if (commentMatch.Success)
            {
                var comment = commentMatch.Groups[1].Value.Trim();

                // Skip explanatory comments
                bool skip = false;
                foreach (var p in skipPatterns)
                {
                    if (comment.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        skip = true;
                        break;
                    }
                }
                if (skip) continue;

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
        int totalStrings = 0;
        foreach (var v in categoryMappings.Values)
            totalStrings += v.Count;

        Console.WriteLine($"Found {categoryMappings.Count} categories with {totalStrings} total strings");
        Console.WriteLine();

        var sorted = categoryMappings.OrderBy(k => k.Key).ToList();
        foreach (var kvp in sorted)
        {
            Console.WriteLine($"## {kvp.Key} ({kvp.Value.Count} strings)");
            int showCount = Math.Min(3, kvp.Value.Count);
            for (int i = 0; i < showCount; i++)
                Console.WriteLine($"   - {kvp.Value[i]}");
            if (kvp.Value.Count > 3)
                Console.WriteLine($"   ... and {kvp.Value.Count - 3} more");
            Console.WriteLine();
        }
    }
}
