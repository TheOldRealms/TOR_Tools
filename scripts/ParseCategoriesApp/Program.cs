using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using System.Text.Json;

class Program
{
    static string EscapeXml(string s)
    {
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }

    static void Main()
    {
        var filePath = @"C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\TOR_Core\ModuleData\tor_strings.xml";
        var outputDir = @"C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\TORTools\docs";
        var lines = File.ReadAllLines(filePath);

        var categoryMappings = new Dictionary<string, List<string>>();
        var stringToCategory = new Dictionary<string, string>();
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

                // Clean up category name
                currentCategory = comment.TrimEnd('!', ' ');
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
                stringToCategory[stringId] = currentCategory;
            }
        }

        // Output summary
        int totalStrings = 0;
        foreach (var v in categoryMappings.Values)
            totalStrings += v.Count;

        Console.WriteLine($"Found {categoryMappings.Count} categories with {totalStrings} total strings");
        Console.WriteLine();

        // Write JSON mapping (stringId -> category)
        var jsonPath = Path.Combine(outputDir, "string_category_mapping.json");
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(stringToCategory, options));
        Console.WriteLine($"Wrote {stringToCategory.Count} mappings to {jsonPath}");

        // Generate XML metadata file
        var xmlPath = @"C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\TORTools\data\tor_strings_metadata.xml";
        Directory.CreateDirectory(Path.GetDirectoryName(xmlPath)!);

        using (var writer = new StreamWriter(xmlPath, false, System.Text.Encoding.UTF8))
        {
            writer.WriteLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            writer.WriteLine("<StringMetadata>");

            // Group by category for cleaner output
            var byCategory = stringToCategory
                .GroupBy(kvp => kvp.Value)
                .OrderBy(g => g.Key);

            foreach (var group in byCategory)
            {
                writer.WriteLine($"  <!-- {group.Key} -->");
                foreach (var item in group.OrderBy(x => x.Key))
                {
                    writer.WriteLine($"  <String id=\"{item.Key}\" category=\"{EscapeXml(group.Key)}\" />");
                }
                writer.WriteLine();
            }

            writer.WriteLine("</StringMetadata>");
        }
        Console.WriteLine($"Wrote metadata XML to {xmlPath}");

        // Write summary
        var sorted = categoryMappings.OrderBy(k => k.Key).ToList();
        foreach (var kvp in sorted)
        {
            if (kvp.Value.Count == 0) continue; // Skip empty categories
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
