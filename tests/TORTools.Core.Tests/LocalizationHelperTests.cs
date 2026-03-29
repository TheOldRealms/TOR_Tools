using TORTools.Core.Services;

namespace TORTools.Core.Tests;

public class LocalizationHelperTests
{
    [Theory]
    [InlineData("{=str_test}Display Text", "str_test", "Display Text")]
    [InlineData("{=empirefaction}Empire of Men", "empirefaction", "Empire of Men")]
    [InlineData("{=str_tor_empire_head_helm_001}Empire Sallet", "str_tor_empire_head_helm_001", "Empire Sallet")]
    [InlineData("Plain text without localization", null, "Plain text without localization")]
    [InlineData("", null, "")]
    [InlineData(null, null, "")]
    public void Unwrap_ExtractsKeyAndText(string? input, string? expectedKey, string expectedText)
    {
        var (key, text) = LocalizationHelper.Unwrap(input);

        key.Should().Be(expectedKey);
        text.Should().Be(expectedText);
    }

    [Theory]
    [InlineData("str_test", "Display Text", "{=str_test}Display Text")]
    [InlineData("empirefaction", "Empire of Men", "{=empirefaction}Empire of Men")]
    [InlineData(null, "Plain text", "Plain text")]
    [InlineData("", "Plain text", "Plain text")]
    public void Wrap_CombinesKeyAndText(string? key, string text, string expected)
    {
        var result = LocalizationHelper.Wrap(key, text);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("my_item_id", "str_my_item_id")]
    [InlineData("tor_empire_sword_001", "str_tor_empire_sword_001")]
    public void GenerateKey_CreatesValidKey(string id, string expectedKey)
    {
        var result = LocalizationHelper.GenerateKey(id);

        result.Should().Be(expectedKey);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("-", true)]
    [InlineData("none", true)]
    [InlineData("None", true)]
    [InlineData("NONE", true)]
    [InlineData("actual_value", false)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    public void IsNullValue_IdentifiesNullValues(string? value, bool expectedIsNull)
    {
        var result = LocalizationHelper.IsNullValue(value);

        result.Should().Be(expectedIsNull);
    }

    [Fact]
    public void Unwrap_ThenWrap_PreservesOriginalValue()
    {
        var original = "{=str_test}Hello World";

        var (key, text) = LocalizationHelper.Unwrap(original);
        var rewrapped = LocalizationHelper.Wrap(key, text);

        rewrapped.Should().Be(original);
    }
}
