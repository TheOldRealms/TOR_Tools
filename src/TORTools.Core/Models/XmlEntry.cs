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
}
