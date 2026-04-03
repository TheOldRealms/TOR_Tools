using System.Xml.Linq;
using TORTools.Core.Schema;

namespace TORTools.Core.Services;

/// <summary>
/// Service for loading and managing tuple list data from external XML files.
/// </summary>
public class TupleListService
{
    private readonly Dictionary<string, Dictionary<string, List<Dictionary<string, string>>>> _cache = new();
    private readonly object _cacheLock = new();

    /// <summary>
    /// Loads tuple list data from a source XML file.
    /// </summary>
    /// <param name="sourceFilePath">Path to the source XML file</param>
    /// <param name="config">Tuple list configuration</param>
    /// <returns>Dictionary mapping local keys to lists of tuple dictionaries</returns>
    public Dictionary<string, List<Dictionary<string, string>>> LoadTupleData(string sourceFilePath, TupleListConfig config)
    {
        var cacheKey = $"{sourceFilePath}|{config.ContainerPath}|{config.ElementName}";

        lock (_cacheLock)
        {
            if (_cache.TryGetValue(cacheKey, out var cached))
                return cached;
        }

        var result = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(sourceFilePath))
        {
            Console.WriteLine($"[TupleListService] Source file not found: {sourceFilePath}");
            return result;
        }

        try
        {
            var doc = XDocument.Load(sourceFilePath);
            var root = doc.Root;
            if (root == null)
                return result;

            foreach (var entry in root.Elements())
            {
                var keyAttr = entry.Attribute(config.SourceKeyField);
                if (keyAttr == null)
                    continue;

                var key = keyAttr.Value;
                var tuples = new List<Dictionary<string, string>>();

                // Find the container element
                var container = entry.Element(config.ContainerPath);
                if (container != null)
                {
                    // Find all tuple elements
                    foreach (var tupleElement in container.Elements(config.ElementName))
                    {
                        var tupleDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        // Extract all configured column attributes
                        foreach (var column in config.Columns)
                        {
                            var attrValue = tupleElement.Attribute(column.Attribute)?.Value ?? "";
                            tupleDict[column.Attribute] = attrValue;
                        }

                        if (tupleDict.Count > 0)
                        {
                            tuples.Add(tupleDict);
                        }
                    }
                }

                if (tuples.Count > 0)
                {
                    result[key] = tuples;
                }
            }

            Console.WriteLine($"[TupleListService] Loaded {result.Count} entries with {config.ElementName} from {Path.GetFileName(sourceFilePath)}");

            lock (_cacheLock)
            {
                _cache[cacheKey] = result;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TupleListService] Error loading {sourceFilePath}: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Gets the tuple list for a given local key.
    /// </summary>
    public List<Dictionary<string, string>> GetTuples(Dictionary<string, List<Dictionary<string, string>>> data, string localKey)
    {
        if (data.TryGetValue(localKey, out var tuples))
            return tuples;
        return new List<Dictionary<string, string>>();
    }

    /// <summary>
    /// Formats tuple data for display in a cell (e.g., "Physical: 100%").
    /// </summary>
    public string FormatTuplesForDisplay(List<Dictionary<string, string>> tuples, TupleListConfig config)
    {
        if (tuples.Count == 0)
            return "-";

        var parts = new List<string>();
        foreach (var tuple in tuples)
        {
            var displayParts = new List<string>();
            foreach (var column in config.Columns)
            {
                if (tuple.TryGetValue(column.Attribute, out var value) && !string.IsNullOrEmpty(value))
                {
                    // Format percentage values
                    if (column.Type == "number" && double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var numVal))
                    {
                        // Convert 0.00-1.00 to percentage display
                        if (numVal >= -1 && numVal <= 1)
                        {
                            displayParts.Add($"{numVal * 100:0}%");
                        }
                        else
                        {
                            displayParts.Add($"{numVal:0.##}");
                        }
                    }
                    else
                    {
                        displayParts.Add(value);
                    }
                }
            }
            if (displayParts.Count > 0)
            {
                parts.Add(string.Join(": ", displayParts));
            }
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Saves tuple data to the source XML file.
    /// </summary>
    /// <param name="sourceFilePath">Path to the source XML file</param>
    /// <param name="config">Tuple list configuration</param>
    /// <param name="localKey">The entry key to update</param>
    /// <param name="tuples">The tuple data to save</param>
    /// <returns>True if save was successful</returns>
    public bool SaveTupleData(string sourceFilePath, TupleListConfig config, string localKey, List<Dictionary<string, string>> tuples)
    {
        if (!File.Exists(sourceFilePath))
        {
            Console.WriteLine($"[TupleListService] Source file not found: {sourceFilePath}");
            return false;
        }

        try
        {
            var doc = XDocument.Load(sourceFilePath, LoadOptions.PreserveWhitespace);
            var root = doc.Root;
            if (root == null)
            {
                Console.WriteLine($"[TupleListService] No root element in {sourceFilePath}");
                return false;
            }

            // Find the entry with the matching key
            XElement? targetEntry = null;
            foreach (var entry in root.Elements())
            {
                var keyAttr = entry.Attribute(config.SourceKeyField);
                if (keyAttr != null && keyAttr.Value.Equals(localKey, StringComparison.OrdinalIgnoreCase))
                {
                    targetEntry = entry;
                    break;
                }
            }

            if (targetEntry == null)
            {
                Console.WriteLine($"[TupleListService] Entry not found for key: {localKey}");
                return false;
            }

            // Find or create the container element
            var container = targetEntry.Element(config.ContainerPath);
            if (container == null)
            {
                container = new XElement(config.ContainerPath);
                targetEntry.Add(container);
            }

            // Remove existing tuple elements
            container.RemoveNodes();

            // Add new tuple elements
            foreach (var tuple in tuples)
            {
                var tupleElement = new XElement(config.ElementName);
                foreach (var column in config.Columns)
                {
                    if (tuple.TryGetValue(column.Attribute, out var value) && !string.IsNullOrEmpty(value))
                    {
                        tupleElement.SetAttributeValue(column.Attribute, value);
                    }
                }
                container.Add(tupleElement);
            }

            // Save the file
            doc.Save(sourceFilePath);
            Console.WriteLine($"[TupleListService] Saved {tuples.Count} tuples for {localKey} to {Path.GetFileName(sourceFilePath)}");

            // Update the cache
            var cacheKey = $"{sourceFilePath}|{config.ContainerPath}|{config.ElementName}";
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(cacheKey, out var cachedData))
                {
                    cachedData[localKey] = tuples;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TupleListService] Error saving {sourceFilePath}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Clears the cache.
    /// </summary>
    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _cache.Clear();
        }
    }
}
