using Avalonia.Controls;
using Avalonia.Interactivity;
using TORTools.App.ViewModels.Translation;
using TORTools.App.Views.Dialogs;

namespace TORTools.App.Views;

public partial class TranslationSheetView : UserControl
{
    public TranslationSheetView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Opens a readonly popup to view the full English text.
    /// </summary>
    private async void ViewEnglishText_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        if (button.DataContext is not TranslationEntryRowViewModel rowVm)
            return;

        var dialog = new TextEditorDialog("English Text", rowVm.EnglishText, isReadOnly: true);
        var parentWindow = TopLevel.GetTopLevel(this) as Window;

        if (parentWindow != null)
        {
            // Use the base Window.ShowDialog which is async
            await dialog.ShowDialog<object?>(parentWindow);
        }
    }

    /// <summary>
    /// Opens an editable popup to edit the translation.
    /// </summary>
    private async void EditTranslation_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        if (button.DataContext is not TranslationEntryRowViewModel rowVm)
            return;

        var dialog = new TextEditorDialog("Translation", rowVm.TranslatedText, isReadOnly: false);
        var parentWindow = TopLevel.GetTopLevel(this) as Window;

        if (parentWindow != null)
        {
            // ShowDialog<T> returns what was passed to Close(value)
            // TextEditorDialog calls Close(Result) where Result is the text
            var result = await dialog.ShowDialog<object?>(parentWindow);

            if (result is string text)
            {
                rowVm.TranslatedText = text;
            }
        }
    }
}
