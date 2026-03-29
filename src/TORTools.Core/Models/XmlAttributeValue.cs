using TORTools.Core.Services;

namespace TORTools.Core.Models;

/// <summary>
/// Represents a single XML attribute with its raw and display values.
/// </summary>
public class XmlAttributeValue
{
    /// <summary>
    /// The attribute name as it appears in XML.
    /// </summary>
    public string Name { get; }

    private string _rawValue;

    /// <summary>
    /// The raw value as stored in XML.
    /// </summary>
    public string RawValue
    {
        get => _rawValue;
        set
        {
            _rawValue = value;
            UpdateDisplayValue();
        }
    }

    /// <summary>
    /// The display value for editing (with localization unwrapped).
    /// </summary>
    public string DisplayValue { get; private set; }

    /// <summary>
    /// The localization key if the value contains one (e.g., "str_xxx").
    /// </summary>
    public string? LocalizationKey { get; private set; }

    /// <summary>
    /// Indicates whether this attribute has been modified.
    /// </summary>
    public bool IsModified { get; set; }

    public XmlAttributeValue(string name, string rawValue)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _rawValue = rawValue ?? "";
        DisplayValue = "";
        UpdateDisplayValue();
    }

    private void UpdateDisplayValue()
    {
        var (key, text) = LocalizationHelper.Unwrap(_rawValue);
        LocalizationKey = key;
        DisplayValue = text;
    }

    /// <summary>
    /// Sets the display value and re-wraps with localization key if present.
    /// </summary>
    public void SetDisplayValue(string displayValue)
    {
        DisplayValue = displayValue;
        RawValue = LocalizationHelper.Wrap(LocalizationKey, displayValue);
        IsModified = true;
    }

    /// <summary>
    /// Returns true if this value represents null/empty in TOR XML conventions.
    /// Values like "-", "none", empty string, and whitespace are all considered null.
    /// </summary>
    public bool IsNullOrEmpty => LocalizationHelper.IsNullValue(RawValue);

    public override string ToString() => DisplayValue;
}
