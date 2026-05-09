using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TORTools.App.Views.Dialogs;

/// <summary>
/// Base class for editor dialogs with common functionality.
/// </summary>
public abstract class BaseEditorDialog : Window
{
    protected TextBox? SearchBox;
    protected ListBox? ItemsListBox;
    protected StackPanel MainStack;
    protected string? Result;
    protected bool Completed;

    protected BaseEditorDialog(string title, int width = 500, int height = 500)
    {
        Title = title;
        Width = width;
        Height = height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;
        ShowInTaskbar = false;
        MinWidth = 400;
        MinHeight = 400;

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            Padding = new Thickness(16)
        };

        MainStack = new StackPanel { Spacing = 8 };

        // Note: BuildContent is NOT called here - derived classes must call Initialize() after their constructor

        border.Child = MainStack;
        Content = border;

        // Handle window closing
        Closing += (s, e) =>
        {
            if (!Completed)
            {
                Completed = true;
                OnCancel();
            }
        };
    }

    /// <summary>
    /// Call this after the derived class constructor to build content and add buttons.
    /// </summary>
    protected void Initialize()
    {
        BuildContent();
        AddButtons();
    }

    /// <summary>
    /// Override in derived classes to build the editor content.
    /// </summary>
    protected abstract void BuildContent();

    /// <summary>
    /// Override to get the result value when OK is clicked.
    /// </summary>
    protected abstract string? GetResultValue();

    /// <summary>
    /// Called when cancel is clicked or window is closed.
    /// </summary>
    protected virtual void OnCancel()
    {
        Result = null;
    }

    private void AddButtons()
    {
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var okButton = new Button
        {
            Content = "OK",
            Padding = new Thickness(24, 6),
            Background = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
            Foreground = Brushes.White
        };
        okButton.Click += (s, e) =>
        {
            if (!Completed)
            {
                Completed = true;
                Result = GetResultValue();
                Close();
            }
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(24, 6),
            Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
            Foreground = Brushes.White
        };
        cancelButton.Click += (s, e) =>
        {
            if (!Completed)
            {
                Completed = true;
                OnCancel();
                Close();
            }
        };

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        MainStack.Children.Add(buttonPanel);
    }

    /// <summary>
    /// Shows the dialog and returns the result.
    /// </summary>
    public new string? ShowDialog(Window owner)
    {
        base.ShowDialog(owner);
        return Result;
    }

    /// <summary>
    /// Helper to create a search box with filtering.
    /// </summary>
    protected TextBox CreateSearchBox(string watermark = "Type to filter...")
    {
        return new TextBox { PlaceholderText = watermark };
    }

    /// <summary>
    /// Helper to create a styled label.
    /// </summary>
    protected TextBlock CreateLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
    }

    /// <summary>
    /// Helper to create a section label with top margin.
    /// </summary>
    protected TextBlock CreateSectionLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 12, 0, 4)
        };
    }
}
