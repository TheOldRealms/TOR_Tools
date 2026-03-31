using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using TORTools.App.Helpers;
using TORTools.App.ViewModels;
using TORTools.Core.Schema;
using TORTools.Core.Services;
using TORTools.Core.Validation;

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
        Console.WriteLine($"[FileTabView] Schema loaded: {vm.Schema != null}");

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

        // Get ordered column display info, using schema Order if available
        var orderedColumns = vm.ColumnNames
            .Select(attr => {
                var displayInfo = ColumnDisplayMappings.GetDisplayInfo(attr, vm.Title);
                var fieldDef = vm.GetFieldDefinition(attr);
                // Use schema order if defined, otherwise fall back to display mappings
                var order = fieldDef?.Order ?? displayInfo.Order;
                return new { Info = displayInfo, Order = order };
            })
            .OrderBy(x => x.Order)
            .Select(x => x.Info)
            .ToList();
        Console.WriteLine($"[FileTabView] Ordered columns count: {orderedColumns.Count}");

        foreach (var displayInfo in orderedColumns)
        {
            // Check if schema has enum values for this field
            var fieldDef = vm.GetFieldDefinition(displayInfo.AttributeName);

            // Skip hidden fields
            if (fieldDef?.Hidden == true)
            {
                Console.WriteLine($"[FileTabView] Skipping hidden column: {displayInfo.DisplayName} ({displayInfo.AttributeName})");
                continue;
            }

            // CrossReference fields with enumValues should display as enum dropdowns (like RaceLock)
            // CrossReference fields without enumValues display as tag editors (like ItemTraits)
            var isCrossRefWithEnum = fieldDef?.Type == "crossReference" && fieldDef?.CrossReference != null && fieldDef.EnumValues?.Count > 0;
            var isEnumField = (fieldDef?.Type == "enum" && fieldDef.EnumValues?.Count > 0) || isCrossRefWithEnum;
            var isCrossRefField = (fieldDef?.Type == "crossReference" || fieldDef?.Type == "reverseCrossReference") && fieldDef?.CrossReference != null && !isCrossRefWithEnum;
            var isIconField = fieldDef?.Type == "icon";

            Console.WriteLine($"[FileTabView] Adding column: {displayInfo.DisplayName} ({displayInfo.AttributeName}) - Enum: {isEnumField}, CrossRef: {isCrossRefField}, Icon: {isIconField}");

            // Check if this is the ID column - if so, add lock toggle to header
            var isIdColumn = displayInfo.AttributeName.Equals("id", StringComparison.OrdinalIgnoreCase);

            DataGridColumn column;
            if (isIconField)
            {
                // Icon picker field
                column = new DataGridTemplateColumn
                {
                    Header = CreateColumnHeader(displayInfo, fieldDef),
                    Width = new DataGridLength(displayInfo.Width),
                    IsReadOnly = false,
                    CellTemplate = CreateIconCellTemplate(displayInfo.AttributeName, fieldDef!, vm)
                };
            }
            else if (isCrossRefField)
            {
                var isReverseCrossRef = fieldDef!.Type == "reverseCrossReference";

                if (isReverseCrossRef)
                {
                    // Read-only: clickable links with single-click navigation
                    column = new DataGridTemplateColumn
                    {
                        Header = CreateColumnHeader(displayInfo, fieldDef),
                        Width = new DataGridLength(displayInfo.Width),
                        IsReadOnly = true,
                        CellTemplate = CreateReadOnlyCrossRefTemplate(displayInfo.AttributeName, fieldDef, vm)
                    };
                }
                else
                {
                    // Editable: tag editor with autocomplete
                    column = new DataGridTemplateColumn
                    {
                        Header = CreateColumnHeader(displayInfo, fieldDef),
                        Width = new DataGridLength(displayInfo.Width),
                        IsReadOnly = false,
                        CellTemplate = CreateEditableCrossRefTemplate(displayInfo.AttributeName, fieldDef, vm)
                    };
                }
            }
            else if (isEnumField)
            {
                // Create ComboBox column for enum fields
                column = new DataGridTemplateColumn
                {
                    Header = CreateColumnHeader(displayInfo, fieldDef),
                    Width = new DataGridLength(displayInfo.Width),
                    IsReadOnly = displayInfo.IsReadOnly,
                    CellTemplate = CreateEnumCellTemplate(displayInfo.AttributeName, fieldDef!, vm),
                    CellEditingTemplate = CreateEnumEditingTemplate(displayInfo.AttributeName, fieldDef!)
                };
            }
            else
            {
                // Create text column - all text cells now use templates for consistent styling
                column = new DataGridTemplateColumn
                {
                    Header = isIdColumn ? CreateIdColumnHeader(displayInfo, vm) : CreateColumnHeader(displayInfo, fieldDef),
                    Width = new DataGridLength(displayInfo.Width),
                    IsReadOnly = displayInfo.IsReadOnly,
                    CellTemplate = CreateTextCellTemplate(displayInfo.AttributeName, fieldDef, vm),
                    CellEditingTemplate = CreateTextEditingTemplate(displayInfo.AttributeName)
                };
            }

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
    private static object CreateColumnHeader(ColumnDisplayInfo info, FieldDefinition? fieldDef = null)
    {
        var panel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Vertical,
            Spacing = 0
        };

        // Main display name (prefer schema displayName if available)
        var displayName = fieldDef?.DisplayName ?? info.DisplayName;
        var displayText = new TextBlock
        {
            Text = displayName,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12
        };

        panel.Children.Add(displayText);

        // Show underlying attribute name if different from display name
        var normalizedDisplay = displayName.Replace(" ", "").ToLowerInvariant();
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

        // Add tooltip with description (prefer schema description)
        var description = fieldDef?.Description ?? info.Description;
        if (!string.IsNullOrEmpty(description))
        {
            ToolTip.SetTip(panel, description);
        }
        else
        {
            ToolTip.SetTip(panel, $"XML Attribute: {info.AttributeName}");
        }

        return panel;
    }

    /// <summary>
    /// Creates a read-only cell template for reverse cross-reference fields.
    /// Uses single-click navigation (no Ctrl required).
    /// </summary>
    private static IDataTemplate CreateReadOnlyCrossRefTemplate(string attributeName, FieldDefinition fieldDef, FileTabViewModel vm)
    {
        return new FuncDataTemplate<EntryRowViewModel>((rowVm, _) =>
        {
            var wrapPanel = new WrapPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal
            };

            if (rowVm != null)
            {
                var value = rowVm[attributeName];
                if (!string.IsNullOrEmpty(value))
                {
                    // Split by comma to get individual IDs
                    var ids = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    for (int i = 0; i < ids.Length; i++)
                    {
                        var id = ids[i].Trim();
                        if (string.IsNullOrEmpty(id)) continue;

                        // Create a clickable link button for each ID
                        var capturedId = id;
                        var capturedFieldName = attributeName;
                        var linkButton = new Button
                        {
                            Content = id,
                            Tag = id,
                            Padding = new Thickness(4, 2),
                            Margin = new Thickness(0, 0, 4, 0),
                            Background = Brushes.Transparent,
                            BorderThickness = new Thickness(0),
                            Foreground = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
                            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                            FontSize = 12,
                            MinHeight = 0,
                            MinWidth = 0
                        };

                        ToolTip.SetTip(linkButton, $"Click to navigate to: {id}");

                        // Single-click navigation (no Ctrl required)
                        linkButton.Click += (s, e) =>
                        {
                            Console.WriteLine($"[CrossRef] Click - Navigating to: {capturedId}");
                            vm.NavigateToReferenceForField(capturedFieldName, capturedId);
                        };

                        wrapPanel.Children.Add(linkButton);
                    }
                }
            }

            return wrapPanel;
        });
    }

    /// <summary>
    /// Creates an editable cell template for cross-reference fields.
    /// Shows clickable links for navigation, plus an edit button for modifications.
    /// </summary>
    private static IDataTemplate CreateEditableCrossRefTemplate(string attributeName, FieldDefinition fieldDef, FileTabViewModel vm)
    {
        return new FuncDataTemplate<EntryRowViewModel>((rowVm, _) =>
        {
            // Outer border for modification highlighting
            var border = new Border();
            border.Classes.Add("dataCell");

            var panel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 4
            };

            if (rowVm == null)
            {
                border.Child = panel;
                return border;
            }

            // Wrap panel for the clickable links
            var linksPanel = new WrapPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal
            };

            // Get available IDs for validation
            var availableIdsSet = new HashSet<string>(vm.GetAvailableIds(attributeName), StringComparer.OrdinalIgnoreCase);

            // Helper to rebuild links and register validation errors
            void RebuildLinks()
            {
                linksPanel.Children.Clear();
                bool hasInvalidTrait = false;
                var invalidTraits = new List<string>();

                // Get row index for validation (RowNumber is 1-indexed)
                var rowIndex = rowVm.RowNumber - 1;
                var entryId = rowVm["id"];

                // Unregister any existing errors for this row/field first
                vm.ValidationManager.UnregisterErrors(rowIndex, attributeName);

                var value = rowVm[attributeName];
                if (!string.IsNullOrEmpty(value))
                {
                    var ids = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var id in ids)
                    {
                        var trimmedId = id.Trim();
                        if (string.IsNullOrEmpty(trimmedId)) continue;

                        var capturedId = trimmedId;
                        var isValid = availableIdsSet.Contains(trimmedId);
                        if (!isValid)
                        {
                            hasInvalidTrait = true;
                            invalidTraits.Add(trimmedId);
                        }

                        if (isValid)
                        {
                            // Valid trait - clickable link
                            // Use orange text if field was saved but not committed
                            var linkColor = rowVm.IsFieldSaved(attributeName)
                                ? Color.FromRgb(255, 165, 0)   // Orange for saved
                                : Color.FromRgb(0, 120, 215);  // Blue for normal

                            var linkButton = new Button
                            {
                                Content = trimmedId,
                                Padding = new Thickness(4, 2),
                                Margin = new Thickness(0, 0, 4, 0),
                                Background = Brushes.Transparent,
                                BorderThickness = new Thickness(0),
                                Foreground = new SolidColorBrush(linkColor),
                                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                                FontSize = 12,
                                MinHeight = 0,
                                MinWidth = 0
                            };
                            ToolTip.SetTip(linkButton, $"Click to navigate to: {trimmedId}");
                            linkButton.Click += (s, e) =>
                            {
                                vm.NavigateToReferenceForField(attributeName, capturedId);
                            };
                            linksPanel.Children.Add(linkButton);
                        }
                        else
                        {
                            // Invalid trait - non-clickable text
                            var invalidText = new TextBlock
                            {
                                Text = trimmedId,
                                Padding = new Thickness(4, 2),
                                Margin = new Thickness(0, 0, 4, 0),
                                Background = new SolidColorBrush(Color.FromRgb(255, 220, 220)),
                                Foreground = new SolidColorBrush(Color.FromRgb(200, 0, 0)),
                                FontSize = 12,
                                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                            };
                            ToolTip.SetTip(invalidText, $"ERROR: Trait '{trimmedId}' does not exist!");
                            linksPanel.Children.Add(invalidText);
                        }
                    }
                }

                // Register validation errors for invalid traits
                foreach (var invalidTrait in invalidTraits)
                {
                    vm.ValidationManager.RegisterError(
                        rowIndex,
                        attributeName,
                        $"Invalid trait '{invalidTrait}' does not exist",
                        entryId,
                        invalidTrait);
                }

                // Use centralized styling (handles all states: error, modified, saved, etc.)
                CellStyleHelper.UpdateCellState(border, rowVm, attributeName, vm);
            }

            // Initial build
            RebuildLinks();

            // Subscribe to centralized refresh event
            vm.CellRefreshRequested += (s, args) =>
            {
                RebuildLinks();
            };

            panel.Children.Add(linksPanel);

            // Edit button - opens a simple text editor popup (hidden for removed rows)
            var editButton = new Button
            {
                Content = "...",
                FontSize = 10,
                Padding = new Thickness(4, 2),
                MinWidth = 20,
                MinHeight = 0,
                Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
                Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                IsVisible = !rowVm.IsRemoved  // Hide for removed rows
            };
            ToolTip.SetTip(editButton, "Edit traits (comma-separated)");

            editButton.Click += (s, e) =>
            {
                Console.WriteLine($"[CrossRef] Edit button clicked for {attributeName}");
                var currentValue = rowVm[attributeName] ?? "";
                var availableIds = vm.GetAvailableIds(attributeName).ToList();
                Console.WriteLine($"[CrossRef] Current value: {currentValue}, Available IDs: {availableIds.Count}");

                // Get the local key (item ID) for updating the source file
                var localKeyField = fieldDef.CrossReference?.LocalKeyField ?? "id";
                var localKey = rowVm[localKeyField] ?? "";

                ShowTraitEditorPopup(editButton, currentValue, availableIds, (result) =>
                {
                    if (result != null && result != currentValue)
                    {
                        // Update the cross-reference in the source file
                        var success = vm.UpdateCrossReferenceValue(attributeName, localKey, result);
                        if (success)
                        {
                            rowVm[attributeName] = result;
                            Console.WriteLine($"[CrossRef] Updated traits to: {result}");

                            // Rebuild the links to show the new values
                            RebuildLinks();
                        }
                        else
                        {
                            Console.WriteLine($"[CrossRef] Failed to update cross-reference");
                        }
                    }
                });
            };

            panel.Children.Add(editButton);
            border.Child = panel;

            return border;
        });
    }

    /// <summary>
    /// Shows a dialog window for editing traits with autocomplete support.
    /// </summary>
    private static void ShowTraitEditorPopup(Control anchor, string currentValue, List<string> availableIds, Action<string?> onComplete)
    {
        Console.WriteLine("[TraitEditor] Creating dialog...");

        var topLevel = TopLevel.GetTopLevel(anchor);
        if (topLevel == null)
        {
            Console.WriteLine("[TraitEditor] ERROR: Could not find TopLevel");
            onComplete(null);
            return;
        }

        // Create a dialog window
        var dialog = new Window
        {
            Title = "Edit Traits",
            Width = 450,
            Height = 450,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true,
            ShowInTaskbar = false,
            MinWidth = 350,
            MinHeight = 350
        };

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)), // Dark theme background
            Padding = new Thickness(16)
        };

        var stack = new StackPanel { Spacing = 8 };

        // Current value editor
        var label = new TextBlock
        {
            Text = "Traits (comma-separated):",
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        stack.Children.Add(label);

        var textBox = new TextBox
        {
            Text = currentValue,
            Watermark = "Enter trait IDs...",
            Height = 60,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap
        };
        stack.Children.Add(textBox);

        // Autocomplete section
        var acLabel = new TextBlock
        {
            Text = "Available traits (double-click to add):",
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 12, 0, 4)
        };
        stack.Children.Add(acLabel);

        var searchBox = new TextBox
        {
            Watermark = "Type to filter..."
        };
        stack.Children.Add(searchBox);

        var listBox = new ListBox
        {
            Height = 180,
            ItemsSource = availableIds.Take(50).ToList()
        };
        stack.Children.Add(listBox);

        // Filter suggestions
        searchBox.TextChanged += (s, e) =>
        {
            var searchText = searchBox.Text ?? "";
            var currentIds = (textBox.Text ?? "")
                .Split(new[] { ',', ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().ToLowerInvariant())
                .ToHashSet();

            var filtered = availableIds
                .Where(id => !currentIds.Contains(id.ToLowerInvariant()))
                .Where(id => string.IsNullOrEmpty(searchText) ||
                             id.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .Take(50)
                .ToList();
            listBox.ItemsSource = filtered;
        };

        // Double-click to add
        listBox.DoubleTapped += (s, e) =>
        {
            if (listBox.SelectedItem is string selected)
            {
                var current = textBox.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(current))
                    textBox.Text = selected;
                else
                    textBox.Text = current + ", " + selected;
                searchBox.Text = "";
                Console.WriteLine($"[TraitEditor] Added trait: {selected}");
            }
        };

        // Buttons
        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        string? result = null;
        bool completed = false;

        var okButton = new Button
        {
            Content = "OK",
            Padding = new Thickness(24, 6),
            Background = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
            Foreground = Brushes.White
        };
        okButton.Click += (s, e) =>
        {
            if (!completed)
            {
                completed = true;
                result = textBox.Text;
                Console.WriteLine($"[TraitEditor] OK clicked, value: {result}");
                dialog.Close();
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
            if (!completed)
            {
                completed = true;
                Console.WriteLine("[TraitEditor] Cancel clicked");
                dialog.Close();
            }
        };

        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(okButton);
        stack.Children.Add(buttonPanel);

        border.Child = stack;
        dialog.Content = border;

        // Handle Escape key
        dialog.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape && !completed)
            {
                completed = true;
                dialog.Close();
                e.Handled = true;
            }
        };

        // Handle dialog closed
        dialog.Closed += (s, e) =>
        {
            Console.WriteLine($"[TraitEditor] Dialog closed, result: {result}");
            onComplete(result);
        };

        // Show the dialog
        Console.WriteLine("[TraitEditor] Showing dialog...");
        if (topLevel is Window parentWindow)
        {
            dialog.ShowDialog(parentWindow);
        }
        else
        {
            dialog.Show();
        }

        textBox.Focus();
    }

    /// <summary>
    /// Creates a display template for enum cells (shows current value as text with validation).
    /// Uses AXAML styles via pseudo-classes (defined in CellStyles.axaml).
    /// </summary>
    private static IDataTemplate CreateEnumCellTemplate(string attributeName, FieldDefinition fieldDef, FileTabViewModel vm)
    {
        // Build lookup for value -> displayName
        var displayNameLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var enumValue in fieldDef.EnumValues ?? [])
        {
            displayNameLookup[enumValue.Value] = enumValue.DisplayName ?? enumValue.Value;
        }

        // Helper to get display name for a value
        string GetDisplayName(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return displayNameLookup.TryGetValue(value, out var displayName) ? displayName : value;
        }

        return new FuncDataTemplate<EntryRowViewModel>((rowVm, _) =>
        {
            var border = new Border();
            border.Classes.Add("dataCell");

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto")
            };

            var text = new TextBlock();
            text.Classes.Add("cellText");
            Grid.SetColumn(text, 0);
            grid.Children.Add(text);

            // Warning/error icon (styled via pseudo-classes)
            var icon = new TextBlock();
            icon.Classes.Add("cellIcon");
            Grid.SetColumn(icon, 1);
            grid.Children.Add(icon);

            if (rowVm != null)
            {
                // Show display name instead of raw value
                text.Text = GetDisplayName(rowVm[attributeName]);

                // Validate on render
                var rowIndex = rowVm.RowNumber - 1;
                var value = rowVm[attributeName];
                var entryId = rowVm["id"];

                CellValidationHelper.ValidateAndRegister(
                    vm.ValidationManager, rowIndex, attributeName, value, fieldDef, entryId);

                // Initial styling
                CellStyleHelper.UpdateCellState(border, rowVm, attributeName, vm);

                // Subscribe to centralized refresh event for all updates
                vm.CellRefreshRequested += (s, args) =>
                {
                    // Re-read value from row and show display name
                    text.Text = GetDisplayName(rowVm[attributeName]);
                    // Update styling
                    CellStyleHelper.UpdateCellState(border, rowVm, attributeName, vm);
                };
            }

            border.Child = grid;
            return border;
        });
    }

    /// <summary>
    /// Creates an editing template for enum cells (ComboBox with enum values).
    /// </summary>
    private static IDataTemplate CreateEnumEditingTemplate(string attributeName, FieldDefinition fieldDef)
    {
        return new FuncDataTemplate<EntryRowViewModel>((rowVm, _) =>
        {
            var comboBox = new ComboBox
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                Padding = new Thickness(4, 0),
                MinHeight = 0
            };

            // Create items from enum values
            var items = new List<ComboBoxItem>();

            // Add empty option
            items.Add(new ComboBoxItem { Content = "", Tag = "" });

            foreach (var enumValue in fieldDef.EnumValues ?? [])
            {
                var item = new ComboBoxItem
                {
                    Content = enumValue.DisplayName ?? enumValue.Value,
                    Tag = enumValue.Value
                };
                if (!string.IsNullOrEmpty(enumValue.Description))
                {
                    ToolTip.SetTip(item, enumValue.Description);
                }
                items.Add(item);
            }

            comboBox.ItemsSource = items;

            if (rowVm != null)
            {
                // Disable editing for removed rows
                if (rowVm.IsRemoved)
                {
                    comboBox.IsEnabled = false;
                }

                // Set initial selection based on current value
                var currentValue = rowVm[attributeName];
                var selectedItem = items.FirstOrDefault(i => (string?)i.Tag == currentValue);
                if (selectedItem != null)
                {
                    comboBox.SelectedItem = selectedItem;
                }

                // Update row value when selection changes
                comboBox.SelectionChanged += (s, e) =>
                {
                    if (comboBox.SelectedItem is ComboBoxItem selected)
                    {
                        rowVm[attributeName] = (string?)selected.Tag ?? "";
                    }
                };
            }

            return comboBox;
        });
    }

    /// <summary>
    /// Creates a text cell template that handles validation and dynamic styling.
    /// Uses AXAML styles via pseudo-classes (defined in CellStyles.axaml).
    /// </summary>
    private static IDataTemplate CreateTextCellTemplate(string attributeName, FieldDefinition? fieldDef, FileTabViewModel vm)
    {
        return new FuncDataTemplate<EntryRowViewModel>((rowVm, _) =>
        {
            var border = new Border();
            border.Classes.Add("dataCell");

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto")
            };

            var text = new TextBlock();
            text.Classes.Add("cellText");
            Grid.SetColumn(text, 0);
            grid.Children.Add(text);

            // Warning/error icon (styled via pseudo-classes)
            var icon = new TextBlock();
            icon.Classes.Add("cellIcon");
            Grid.SetColumn(icon, 1);
            grid.Children.Add(icon);

            if (rowVm != null)
            {
                text.Bind(TextBlock.TextProperty, new Binding($"[{attributeName}]"));

                // Validate on render if we have field definition
                if (fieldDef != null)
                {
                    var rowIndex = rowVm.RowNumber - 1;
                    var value = rowVm[attributeName];
                    var entryId = rowVm["id"];

                    CellValidationHelper.ValidateAndRegister(
                        vm.ValidationManager, rowIndex, attributeName, value, fieldDef, entryId);
                }

                // Initial styling
                CellStyleHelper.UpdateCellState(border, rowVm, attributeName, vm);

                // Subscribe to centralized refresh event for all updates
                vm.CellRefreshRequested += (s, args) =>
                {
                    // Re-read value from row and update text
                    text.Text = rowVm[attributeName];
                    // Update styling
                    CellStyleHelper.UpdateCellState(border, rowVm, attributeName, vm);
                };
            }

            border.Child = grid;
            return border;
        });
    }

    /// <summary>
    /// Creates a text editing template for validated text cells.
    /// </summary>
    private static IDataTemplate CreateTextEditingTemplate(string attributeName)
    {
        return new FuncDataTemplate<EntryRowViewModel>((rowVm, _) =>
        {
            var textBox = new TextBox
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                Padding = new Thickness(4, 0),
                MinHeight = 0
            };

            if (rowVm != null)
            {
                // Disable editing for removed rows
                if (rowVm.IsRemoved)
                {
                    textBox.IsReadOnly = true;
                    textBox.IsEnabled = false;
                }
                textBox.Bind(TextBox.TextProperty, new Binding($"[{attributeName}]", BindingMode.TwoWay));
            }

            return textBox;
        });
    }

    /// <summary>
    /// Creates a cell template for icon fields with thumbnail preview and picker button.
    /// </summary>
    private static IDataTemplate CreateIconCellTemplate(string attributeName, FieldDefinition fieldDef, FileTabViewModel vm)
    {
        return new FuncDataTemplate<EntryRowViewModel>((rowVm, _) =>
        {
            var border = new Border();
            border.Classes.Add("dataCell");

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4
            };

            if (rowVm == null)
            {
                border.Child = panel;
                return border;
            }

            // Icon thumbnail
            var iconImage = new Image
            {
                Width = 24,
                Height = 24,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0)
            };

            // Icon name text
            var iconText = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0),
                FontSize = 12
            };

            // Helper to update the icon display
            void UpdateIconDisplay()
            {
                var iconName = rowVm[attributeName];
                iconText.Text = iconName ?? "";

                if (!string.IsNullOrEmpty(iconName) && vm.IconService != null)
                {
                    var iconPath = vm.IconService.GetIconPath(iconName);
                    if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                    {
                        try
                        {
                            iconImage.Source = new Bitmap(iconPath);
                            iconImage.IsVisible = true;
                        }
                        catch
                        {
                            iconImage.IsVisible = false;
                        }
                    }
                    else
                    {
                        iconImage.IsVisible = false;
                    }
                }
                else
                {
                    iconImage.IsVisible = false;
                }

                CellStyleHelper.UpdateCellState(border, rowVm, attributeName, vm);
            }

            // Initial display
            UpdateIconDisplay();

            // Subscribe to refresh events
            vm.CellRefreshRequested += (s, args) => UpdateIconDisplay();

            panel.Children.Add(iconImage);
            panel.Children.Add(iconText);

            // Edit button (hidden for removed rows)
            var editButton = new Button
            {
                Content = "...",
                FontSize = 10,
                Padding = new Thickness(4, 2),
                MinWidth = 20,
                MinHeight = 0,
                Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
                Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Cursor = new Cursor(StandardCursorType.Hand),
                IsVisible = !rowVm.IsRemoved,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
            };
            ToolTip.SetTip(editButton, "Select icon");

            editButton.Click += (s, e) =>
            {
                if (vm.IconService == null) return;

                var currentValue = rowVm[attributeName] ?? "";
                ShowIconPickerPopup(editButton, currentValue, vm.IconService, (result) =>
                {
                    if (result != null && result != currentValue)
                    {
                        rowVm[attributeName] = result;
                        Console.WriteLine($"[IconPicker] Selected icon: {result}");
                        UpdateIconDisplay();
                    }
                });
            };

            panel.Children.Add(editButton);
            border.Child = panel;

            return border;
        });
    }

    /// <summary>
    /// Shows a dialog for selecting an icon with visual preview and filtering.
    /// </summary>
    private static void ShowIconPickerPopup(Control anchor, string currentValue, IIconService iconService, Action<string?> onComplete)
    {
        Console.WriteLine("[IconPicker] Creating dialog...");

        var topLevel = TopLevel.GetTopLevel(anchor);
        if (topLevel == null)
        {
            Console.WriteLine("[IconPicker] ERROR: Could not find TopLevel");
            onComplete(null);
            return;
        }

        // Create a dialog window
        var dialog = new Window
        {
            Title = "Select Icon",
            Width = 600,
            Height = 550,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true,
            ShowInTaskbar = false,
            MinWidth = 400,
            MinHeight = 400
        };

        var mainBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            Padding = new Thickness(16)
        };

        var mainStack = new StackPanel { Spacing = 12 };

        // Current selection display
        var currentPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        var currentLabel = new TextBlock
        {
            Text = "Current:",
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var currentIcon = new Image
        {
            Width = 32,
            Height = 32,
            Stretch = Stretch.Uniform
        };
        var currentText = new TextBlock
        {
            Text = currentValue,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0, 180, 255))
        };

        // Show current icon
        if (!string.IsNullOrEmpty(currentValue))
        {
            var iconPath = iconService.GetIconPath(currentValue);
            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
            {
                try { currentIcon.Source = new Bitmap(iconPath); } catch { }
            }
        }

        currentPanel.Children.Add(currentLabel);
        currentPanel.Children.Add(currentIcon);
        currentPanel.Children.Add(currentText);
        mainStack.Children.Add(currentPanel);

        // Search box
        var searchBox = new TextBox
        {
            Watermark = "Type to filter icons...",
            Margin = new Thickness(0, 8, 0, 0)
        };
        mainStack.Children.Add(searchBox);

        // Icon grid in a scroll viewer
        var scrollViewer = new ScrollViewer
        {
            Height = 350,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var iconWrapPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal
        };

        string? selectedIconName = null;
        Button? selectedButton = null;

        // Helper to populate icons
        void PopulateIcons(string filter)
        {
            iconWrapPanel.Children.Clear();
            var icons = iconService.SearchIcons(filter, 100);

            foreach (var icon in icons)
            {
                var iconButton = new Button
                {
                    Width = 64,
                    Height = 64,
                    Padding = new Thickness(4),
                    Margin = new Thickness(2),
                    Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                    BorderThickness = new Thickness(1),
                    Tag = icon.Name,
                    Cursor = new Cursor(StandardCursorType.Hand)
                };

                var iconStack = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                var img = new Image
                {
                    Width = 40,
                    Height = 40,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                if (File.Exists(icon.FilePath))
                {
                    try { img.Source = new Bitmap(icon.FilePath); } catch { }
                }

                iconStack.Children.Add(img);

                iconButton.Content = iconStack;
                ToolTip.SetTip(iconButton, $"{icon.Name}\n({icon.Category})");

                // Highlight if this is the current value
                if (icon.Name.Equals(currentValue, StringComparison.OrdinalIgnoreCase))
                {
                    iconButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 180, 255));
                    iconButton.BorderThickness = new Thickness(2);
                    selectedIconName = icon.Name;
                    selectedButton = iconButton;
                }

                var capturedName = icon.Name;
                iconButton.Click += (s, e) =>
                {
                    // Clear previous selection
                    if (selectedButton != null)
                    {
                        selectedButton.BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80));
                        selectedButton.BorderThickness = new Thickness(1);
                    }

                    // Set new selection
                    selectedIconName = capturedName;
                    selectedButton = iconButton;
                    iconButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 180, 255));
                    iconButton.BorderThickness = new Thickness(2);

                    // Update current display
                    currentText.Text = capturedName;
                    var path = iconService.GetIconPath(capturedName);
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        try { currentIcon.Source = new Bitmap(path); } catch { }
                    }
                };

                // Double-click to select and close
                iconButton.DoubleTapped += (s, e) =>
                {
                    selectedIconName = capturedName;
                    dialog.Close();
                };

                iconWrapPanel.Children.Add(iconButton);
            }
        }

        // Initial population
        PopulateIcons("");

        // Filter on search text change
        searchBox.TextChanged += (s, e) =>
        {
            PopulateIcons(searchBox.Text ?? "");
        };

        scrollViewer.Content = iconWrapPanel;
        mainStack.Children.Add(scrollViewer);

        // Buttons
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        string? result = null;
        bool completed = false;

        var clearButton = new Button
        {
            Content = "Clear",
            Padding = new Thickness(16, 6),
            Background = new SolidColorBrush(Color.FromRgb(80, 60, 60)),
            Foreground = Brushes.White
        };
        clearButton.Click += (s, e) =>
        {
            if (!completed)
            {
                completed = true;
                result = "";  // Clear the icon
                dialog.Close();
            }
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
            if (!completed)
            {
                completed = true;
                result = selectedIconName;
                dialog.Close();
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
            if (!completed)
            {
                completed = true;
                dialog.Close();
            }
        };

        buttonPanel.Children.Add(clearButton);
        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(okButton);
        mainStack.Children.Add(buttonPanel);

        mainBorder.Child = mainStack;
        dialog.Content = mainBorder;

        // Handle Escape key
        dialog.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape && !completed)
            {
                completed = true;
                dialog.Close();
                e.Handled = true;
            }
        };

        // Handle dialog closed
        dialog.Closed += (s, e) =>
        {
            Console.WriteLine($"[IconPicker] Dialog closed, result: {result}");
            onComplete(result);
        };

        // Show the dialog
        if (topLevel is Window parentWindow)
        {
            dialog.ShowDialog(parentWindow);
        }
        else
        {
            dialog.Show();
        }

        searchBox.Focus();
    }

    /// <summary>
    /// Handles click on validation panel header to toggle expansion.
    /// </summary>
    private void OnValidationHeaderClick(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (DataContext is FileTabViewModel vm)
        {
            vm.IsValidationPanelExpanded = !vm.IsValidationPanelExpanded;
        }
    }

    /// <summary>
    /// Handles click on validation issue to navigate to that row.
    /// </summary>
    private void OnValidationIssueClick(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is TORTools.Core.Validation.ValidationIssue issue)
        {
            if (DataContext is FileTabViewModel vm)
            {
                vm.NavigateToIssueCommand.Execute(issue);
            }
        }
    }
}
