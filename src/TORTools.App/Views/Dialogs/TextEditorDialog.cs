using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;

namespace TORTools.App.Views.Dialogs;

/// <summary>
/// Dialog for editing multi-line text fields.
/// </summary>
public class TextEditorDialog : BaseEditorDialog
{
    private readonly TextBox _textBox;
    private readonly string _currentValue;

    public TextEditorDialog(string fieldName, string currentValue)
        : base($"Edit {fieldName}", 700, 500)
    {
        _currentValue = currentValue;
        _textBox = new TextBox
        {
            Text = currentValue,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 13,
            Padding = new Avalonia.Thickness(8),
            BorderThickness = new Avalonia.Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60))
        };

        Initialize();
    }

    protected override void BuildContent()
    {
        var scrollViewer = new ScrollViewer
        {
            Content = _textBox,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Height = 400
        };

        MainStack.Children.Add(scrollViewer);
    }

    protected override string? GetResultValue()
    {
        var text = _textBox.Text?.Trim() ?? "";
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
