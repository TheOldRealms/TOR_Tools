using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using TORTools.App.ViewModels;
using TORTools.Core.Schema;

namespace TORTools.App.Views;

public partial class FileTabView : UserControl
{
    private bool _columnsGenerated;

    public FileTabView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        TryGenerateColumns();
        SetupScrollToSelection();
        SetupKeyboardShortcuts();
    }

    private void SetupScrollToSelection()
    {
        var grid = this.FindControl<DataGrid>("EntryGrid");
        if (grid == null) return;

        // Subscribe to selection changes to scroll into view
        grid.SelectionChanged += (s, e) =>
        {
            if (grid.SelectedItem != null)
            {
                grid.ScrollIntoView(grid.SelectedItem, null);
            }
        };
    }

    private void SetupKeyboardShortcuts()
    {
        var grid = this.FindControl<DataGrid>("EntryGrid");
        if (grid == null) return;

        // Use tunneling (Preview) to catch keys before DataGrid handles them
        grid.AddHandler(KeyDownEvent, OnDataGridKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void OnDataGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not FileTabViewModel vm) return;

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (!ctrl) return;

        // Check if we're editing a cell - if so, let normal copy/paste work
        var focusedElement = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (focusedElement is TextBox)
        {
            return;
        }

        if (e.Key == Key.C)
        {
            // Ctrl+C: Copy selected row (highlights it with blue border)
            Console.WriteLine("[KeyDown] Copying row...");
            vm.CopyRow();
            e.Handled = true;
        }
        else if (e.Key == Key.V && vm.HasCopiedRow)
        {
            // Ctrl+V: Paste row data
            Console.WriteLine("[KeyDown] Pasting row...");
            vm.PasteRow();
            e.Handled = true;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _columnsGenerated = false;
        TryGenerateColumns();
    }

    private void TryGenerateColumns()
    {
        if (_columnsGenerated) return;
        if (DataContext is not FileTabViewModel vm) return;
        if (vm.ColumnNames.Count == 0) return;

        var grid = this.FindControl<DataGrid>("EntryGrid");
        if (grid == null) return;

        GenerateColumns(vm, grid);
        _columnsGenerated = true;
    }

    private void GenerateColumns(FileTabViewModel vm, DataGrid grid)
    {
        Console.WriteLine($"[FileTabView] GenerateColumns called with {vm.ColumnNames.Count} columns, {vm.Rows.Count} rows");

        grid.Columns.Clear();

        // Add row number column
        var rowNumColumn = new DataGridTemplateColumn
        {
            Header = "#",
            Width = new DataGridLength(40),
            IsReadOnly = true,
            CellTemplate = CreateRowNumberTemplate()
        };
        grid.Columns.Add(rowNumColumn);

        // Get ordered column display info
        var orderedColumns = ColumnDisplayMappings.GetOrderedDisplayInfo(vm.ColumnNames, vm.Title).ToList();
        Console.WriteLine($"[FileTabView] Ordered columns count: {orderedColumns.Count}");

        foreach (var displayInfo in orderedColumns)
        {
            Console.WriteLine($"[FileTabView] Adding column: {displayInfo.DisplayName} ({displayInfo.AttributeName})");

            // Check if this is the ID column - if so, add lock toggle to header
            var isIdColumn = displayInfo.AttributeName.Equals("id", StringComparison.OrdinalIgnoreCase);
            var column = new DataGridTextColumn
            {
                Header = isIdColumn ? CreateIdColumnHeader(displayInfo, vm) : CreateColumnHeader(displayInfo),
                Binding = new Binding($"[{displayInfo.AttributeName}]", BindingMode.TwoWay),
                Width = new DataGridLength(displayInfo.Width),
                IsReadOnly = displayInfo.IsReadOnly
            };

            grid.Columns.Add(column);
        }

        Console.WriteLine($"[FileTabView] Final column count: {grid.Columns.Count}");
    }

    /// <summary>
    /// Called when a DataGrid row is being loaded. Applies styling based on row state.
    /// </summary>
    private void OnDataGridLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.DataContext is EntryRowViewModel rowVm)
        {
            UpdateRowStyle(e.Row, rowVm);

            // Subscribe to property changes to update styling dynamically
            rowVm.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(EntryRowViewModel.IsNew) ||
                    args.PropertyName == nameof(EntryRowViewModel.IsSelectedForCopy))
                {
                    UpdateRowStyle(e.Row, rowVm);
                }
            };
        }
    }

    private static void UpdateRowStyle(DataGridRow row, EntryRowViewModel rowVm)
    {
        // New entry styling
        if (rowVm.IsNew)
        {
            if (!row.Classes.Contains("newEntry"))
                row.Classes.Add("newEntry");
        }
        else
        {
            row.Classes.Remove("newEntry");
        }

        // Selected for copy styling
        if (rowVm.IsSelectedForCopy)
        {
            if (!row.Classes.Contains("selectedForCopy"))
                row.Classes.Add("selectedForCopy");
        }
        else
        {
            row.Classes.Remove("selectedForCopy");
        }
    }

    /// <summary>
    /// Creates the cell template for the row number column.
    /// </summary>
    private IDataTemplate CreateRowNumberTemplate()
    {
        return new FuncDataTemplate<EntryRowViewModel>((rowVm, _) =>
        {
            var text = new TextBlock
            {
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };

            if (rowVm != null)
            {
                text.Bind(TextBlock.TextProperty, new Binding(nameof(EntryRowViewModel.RowNumber)));
            }

            return text;
        });
    }

    /// <summary>
    /// Creates the ID column header with a lock toggle button.
    /// </summary>
    private object CreateIdColumnHeader(ColumnDisplayInfo info, FileTabViewModel vm)
    {
        var panel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 6
        };

        // Main display name
        var displayText = new TextBlock
        {
            Text = info.DisplayName,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        panel.Children.Add(displayText);

        // Lock toggle button
        var lockButton = new Button
        {
            Content = vm.IsIdColumnLocked ? "🔒" : "🔓",
            FontSize = 12,
            Padding = new Thickness(4, 2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        ToolTip.SetTip(lockButton, vm.IsIdColumnLocked
            ? "IDs are locked. Click to unlock for editing."
            : "IDs are unlocked. Click to lock.");

        lockButton.Click += (s, e) =>
        {
            vm.IsIdColumnLocked = !vm.IsIdColumnLocked;
            lockButton.Content = vm.IsIdColumnLocked ? "🔒" : "🔓";
            ToolTip.SetTip(lockButton, vm.IsIdColumnLocked
                ? "IDs are locked. Click to unlock for editing."
                : "IDs are unlocked. Click to lock.");
            Console.WriteLine($"[IdColumnHeader] ID lock toggled: {vm.IsIdColumnLocked}");
        };

        panel.Children.Add(lockButton);

        // Tooltip for the header
        ToolTip.SetTip(panel, "XML Attribute: id\nClick lock icon to enable/disable ID editing.");

        return panel;
    }

    /// <summary>
    /// Creates a column header with display name and tooltip showing the attribute name.
    /// </summary>
    private static object CreateColumnHeader(ColumnDisplayInfo info)
    {
        var panel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Vertical,
            Spacing = 0
        };

        // Main display name
        var displayText = new TextBlock
        {
            Text = info.DisplayName,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12
        };

        panel.Children.Add(displayText);

        // Show underlying attribute name if different from display name
        var normalizedDisplay = info.DisplayName.Replace(" ", "").ToLowerInvariant();
        var normalizedAttr = info.AttributeName.Replace("_", "").ToLowerInvariant();

        if (normalizedDisplay != normalizedAttr)
        {
            var attrText = new TextBlock
            {
                Text = info.AttributeName,
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                Margin = new Thickness(0, -2, 0, 0)
            };
            panel.Children.Add(attrText);
        }

        // Add tooltip with description if available
        if (!string.IsNullOrEmpty(info.Description))
        {
            ToolTip.SetTip(panel, info.Description);
        }
        else
        {
            ToolTip.SetTip(panel, $"XML Attribute: {info.AttributeName}");
        }

        return panel;
    }
}
