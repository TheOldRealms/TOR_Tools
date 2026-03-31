using System.Xml.Linq;
using TORTools.Core.Schema;

namespace TORTools.Core.Services;

/// <summary>
/// Service for loading and querying cross-reference data between XML files.
/// </summary>
public class CrossReferenceService
{
    private readonly Dictionary<string, Dictionary<string, List<string>>> _crossRefCache = new();
    private readonly object _cacheLock = new();

    /// <summary>
    /// Loads cross-reference data from a source XML file.
    /// </summary>
    /// <param name="sourceFilePath">Path to the source XML file (e.g., tor_extendeditemproperties.xml)</param>
    /// <param name="config">Cross-reference configuration from the schema</param>
    /// <returns>Dictionary mapping local keys to lists of referenced values</returns>
    public Dictionary<string, List<string>> LoadCrossReferences(string sourceFilePath, CrossReferenceConfig config)
    {
        var cacheKey = $"{sourceFilePath}|{config.SourceKeyField}|{config.SourceValuePath}";

        lock (_cacheLock)
        {
            if (_crossRefCache.TryGetValue(cacheKey, out var cached))
                return cached;
        }

        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(sourceFilePath))
        {
            Console.WriteLine($"[CrossReferenceService] Source file not found: {sourceFilePath}");
            return result;
        }

        try
        {
            var doc = XDocument.Load(sourceFilePath);
            var root = doc.Root;
            if (root == null)
                return result;

            // Parse the value path (e.g., "ItemTraits/ItemTrait")
            var pathParts = config.SourceValuePath.Split('/');

            foreach (var entry in root.Elements())
            {
                // Get the key field value (e.g., ItemStringId)
                var keyAttr = entry.Attribute(config.SourceKeyField);
                if (keyAttr == null)
                    continue;

                var key = keyAttr.Value;
                var values = new List<string>();

                // Navigate the path to get values
                IEnumerable<XElement> currentElements = new[] { entry };
                foreach (var part in pathParts)
                {
                    currentElements = currentElements.SelectMany(e => e.Elements(part));
                }

                // Collect the values (text content of final elements)
                foreach (var valueElement in currentElements)
                {
                    var value = valueElement.Value?.Trim();
                    if (!string.IsNullOrEmpty(value))
                        values.Add(value);
                }

                if (values.Count > 0)
                {
                    result[key] = values;
                }
            }

            Console.WriteLine($"[CrossReferenceService] Loaded {result.Count} cross-references from {Path.GetFileName(sourceFilePath)}");

            lock (_cacheLock)
            {
                _crossRefCache[cacheKey] = result;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CrossReferenceService] Error loading {sourceFilePath}: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Gets the cross-referenced values for a given local key.
    /// </summary>
    /// <param name="crossRefs">The loaded cross-reference dictionary</param>
    /// <param name="localKey">The local key to look up (e.g., item ID)</param>
    /// <returns>List of referenced values, or empty list if not found</returns>
    public List<string> GetValues(Dictionary<string, List<string>> crossRefs, string localKey)
    {
        if (crossRefs.TryGetValue(localKey, out var values))
            return values;
        return new List<string>();
    }

    /// <summary>
    /// Formats a list of cross-reference values for display.
    /// </summary>
    /// <param name="values">List of values</param>
    /// <returns>Comma-separated string of values</returns>
    public string FormatValues(List<string> values)
    {
        return string.Join(", ", values);
    }

    /// <summary>
    /// Clears the cross-reference cache.
    /// </summary>
    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _crossRefCache.Clear();
        }
    }

    /// <summary>
    /// Loads reverse cross-references - maps values back to their source keys.
    /// For example: which items use each trait.
    /// </summary>
    /// <param name="sourceFilePath">Path to the source XML file</param>
    /// <param name="config">Cross-reference configuration</param>
    /// <returns>Dictionary mapping values to lists of source keys that reference them</returns>
    public Dictionary<string, List<string>> LoadReverseCrossReferences(string sourceFilePath, CrossReferenceConfig config)
    {
        var cacheKey = $"reverse|{sourceFilePath}|{config.SourceKeyField}|{config.SourceValuePath}";

        lock (_cacheLock)
        {
            if (_crossRefCache.TryGetValue(cacheKey, out var cached))
                return cached;
        }

        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(sourceFilePath))
        {
            Console.WriteLine($"[CrossReferenceService] Source file not found for reverse lookup: {sourceFilePath}");
            return result;
        }

        try
        {
            var doc = XDocument.Load(sourceFilePath);
            var root = doc.Root;
            if (root == null)
                return result;

            // Parse the value path (e.g., "ItemTraits/ItemTrait")
            var pathParts = config.SourceValuePath.Split('/');

            foreach (var entry in root.Elements())
            {
                // Get the source key field value (e.g., ItemStringId = item ID)
                var keyAttr = entry.Attribute(config.SourceKeyField);
                if (keyAttr == null)
                    continue;

                var sourceKey = keyAttr.Value;

                // Navigate the path to get values
                IEnumerable<XElement> currentElements = new[] { entry };
                foreach (var part in pathParts)
                {
                    currentElements = currentElements.SelectMany(e => e.Elements(part));
                }

                // For each value, add the source key to the reverse mapping
                foreach (var valueElement in currentElements)
                {
                    var value = valueElement.Value?.Trim();
                    if (!string.IsNullOrEmpty(value))
                    {
                        if (!result.TryGetValue(value, out var keyList))
                        {
                            keyList = new List<string>();
                            result[value] = keyList;
                        }
                        if (!keyList.Contains(sourceKey))
                        {
                            keyList.Add(sourceKey);
                        }
                    }
                }
            }

            Console.WriteLine($"[CrossReferenceService] Loaded {result.Count} reverse cross-references from {Path.GetFileName(sourceFilePath)}");

            lock (_cacheLock)
            {
                _crossRefCache[cacheKey] = result;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CrossReferenceService] Error loading reverse refs from {sourceFilePath}: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Updates cross-reference values in the source XML file.
    /// </summary>
    /// <param name="sourceFilePath">Path to the source XML file (e.g., tor_extendeditemproperties.xml)</param>
    /// <param name="config">Cross-reference configuration from the schema</param>
    /// <param name="localKey">The local key (e.g., item ID)</param>
    /// <param name="newValues">The new values to set</param>
    /// <returns>True if update was successful</returns>
    public bool UpdateCrossReference(string sourceFilePath, CrossReferenceConfig config, string localKey, List<string> newValues)
    {
        if (!File.Exists(sourceFilePath))
        {
            Console.WriteLine($"[CrossReferenceService] Cannot update - source file not found: {sourceFilePath}");
            return false;
        }

        try
        {
            var doc = XDocument.Load(sourceFilePath, LoadOptions.PreserveWhitespace);
            var root = doc.Root;
            if (root == null)
                return false;

            // Parse the value path (e.g., "ItemTraits/ItemTrait")
            var pathParts = config.SourceValuePath.Split('/');
            var containerElementName = pathParts.Length > 1 ? pathParts[0] : null;
            var valueElementName = pathParts.Length > 1 ? pathParts[1] : pathParts[0];

            // Find the entry with matching key
            XElement? targetEntry = null;
            foreach (var entry in root.Elements())
            {
                var keyAttr = entry.Attribute(config.SourceKeyField);
                if (keyAttr != null && string.Equals(keyAttr.Value, localKey, StringComparison.OrdinalIgnoreCase))
                {
                    targetEntry = entry;
                    break;
                }
            }

            if (targetEntry == null)
            {
                // Create new entry if it doesn't exist
                if (newValues.Count == 0)
                {
                    // Nothing to add, nothing to create
                    return true;
                }

                var entryName = root.Elements().FirstOrDefault()?.Name.LocalName ?? "Entry";
                targetEntry = new XElement(entryName);
                targetEntry.SetAttributeValue(config.SourceKeyField, localKey);
                root.Add(targetEntry);
                Console.WriteLine($"[CrossReferenceService] Created new entry for {localKey}");
            }

            // Find or create the container element
            XElement container;
            if (containerElementName != null)
            {
                container = targetEntry.Element(containerElementName) ?? new XElement(containerElementName);
                if (container.Parent == null)
                    targetEntry.Add(container);
            }
            else
            {
                container = targetEntry;
            }

            // Remove existing value elements and any text nodes (whitespace)
            container.Elements(valueElementName).Remove();
            container.Nodes().OfType<XText>().Remove();

            // Detect indentation from document (usually 2 spaces per level)
            var baseIndent = "      "; // 6 spaces for ItemTrait level

            // Add new value elements with proper formatting
            foreach (var value in newValues)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    // Add newline + indent before each element
                    container.Add(new XText("\n" + baseIndent));
                    container.Add(new XElement(valueElementName, value.Trim()));
                }
            }

            // Add closing indent if we have elements
            if (newValues.Count > 0)
            {
                container.Add(new XText("\n    ")); // 4 spaces for closing tag indent
            }

            // If container is now empty and it's a child element, remove it
            if (containerElementName != null && !container.HasElements)
            {
                container.Remove();
            }

            // Save the file with UTF-8 BOM and uppercase encoding declaration
            // First save to memory to get the content
            string xmlContent;
            using (var memStream = new MemoryStream())
            {
                doc.Save(memStream);
                memStream.Position = 0;
                using (var reader = new StreamReader(memStream))
                {
                    xmlContent = reader.ReadToEnd();
                }
            }

            // Replace lowercase utf-8 with uppercase UTF-8
            xmlContent = xmlContent.Replace("encoding=\"utf-8\"", "encoding=\"UTF-8\"");

            // Write with BOM
            File.WriteAllText(sourceFilePath, xmlContent, new System.Text.UTF8Encoding(true));
            Console.WriteLine($"[CrossReferenceService] Saved file to: {sourceFilePath}");
            Console.WriteLine($"[CrossReferenceService] Updated {localKey} with {newValues.Count} values in {Path.GetFileName(sourceFilePath)}");

            // Clear cache to force reload
            ClearCache();

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CrossReferenceService] Error updating {sourceFilePath}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Loads all key values from a target XML file for autocomplete.
    /// For example: load all trait IDs from tor_itemtraits.xml.
    /// </summary>
    /// <param name="targetFilePath">Path to the target XML file</param>
    /// <param name="keyField">The attribute name containing the key (e.g., "ItemTraitStringId")</param>
    /// <returns>List of all key values in the file</returns>
    public List<string> LoadTargetKeys(string targetFilePath, string keyField)
    {
        var cacheKey = $"keys|{targetFilePath}|{keyField}";

        lock (_cacheLock)
        {
            if (_crossRefCache.TryGetValue(cacheKey, out var cached))
                return cached.Keys.ToList();
        }

        var result = new List<string>();

        if (!File.Exists(targetFilePath))
        {
            Console.WriteLine($"[CrossReferenceService] Target file not found for keys: {targetFilePath}");
            return result;
        }

        try
        {
            var doc = XDocument.Load(targetFilePath);
            var root = doc.Root;
            if (root == null)
                return result;

            foreach (var entry in root.Elements())
            {
                var keyAttr = entry.Attribute(keyField);
                if (keyAttr != null && !string.IsNullOrEmpty(keyAttr.Value))
                {
                    result.Add(keyAttr.Value);
                }
            }

            Console.WriteLine($"[CrossReferenceService] Loaded {result.Count} target keys from {Path.GetFileName(targetFilePath)}");

            // Cache as dictionary for consistent cache format
            lock (_cacheLock)
            {
                _crossRefCache[cacheKey] = result.ToDictionary(k => k, k => new List<string>());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CrossReferenceService] Error loading keys from {targetFilePath}: {ex.Message}");
        }

        return result;
    }
}
