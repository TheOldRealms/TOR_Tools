using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TORTools.App.ViewModels;

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

        if (treeView.SelectedItem is FileNode fileNode)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.OpenFile(fileNode.FilePath);
            }
        }
    }
}
