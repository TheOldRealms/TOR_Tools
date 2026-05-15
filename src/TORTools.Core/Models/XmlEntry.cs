using System.Collections.ObjectModel;
using System.Xml;
using System.Xml.Linq;

namespace TORTools.Core.Models;

/// <summary>
/// Represents a single XML entry (element) with its attributes and children.
/// Wraps the underlying XElement to preserve formatting on save.
/// </summary>
public class XmlEntry
{
    /// <summary>
    /// Reference to the original XElement for formatting preservation.
    /// </summary>
    public XElement OriginalElement { get; }

    /// <summary>
    /// The element name (e.g., "Item", "Culture", "Hero").
    /// </summary>
    public string ElementName => OriginalElement.Name.LocalName;

    /// <summary>
    /// Ordered collection of attributes as they appear in the XML.
    /// </summary>
    public ObservableCollection<XmlAttributeValue> Attributes { get; } = new();

    /// <summary>
    /// Child entries for nested XML elements.
    /// </summary>
    public ObservableCollection<XmlEntry> Children { get; } = new();

    /// <summary>
    /// Quick access to the 'id' attribute value.
    /// </summary>
    public string? Id => GetAttributeValue("id");

    /// <summary>
    /// Quick access to the 'name' attribute value (display value, unwrapped).
    /// </summary>
    public string? Name => GetAttribute("name")?.DisplayValue;

    /// <summary>
    /// Indicates whether this entry has unsaved modifications.
    /// </summary>
    public bool IsModified { get; set; }

    public XmlEntry(XElement element)
    {
        OriginalElement = element ?? throw new ArgumentNullException(nameof(element));
        LoadAttributes();
        LoadChildren();
    }

    private void LoadAttributes()
    {
        foreach (var attr in OriginalElement.Attributes())
        {
            Attributes.Add(new XmlAttributeValue(attr.Name.LocalName, attr.Value));
        }
    }

    private void LoadChildren()
    {
        foreach (var child in OriginalElement.Elements())
        {
            Children.Add(new XmlEntry(child));
        }
    }

    /// <summary>
    /// Refreshes the Children collection from the underlying XElement.
    /// Call this after programmatically adding/removing child elements.
    /// </summary>
    public void RefreshChildren()
    {
        Children.Clear();
        LoadChildren();
    }

    /// <summary>
    /// Gets an attribute by name.
    /// </summary>
    public XmlAttributeValue? GetAttribute(string name)
    {
        return Attributes.FirstOrDefault(a => a.Name == name);
    }

    /// <summary>
    /// Gets the raw value of an attribute by name.
    /// </summary>
    public string? GetAttributeValue(string name)
    {
        return GetAttribute(name)?.RawValue;
    }

    /// <summary>
    /// Sets an attribute value. Creates the attribute if it doesn't exist.
    /// Empty/null/whitespace values are treated as "remove attribute".
    /// </summary>
    public void SetAttributeValue(string name, string? value)
    {
        // Treat empty, whitespace, "-", "none" as null (remove attribute)
        var normalizedValue = value;
        if (string.IsNullOrWhiteSpace(value) || value == "-" || value?.Equals("none", StringComparison.OrdinalIgnoreCase) == true)
        {
            normalizedValue = null;
        }

        var attr = GetAttribute(name);
        if (normalizedValue == null)
        {
            // Remove attribute if it exists
            if (attr != null)
            {
                Attributes.Remove(attr);
            }
            // Remove from XElement
            OriginalElement.SetAttributeValue(name, null);
        }
        else
        {
            // Set or create attribute
            if (attr != null)
            {
                attr.RawValue = normalizedValue;
                attr.IsModified = true;
            }
            else
            {
                Attributes.Add(new XmlAttributeValue(name, normalizedValue) { IsModified = true });
            }
            // Update the underlying XElement
            OriginalElement.SetAttributeValue(name, normalizedValue);
        }
        IsModified = true;
    }

    /// <summary>
    /// Gets a value from a nested path.
    /// Path formats:
    /// - "ChildElement" - returns text content of child element
    /// - "ChildElement/@AttributeName" - returns attribute value from child element
    /// - "Parent/Child/@Attr" - multi-level path
    /// - "Parent/Child[1]/@Attr" - indexed child (1-based)
    /// - "Parent/Child[@attr='value']/@Attr" - attribute filter
    /// </summary>
    public string? GetNestedValue(string nestedPath)
    {
        if (string.IsNullOrEmpty(nestedPath)) return null;

        // Split off the final attribute reference if present
        var parts = nestedPath.Split(new[] { "/@" }, StringSplitOptions.None);
        var elementPath = parts[0];
        var attributeName = parts.Length > 1 ? parts[1] : null;

        // Navigate through the element path
        var currentElement = NavigateToElement(OriginalElement, elementPath);
        if (currentElement == null) return null;

        if (attributeName != null)
        {
            return currentElement.Attribute(attributeName)?.Value;
        }
        else
        {
            return currentElement.Value;
        }
    }

    /// <summary>
    /// Navigates to an element using a path that supports multi-level, indexing, and attribute filters.
    /// </summary>
    private static XElement? NavigateToElement(XElement root, string path)
    {
        var current = root;
        var segments = path.Split('/');

        foreach (var segment in segments)
        {
            if (current == null) return null;
            if (string.IsNullOrEmpty(segment)) continue;

            // Check for index: Element[1] or Element[2]
            if (segment.Contains('[') && segment.EndsWith(']'))
            {
                var bracketStart = segment.IndexOf('[');
                var elementName = segment.Substring(0, bracketStart);
                var bracketContent = segment.Substring(bracketStart + 1, segment.Length - bracketStart - 2);

                if (bracketContent.StartsWith("@"))
                {
                    // Attribute filter: Element[@attr='value']
                    var filterParts = bracketContent.Substring(1).Split('=');
                    if (filterParts.Length == 2)
                    {
                        var filterAttr = filterParts[0];
                        var filterValue = filterParts[1].Trim('\'', '"');
                        current = current.Elements(elementName)
                            .FirstOrDefault(e => e.Attribute(filterAttr)?.Value == filterValue);
                    }
                    else
                    {
                        current = null;
                    }
                }
                else if (int.TryParse(bracketContent, out var index))
                {
                    // Numeric index (1-based)
                    current = current.Elements(elementName).ElementAtOrDefault(index - 1);
                }
                else
                {
                    current = null;
                }
            }
            else
            {
                // Simple element name
                current = current.Element(segment);
            }
        }

        return current;
    }

    /// <summary>
    /// Sets a value at a nested path.
    /// Path formats:
    /// - "ChildElement" - sets text content of child element (creates if needed)
    /// - "ChildElement/@AttributeName" - sets attribute value on child element (creates element if needed)
    /// - "Parent/Child/@Attr" - multi-level path (creates parents as needed)
    /// - "Parent/Child[1]/@Attr" - indexed child (must exist)
    /// - "Parent/Child[@attr='value']/@Attr" - attribute filter (must exist)
    /// </summary>
    public void SetNestedValue(string nestedPath, string? value)
    {
        if (string.IsNullOrEmpty(nestedPath)) return;

        var parts = nestedPath.Split(new[] { "/@" }, StringSplitOptions.None);
        var elementPath = parts[0];
        var attributeName = parts.Length > 1 ? parts[1] : null;

        // Navigate to or create the target element
        var targetElement = NavigateOrCreateElement(OriginalElement, elementPath);
        if (targetElement == null) return; // Can't create indexed/filtered elements

        if (string.IsNullOrEmpty(value))
        {
            if (attributeName != null)
            {
                // Remove just the attribute
                targetElement.SetAttributeValue(attributeName, null);
            }
            else
            {
                // Remove the element
                targetElement.Remove();
            }
        }
        else
        {
            if (attributeName != null)
            {
                targetElement.SetAttributeValue(attributeName, value);
            }
            else
            {
                targetElement.Value = value;
            }
        }

        IsModified = true;
    }

    /// <summary>
    /// Gets a list of tag values from nested tag elements.
    /// Returns tags as comma-separated string (e.g., "IsOrcTag, EmpireTag").
    /// Prefers nested elements, falls back to attribute only if no nested elements exist.
    /// </summary>
    /// <param name="containerElement">Container element name (e.g., "tags")</param>
    /// <param name="itemElement">Item element name (e.g., "tag")</param>
    /// <param name="nameAttribute">Attribute containing tag name (e.g., "tag_name")</param>
    /// <param name="weightAttribute">Optional attribute for weight (e.g., "weight")</param>
    public string? GetTagList(string containerElement, string itemElement, string nameAttribute, string? weightAttribute = null)
    {
        // Check for nested element format first (preferred)
        var container = OriginalElement.Element(containerElement);
        if (container != null)
        {
            var tags = new List<string>();
            foreach (var tagElement in container.Elements(itemElement))
            {
                var tagName = tagElement.Attribute(nameAttribute)?.Value;
                if (string.IsNullOrEmpty(tagName)) continue;

                if (!string.IsNullOrEmpty(weightAttribute))
                {
                    var weight = tagElement.Attribute(weightAttribute)?.Value;
                    if (!string.IsNullOrEmpty(weight) && weight != "0")
                    {
                        tagName += $"({weight})";
                    }
                }
                tags.Add(tagName);
            }

            if (tags.Count > 0)
                return string.Join(", ", tags);
        }

        // Fall back to attribute format (for backwards compatibility)
        var attrValue = OriginalElement.Attribute(containerElement)?.Value;
        if (!string.IsNullOrWhiteSpace(attrValue))
            return attrValue;

        return null;
    }

    /// <summary>
    /// Sets tag values from a comma-separated string.
    /// Creates or updates the nested tag structure.
    /// </summary>
    /// <param name="value">Comma-separated tags (e.g., "IsOrcTag, EmpireTag") or tags with weights (e.g., "HonorTag(1)")</param>
    /// <param name="containerElement">Container element name (e.g., "tags")</param>
    /// <param name="itemElement">Item element name (e.g., "tag")</param>
    /// <param name="nameAttribute">Attribute containing tag name (e.g., "tag_name")</param>
    /// <param name="weightAttribute">Optional attribute for weight (e.g., "weight")</param>
    public void SetTagList(string? value, string containerElement, string itemElement, string nameAttribute, string? weightAttribute = null)
    {
        // Remove any existing container element
        var existingContainer = OriginalElement.Element(containerElement);
        existingContainer?.Remove();

        // Also remove any duplicate attribute with the same name
        OriginalElement.SetAttributeValue(containerElement, null);
        var existingAttr = Attributes.FirstOrDefault(a => a.Name == containerElement);
        if (existingAttr != null)
        {
            Attributes.Remove(existingAttr);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            IsModified = true;
            return;
        }

        // Parse tags
        var tagValues = value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();

        if (tagValues.Count == 0)
        {
            IsModified = true;
            return;
        }

        // Create container element
        var newContainer = new XElement(containerElement);

        foreach (var tagValue in tagValues)
        {
            var tagName = tagValue;
            string? weight = null;

            // Parse weight if present: TagName(1) -> TagName, weight=1
            var parenIndex = tagValue.IndexOf('(');
            if (parenIndex > 0 && tagValue.EndsWith(')'))
            {
                tagName = tagValue.Substring(0, parenIndex);
                weight = tagValue.Substring(parenIndex + 1, tagValue.Length - parenIndex - 2);
            }

            var tagElement = new XElement(itemElement);
            tagElement.SetAttributeValue(nameAttribute, tagName);

            if (!string.IsNullOrEmpty(weightAttribute) && !string.IsNullOrEmpty(weight))
            {
                tagElement.SetAttributeValue(weightAttribute, weight);
            }

            newContainer.Add(tagElement);
        }

        OriginalElement.Add(newContainer);
        IsModified = true;
    }

    /// <summary>
    /// Navigates to or creates elements along a path.
    /// Only simple paths and multi-level paths support creation.
    /// Indexed and filtered paths require elements to exist.
    /// </summary>
    private static XElement? NavigateOrCreateElement(XElement root, string path)
    {
        var current = root;
        var segments = path.Split('/');

        foreach (var segment in segments)
        {
            if (current == null) return null;
            if (string.IsNullOrEmpty(segment)) continue;

            if (segment.Contains('[') && segment.EndsWith(']'))
            {
                var bracketStart = segment.IndexOf('[');
                var elementName = segment.Substring(0, bracketStart);
                var bracketContent = segment.Substring(bracketStart + 1, segment.Length - bracketStart - 2);

                if (bracketContent.StartsWith("@"))
                {
                    // Attribute filter - must exist, can't create
                    var filterParts = bracketContent.Substring(1).Split('=');
                    if (filterParts.Length == 2)
                    {
                        var filterAttr = filterParts[0];
                        var filterValue = filterParts[1].Trim('\'', '"');
                        current = current.Elements(elementName)
                            .FirstOrDefault(e => e.Attribute(filterAttr)?.Value == filterValue);
                    }
                    else
                    {
                        return null;
                    }
                }
                else if (int.TryParse(bracketContent, out var index))
                {
                    // Numeric index - must exist, can't create
                    current = current.Elements(elementName).ElementAtOrDefault(index - 1);
                }
                else
                {
                    return null;
                }
            }
            else
            {
                // Simple element - create if needed
                var child = current.Element(segment);
                if (child == null)
                {
                    child = new XElement(segment);
                    current.Add(child);
                }
                current = child;
            }
        }

        return current;
    }
}
