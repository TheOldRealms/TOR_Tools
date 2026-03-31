using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using TORTools.Core.Validation;

namespace TORTools.App.Converters;

/// <summary>
/// Converts ValidationSeverity to a background color.
/// </summary>
public class SeverityToColorConverter : IValueConverter
{
    public static readonly SeverityToColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ValidationSeverity severity)
        {
            return severity switch
            {
                ValidationSeverity.Error => new SolidColorBrush(Color.FromRgb(237, 66, 69)),    // #ed4245 - Red
                ValidationSeverity.Warning => new SolidColorBrush(Color.FromRgb(250, 166, 26)), // #faa61a - Yellow/Orange
                ValidationSeverity.Info => new SolidColorBrush(Color.FromRgb(88, 101, 242)),   // #5865f2 - Blue
                _ => new SolidColorBrush(Color.FromRgb(237, 66, 69))
            };
        }

        return new SolidColorBrush(Color.FromRgb(237, 66, 69));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
