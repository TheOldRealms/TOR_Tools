using System.Collections.ObjectModel;
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
    /// </summary>
    public void SetAttributeValue(string name, string? value)
    {
        var attr = GetAttribute(name);
        if (attr != null)
        {
            attr.RawValue = value ?? "";
            attr.IsModified = true;
        }
        else if (value != null)
        {
            Attributes.Add(new XmlAttributeValue(name, value) { IsModified = true });
        }
        IsModified = true;

        // Update the underlying XElement
        OriginalElement.SetAttributeValue(name, value);
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
