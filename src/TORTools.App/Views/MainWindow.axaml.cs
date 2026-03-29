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
