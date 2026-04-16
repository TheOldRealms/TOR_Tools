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
    /// Parses a source value path into element parts and optional attribute name.
    /// Handles: @AttrName, Container/Element, Container/Element/@AttrName
    /// </summary>
    private static (string[] elementParts, string? attributeToExtract) ParseSourceValuePath(string sourcePath)
    {
        if (sourcePath.StartsWith("@"))
        {
            // Direct attribute from entry element: @AttrName
            return (Array.Empty<string>(), sourcePath.Substring(1));
        }
        else if (sourcePath.Contains("/@"))
        {
            // Nested path with attribute: Container/Element/@AttrName
            var attrIndex = sourcePath.LastIndexOf("/@");
            var attributeToExtract = sourcePath.Substring(attrIndex + 2);
            var elementPath = sourcePath.Substring(0, attrIndex);
            return (elementPath.Split('/'), attributeToExtract);
        }
        else
        {
            // Pure element path: Container/Element (extract text content)
            return (sourcePath.Split('/'), null);
        }
    }

    /// <summary>
    /// Extracts values from an XML entry based on parsed path.
    /// </summary>
    private static List<string> ExtractValuesFromEntry(XElement entry, string[] elementParts, string? attributeToExtract)
    {
        var values = new List<string>();

        if (attributeToExtract != null && elementParts.Length == 0)
        {
            // Read attribute value directly from the entry element
            var attrValue = entry.Attribute(attributeToExtract)?.Value?.Trim();
            if (!string.IsNullOrEmpty(attrValue))
                values.Add(attrValue);
        }
        else
        {
            // Navigate the element path
            IEnumerable<XElement> currentElements = new[] { entry };
            foreach (var part in elementParts)
            {
                currentElements = currentElements.SelectMany(e => e.Elements(part));
            }

            // Collect values - either attribute or text content
            foreach (var valueElement in currentElements)
            {
                string? value;
                if (attributeToExtract != null)
                {
                    // Extract attribute from nested elements
                    value = valueElement.Attribute(attributeToExtract)?.Value?.Trim();
                }
                else
                {
                    // Extract text content
                    value = valueElement.Value?.Trim();
                }

                if (!string.IsNullOrEmpty(value))
                    values.Add(value);
            }
        }

        return values;
    }

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

            var (elementParts, attributeToExtract) = ParseSourceValuePath(config.SourceValuePath);

            foreach (var entry in root.Elements())
            {
                // Get the key field value (e.g., ItemStringId)
                var keyAttr = entry.Attribute(config.SourceKeyField);
                if (keyAttr == null)
                    continue;

                var key = keyAttr.Value;
                var values = ExtractValuesFromEntry(entry, elementParts, attributeToExtract);

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

            var (elementParts, attributeToExtract) = ParseSourceValuePath(config.SourceValuePath);

            foreach (var entry in root.Elements())
            {
                // Get the source key field value (e.g., ItemStringId = item ID)
                var keyAttr = entry.Attribute(config.SourceKeyField);
                if (keyAttr == null)
                    continue;

                var sourceKey = keyAttr.Value;
                var values = ExtractValuesFromEntry(entry, elementParts, attributeToExtract);

                // Add each value to the reverse mapping
                foreach (var value in values)
                {
                    // Strip prefix if configured (e.g., "SkillSet." from "SkillSet.tor_skills_level26")
                    var normalizedValue = value;
                    if (!string.IsNullOrEmpty(config.PrefixToStrip) &&
                        value.StartsWith(config.PrefixToStrip, StringComparison.OrdinalIgnoreCase))
                    {
                        normalizedValue = value.Substring(config.PrefixToStrip.Length);
                    }

                    if (!result.TryGetValue(normalizedValue, out var keyList))
                    {
                        keyList = new List<string>();
                        result[normalizedValue] = keyList;
                    }
                    if (!keyList.Contains(sourceKey))
                    {
                        keyList.Add(sourceKey);
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
    /// <param name="compactFormat">If true, write attributes on single line; if false, each on new line</param>
    /// <returns>True if update was successful</returns>
    public bool UpdateCrossReference(string sourceFilePath, CrossReferenceConfig config, string localKey, List<string> newValues, bool compactFormat = true)
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

            // Check if this is an attribute path (starts with "@")
            var isAttributePath = config.SourceValuePath.StartsWith("@");
            var attributeName = isAttributePath ? config.SourceValuePath.Substring(1) : null;

            // Parse the value path for element paths (e.g., "ItemTraits/ItemTrait")
            var pathParts = isAttributePath
                ? Array.Empty<string>()
                : config.SourceValuePath.Split('/');
            var containerElementName = pathParts.Length > 1 ? pathParts[0] : null;
            var valueElementName = pathParts.Length > 1 ? pathParts[1] : (pathParts.Length > 0 ? pathParts[0] : null);

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

            // Handle attribute path differently from element path
            if (isAttributePath && attributeName != null)
            {
                // For attribute paths, set or remove the attribute
                if (newValues.Count > 0 && !string.IsNullOrWhiteSpace(newValues[0]))
                {
                    targetEntry.SetAttributeValue(attributeName, newValues[0].Trim());
                }
                else
                {
                    targetEntry.Attribute(attributeName)?.Remove();
                }
            }
            else if (valueElementName != null)
            {
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
            }

            // Save with proper formatting based on compactFormat parameter
            SaveDocument(doc, sourceFilePath, compactFormat);
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
    /// Saves an XDocument with the specified format.
    /// </summary>
    private static void SaveDocument(XDocument doc, string filePath, bool compactFormat)
    {
        var sb = new System.Text.StringBuilder();
        var indent = "  "; // 2 spaces

        // Write XML declaration
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");

        var root = doc.Root;
        if (root != null)
        {
            // Write root element
            sb.AppendLine($"<{root.Name.LocalName}>");

            // Write each entry with appropriate formatting
            foreach (var element in root.Elements())
            {
                if (compactFormat)
                    WriteElementCompact(sb, element, indent);
                else
                    WriteElementWithMultiLineAttributes(sb, element, indent);
            }

            sb.Append($"</{root.Name.LocalName}>");
        }

        var content = sb.ToString();

        // Write with UTF-8 BOM
        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        byte[] bom = { 0xEF, 0xBB, 0xBF };
        stream.Write(bom, 0, bom.Length);

        using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
        writer.Write(content);
    }

    /// <summary>
    /// Writes an element with all attributes on a single line (compact format).
    /// </summary>
    private static void WriteElementCompact(System.Text.StringBuilder sb, XElement element, string baseIndent, int depth = 1)
    {
        var indent = string.Concat(Enumerable.Repeat(baseIndent, depth));
        var elementName = element.Name.LocalName;
        var attributes = element.Attributes().ToList();
        var children = element.Elements().ToList();
        var hasTextContent = !string.IsNullOrWhiteSpace(element.Value) && !children.Any();

        sb.Append(indent);
        sb.Append($"<{elementName}");

        // Write all attributes on one line
        foreach (var attr in attributes)
        {
            sb.Append($" {attr.Name.LocalName}=\"{EscapeXmlAttributeValue(attr.Value)}\"");
        }

        // Close element
        if (children.Any())
        {
            sb.AppendLine(">");
            foreach (var child in children)
            {
                WriteElementCompact(sb, child, baseIndent, depth + 1);
            }
            sb.AppendLine($"{indent}</{elementName}>");
        }
        else if (hasTextContent)
        {
            sb.AppendLine($">{EscapeXmlText(element.Value)}</{elementName}>");
        }
        else
        {
            sb.AppendLine(" />");
        }
    }

    /// <summary>
    /// Writes an element with each attribute on its own line (rich format).
    /// </summary>
    private static void WriteElementWithMultiLineAttributes(System.Text.StringBuilder sb, XElement element, string baseIndent, int depth = 1)
    {
        var indent = string.Concat(Enumerable.Repeat(baseIndent, depth));
        var elementName = element.Name.LocalName;
        var attributes = element.Attributes().ToList();
        var children = element.Elements().ToList();
        var hasTextContent = !string.IsNullOrWhiteSpace(element.Value) && !children.Any();

        // Calculate alignment padding (align under first attribute)
        var alignPad = new string(' ', indent.Length + elementName.Length + 2);

        sb.Append(indent);
        sb.Append($"<{elementName}");

        // Write attributes
        for (int i = 0; i < attributes.Count; i++)
        {
            var attr = attributes[i];
            var attrStr = $"{attr.Name.LocalName}=\"{EscapeXmlAttributeValue(attr.Value)}\"";

            if (i == 0)
            {
                sb.Append($" {attrStr}");
            }
            else
            {
                sb.AppendLine();
                sb.Append($"{alignPad}{attrStr}");
            }
        }

        // Close element
        if (children.Any())
        {
            sb.AppendLine(">");
            foreach (var child in children)
            {
                WriteElementWithMultiLineAttributes(sb, child, baseIndent, depth + 1);
            }
            sb.AppendLine($"{indent}</{elementName}>");
        }
        else if (hasTextContent)
        {
            sb.AppendLine($">{EscapeXmlText(element.Value)}</{elementName}>");
        }
        else
        {
            sb.AppendLine(" />");
        }
    }

    private static string EscapeXmlText(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private static string EscapeXmlAttributeValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
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

    /// <summary>
    /// Loads descriptions from a target XML file for display in cross-reference fields.
    /// Prefers "display_description" attribute (player-friendly), falls back to "description".
    /// </summary>
    /// <param name="targetFilePath">Path to the target XML file</param>
    /// <param name="keyField">Name of the key attribute (e.g., "id")</param>
    /// <returns>Dictionary mapping IDs to their descriptions</returns>
    public Dictionary<string, string> LoadTargetDescriptions(string targetFilePath, string keyField)
    {
        var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(targetFilePath))
        {
            Console.WriteLine($"[CrossReferenceService] Target file not found for descriptions: {targetFilePath}");
            return descriptions;
        }

        try
        {
            var doc = XDocument.Load(targetFilePath);
            foreach (var element in doc.Root?.Elements() ?? Enumerable.Empty<XElement>())
            {
                var key = element.Attribute(keyField)?.Value;
                // Prefer display_description (player-friendly), fallback to description
                var description = element.Attribute("display_description")?.Value
                               ?? element.Attribute("description")?.Value;

                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(description))
                {
                    descriptions[key] = description;
                }
            }

            Console.WriteLine($"[CrossReferenceService] Loaded {descriptions.Count} descriptions from {Path.GetFileName(targetFilePath)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CrossReferenceService] Error loading descriptions from {targetFilePath}: {ex.Message}");
        }

        return descriptions;
    }
}
