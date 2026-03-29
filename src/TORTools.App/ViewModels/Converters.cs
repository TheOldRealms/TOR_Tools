using Avalonia.Data.Converters;
using Avalonia.Media;

namespace TORTools.App.ViewModels;

public static class Converters
{
    public static FuncValueConverter<bool, FontWeight> BoolToFontWeight { get; } =
        new(isRepo => isRepo ? FontWeight.SemiBold : FontWeight.Normal);
}
