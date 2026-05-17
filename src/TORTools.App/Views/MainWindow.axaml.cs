using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TORTools.App.ViewModels;
using TORTools.App.ViewModels.Settlement;
using TORTools.App.ViewModels.Translation;

namespace TORTools.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Subscribe to focus search event when DataContext is set
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.FocusSearchRequested += OnFocusSearchRequested;
        }
    }

    private void OnFocusSearchRequested(object? sender, EventArgs e)
    {
        var searchBox = this.FindControl<TextBox>("SearchBox");
        searchBox?.Focus();
        searchBox?.SelectAll();
    }

    private void TreeView_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not TreeView treeView)
            return;

        if (DataContext is not MainWindowViewModel vm)
            return;

        // Handle file node double-click (regular XML files)
        if (treeView.SelectedItem is FileNode fileNode)
        {
            vm.OpenFile(fileNode.FilePath);
            return;
        }

        // Handle settlement view node double-click
        if (treeView.SelectedItem is SettlementViewNode settlementViewNode)
        {
            settlementViewNode.Open();
            return;
        }

        // Handle translation file double-click
        if (treeView.SelectedItem is TranslationFileTreeItem fileItem && !fileItem.IsModule)
        {
            Console.WriteLine($"[Translation] Double-clicked: {fileItem.DisplayName}, RelativePath: {fileItem.RelativePath}");

            if (!string.IsNullOrEmpty(fileItem.RelativePath) && vm.TranslationsSidebar != null)
            {
                // Extract language code from path (e.g., "DE/TOR_Core/...")
                var parts = fileItem.RelativePath.Split('/');
                Console.WriteLine($"[Translation] Path parts: {string.Join(", ", parts)}");

                if (parts.Length > 0)
                {
                    var langCode = parts[0];
                    Console.WriteLine($"[Translation] Looking for language: {langCode}");
                    Console.WriteLine($"[Translation] Available languages: {string.Join(", ", vm.TranslationsSidebar.Languages.Select(l => l.Config.LanguageCode))}");

                    var languageItem = vm.TranslationsSidebar.Languages
                        .FirstOrDefault(l => l.Config.LanguageCode == langCode);

                    if (languageItem != null)
                    {
                        Console.WriteLine($"[Translation] Found language, opening file...");
                        languageItem.OpenFile(fileItem.RelativePath);
                    }
                    else
                    {
                        Console.WriteLine($"[Translation] Language not found!");
                    }
                }
            }
            else
            {
                Console.WriteLine($"[Translation] RelativePath empty or TranslationsSidebar null");
            }
        }
    }

    private async void AddLanguage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.TranslationsSidebar == null)
            return;

        // Show folder picker dialog
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Language Folder",
            AllowMultiple = false
        });

        if (folders.Count == 0)
            return;

        var folderPath = folders[0].Path.LocalPath;

        // Check if language_data.xml exists
        var languageDataPath = System.IO.Path.Combine(folderPath, "language_data.xml");
        if (System.IO.File.Exists(languageDataPath))
        {
            // Load existing language
            vm.AddExistingLanguageFolder(folderPath);
        }
        else
        {
            // Show dialog to create new language
            await ShowCreateLanguageDialog(vm, folderPath);
        }
    }

    private async Task ShowCreateLanguageDialog(MainWindowViewModel vm, string folderPath)
    {
        // Simple input dialog for language code
        var dialog = new Window
        {
            Title = "Create New Language",
            Width = 350,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var languageCodeBox = new TextBox
        {
            PlaceholderText = "Language Code (e.g., DE, FR, SP)",
            Margin = new Avalonia.Thickness(10)
        };

        var languageNameBox = new TextBox
        {
            PlaceholderText = "Language Name (e.g., Deutsch, Français)",
            Margin = new Avalonia.Thickness(10)
        };

        var createButton = new Button
        {
            Content = "Create",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(10)
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(10)
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(10)
        };
        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(createButton);

        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(10)
        };
        panel.Children.Add(new TextBlock { Text = "Create translation stubs for a new language:", Margin = new Avalonia.Thickness(10, 10, 10, 5) });
        panel.Children.Add(languageCodeBox);
        panel.Children.Add(languageNameBox);
        panel.Children.Add(buttonPanel);

        dialog.Content = panel;

        string? resultCode = null;
        string? resultName = null;

        createButton.Click += (s, e) =>
        {
            resultCode = languageCodeBox.Text?.Trim().ToUpperInvariant();
            resultName = languageNameBox.Text?.Trim();
            dialog.Close();
        };

        cancelButton.Click += (s, e) =>
        {
            dialog.Close();
        };

        await dialog.ShowDialog(this);

        if (!string.IsNullOrEmpty(resultCode) && !string.IsNullOrEmpty(resultName))
        {
            vm.CreateNewLanguageFolder(folderPath, resultCode, resultName);
        }
    }
}
