using System.Collections.Generic;
using System.Linq;
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
using TORTools.App.Services;
using TORTools.App.ViewModels;
using TORTools.App.Views.Dialogs;
using TORTools.Core.Schema;
using TORTools.Core.Services;
using TORTools.Core.Validation;

namespace TORTools.App.Views;

public partial class FileTabView : UserControl
{
    private bool _columnsGenerated;
    private object? _pendingScrollTarget;
    private bool _scrollPending;

    // Fill handle (drag-to-fill) tracking
    private bool _isFillDragging;
    private int _fillStartRowIndex = -1;
    private int _fillEndRowIndex = -1;
    private double _fillStartY = 0;
    private string? _fillColumnName;
    private string? _fillValue;
    private Border? _activeFillHandle;

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
        // Use debouncing to avoid jumps during row refresh (multiple selection changes)
        grid.SelectionChanged += (s, e) =>
        {
            // Check if scroll is suppressed (during undo/redo operations)
            if (DataContext is FileTabViewModel vm && vm.SuppressScrollIntoView)
            {
                return;
            }

            if (grid.SelectedItem != null)
            {
                // Store the target and schedule a scroll if not already pending
                _pendingScrollTarget = grid.SelectedItem;

                if (!_scrollPending)
                {
                    _scrollPending = true;
                    // Defer scroll until after all layout updates have settled
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        _scrollPending = false;

                        // Check again in case flag was set during the delay
                        if (DataContext is FileTabViewModel vm2 && vm2.SuppressScrollIntoView)
                        {
                            _pendingScrollTarget = null;
                            return;
                        }

                        // Scroll to the most recent target
                        if (_pendingScrollTarget != null && grid.SelectedItem == _pendingScrollTarget)
                        {
                            grid.ScrollIntoView(_pendingScrollTarget, null);
                        }
                        _pendingScrollTarget = null;
                    }, Avalonia.Threading.DispatcherPriority.Background);
                }
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

        // Check if we're editing a cell - if so, let normal text editing work
        var focusedElement = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (focusedElement is TextBox)
        {
            return;
        }

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        // Delete key (without Ctrl): Clear selected cell contents
        if (e.Key == Key.Delete && !ctrl)
        {
            var grid = sender as DataGrid;
            if (grid?.CurrentColumn != null && vm.SelectedIndex >= 0 && vm.SelectedIndex < vm.Rows.Count)
            {
                var selectedRow = vm.Rows[vm.SelectedIndex];

                // Get the column's binding path (attribute name)
                var column = grid.CurrentColumn;
                string? attributeName = null;

                // Try to get the attribute name from the column tag
                if (column.Tag is string tag)
                {
                    attributeName = tag;
                }
                else if (column.Header is string header)
                {
                    // Try to find field by display name
                    var field = vm.Schema?.Fields.FirstOrDefault(f =>
                        f.Value.DisplayName == header || f.Key == header);
                    if (field.HasValue)
                    {
                        attributeName = field.Value.Key;
                    }
                }

                if (!string.IsNullOrEmpty(attributeName))
                {
                    Console.WriteLine($"[KeyDown] Delete - Clearing cell: {attributeName}");
                    selectedRow[attributeName] = "";
                    vm.RequestCellRefresh();
                    e.Handled = true;
                }
            }
            return;
        }

        if (!ctrl) return;

        if (e.Key == Key.C)
        {
            // Ctrl+C: Copy selected row (highlights it with blue border)
            Console.WriteLine("[KeyDown] Copying row...");
            vm.CopyRow();
            e.Handled = true;
        }
        else if (e.Key == Key.V)
        {
            // Ctrl+V: Check for equipment clipboard data first, then fall back to row paste
            _ = HandlePasteAsync(vm);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles paste operation, checking for equipment clipboard format first.
    /// </summary>
    private async Task HandlePasteAsync(FileTabViewModel vm)
    {
        // Check if this is an equipment set file
        if (vm.Schema?.ItemCatalogCrossRef == true && vm.ItemCatalogService != null)
        {
            // Try to get clipboard text
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                var dataTransfer = await clipboard.TryGetDataAsync();
                var clipboardText = dataTransfer != null ? await dataTransfer.TryGetTextAsync() : null;
                if (!string.IsNullOrWhiteSpace(clipboardText))
                {
                    // Check if it looks like equipment clipboard format (comma-separated, has Item. or "none")
                    if (IsEquipmentClipboardFormat(clipboardText))
                    {
                        Console.WriteLine($"[Paste] Detected equipment clipboard format: {clipboardText}");
                        PasteEquipmentFromClipboard(vm, clipboardText);
                        return;
                    }
                }
            }
        }

        // Fall back to regular row paste
        if (vm.HasCopiedRow)
        {
            Console.WriteLine("[KeyDown] Pasting row...");
            vm.PasteRow();
        }
    }

    /// <summary>
    /// Checks if clipboard text matches the equipment clipboard format from CopyEquipmentToClipBoard.
    /// Format: comma-separated values with "Item.{id}" or "none" for each slot.
    /// </summary>
    private static bool IsEquipmentClipboardFormat(string text)
    {
        // Equipment clipboard format has 11 comma-separated values
        var parts = text.Split(',');
        if (parts.Length < 10 || parts.Length > 12)
            return false;

        // Check if values look like equipment (Item.xxx or none)
        int validCount = 0;
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("Item.", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(trimmed))
            {
                validCount++;
            }
        }

        // At least half should be valid equipment format
        return validCount >= parts.Length / 2;
    }

    /// <summary>
    /// Pastes equipment data from clipboard into the current row.
    /// </summary>
    private void PasteEquipmentFromClipboard(FileTabViewModel vm, string clipboardText)
    {
        if (vm.SelectedIndex < 0 || vm.SelectedIndex >= vm.Rows.Count)
        {
            Console.WriteLine("[Paste] No row selected for equipment paste");
            return;
        }

        var selectedRow = vm.Rows[vm.SelectedIndex];
        var equipmentData = vm.ItemCatalogService!.ParseClipboardEquipment(clipboardText);

        Console.WriteLine($"[Paste] Parsed {equipmentData.Count} equipment slots");

        // Get equipment slot definitions from schema
        var slotDefs = vm.Schema?.EquipmentSlots;
        if (slotDefs == null || slotDefs.Count == 0)
        {
            Console.WriteLine("[Paste] No equipment slot definitions in schema");
            return;
        }

        // Apply equipment to the row
        // Note: For now, we apply to direct attributes. For nested variations, we'd need different handling.
        foreach (var (slot, itemId) in equipmentData)
        {
            // Try lowercase first, then original case
            var slotFieldName = slot.ToLowerInvariant();
            if (vm.Schema?.Fields.ContainsKey(slotFieldName) == true)
            {
                selectedRow[slotFieldName] = itemId;
                Console.WriteLine($"[Paste] Set {slotFieldName} = {itemId}");
            }
            else if (vm.Schema?.Fields.ContainsKey(slot) == true)
            {
                selectedRow[slot] = itemId;
                Console.WriteLine($"[Paste] Set {slot} = {itemId}");
            }
            else
            {
                // Field not in schema, but set it anyway for flexibility
                selectedRow[slot] = itemId;
                Console.WriteLine($"[Paste] Set {slot} = {itemId} (not in schema)");
            }
        }

        vm.RequestCellRefresh();
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

        // Add row number column with paste button
        var rowNumColumn = new DataGridTemplateColumn
        {
            Header = "#",
            Width = new DataGridLength(60),
            IsReadOnly = true,
            CellTemplate = CreateRowNumberTemplate(vm)
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
            var isBannerField = fieldDef?.Type == "banner";
            var isColorField = fieldDef?.Type == "color";
            var isTupleListField = fieldDef?.Type == "tupleList" && fieldDef?.TupleList != null;
            var isTagListField = fieldDef?.Type == "tagList" && fieldDef?.TagList != null;

            // Use schema width if defined (non-default), otherwise fall back to display mappings
            var columnWidth = (fieldDef?.Width > 0 && fieldDef.Width != 120) ? fieldDef.Width : displayInfo.Width;
            Console.WriteLine($"[FileTabView] Adding column: {displayInfo.DisplayName} ({displayInfo.AttributeName}) - Width: {columnWidth}, Enum: {isEnumField}, CrossRef: {isCrossRefField}, Icon: {isIconField}, Banner: {isBannerField}, Color: {isColorField}, TupleList: {isTupleListField}, TagList: {isTagListField}");

            // Check if this is the ID column - if so, add lock toggle to header
            var isIdColumn = displayInfo.AttributeName.Equals("id", StringComparison.OrdinalIgnoreCase);

            DataGridColumn column;
            if (isColorField)
            {
                // Color swatch field
                column = new DataGridTemplateColumn
                {
                    Header = CreateColumnHeader(displayInfo, fieldDef),
                    Width = new DataGridLength(columnWidth),
                    IsReadOnly = false,
                    CellTemplate = CreateColorCellTemplate(displayInfo.AttributeName, fieldDef!, vm)
                };
            }
            else if (isBannerField)
            {
                // Banner image field
                column = new DataGridTemplateColumn
                {
                    Header = CreateColumnHeader(displayInfo, fieldDef),
                    Width = new DataGridLength(columnWidth),
                    IsReadOnly = false,
                    CellTemplate = CreateBannerCellTemplate(displayInfo.AttributeName, fieldDef!, vm)
                };
            }
            else if (isIconField)
            {
                // Icon picker field
                column = new DataGridTemplateColumn
                {
                    Header = CreateColumnHeader(displayInfo, fieldDef),
                    Width = new DataGridLength(columnWidth),
                    IsReadOnly = false,
                    CellTemplate = CreateIconCellTemplate(displayInfo.AttributeName, fieldDef!, vm)
                };
            }
            else if (isCrossRefField)
            {
                var isReverseCrossRef = fieldDef!.Type == "reverseCrossReference";
                var valueType = fieldDef.CrossReference?.ValueType ?? "crossRef";
                var renderAs = fieldDef.RenderAs ?? "advanced";

                if (renderAs == "dropdown")
                {
                    // Simple dropdown for cross-references (like culture selection)
                    column = new DataGridTemplateColumn
                    {
                        Header = CreateColumnHeader(displayInfo, fieldDef),
                        Width = new DataGridLength(columnWidth),
                        IsReadOnly = false,
                        CellTemplate = CreateCrossRefDropdownTemplate(displayInfo.AttributeName, fieldDef, vm),
                        CellEditingTemplate = CreateCrossRefDropdownEditingTemplate(displayInfo.AttributeName, fieldDef, vm)
                    };
                }
                else if (valueType == "enum" && fieldDef.EnumValues?.Count > 0)
                {
                    // External value with enum rendering
                    column = new DataGridTemplateColumn
                    {
                        Header = CreateColumnHeader(displayInfo, fieldDef),
                        Width = new DataGridLength(columnWidth),
                        IsReadOnly = false,
                        CellTemplate = CreateExternalEnumCellTemplate(displayInfo.AttributeName, fieldDef, vm),
                        CellEditingTemplate = CreateEnumEditingTemplate(displayInfo.AttributeName, fieldDef, vm)
                    };
                }
                else if (valueType == "int" || valueType == "string")
                {
                    // External value with simple text/int rendering
                    column = new DataGridTemplateColumn
                    {
                        Header = CreateColumnHeader(displayInfo, fieldDef),
                        Width = new DataGridLength(columnWidth),
                        IsReadOnly = false,
                        CellTemplate = CreateExternalValueCellTemplate(displayInfo.AttributeName, fieldDef, vm),
                        CellEditingTemplate = CreateTextEditingTemplate(displayInfo.AttributeName, fieldDef)
                    };
                }
                else if (isReverseCrossRef)
                {
                    // Read-only: clickable links with single-click navigation
                    column = new DataGridTemplateColumn
                    {
                        Header = CreateColumnHeader(displayInfo, fieldDef),
                        Width = new DataGridLength(columnWidth),
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
                        Width = new DataGridLength(columnWidth),
                        IsReadOnly = false,
                        CellTemplate = CreateEditableCrossRefTemplate(displayInfo.AttributeName, fieldDef, vm)
                    };
                }
            }
            else if (isTupleListField)
            {
                // Tuple list field (e.g., DamageProportions, Resistances, Amplifiers)
                column = new DataGridTemplateColumn
                {
                    Header = CreateColumnHeader(displayInfo, fieldDef),
                    Width = new DataGridLength(columnWidth),
                    IsReadOnly = true, // Editing happens via popup
                    CellTemplate = CreateTupleListCellTemplate(displayInfo.AttributeName, fieldDef!, vm)
                };
            }
            else if (isTagListField)
            {
                // Tag list field (e.g., Tags on tor_strings)
                column = new DataGridTemplateColumn
                {
                    Header = CreateColumnHeader(displayInfo, fieldDef),
                    Width = new DataGridLength(columnWidth),
                    IsReadOnly = true, // Editing happens via popup
                    CellTemplate = CreateTagListCellTemplate(displayInfo.AttributeName, fieldDef!, vm)
                };
            }
            else if (isEnumField)
            {
                // Create ComboBox column for enum fields
                column = new DataGridTemplateColumn
                {
                    Header = CreateColumnHeader(displayInfo, fieldDef),
                    Width = new DataGridLength(columnWidth),
                    IsReadOnly = displayInfo.IsReadOnly,
                    CellTemplate = CreateEnumCellTemplate(displayInfo.AttributeName, fieldDef!, vm),
                    CellEditingTemplate = CreateEnumEditingTemplate(displayInfo.AttributeName, fieldDef!, vm)
                };
            }
            else if (fieldDef?.Type == "action")
            {
                // Action button field (e.g., Open Parts Editor)
                column = new DataGridTemplateColumn
                {
                    Header = CreateColumnHeader(displayInfo, fieldDef),
                    Width = new DataGridLength(displayInfo.Width),
                    IsReadOnly = true,
                    CellTemplate = CreateActionButtonTemplate(displayInfo.AttributeName, fieldDef, vm)
                };
            }
            else if (fieldDef?.Multiline == true)
            {
                // Multiline text field with edit button (but also allows inline editing)
                column = new DataGridTemplateColumn
                {
                    Header = CreateColumnHeader(displayInfo, fieldDef),
                    Width = new DataGridLength(columnWidth),
                    IsReadOnly = false,
                    CellTemplate = CreateMultilineTextCellTemplate(displayInfo.AttributeName, fieldDef, vm),
                    CellEditingTemplate = CreateTextEditingTemplate(displayInfo.AttributeName, fieldDef)
                };
            }
            else
            {
                // Create text column - all text cells now use templates for consistent styling
                // IsReadOnly is true if either the display mapping says so OR the schema field has readOnly: true
                var isColumnReadOnly = displayInfo.IsReadOnly || fieldDef?.ReadOnly == true;
                column = new DataGridTemplateColumn
                {
                    Header = isIdColumn ? CreateIdColumnHeader(displayInfo, vm) : CreateColumnHeader(displayInfo, fieldDef),
                    Width = new DataGridLength(columnWidth),
                    IsReadOnly = displayInfo.IsReadOnly,
                    CellTemplate = CreateTextCellTemplate(displayInfo.AttributeName, fieldDef, vm),
                    CellEditingTemplate = isColumnReadOnly ? null : CreateTextEditingTemplate(displayInfo.AttributeName, fieldDef)
                };
            }

            // Store attribute name in Tag for keyboard handling (Delete key)
            column.Tag = displayInfo.AttributeName;

            grid.Columns.Add(column);

            // For Equipment Sets: add a paste button column right after "_variation"
            if (displayInfo.AttributeName == "_variation" && vm.Schema?.HasNestedVariations == true)
            {
                var pasteColumn = new DataGridTemplateColumn
                {
                    Header = "📋",
                    Width = new DataGridLength(36),
                    IsReadOnly = true,
                    CellTemplate = CreateClipboardPasteTemplate(vm)
                };
                grid.Columns.Add(pasteColumn);
            }
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
            var vm = DataContext as FileTabViewModel;
            var factionService = vm?.FactionCatalogService;
            UpdateRowStyle(e.Row, rowVm, factionService);

            // Subscribe to property changes to update styling dynamically
            rowVm.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(EntryRowViewModel.IsNew) ||
                    args.PropertyName == nameof(EntryRowViewModel.IsSelectedForCopy))
                {
                    UpdateRowStyle(e.Row, rowVm, factionService);
                }
            };
        }
    }

    private static void UpdateRowStyle(DataGridRow row, EntryRowViewModel rowVm, FactionCatalogService? factionService)
    {
        // Equipment set roster grouping - apply alternating row background
        if (rowVm.IsEquipmentSetVariation && !string.IsNullOrEmpty(rowVm.RosterId))
        {
            // Use hash of roster ID to determine group color
            var rosterHash = rowVm.RosterId.GetHashCode();
            if ((rosterHash & 1) == 0)
            {
                // Even hash: slightly lighter background
                row.Classes.Add("rosterGroupAlt");
            }
            else
            {
                row.Classes.Remove("rosterGroupAlt");
            }
        }

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

        // Culture-based background tinting (subtle)
        if (factionService != null && !rowVm.IsNew && !rowVm.IsSelectedForCopy)
        {
            var culture = rowVm["culture"];
            var cultureColor = factionService.GetCultureColor(culture);
            if (!string.IsNullOrEmpty(cultureColor))
            {
                // Parse the color and create a very subtle tint (alpha ~10-15%)
                var color = ParseHexColor(cultureColor);
                if (color.HasValue)
                {
                    var tint = Color.FromArgb(25, color.Value.R, color.Value.G, color.Value.B); // ~10% opacity
                    row.Background = new SolidColorBrush(tint);
                }
            }
            else
            {
                row.Background = null; // Clear any previous tint
            }
        }
    }

    /// <summary>
    /// Parses a hex color string (0xFFRRGGBB, #RRGGBB, FFRRGGBB formats) to an Avalonia Color.
    /// </summary>
    private static Color? ParseHexColor(string? colorValue)
    {
        if (string.IsNullOrEmpty(colorValue))
            return null;

        try
        {
            var hex = colorValue.Replace("0x", "").Replace("#", "").TrimStart('f', 'F');
            // Ensure we have at least 6 chars (RGB)
            if (hex.Length < 6)
                return null;

            // Take last 6 characters (RGB portion)
            hex = hex.Length > 6 ? hex.Substring(hex.Length - 6) : hex;

            if (byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                return Color.FromRgb(r, g, b);
            }
        }
        catch
        {
            // Ignore parsing errors
        }

        return null;
    }

    /// <summary>
    /// Creates the cell template for the row number column with paste button.
    /// </summary>
    private IDataTemplate CreateRowNumberTemplate(FileTabViewModel vm)
    {
        return new FuncDataTemplate<EntryRowViewModel>((rowVm, _) =>
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*")
            };

            // Paste button (only visible when there's copied data)
            var pasteButton = new Button
            {
                Content = "📋",
                FontSize = 10,
                Padding = new Thickness(2),
                MinWidth = 20,
                MinHeight = 16,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                IsVisible = vm.HasCopiedRow
            };
            ToolTip.SetTip(pasteButton, "Paste copied row data here");
            Grid.SetColumn(pasteButton, 0);

            if (rowVm != null)
            {
                pasteButton.Click += (s, e) =>
                {
                    // Find the row index and select it, then paste
                    var rowIndex = vm.Rows.IndexOf(rowVm);
                    if (rowIndex >= 0)
                    {
                        vm.SelectedIndex = rowIndex;
                        vm.PasteRow();
                    }
                };
            }

            // Update visibility when copy state changes
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(FileTabViewModel.HasCopiedRow))
                {
                    pasteButton.IsVisible = vm.HasCopiedRow;
                }
            };

            grid.Children.Add(pasteButton);

            // Row number text
            var text = new TextBlock
            {
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };
            Grid.SetColumn(text, 1);

            if (rowVm != null)
            {
                text.Bind(TextBlock.TextProperty, new Binding(nameof(EntryRowViewModel.RowNumber)));
            }

            grid.Children.Add(text);

            return grid;
        });
    }

    /// <summary>
    /// Creates a paste button template for Equipment Sets that reads from system clipboard
    /// and pastes into equipment slot columns.
    /// </summary>
    private IDataTemplate CreateClipboardPasteTemplate(FileTabViewModel vm)
    {
        // Equipment slot column names in order
        var equipmentSlots = new[] { "Item0", "Item1", "Item2", "Item3", "Head", "Body", "Cape", "Gloves", "Leg", "Horse", "HorseHarness" };

        return new FuncDataTemplate<EntryRowViewModel>((rowVm, _) =>
        {
            var pasteButton = new Button
            {
                Content = "📋",
                FontSize = 12,
                Padding = new Thickness(4, 2),
                MinWidth = 28,
                Background = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            ToolTip.SetTip(pasteButton, "Paste equipment from clipboard (tab or newline separated)");

            if (rowVm != null)
            {
                pasteButton.Click += async (s, e) =>
                {
                    try
                    {
                        var clipboard = TopLevel.GetTopLevel(pasteButton)?.Clipboard;
                        if (clipboard == null) return;

                        var dataTransfer = await clipboard.TryGetDataAsync();
                        var clipboardText = dataTransfer != null ? await dataTransfer.TryGetTextAsync() : null;
                        if (string.IsNullOrWhiteSpace(clipboardText)) return;

                        // Parse clipboard - support tab-separated, newline-separated, or comma-separated
                        var values = clipboardText
                            .Split(new[] { '\t', '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(v => v.Trim())
                            .Where(v => !string.IsNullOrEmpty(v))
                            .ToArray();

                        Console.WriteLine($"[ClipboardPaste] Parsed {values.Length} values from clipboard");

                        // Paste values into equipment slots
                        for (int i = 0; i < Math.Min(values.Length, equipmentSlots.Length); i++)
                        {
                            var slotName = equipmentSlots[i];
                            var value = values[i];

                            // Skip empty/dash values
                            if (value == "-" || value.ToLower() == "none")
                            {
                                rowVm[slotName] = "";
                            }
                            else
                            {
                                // Strip "Item." prefix if present (display without prefix, saved with prefix via prefixToAdd)
                                if (value.StartsWith("Item.", StringComparison.OrdinalIgnoreCase))
                                {
                                    value = value.Substring(5);
                                }
                                rowVm[slotName] = value;
                            }
                            Console.WriteLine($"[ClipboardPaste] Set {slotName} = {rowVm[slotName]}");
                        }

                        vm.MarkAsModified();
                        vm.RequestCellRefresh();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ClipboardPaste] Error: {ex.Message}");
                    }
                };
            }

            return pasteButton;
        });
    }

    /// <summary>
    /// Creates the ID column header with a lock toggle button.
    /// </summary>
    private static object CreateIdColumnHeader(ColumnDisplayInfo info, FileTabViewModel vm)
    {
        // Use string header to avoid control reuse issues
        // The lock toggle functionality is moved to context menu or toolbar
        return vm.IsIdColumnLocked ? $"{info.DisplayName} 🔒" : info.DisplayName;
    }

    /// <summary>
    /// Creates a column header with display name and tooltip showing the attribute name.
    /// </summary>
    private static object CreateColumnHeader(ColumnDisplayInfo info, FieldDefinition? fieldDef = null)
    {
        // Use string header to avoid control reuse issues with StackPanel
        // The display name with optional attribute name suffix
        var displayName = fieldDef?.DisplayName ?? info.DisplayName;

        // Check if attribute name is significantly different from display name
        var normalizedDisplay = displayName.Replace(" ", "").ToLowerInvariant();
        var normalizedAttr = info.AttributeName.Replace("_", "").ToLowerInvariant();

        if (normalizedDisplay != normalizedAttr)
        {
            // Show both display name and attribute name
            return $"{displayName}\n({info.AttributeName})";
        }

        return displayName;
    }

    /// <summary>
    /// Creates a read-only cell template for reverse cross-reference fields.
    /// Uses single-click navigation (no Ctrl required).
    /// </summary>
    private static IDataTemplate CreateReadOnlyCrossRefTemplate(string attributeName, FieldDefinition fieldDef, FileTabViewModel vm)
    {
        const int MaxVisibleItems = 3;

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
                    var totalCount = ids.Length;
                    var visibleCount = Math.Min(totalCount, MaxVisibleItems);

                    for (int i = 0; i < visibleCount; i++)
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

                    // Show "..." if there are more items
                    if (totalCount > MaxVisibleItems)
                    {
                        var remainingCount = totalCount - MaxVisibleItems;
                        var remainingIds = string.Join(", ", ids.Skip(MaxVisibleItems).Select(id => id.Trim()));

                        var moreButton = new Button
                        {
                            Content = $"... +{remainingCount}",
                            Padding = new Thickness(4, 2),
                            Margin = new Thickness(0, 0, 4, 0),
                            Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                            BorderThickness = new Thickness(0),
                            Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                            FontSize = 11,
                            MinHeight = 0,
                            MinWidth = 0
                        };

                        ToolTip.SetTip(moreButton, $"More entries:\n{remainingIds}");
                        wrapPanel.Children.Add(moreButton);
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

            // Get prefix to strip for validation (e.g., "SkillSet." for skill_template)
            var prefixToStrip = fieldDef.CrossReference?.PrefixToStrip;

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

                // Check if required field is empty
                if (string.IsNullOrEmpty(value) && fieldDef.Required)
                {
                    vm.ValidationManager.RegisterError(
                        rowIndex,
                        attributeName,
                        "Required field is empty - game will crash!",
                        entryId,
                        "");
                }

                if (!string.IsNullOrEmpty(value))
                {
                    var ids = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var id in ids)
                    {
                        var trimmedId = id.Trim();
                        if (string.IsNullOrEmpty(trimmedId)) continue;

                        var capturedId = trimmedId;

                        // Strip prefix for validation AND display if configured
                        var idForValidation = trimmedId;
                        var displayId = trimmedId;
                        if (!string.IsNullOrEmpty(prefixToStrip) &&
                            trimmedId.StartsWith(prefixToStrip, StringComparison.OrdinalIgnoreCase))
                        {
                            idForValidation = trimmedId.Substring(prefixToStrip.Length);
                            displayId = idForValidation; // Show without prefix
                        }

                        var isValid = availableIdsSet.Contains(idForValidation);
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

                            // Check if this is an ability or trait field and try to show icon
                            var isAbilityField = fieldDef.CrossReference?.TargetFile == "tor_abilitytemplates.xml";
                            var isTraitField = fieldDef.CrossReference?.TargetFile == "tor_itemtraits.xml";
                            string? iconPath = null;

                            if (isAbilityField && vm.AbilityCatalogService != null && vm.IconService != null)
                            {
                                var spriteName = vm.AbilityCatalogService.GetAbilitySprite(displayId);
                                if (!string.IsNullOrEmpty(spriteName))
                                {
                                    iconPath = vm.IconService.GetIconPath(spriteName);
                                }
                            }
                            else if (isTraitField && vm.ItemTraitCatalogService != null && vm.IconService != null)
                            {
                                var iconName = vm.ItemTraitCatalogService.GetTraitIcon(displayId);
                                if (!string.IsNullOrEmpty(iconName))
                                {
                                    iconPath = vm.IconService.GetIconPath(iconName);
                                }
                            }

                            // Create a container for icon + text
                            var linkContent = new StackPanel
                            {
                                Orientation = Avalonia.Layout.Orientation.Horizontal,
                                Spacing = 3
                            };

                            // Add ability icon if available
                            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                            {
                                var iconImage = new Image
                                {
                                    Width = 16,
                                    Height = 16,
                                    Source = new Avalonia.Media.Imaging.Bitmap(iconPath),
                                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                                };
                                linkContent.Children.Add(iconImage);
                            }

                            linkContent.Children.Add(new TextBlock
                            {
                                Text = displayId,
                                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                            });

                            var linkButton = new Button
                            {
                                Content = linkContent,
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
                            // Use description as tooltip
                            string? description = null;

                            // For item traits, use ItemTraitCatalogService to get description
                            if (isTraitField && vm.ItemTraitCatalogService != null)
                            {
                                description = vm.ItemTraitCatalogService.GetTraitDescription(displayId);
                            }
                            else
                            {
                                // For other fields, use cross-ref description
                                description = vm.GetCrossRefDescription(attributeName, displayId);
                            }

                            var tooltipText = !string.IsNullOrEmpty(description)
                                ? description  // Show only description, no ID prefix
                                : $"Click to navigate to: {capturedId}";
                            ToolTip.SetTip(linkButton, tooltipText);
                            linkButton.Click += (s, e) =>
                            {
                                vm.NavigateToReferenceForField(attributeName, capturedId);
                            };
                            linksPanel.Children.Add(linkButton);
                        }
                        else
                        {
                            // Invalid trait - non-clickable text (show without prefix)
                            var invalidText = new TextBlock
                            {
                                Text = displayId,
                                Padding = new Thickness(4, 2),
                                Margin = new Thickness(0, 0, 4, 0),
                                Background = new SolidColorBrush(Color.FromRgb(255, 220, 220)),
                                Foreground = new SolidColorBrush(Color.FromRgb(200, 0, 0)),
                                FontSize = 12,
                                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                            };

                            // Get entity type from target file for better error message
                            var targetFile = fieldDef.CrossReference?.TargetFile ?? "";
                            var entityType = targetFile
                                .Replace("tor_", "")
                                .Replace(".xml", "")
                                .Replace("_", " ");
                            if (string.IsNullOrEmpty(entityType)) entityType = "target entities";

                            ToolTip.SetTip(invalidText, $"ERROR: '{displayId}' does not exist in {entityType}!");
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

            // Check if this is a single-value field (clan, skill_template, equipment set) vs multi-value (abilities, traits)
            var isSingleValue = fieldDef.CrossReference?.SingleValue ?? false;
            var fieldDisplayName = fieldDef.DisplayName ?? attributeName;
            var buttonText = "...";
            var tooltipText = $"Edit {fieldDisplayName}";

            // Edit button - opens a simple text editor popup (hidden for removed rows)
            var editButton = new Button
            {
                Content = buttonText,
                FontSize = 11,
                Padding = new Thickness(6, 2),
                MinWidth = 28,
                MinHeight = 0,
                Background = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(2),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                IsVisible = !rowVm.IsRemoved  // Hide for removed rows
            };
            ToolTip.SetTip(editButton, tooltipText);

            editButton.Click += (s, e) =>
            {
                Console.WriteLine($"[CrossRef] Edit button clicked for {attributeName}");
                var currentValue = rowVm[attributeName] ?? "";
                var availableIds = vm.GetAvailableIds(attributeName).ToList();
                Console.WriteLine($"[CrossRef] Current value: {currentValue}, Available IDs: {availableIds.Count}");

                // Get the local key (item ID) for updating the source file
                var localKeyField = fieldDef.CrossReference?.LocalKeyField ?? "id";
                var localKey = rowVm[localKeyField] ?? "";

                // Custom title based on field type - include entry ID and name for context
                var isAbilityField = fieldDef.CrossReference?.TargetFile == "tor_abilitytemplates.xml";
                var entryId = rowVm["id"] ?? "";
                var entryName = rowVm["name"] ?? "";
                var entryContext = !string.IsNullOrEmpty(entryName) ? $"{entryName} ({entryId})" : entryId;
                var dialogTitle = $"Edit {fieldDisplayName} - {entryContext}";

                // Get level for skill template default calculation (only for skill_template field)
                int? troopLevel = null;
                if (attributeName.Equals("skill_template", StringComparison.OrdinalIgnoreCase))
                {
                    var levelStr = rowVm["level"] ?? "1";
                    if (int.TryParse(levelStr, out var level))
                        troopLevel = level;
                }

                // Use specialized dialog for abilities, generic for others
                if (isAbilityField)
                {
                    var parentWindow = TopLevel.GetTopLevel(editButton) as Window;
                    if (parentWindow != null)
                    {
                        var abilityDialog = new AbilityEditorDialog(currentValue, availableIds, vm);
                        var dialogResult = abilityDialog.ShowDialog(parentWindow);
                        if (dialogResult != null && dialogResult != currentValue)
                        {
                            var success = vm.UpdateCrossReferenceValue(attributeName, localKey, dialogResult);
                            if (success)
                            {
                                rowVm[attributeName] = dialogResult;
                                Console.WriteLine($"[CrossRef] Updated abilities to: {dialogResult}");
                                RebuildLinks();
                            }
                        }
                    }
                }
                else
                {
                    ShowTraitEditorPopup(editButton, currentValue, availableIds, dialogTitle, prefixToStrip, troopLevel, isSingleValue, null, (result) =>
                    {
                        if (result != null && result != currentValue)
                        {
                            // Check if this is a direct cross-reference (no sourceFile) or indirect (with sourceFile)
                            var isDirectCrossRef = string.IsNullOrEmpty(fieldDef.CrossReference?.SourceFile);

                            if (isDirectCrossRef)
                            {
                                // Direct cross-reference: value is stored on the entry itself (or linked file)
                                // Update both the ViewModel and the underlying XmlEntry
                                var valueToStore = result;
                                // Add prefix if configured (e.g., "Faction." for clan)
                                var prefixToAdd = fieldDef.CrossReference?.PrefixToAdd;
                                if (!string.IsNullOrEmpty(prefixToAdd) && !string.IsNullOrEmpty(result) && !result.StartsWith(prefixToAdd, StringComparison.OrdinalIgnoreCase))
                                {
                                    valueToStore = prefixToAdd + result;
                                }
                                // Update ViewModel
                                rowVm[attributeName] = valueToStore;
                                // Update underlying XmlEntry so it gets saved correctly
                                // Check if this is a nested field (like EquipmentRosterId with nestedPath)
                                if (fieldDef.Nested && !string.IsNullOrEmpty(fieldDef.NestedPath))
                                {
                                    rowVm.XmlEntry.SetNestedValue(fieldDef.NestedPath, valueToStore);
                                    Console.WriteLine($"[CrossRef] Updated nested crossref {attributeName} at {fieldDef.NestedPath} to: {valueToStore}");
                                }
                                else
                                {
                                    rowVm.XmlEntry.SetAttributeValue(attributeName, valueToStore);
                                    Console.WriteLine($"[CrossRef] Updated direct crossref {attributeName} to: {valueToStore}");
                                }
                                RebuildLinks();
                            }
                            else
                            {
                                // Indirect cross-reference: update the source file (e.g., tor_extendedunitproperties.xml)
                                var success = vm.UpdateCrossReferenceValue(attributeName, localKey, result);
                                if (success)
                                {
                                    rowVm[attributeName] = result;
                                    Console.WriteLine($"[CrossRef] Updated indirect crossref {attributeName} to: {result}");
                                    RebuildLinks();
                                }
                                else
                                {
                                    Console.WriteLine($"[CrossRef] Failed to update cross-reference");
                                }
                            }
                        }
                    });
                }
            };

            // Add edit button first, then links panel (button appears before attributes)
            panel.Children.Add(editButton);
            panel.Children.Add(linksPanel);
            border.Child = panel;

            return border;
        });
    }

    /// <summary>
    /// Shows a dialog window for editing traits/skills with autocomplete support.
    /// </summary>
    private static void ShowTraitEditorPopup(Control anchor, string currentValue, List<string> availableIds, string title, string? prefixToStrip, int? troopLevel, bool isSingleValue, FileTabViewModel? vmForIcons, Action<string?> onComplete)
    {
        Console.WriteLine("[TraitEditor] Creating dialog...");

        var topLevel = TopLevel.GetTopLevel(anchor);
        if (topLevel == null)
        {
            Console.WriteLine("[TraitEditor] ERROR: Could not find TopLevel");
            onComplete(null);
            return;
        }

        // Single value fields (clan, skill_template, equipment set) vs multi-value (traits, abilities)
        var labelText = isSingleValue ? "Value:" : "Values (comma-separated):";

        // For skill template fields, calculate the default skill set based on troop level
        var hasDefaultSkillSet = troopLevel.HasValue;
        var defaultSkillSet = hasDefaultSkillSet ? $"tor_skills_level{troopLevel.Value}" : null;

        // Check if current value matches the default (use default toggle should be ON)
        var currentWithoutPrefix = currentValue;
        if (!string.IsNullOrEmpty(prefixToStrip) && currentValue.StartsWith(prefixToStrip, StringComparison.OrdinalIgnoreCase))
        {
            currentWithoutPrefix = currentValue.Substring(prefixToStrip.Length);
        }
        var isUsingDefault = hasDefaultSkillSet && (string.IsNullOrEmpty(currentValue) || currentWithoutPrefix == defaultSkillSet);

        // Create a dialog window
        var dialog = new Window
        {
            Title = title,
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

        // For skill editor, add "Use Default" toggle
        CheckBox? useDefaultCheckBox = null;
        TextBox? textBox = null;
        StackPanel? customSection = null;

        if (hasDefaultSkillSet && defaultSkillSet != null)
        {
            useDefaultCheckBox = new CheckBox
            {
                Content = $"Use default for level {troopLevel} (tor_skills_level{troopLevel})",
                IsChecked = isUsingDefault,
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(useDefaultCheckBox);

            // Custom skill set section (hidden when using default)
            customSection = new StackPanel { Spacing = 8, IsVisible = !isUsingDefault };

            var label = new TextBlock
            {
                Text = "Custom Skill Set:",
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            customSection.Children.Add(label);

            // Strip prefix from current value for display
            var displayValue = isUsingDefault ? "" : currentWithoutPrefix;

            textBox = new TextBox
            {
                Text = displayValue,
                PlaceholderText = "Enter skill set ID...",
                Height = 30,
                AcceptsReturn = false,
                TextWrapping = TextWrapping.NoWrap
            };
            customSection.Children.Add(textBox);

            // Autocomplete section
            var acLabel = new TextBlock
            {
                Text = "Available skill sets (double-click to select):",
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 12, 0, 4)
            };
            customSection.Children.Add(acLabel);

            var searchBox = new TextBox
            {
                PlaceholderText = "Type to filter..."
            };
            customSection.Children.Add(searchBox);

            var listBox = new ListBox
            {
                Height = 180,
                ItemsSource = availableIds.Take(50).ToList()
            };
            customSection.Children.Add(listBox);

            // Filter suggestions
            searchBox.TextChanged += (s, e) =>
            {
                var searchText = searchBox.Text ?? "";
                var filtered = availableIds
                    .Where(id => string.IsNullOrEmpty(searchText) ||
                                 id.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    .Take(50)
                    .ToList();
                listBox.ItemsSource = filtered;
            };

            // Double-click to select - also turns off the default toggle
            listBox.DoubleTapped += (s, e) =>
            {
                if (listBox.SelectedItem is string selected)
                {
                    textBox.Text = selected;
                    useDefaultCheckBox.IsChecked = false;
                    customSection.IsVisible = true;
                    Console.WriteLine($"[TraitEditor] Selected skill set: {selected}");
                    searchBox.Text = "";
                }
            };

            // Toggle handler
            useDefaultCheckBox.IsCheckedChanged += (s, e) =>
            {
                var useDefault = useDefaultCheckBox.IsChecked == true;
                customSection.IsVisible = !useDefault;
                if (useDefault)
                {
                    textBox.Text = "";
                }
            };

            stack.Children.Add(customSection);
        }
        else
        {
            // Regular trait editor (original code)
            var label = new TextBlock
            {
                Text = labelText,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            stack.Children.Add(label);

            // Strip prefix from current value for display
            var displayValue = currentValue;
            if (!string.IsNullOrEmpty(prefixToStrip) && displayValue.StartsWith(prefixToStrip, StringComparison.OrdinalIgnoreCase))
            {
                displayValue = displayValue.Substring(prefixToStrip.Length);
            }

            textBox = new TextBox
            {
                Text = displayValue,
                PlaceholderText = isSingleValue ? "Select a value..." : "Enter values...",
                Height = isSingleValue ? 32 : 60,
                AcceptsReturn = !isSingleValue,
                TextWrapping = isSingleValue ? TextWrapping.NoWrap : TextWrapping.Wrap
            };
            stack.Children.Add(textBox);

            // Autocomplete section
            var acLabel = new TextBlock
            {
                Text = isSingleValue ? "Available options (double-click to select):" : "Available values (double-click to add):",
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 12, 0, 4)
            };
            stack.Children.Add(acLabel);

            var searchBox = new TextBox
            {
                PlaceholderText = "Type to filter..."
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

            // Double-click to add (or replace for single-value mode)
            listBox.DoubleTapped += (s, e) =>
            {
                if (listBox.SelectedItem is string selected)
                {
                    if (isSingleValue)
                    {
                        // Single-value mode: replace the current value
                        textBox.Text = selected;
                        Console.WriteLine($"[TraitEditor] Selected: {selected}");
                    }
                    else
                    {
                        // Multi-value mode: append to existing values
                        var current = textBox.Text?.Trim() ?? "";
                        if (string.IsNullOrEmpty(current))
                            textBox.Text = selected;
                        else
                            textBox.Text = current + ", " + selected;
                        Console.WriteLine($"[TraitEditor] Added trait: {selected}");
                    }
                    searchBox.Text = "";
                }
            };
        }

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

                // Check if using default skill set
                if (useDefaultCheckBox?.IsChecked == true && defaultSkillSet != null)
                {
                    result = defaultSkillSet;
                    Console.WriteLine($"[SkillTemplate] Setting default: {result} (level={troopLevel})");
                }
                else
                {
                    result = textBox?.Text?.Trim() ?? "";

                    // In single-value mode, only take the first value if multiple were entered
                    if (isSingleValue && result.Contains(','))
                    {
                        result = result.Split(',')[0].Trim();
                        Console.WriteLine($"[TraitEditor] Single-value mode: trimmed to first value: {result}");
                    }
                }

                // Add prefix back for skill template if needed
                if (!string.IsNullOrEmpty(prefixToStrip) && !string.IsNullOrEmpty(result))
                {
                    // Only add prefix if not already present
                    if (!result.StartsWith(prefixToStrip, StringComparison.OrdinalIgnoreCase))
                    {
                        result = prefixToStrip + result;
                    }
                }

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

        textBox?.Focus();
    }

    /// <summary>
    /// Creates a display template for enum cells (shows current value as text with validation).
    /// Uses AXAML styles via pseudo-classes (defined in CellStyles.axaml).
    /// </summary>
    private static IDataTemplate CreateEnumCellTemplate(string attributeName, FieldDefinition fieldDef, FileTabViewModel vm)
    {
        // Build lookup for value -> displayName and value -> icon
        var displayNameLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var iconLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var enumValue in fieldDef.EnumValues ?? [])
        {
            displayNameLookup[enumValue.Value] = enumValue.DisplayName ?? enumValue.Value;
            if (!string.IsNullOrEmpty(enumValue.Icon))
            {
                iconLookup[enumValue.Value] = enumValue.Icon;
            }
        }

        // Helper to get display name for a value
        string GetDisplayName(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return displayNameLookup.TryGetValue(value, out var displayName) ? displayName : value;
        }

        // Helper to get icon path for a value
        string? GetIconPath(string? value)
        {
            if (string.IsNullOrEmpty(value) || vm.IconService == null) return null;
            if (iconLookup.TryGetValue(value, out var iconName))
            {
                return vm.IconService.GetIconPath(iconName);
            }
            return null;
        }

        return new FuncDataTemplate<EntryRowViewModel>((rowVm, _) =>
        {
            var border = new Border();
            border.Classes.Add("dataCell");

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
            };

            // Optional value icon (resource icon, etc.)
            var valueIcon = new Image
            {
                Width = 18,
                Height = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                IsVisible = false
            };
            Grid.SetColumn(valueIcon, 0);
            grid.Children.Add(valueIcon);

            var text = new TextBlock();
            text.Classes.Add("cellText");
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            // Warning/error icon (styled via pseudo-classes)
            var icon = new TextBlock();
            icon.Classes.Add("cellIcon");
            Grid.SetColumn(icon, 2);
            grid.Children.Add(icon);

            // Helper to update icon visibility
            void UpdateValueIcon(string? value)
            {
                var iconPath = GetIconPath(value);
                if (!string.IsNullOrEmpty(iconPath) && System.IO.File.Exists(iconPath))
                {
                    try
                    {
                        valueIcon.Source = new Bitmap(iconPath);
                        valueIcon.IsVisible = true;
                    }
                    catch
                    {
                        valueIcon.IsVisible = false;
                    }
                }
                else
                {
                    valueIcon.IsVisible = false;
                }
            }

            if (rowVm != null)
            {
                // Equipment set: hide roster-level fields (culture) for non-first variations
                var isRosterLevelField = attributeName == "culture";
                if (isRosterLevelField && rowVm.IsEquipmentSetVariation && !rowVm.IsFirstVariation)
                {
                    text.Text = "";
                    valueIcon.IsVisible = false;
                }
                else
                {
                    var currentValue = rowVm[attributeName];
                    // Show display name instead of raw value
                    text.Text = GetDisplayName(currentValue);
                    UpdateValueIcon(currentValue);
                    if (isRosterLevelField && rowVm.IsEquipmentSetVariation && rowVm.IsFirstVariation)
                    {
                        text.FontWeight = FontWeight.SemiBold;
                    }
                }

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
                    if (isRosterLevelField && rowVm.IsEquipmentSetVariation && !rowVm.IsFirstVariation)
                    {
                        text.Text = "";
                        valueIcon.IsVisible = false;
                    }
                    else
                    {
                        var newValue = rowVm[attributeName];
                        text.Text = GetDisplayName(newValue);
                        UpdateValueIcon(newValue);
                    }
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
    private static IDataTemplate CreateEnumEditingTemplate(string attributeName, FieldDefinition fieldDef, FileTabViewModel? vm = null)
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
                object content;

                // Check if enum value has an icon
                if (!string.IsNullOrEmpty(enumValue.Icon) && vm?.IconService != null)
                {
                    var iconPath = vm.IconService.GetIconPath(enumValue.Icon);
                    if (!string.IsNullOrEmpty(iconPath) && System.IO.File.Exists(iconPath))
                    {
                        try
                        {
                            var panel = new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 6
                            };

                            var icon = new Image
                            {
                                Width = 20,
                                Height = 20,
                                Source = new Bitmap(iconPath),
                                VerticalAlignment = VerticalAlignment.Center
                            };
                            panel.Children.Add(icon);

                            var text = new TextBlock
                            {
                                Text = enumValue.DisplayName ?? enumValue.Value,
                                VerticalAlignment = VerticalAlignment.Center
                            };
                            panel.Children.Add(text);

                            content = panel;
                        }
                        catch
                        {
                            // Fall back to text-only if icon loading fails
                            content = enumValue.DisplayName ?? enumValue.Value;
                        }
                    }
                    else
                    {
                        content = enumValue.DisplayName ?? enumValue.Value;
                    }
                }
                else
                {
                    content = enumValue.DisplayName ?? enumValue.Value;
                }

                var item = new ComboBoxItem
                {
                    Content = content,
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
                // Disable editing for removed rows or roster-level fields on non-first variations
                var isRosterLevelField = attributeName == "culture";
                var isNonFirstVariation = isRosterLevelField && rowVm.IsEquipmentSetVariation && !rowVm.IsFirstVariation;

                if (rowVm.IsRemoved || isNonFirstVariation)
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
    /// Creates a display template for external enum values (crossReference with valueType="enum").
    /// Similar to regular enum template but for values loaded from external files.
    /// </summary>
    private static IDataTemplate CreateExternalEnumCellTemplate(string attributeName, FieldDefinition fieldDef, FileTabViewModel vm)
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
            if (string.IsNullOrEmpty(value)) return "-";
            return displayNameLookup.TryGetValue(value, out var displayName) ? displayName : value;
        }

        return new FuncDataTemplate<EntryRowViewModel>((rowVm, _) =>
        {
            var border = new Border();
            border.Classes.Add("dataCell");

            var text = new TextBlock();
            text.Classes.Add("cellText");

            if (rowVm != null)
            {
                text.Text = GetDisplayName(rowVm[attributeName]);

                // Subscribe to refresh event for updates
                vm.CellRefreshRequested += (s, args) =>
                {
                    text.Text = GetDisplayName(rowVm[attributeName]);
                };
            }

            border.Child = text;
            return border;
        });
    }

    /// <summary>
    /// Creates a display template for external simple values (crossReference with valueType="int" or "string").
    /// </summary>
    private static IDataTemplate CreateExternalValueCellTemplate(string attributeName, FieldDefinition fieldDef, FileTabViewModel vm)
    {
        return new FuncDataTemplate<EntryRowViewModel>((rowVm, _) =>
        {
            var border = new Border();
            border.Classes.Add("dataCell");

            var text = new TextBlock();
            text.Classes.Add("cellText");

            if (rowVm != null)
            {
                var value = rowVm[attributeName];
                text.Text = string.IsNullOrEmpty(value) ? "-" : value;

                // Subscribe to refresh event for updates
                vm.CellRefreshRequested += (s, args) =>
                {
                    var newValue = rowVm[attributeName];
                    text.Text = string.IsNullOrEmpty(newValue) ? "-" : newValue;
                };
            }

            border.Child = text;
            return border;
        });
    }

    /// <summary>
    /// Creates a display template for cross-reference dropdown fields (renderAs="dropdown").
    /// Shows the selected value with prefix stripped for clean display.
    /// </summary>
    private static IDataTemplate CreateCrossRefDropdownTemplate(string attributeName, FieldDefinition fieldDef, FileTabViewModel vm)
    {
        var prefixToStrip = fieldDef.CrossReference?.PrefixToStrip;

        return new FuncDataTemplate<EntryRowViewModel>((rowVm, _) =>
        {
            var border = new Border();
            border.Classes.Add("dataCell");

            var text = new TextBlock();
            text.Classes.Add("cellText");

            if (rowVm != null)
            {
                var value = rowVm[attributeName];
                var displayValue = GetDisplayValue(value, prefixToStrip, attributeName, vm);

                text.Text = string.IsNullOrEmpty(displayValue) ? "" : displayValue;

                // Subscribe to refresh event for updates
                vm.CellRefreshRequested += (s, args) =>
                {
                    var newValue = rowVm[attributeName];
                    var newDisplayValue = GetDisplayValue(newValue, prefixToStrip, attributeName, vm);

                    text.Text = string.IsNullOrEmpty(newDisplayValue) ? "" : newDisplayValue;

                    // Update cell state
                    CellStyleHelper.UpdateCellState(border, rowVm, attributeName, vm);
                };
            }

            border.Child = text;

            // Apply initial cell state
            if (rowVm != null)
            {
                CellStyleHelper.UpdateCellState(border, rowVm, attributeName, vm);
            }

            return border;
        });
    }

    /// <summary>
    /// Gets the display value for a cross-reference field, using display names if available.
    /// </summary>
    private static string GetDisplayValue(string? value, string? prefixToStrip, string attributeName, FileTabViewModel vm)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        // Strip prefix to get the ID
        var id = value;
        if (!string.IsNullOrEmpty(prefixToStrip) && value.StartsWith(prefixToStrip, StringComparison.OrdinalIgnoreCase))
        {
            id = value.Substring(prefixToStrip.Length);
        }

        // Try to get display name, fall back to ID
        return vm.GetDisplayName(attributeName, id);
    }

    /// <summary>
    /// Creates an editing template for cross-reference dropdown fields (renderAs="dropdown").
    /// Simple ComboBox populated from GetAvailableIds with prefix handling.
    /// </summary>
    private static IDataTemplate CreateCrossRefDropdownEditingTemplate(string attributeName, FieldDefinition fieldDef, FileTabViewModel vm)
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

            if (rowVm == null) return comboBox;

            var prefixToStrip = fieldDef.CrossReference?.PrefixToStrip;
            var prefixToAdd = fieldDef.CrossReference?.PrefixToAdd;

            // Get available IDs from cross-reference
            var availableIds = vm.GetAvailableIds(attributeName).ToList();
            var displayNames = vm.GetDisplayNames(attributeName);

            // Create items from available IDs
            var items = new List<ComboBoxItem>();

            // Add empty option
            items.Add(new ComboBoxItem { Content = "", Tag = "" });

            foreach (var id in availableIds)
            {
                // Use display name if available, otherwise use ID
                var displayText = id;
                if (displayNames != null && displayNames.TryGetValue(id, out var name))
                {
                    displayText = name;
                }

                var item = new ComboBoxItem
                {
                    Content = displayText,
                    Tag = id  // Always store the ID for saving
                };
                items.Add(item);
            }

            comboBox.ItemsSource = items;

            // Get current value and strip prefix for selection
            var currentValue = rowVm[attributeName];
            var currentDisplayValue = currentValue;

            if (!string.IsNullOrEmpty(prefixToStrip) && !string.IsNullOrEmpty(currentDisplayValue) &&
                currentDisplayValue.StartsWith(prefixToStrip, StringComparison.OrdinalIgnoreCase))
            {
                currentDisplayValue = currentDisplayValue.Substring(prefixToStrip.Length);
            }

            // Select current value
            var selectedItem = items.FirstOrDefault(i => i.Tag?.ToString() == currentDisplayValue);
            comboBox.SelectedItem = selectedItem;

            // Handle selection change
            comboBox.SelectionChanged += (s, e) =>
            {
                if (comboBox.SelectedItem is ComboBoxItem selectedComboItem)
                {
                    var newValue = selectedComboItem.Tag?.ToString() ?? "";

                    // Add prefix when saving
                    if (!string.IsNullOrEmpty(newValue) && !string.IsNullOrEmpty(prefixToAdd))
                    {
                        newValue = prefixToAdd + newValue;
                    }

                    rowVm[attributeName] = newValue;
                }
            };

            return comboBox;
        });
    }

    /// <summary>
    /// Creates a text cell template that handles validation and dynamic styling.
    /// Uses AXAML styles via pseudo-classes (defined in CellStyles.axaml).
    /// </summary>
    private IDataTemplate CreateTextCellTemplate(string attributeName, FieldDefinition? fieldDef, FileTabViewModel vm)
    {
        // Get prefix to strip for display
        var prefixToStrip = fieldDef?.PrefixToStrip;

        // Helper to strip prefix from value for display
        string StripPrefix(string? value)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(prefixToStrip))
                return value ?? "";
            if (value.StartsWith(prefixToStrip, StringComparison.OrdinalIgnoreCase))
                return value.Substring(prefixToStrip.Length);
            return value;
        }

        return new FuncDataTemplate<EntryRowViewModel>((rowVm, _) =>
        {
            var border = new Border();
            border.Classes.Add("dataCell");

            // Main container grid for content + fill handle
            var outerGrid = new Grid();

            var contentGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto")
            };

            var text = new TextBlock();
            text.Classes.Add("cellText");
            Grid.SetColumn(text, 0);
            contentGrid.Children.Add(text);

            // Warning/error icon (styled via pseudo-classes)
            var icon = new TextBlock();
            icon.Classes.Add("cellIcon");
            Grid.SetColumn(icon, 1);
            contentGrid.Children.Add(icon);

            outerGrid.Children.Add(contentGrid);

            // Fill handle - small square at bottom-right corner
            var fillHandle = new Border
            {
                Width = 8,
                Height = 8,
                Background = new SolidColorBrush(Color.FromRgb(88, 101, 242)), // Discord blurple
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 1, 1),
                IsVisible = false, // Hidden by default
                Cursor = new Avalonia.Input.Cursor(StandardCursorType.Cross)
            };
            outerGrid.Children.Add(fillHandle);

            if (rowVm != null)
            {
                // Equipment set visual grouping for roster-level fields (id, culture)
                var isRosterLevelField = attributeName == "id" || attributeName == "culture";
                if (isRosterLevelField && rowVm.IsEquipmentSetVariation)
                {
                    if (rowVm.IsFirstVariation)
                    {
                        // First variation: show full value with bold styling
                        text.Text = StripPrefix(rowVm[attributeName]);
                        text.FontWeight = FontWeight.SemiBold;
                    }
                    else
                    {
                        // Subsequent variations: show indent marker for ID, empty for culture
                        if (attributeName == "id")
                        {
                            text.Text = "  └─";
                            text.Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128));
                        }
                        else
                        {
                            text.Text = "";
                        }
                    }
                }
                else
                {
                    // Normal text display
                    text.Text = StripPrefix(rowVm[attributeName]);
                }

                // Read-only fields: grey out and make non-interactive
                if (fieldDef?.ReadOnly == true)
                {
                    text.Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128));
                    text.IsHitTestVisible = false;
                    border.IsHitTestVisible = false;
                }

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

                // Show fill handle on hover (like Excel)
                border.PointerEntered += (s, e) =>
                {
                    if (!rowVm.IsRemoved)
                    {
                        fillHandle.IsVisible = true;
                    }
                };

                border.PointerExited += (s, e) =>
                {
                    // Don't hide if we're dragging from this handle
                    if (!_isFillDragging)
                    {
                        fillHandle.IsVisible = false;
                    }
                };

                // Fill handle pointer events
                fillHandle.PointerPressed += (s, e) =>
                {
                    _isFillDragging = true;
                    _fillStartRowIndex = rowVm.RowNumber - 1;
                    _fillEndRowIndex = _fillStartRowIndex;
                    _fillColumnName = attributeName;
                    _fillValue = rowVm[attributeName];
                    _activeFillHandle = fillHandle;
                    // Store Y position relative to the fill handle for accurate tracking
                    _fillStartY = e.GetPosition(fillHandle).Y;
                    e.Pointer.Capture(fillHandle);
                    e.Handled = true;
                };

                fillHandle.PointerMoved += (s, e) =>
                {
                    if (_isFillDragging)
                    {
                        // Calculate row offset based on Y distance moved
                        // Use position relative to the captured fill handle for consistency
                        var currentY = e.GetPosition(fillHandle).Y;
                        var deltaY = currentY - _fillStartY;

                        // Row height is approximately 28px (standard DataGrid row)
                        const double rowHeight = 28.0;
                        var rowOffset = (int)Math.Round(deltaY / rowHeight);

                        // Calculate target row index (allow dragging both up and down)
                        var targetRow = _fillStartRowIndex + rowOffset;

                        // Always update to current position, clamped to valid range
                        // This ensures dragging back up reduces the fill range
                        _fillEndRowIndex = Math.Clamp(targetRow, 0, vm.DisplayRows.Count - 1);
                        e.Handled = true;
                    }
                };

                fillHandle.PointerReleased += (s, e) =>
                {
                    if (_isFillDragging && _fillColumnName == attributeName)
                    {
                        e.Pointer.Capture(null);
                        ApplyFillDown(vm);
                        _isFillDragging = false;
                        _fillStartRowIndex = -1;
                        _fillEndRowIndex = -1;
                        _fillColumnName = null;
                        _fillValue = null;
                        _activeFillHandle = null;
                    }
                    e.Handled = true;
                };

                // Subscribe to centralized refresh event for all updates
                vm.CellRefreshRequested += (s, args) =>
                {
                    // Re-read value from row and update text (with prefix stripped)
                    if (isRosterLevelField && rowVm.IsEquipmentSetVariation && !rowVm.IsFirstVariation)
                    {
                        // Keep indent marker for ID, empty for culture on non-first variations
                        text.Text = attributeName == "id" ? "  └─" : "";
                    }
                    else
                    {
                        text.Text = StripPrefix(rowVm[attributeName]);
                    }
                    // Update styling
                    CellStyleHelper.UpdateCellState(border, rowVm, attributeName, vm);
                    // Hide fill handle if row is removed
                    if (rowVm.IsRemoved)
                    {
                        fillHandle.IsVisible = false;
                    }
                };
            }

            border.Child = outerGrid;
            return border;
        });
    }

    /// <summary>
    /// Gets the row index at the given position within the DataGrid using hit testing.
    /// </summary>
    private int GetRowIndexFromPosition(DataGrid grid, double y, FileTabViewModel vm)
    {
        // Use hit testing to find the row under the pointer
        var point = new Point(50, y); // Use middle X, actual Y

        // Find the visual at this point
        var visual = grid.InputHitTest(point) as Visual;

        if (visual != null)
        {
            // Walk up the visual tree to find the DataGridRow
            var current = visual;
            while (current != null)
            {
                if (current is DataGridRow row && row.DataContext is EntryRowViewModel rowVm)
                {
                    return rowVm.RowNumber - 1; // RowNumber is 1-based
                }
                current = current.GetVisualParent();
            }
        }

        // Fallback: keep current position if we can't find a row
        return _fillEndRowIndex >= 0 ? _fillEndRowIndex : _fillStartRowIndex;
    }

    /// <summary>
    /// Applies fill-down from the start row to the end row.
    /// </summary>
    private void ApplyFillDown(FileTabViewModel vm)
    {
        if (_fillStartRowIndex < 0 || _fillEndRowIndex < 0 || string.IsNullOrEmpty(_fillColumnName))
            return;

        var rows = vm.DisplayRows;
        int startRow = Math.Min(_fillStartRowIndex, _fillEndRowIndex);
        int endRow = Math.Max(_fillStartRowIndex, _fillEndRowIndex);

        // Don't fill if it's the same row
        if (startRow == endRow)
            return;

        Console.WriteLine($"[FillDown] Filling {_fillColumnName} from row {startRow} to {endRow} with value '{_fillValue}'");

        // Skip the source row (startRow if dragging down, endRow if dragging up)
        // We only need to fill rows AFTER the source
        int sourceRow = _fillStartRowIndex;
        int fillCount = 0;

        for (int i = startRow; i <= endRow; i++)
        {
            // Skip the source row - it already has the value
            if (i == sourceRow)
                continue;

            if (i >= 0 && i < rows.Count)
            {
                var row = rows[i];
                if (!row.IsRemoved)
                {
                    // Set the UI value (triggers CellValueChanged if value changed)
                    row[_fillColumnName] = _fillValue;

                    // Also directly sync to XmlEntry to ensure it's updated
                    // This handles cases where UI value was already equal but XmlEntry wasn't updated
                    row.XmlEntry.SetAttributeValue(_fillColumnName, _fillValue);

                    // Verify the value was set
                    if (fillCount < 3)
                    {
                        var verifyValue = row.XmlEntry.GetAttributeValue(_fillColumnName);
                        Console.WriteLine($"[FillDown] Set {row.XmlEntry.Id}.{_fillColumnName} = '{_fillValue}', verify: '{verifyValue}'");
                    }
                    fillCount++;
                }
            }
        }

        Console.WriteLine($"[FillDown] Updated {fillCount} rows");
        vm.MarkAsModified();
        vm.RequestCellRefresh();
    }

    /// <summary>
    /// Creates a multiline text cell template with an edit button.
    /// </summary>
    private static IDataTemplate CreateMultilineTextCellTemplate(string attributeName, FieldDefinition? fieldDef, FileTabViewModel vm)
    {
        return new FuncDataTemplate<EntryRowViewModel>((rowVm, _) =>
        {
            var border = new Border();
            border.Classes.Add("dataCell");

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*")
            };

            // Edit button (on the left)
            var editButton = new Button
            {
                Content = "...",
                Width = 24,
                Height = 24,
                Padding = new Avalonia.Thickness(2),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Avalonia.Thickness(2)
            };
            ToolTip.SetTip(editButton, "Edit text");
            Grid.SetColumn(editButton, 0);
            grid.Children.Add(editButton);

            // Text display (truncated)
            var text = new TextBlock
            {
                Text = rowVm?[attributeName] ?? "",
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(4, 0, 4, 0)
            };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            // Handle click
            var isReadOnly = fieldDef?.ReadOnly ?? false;
            editButton.Click += async (sender, e) =>
            {
                if (rowVm == null) return;

                var currentValue = rowVm[attributeName] ?? "";
                var dialog = new TextEditorDialog(fieldDef?.DisplayName ?? attributeName, currentValue, isReadOnly);
                var parentWindow = TopLevel.GetTopLevel(editButton) as Window;
                if (parentWindow == null) return;

                var result = await dialog.ShowDialog<string?>(parentWindow);

                if (!isReadOnly && result != null && result != currentValue)
                {
                    rowVm[attributeName] = result;
                    text.Text = result;
                    vm.HasUnsavedChanges = true;
                }
            };

            // Subscribe to refresh events
            if (rowVm != null)
            {
                vm.CellRefreshRequested += (s, args) =>
                {
                    text.Text = rowVm[attributeName] ?? "";
                };
            }

            border.Child = grid;
            return border;
        });
    }

    /// <summary>
    /// Creates a text editing template for validated text cells.
    /// Handles prefix stripping for display and adding when saving.
    /// </summary>
    private static IDataTemplate CreateTextEditingTemplate(string attributeName, FieldDefinition? fieldDef)
    {
        var prefixToStrip = fieldDef?.PrefixToStrip;
        var prefixToAdd = fieldDef?.PrefixToAdd;

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

                // If we have a prefix to handle, manage the value manually
                if (!string.IsNullOrEmpty(prefixToStrip))
                {
                    // Get raw value and strip prefix for display
                    var rawValue = rowVm[attributeName] ?? "";
                    if (rawValue.StartsWith(prefixToStrip, StringComparison.OrdinalIgnoreCase))
                    {
                        textBox.Text = rawValue.Substring(prefixToStrip.Length);
                    }
                    else
                    {
                        textBox.Text = rawValue;
                    }

                    // When editing ends, add prefix back
                    textBox.LostFocus += (s, e) =>
                    {
                        var newValue = textBox.Text ?? "";
                        if (!string.IsNullOrEmpty(newValue) && !string.IsNullOrEmpty(prefixToAdd))
                        {
                            // Add prefix if not already present
                            if (!newValue.StartsWith(prefixToAdd, StringComparison.OrdinalIgnoreCase))
                            {
                                newValue = prefixToAdd + newValue;
                            }
                        }
                        rowVm[attributeName] = newValue;
                    };
                }
                else
                {
                    // No prefix handling, use standard binding
                    textBox.Bind(TextBox.TextProperty, new Binding { Path = $"[{attributeName}]", Mode = BindingMode.TwoWay });
                }
            }

            return textBox;
        });
    }

    /// <summary>
    /// Creates a cell template for tuple list fields (e.g., DamageProportions, Resistances, Amplifiers).
    /// Displays a summary like "Physical: 100%, Fire: 50%" with an edit button to open popup.
    /// </summary>
    private static IDataTemplate CreateTupleListCellTemplate(string attributeName, FieldDefinition fieldDef, FileTabViewModel vm)
    {
        return new FuncDataTemplate<EntryRowViewModel>((rowVm, _) =>
        {
            var border = new Border();
            border.Classes.Add("dataCell");

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*")
            };

            // Edit button on the left
            var editButton = new Button
            {
                Content = "...",
                Padding = new Thickness(6, 2),
                MinWidth = 28,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
                Foreground = Brushes.White
            };
            Grid.SetColumn(editButton, 0);
            grid.Children.Add(editButton);

            var text = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            if (rowVm != null)
            {
                // Get the local key (usually "id") for looking up tuple data
                var localKeyField = fieldDef.TupleList!.LocalKeyField;
                var localKey = rowVm[localKeyField] ?? "";

                // Get formatted display text
                var displayText = vm.GetTupleDisplayText(attributeName, localKey);
                text.Text = displayText;

                // Set tooltip with full text if truncated
                if (!string.IsNullOrEmpty(displayText) && displayText != "-")
                {
                    ToolTip.SetTip(text, displayText);
                }

                // Edit button click handler - opens popup editor
                editButton.Click += async (s, e) =>
                {
                    var tuples = vm.GetTuples(attributeName, localKey);
                    var config = fieldDef.TupleList!;

                    // Create and show the tuple editor popup
                    var popup = new TupleEditorPopup(localKey, attributeName, tuples, config, vm);
                    var topLevel = TopLevel.GetTopLevel(editButton);
                    if (topLevel is Window window)
                    {
                        var result = await popup.ShowDialog<bool>(window);
                        if (result)
                        {
                            // Refresh the display text after edit
                            var newDisplayText = vm.GetTupleDisplayText(attributeName, localKey);
                            text.Text = newDisplayText;
                            if (!string.IsNullOrEmpty(newDisplayText) && newDisplayText != "-")
                            {
                                ToolTip.SetTip(text, newDisplayText);
                            }
                        }
                    }
                };
            }
            else
            {
                text.Text = "-";
                editButton.IsEnabled = false;
            }

            border.Child = grid;
            return border;
        });
    }

    /// <summary>
    /// Creates a cell template for tag list fields (e.g., conversation tags on strings).
    /// Displays tags as comma-separated text with an edit button to open TagEditorPopup.
    /// </summary>
    private static IDataTemplate CreateTagListCellTemplate(string attributeName, FieldDefinition fieldDef, FileTabViewModel vm)
    {
        return new FuncDataTemplate<EntryRowViewModel>((rowVm, _) =>
        {
            var border = new Border();
            border.Classes.Add("dataCell");

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*")
            };

            // Edit button on the left
            var editButton = new Button
            {
                Content = "...",
                Padding = new Thickness(6, 2),
                MinWidth = 28,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
                Foreground = Brushes.White,
                IsVisible = rowVm != null && !rowVm.IsRemoved
            };
            Grid.SetColumn(editButton, 0);
            grid.Children.Add(editButton);

            var text = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            if (rowVm != null)
            {
                // Get the entry ID for context
                var entryId = rowVm["id"] ?? "";

                // Get current tags value (comma-separated)
                var currentTags = rowVm[attributeName] ?? "";
                text.Text = string.IsNullOrEmpty(currentTags) ? "-" : currentTags;

                // Set tooltip
                if (!string.IsNullOrEmpty(currentTags))
                {
                    ToolTip.SetTip(text, currentTags);
                }

                // Edit button click handler - opens TagEditorPopup
                editButton.Click += async (s, e) =>
                {
                    var currentValue = rowVm[attributeName] ?? "";

                    // Load available tag definitions
                    var availableTags = LoadTagDefinitions(fieldDef, vm);

                    // Create and show the tag editor popup
                    var popup = new TagEditorPopup(entryId, attributeName, currentValue, availableTags, vm);
                    var topLevel = TopLevel.GetTopLevel(editButton);
                    if (topLevel is Window window)
                    {
                        var result = await popup.ShowDialog<bool>(window);
                        if (result)
                        {
                            // Refresh the display text after edit
                            var newValue = rowVm[attributeName] ?? "";
                            text.Text = string.IsNullOrEmpty(newValue) ? "-" : newValue;
                            if (!string.IsNullOrEmpty(newValue))
                            {
                                ToolTip.SetTip(text, newValue);
                            }
                        }
                    }
                };

                // Subscribe to cell refresh events
                vm.CellRefreshRequested += (s, args) =>
                {
                    var newValue = rowVm[attributeName] ?? "";
                    text.Text = string.IsNullOrEmpty(newValue) ? "-" : newValue;
                    if (!string.IsNullOrEmpty(newValue))
                    {
                        ToolTip.SetTip(text, newValue);
                    }
                    // Update visibility for removed rows
                    editButton.IsVisible = !rowVm.IsRemoved;
                };
            }
            else
            {
                text.Text = "-";
                editButton.IsEnabled = false;
            }

            border.Child = grid;
            return border;
        });
    }

    /// <summary>
    /// Loads tag definitions from tor_tags.xml or schema's knownTags.
    /// </summary>
    private static List<TagDefinition> LoadTagDefinitions(FieldDefinition fieldDef, FileTabViewModel vm)
    {
        var tags = new List<TagDefinition>();

        // Try to load from tor_tags.xml in the data directory
        try
        {
            var dataDir = TORTools.Core.Services.FilePathResolver.GetDataDirectory();
            if (!string.IsNullOrEmpty(dataDir))
            {
                var tagsFilePath = Path.Combine(dataDir, "tor_tags.xml");
                if (File.Exists(tagsFilePath))
                {
                    var doc = System.Xml.Linq.XDocument.Load(tagsFilePath);
                    var tagElements = doc.Root?.Elements("Tag");
                    if (tagElements != null)
                    {
                        foreach (var element in tagElements)
                        {
                            var id = element.Attribute("id")?.Value;
                            if (!string.IsNullOrEmpty(id))
                            {
                                tags.Add(new TagDefinition
                                {
                                    Id = id,
                                    Category = element.Attribute("category")?.Value ?? "",
                                    Description = element.Attribute("description")?.Value ?? ""
                                });
                            }
                        }
                    }

                    if (tags.Count > 0)
                    {
                        Console.WriteLine($"[TagList] Loaded {tags.Count} tag definitions from tor_tags.xml");
                        return tags;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TagList] Error loading tor_tags.xml: {ex.Message}");
        }

        // Fallback: use knownTags from schema
        var knownTags = fieldDef.TagList?.KnownTags;
        if (knownTags != null && knownTags.Count > 0)
        {
            Console.WriteLine($"[TagList] Using {knownTags.Count} known tags from schema");
            foreach (var tagName in knownTags)
            {
                tags.Add(new TagDefinition
                {
                    Id = tagName,
                    Category = "",
                    Description = ""
                });
            }
        }

        return tags;
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
                FontSize = 11,
                Padding = new Thickness(6, 2),
                MinWidth = 28,
                MinHeight = 0,
                Background = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
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
    /// Creates a cell template for banner fields with thumbnail preview.
    /// Displays the banner image from the TOR_Armory asset sources based on the banner_key suffix.
    /// Shows image on top row (with primary color background), full banner_key on bottom row.
    /// </summary>
    private static IDataTemplate CreateBannerCellTemplate(string attributeName, FieldDefinition fieldDef, FileTabViewModel vm)
    {
        return new FuncDataTemplate<EntryRowViewModel>((rowVm, _) =>
        {
            var border = new Border();
            border.Classes.Add("dataCell");

            // Vertical layout: image on top, text below
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 2
            };

            if (rowVm == null)
            {
                border.Child = panel;
                return border;
            }

            // Image container with colored background
            var imageContainer = new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(2),
                ClipToBounds = true
            };

            // Banner thumbnail (centered inside container)
            var bannerImage = new Image
            {
                Width = 44,
                Height = 44,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            imageContainer.Child = bannerImage;

            // Full banner_key text below the image
            var bannerText = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(2, 0),
                FontSize = 9,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180))
            };

            // Helper to parse color from hex string (supports "FFRRGGBB" and "0xFFRRGGBB" formats)
            Color? ParseColorValue(string? colorValue)
            {
                if (string.IsNullOrEmpty(colorValue))
                    return null;

                // Remove "0x" prefix if present
                var hex = colorValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? colorValue.Substring(2)
                    : colorValue;

                // Try to parse as ARGB hex (AARRGGBB)
                if (hex.Length == 8 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var argb))
                {
                    return Color.FromUInt32(argb);
                }

                // Try to parse as RGB hex (RRGGBB)
                if (hex.Length == 6 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
                {
                    return Color.FromUInt32(0xFF000000 | rgb);
                }

                return null;
            }

            // Helper to update the banner display
            void UpdateBannerDisplay()
            {
                var bannerKey = rowVm[attributeName];

                // Show the full banner_key as the text
                bannerText.Text = bannerKey ?? "";

                // Set tooltip with full banner key for easy copying
                if (!string.IsNullOrEmpty(bannerKey))
                {
                    ToolTip.SetTip(panel, bannerKey);
                }

                // Try to get primary color for background
                // Check various color field names used across different schemas
                var primaryColorValue = rowVm["color"] ?? rowVm["primary_banner_color"];

                // If no direct color, try to inherit from parent kingdom via super_faction
                if (string.IsNullOrEmpty(primaryColorValue) && vm.FactionCatalogService != null)
                {
                    var superFaction = rowVm["super_faction"];
                    if (!string.IsNullOrEmpty(superFaction))
                    {
                        primaryColorValue = vm.FactionCatalogService.GetKingdomColor(superFaction);
                    }
                }

                var bgColor = ParseColorValue(primaryColorValue);
                if (bgColor.HasValue)
                {
                    imageContainer.Background = new SolidColorBrush(bgColor.Value);
                }
                else
                {
                    imageContainer.Background = new SolidColorBrush(Color.FromRgb(40, 40, 40));
                }

                // Extract suffix to load the image
                var suffix = FactionCatalogService.ExtractBannerImageName(bannerKey);

                // Try to load banner image
                if (!string.IsNullOrEmpty(suffix) && vm.BannerImageService != null)
                {
                    var bitmap = vm.BannerImageService.GetImageByName(suffix);
                    if (bitmap != null)
                    {
                        bannerImage.Source = bitmap;
                        bannerImage.IsVisible = true;
                    }
                    else
                    {
                        bannerImage.IsVisible = false;
                    }
                }
                else
                {
                    bannerImage.IsVisible = false;
                }

                CellStyleHelper.UpdateCellState(border, rowVm, attributeName, vm);
            }

            // Initial display
            UpdateBannerDisplay();

            // Subscribe to refresh events
            vm.CellRefreshRequested += (s, args) => UpdateBannerDisplay();

            // Edit button overlay on the image container
            var editButton = new Button
            {
                Content = "...",
                FontSize = 11,
                Padding = new Thickness(6, 2),
                MinWidth = 28,
                MinHeight = 0,
                Background = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(2),
                Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0),
                IsVisible = !rowVm.IsRemoved
            };
            ToolTip.SetTip(editButton, "Edit banner key");

            editButton.Click += (s, e) =>
            {
                ShowBannerEditorPopup(editButton, rowVm, attributeName, vm.BannerImageService, () =>
                {
                    Console.WriteLine($"[BannerEditor] Banner editor closed");
                    UpdateBannerDisplay();
                    vm.RequestCellRefresh();
                });
            };

            panel.Children.Add(imageContainer);
            panel.Children.Add(editButton);
            panel.Children.Add(bannerText);

            border.Child = panel;

            return border;
        });
    }

    /// <summary>
    /// Shows a dialog for editing a banner key and colors with visual preview.
    /// </summary>
    private static void ShowBannerEditorPopup(Control anchor, EntryRowViewModel rowVm, string bannerAttributeName, BannerImageService? bannerImageService, Action onComplete)
    {
        Console.WriteLine("[BannerEditor] Creating dialog...");

        var topLevel = TopLevel.GetTopLevel(anchor);
        if (topLevel == null)
        {
            Console.WriteLine("[BannerEditor] ERROR: Could not find TopLevel");
            onComplete();
            return;
        }

        // Helper to parse color
        Color ParseColorOrDefault(string? colorValue, Color defaultColor)
        {
            if (string.IsNullOrEmpty(colorValue))
                return defaultColor;

            var hex = colorValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? colorValue.Substring(2)
                : colorValue;

            if (hex.Length == 8 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var argb))
                return Color.FromUInt32(argb);

            if (hex.Length == 6 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
                return Color.FromUInt32(0xFF000000 | rgb);

            return defaultColor;
        }

        // Get current values
        var currentBannerKey = rowVm[bannerAttributeName] ?? "";
        var currentColor = rowVm["color"];
        var currentColor2 = rowVm["color2"];
        var currentAltColor = rowVm["alternative_color"];
        var currentAltColor2 = rowVm["alternative_color2"];

        // Also check for kingdom-style color fields
        var currentPrimaryBannerColor = rowVm["primary_banner_color"];
        var currentSecondaryBannerColor = rowVm["secondary_banner_color"];

        var defaultGray = Color.FromRgb(60, 60, 60);
        var bgColor = ParseColorOrDefault(currentColor ?? currentPrimaryBannerColor, defaultGray);

        // Create dialog
        var dialog = new Window
        {
            Title = "Edit Banner & Colors",
            Width = 450,
            Height = 580,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true,
            ShowInTaskbar = false,
            MinWidth = 350,
            MinHeight = 500
        };

        var mainBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            Padding = new Thickness(16)
        };

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var mainStack = new StackPanel { Spacing = 12 };

        // Banner preview section
        var previewLabel = new TextBlock
        {
            Text = "Banner Preview:",
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        mainStack.Children.Add(previewLabel);

        // Large banner preview with colored background
        var previewContainer = new Border
        {
            Width = 100,
            Height = 100,
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(bgColor),
            ClipToBounds = true
        };

        var previewImage = new Image
        {
            Width = 92,
            Height = 92,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Load initial image
        var initialSuffix = FactionCatalogService.ExtractBannerImageName(currentBannerKey);
        if (!string.IsNullOrEmpty(initialSuffix) && bannerImageService != null)
        {
            var bitmap = bannerImageService.GetImageByName(initialSuffix);
            if (bitmap != null)
            {
                previewImage.Source = bitmap;
            }
        }

        previewContainer.Child = previewImage;
        mainStack.Children.Add(previewContainer);

        // Banner key text field
        var keyLabel = new TextBlock
        {
            Text = "Banner Key:",
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 8, 0, 4)
        };
        mainStack.Children.Add(keyLabel);

        var keyTextBox = new TextBox
        {
            Text = currentBannerKey,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = false,
            Height = 50,
            FontSize = 11
        };
        mainStack.Children.Add(keyTextBox);

        // Update preview when text changes
        keyTextBox.TextChanged += (s, e) =>
        {
            var newSuffix = FactionCatalogService.ExtractBannerImageName(keyTextBox.Text);
            if (!string.IsNullOrEmpty(newSuffix) && bannerImageService != null)
            {
                var bitmap = bannerImageService.GetImageByName(newSuffix);
                previewImage.Source = bitmap;
            }
            else
            {
                previewImage.Source = null;
            }
        };

        // Helper to create a color editor row
        Panel CreateColorRow(string label, string? currentValue, string fieldName, Action<Color> onColorChanged)
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                Margin = new Thickness(0, 4, 0, 4)
            };

            var labelBlock = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 100
            };
            Grid.SetColumn(labelBlock, 0);
            row.Children.Add(labelBlock);

            var colorSwatch = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(4),
                BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(ParseColorOrDefault(currentValue, defaultGray)),
                Margin = new Thickness(8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(colorSwatch, 2);
            row.Children.Add(colorSwatch);

            var textBox = new TextBox
            {
                Text = currentValue ?? "",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(textBox, 1);
            row.Children.Add(textBox);

            // Update swatch when text changes
            textBox.TextChanged += (s, e) =>
            {
                var newColor = ParseColorOrDefault(textBox.Text, defaultGray);
                colorSwatch.Background = new SolidColorBrush(newColor);
                onColorChanged(newColor);
            };

            // Store textbox in Tag for later retrieval
            row.Tag = (fieldName, textBox);

            return row;
        }

        // Colors section
        var colorsLabel = new TextBlock
        {
            Text = "Colors:",
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 12, 0, 4)
        };
        mainStack.Children.Add(colorsLabel);

        var colorRows = new List<(string fieldName, TextBox textBox)>();

        // Determine which color fields exist for this entry
        bool hasStandardColors = currentColor != null || currentColor2 != null;
        bool hasBannerColors = currentPrimaryBannerColor != null || currentSecondaryBannerColor != null;

        if (hasBannerColors)
        {
            // Kingdom-style colors
            var primaryRow = CreateColorRow("Primary:", currentPrimaryBannerColor, "primary_banner_color", (c) =>
            {
                previewContainer.Background = new SolidColorBrush(c);
            });
            mainStack.Children.Add(primaryRow);
            if (primaryRow.Tag is ValueTuple<string, TextBox> pt) colorRows.Add(pt);

            var secondaryRow = CreateColorRow("Secondary:", currentSecondaryBannerColor, "secondary_banner_color", (_) => { });
            mainStack.Children.Add(secondaryRow);
            if (secondaryRow.Tag is ValueTuple<string, TextBox> st) colorRows.Add(st);
        }

        // Standard colors (always show if they exist or if no banner colors)
        if (hasStandardColors || !hasBannerColors)
        {
            var colorRow = CreateColorRow("Color:", currentColor, "color", (c) =>
            {
                if (!hasBannerColors)
                    previewContainer.Background = new SolidColorBrush(c);
            });
            mainStack.Children.Add(colorRow);
            if (colorRow.Tag is ValueTuple<string, TextBox> ct) colorRows.Add(ct);

            var color2Row = CreateColorRow("Color 2:", currentColor2, "color2", (_) => { });
            mainStack.Children.Add(color2Row);
            if (color2Row.Tag is ValueTuple<string, TextBox> c2t) colorRows.Add(c2t);
        }

        // Alternative colors
        if (currentAltColor != null || currentAltColor2 != null || hasStandardColors)
        {
            var altColorRow = CreateColorRow("Alt Color:", currentAltColor, "alternative_color", (_) => { });
            mainStack.Children.Add(altColorRow);
            if (altColorRow.Tag is ValueTuple<string, TextBox> act) colorRows.Add(act);

            var altColor2Row = CreateColorRow("Alt Color 2:", currentAltColor2, "alternative_color2", (_) => { });
            mainStack.Children.Add(altColor2Row);
            if (altColor2Row.Tag is ValueTuple<string, TextBox> ac2t) colorRows.Add(ac2t);
        }

        // Buttons
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        bool completed = false;
        bool saved = false;

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
                saved = true;

                // Save banner key
                var newBannerKey = keyTextBox.Text?.Trim() ?? "";
                if (newBannerKey != currentBannerKey)
                {
                    rowVm[bannerAttributeName] = newBannerKey;
                    Console.WriteLine($"[BannerEditor] Updated banner_key to: {newBannerKey}");
                }

                // Save color values
                foreach (var (fieldName, textBox) in colorRows)
                {
                    var newValue = textBox.Text?.Trim();
                    var oldValue = rowVm[fieldName];
                    if (newValue != oldValue && !string.IsNullOrEmpty(newValue))
                    {
                        rowVm[fieldName] = newValue;
                        Console.WriteLine($"[BannerEditor] Updated {fieldName} to: {newValue}");
                    }
                }

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

        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(okButton);
        mainStack.Children.Add(buttonPanel);

        scrollViewer.Content = mainStack;
        mainBorder.Child = scrollViewer;
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
            Console.WriteLine($"[BannerEditor] Dialog closed, saved: {saved}");
            onComplete();
        };

        // Show dialog
        if (topLevel is Window parentWindow)
        {
            dialog.ShowDialog(parentWindow);
        }
        else
        {
            dialog.Show();
        }

        keyTextBox.Focus();
        keyTextBox.SelectAll();
    }

    /// <summary>
    /// Creates a cell template for color fields with a color swatch and editable hex value.
    /// Supports both "FFRRGGBB" and "0xFFRRGGBB" formats.
    /// </summary>
    private static IDataTemplate CreateColorCellTemplate(string attributeName, FieldDefinition fieldDef, FileTabViewModel vm)
    {
        return new FuncDataTemplate<EntryRowViewModel>((rowVm, _) =>
        {
            var border = new Border();
            border.Classes.Add("dataCell");

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (rowVm == null)
            {
                border.Child = panel;
                return border;
            }

            // Color swatch (small rectangle showing the color)
            var colorSwatch = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(2),
                BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(2, 0)
            };

            // Hex value text (editable via TextBox)
            var hexText = new TextBox
            {
                FontSize = 11,
                Padding = new Thickness(2),
                MinWidth = 70,
                MaxWidth = 90,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200))
            };

            // Helper to parse color from hex string
            Color? ParseHexColor(string? hexValue)
            {
                if (string.IsNullOrEmpty(hexValue))
                    return null;

                // Remove "0x" prefix if present
                var hex = hexValue.TrimStart();
                if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    hex = hex.Substring(2);

                // Remove # prefix if present
                if (hex.StartsWith("#"))
                    hex = hex.Substring(1);

                // Try to parse as ARGB (AARRGGBB) or RGB (RRGGBB)
                try
                {
                    if (hex.Length == 8)
                    {
                        // AARRGGBB format
                        var a = Convert.ToByte(hex.Substring(0, 2), 16);
                        var r = Convert.ToByte(hex.Substring(2, 2), 16);
                        var g = Convert.ToByte(hex.Substring(4, 2), 16);
                        var b = Convert.ToByte(hex.Substring(6, 2), 16);
                        return Color.FromArgb(a, r, g, b);
                    }
                    else if (hex.Length == 6)
                    {
                        // RRGGBB format (assume full alpha)
                        var r = Convert.ToByte(hex.Substring(0, 2), 16);
                        var g = Convert.ToByte(hex.Substring(2, 2), 16);
                        var b = Convert.ToByte(hex.Substring(4, 2), 16);
                        return Color.FromArgb(255, r, g, b);
                    }
                }
                catch
                {
                    // Invalid format
                }

                return null;
            }

            // Helper to update the color display
            void UpdateColorDisplay()
            {
                var colorValue = rowVm[attributeName];
                hexText.Text = colorValue ?? "";

                var color = ParseHexColor(colorValue);
                if (color.HasValue)
                {
                    colorSwatch.Background = new SolidColorBrush(color.Value);
                    ToolTip.SetTip(colorSwatch, $"Color: {colorValue}");
                }
                else
                {
                    // Invalid or empty - show checkerboard pattern or gray
                    colorSwatch.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));
                    ToolTip.SetTip(colorSwatch, "No color / Invalid format");
                }

                CellStyleHelper.UpdateCellState(border, rowVm, attributeName, vm);
            }

            // Initial display
            UpdateColorDisplay();

            // Update row value when text changes and loses focus
            hexText.LostFocus += (s, e) =>
            {
                var newValue = hexText.Text?.Trim() ?? "";
                if (newValue != (rowVm[attributeName] ?? ""))
                {
                    rowVm[attributeName] = newValue;
                    UpdateColorDisplay();
                }
            };

            // Also update on Enter key
            hexText.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    var newValue = hexText.Text?.Trim() ?? "";
                    rowVm[attributeName] = newValue;
                    UpdateColorDisplay();
                    e.Handled = true;
                }
            };

            // Subscribe to refresh events
            vm.CellRefreshRequested += (s, args) => UpdateColorDisplay();

            panel.Children.Add(colorSwatch);
            panel.Children.Add(hexText);

            border.Child = panel;

            return border;
        });
    }

    /// <summary>
    /// Creates a template for action button fields (e.g., Open Parts Editor).
    /// </summary>
    private static IDataTemplate CreateActionButtonTemplate(string attributeName, FieldDefinition fieldDef, FileTabViewModel vm)
    {
        return new FuncDataTemplate<EntryRowViewModel>((rowVm, _) =>
        {
            var border = new Border();
            border.Classes.Add("dataCell");

            if (rowVm == null)
            {
                return border;
            }

            var button = new Button
            {
                Content = fieldDef.DisplayName ?? "Edit",
                Padding = new Thickness(8, 2),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };

            var actionType = fieldDef.ActionType;

            button.Click += async (s, e) =>
            {
                if (actionType == "openWeaponPartsEditor")
                {
                    await OpenWeaponPartsEditorAsync(rowVm, vm, button);
                }
            };

            border.Child = button;
            return border;
        });
    }

    /// <summary>
    /// Opens the weapon parts editor for the specified row.
    /// </summary>
    private static async Task OpenWeaponPartsEditorAsync(EntryRowViewModel rowVm, FileTabViewModel vm, Control anchor)
    {
        // Get current values from the row
        var templateId = rowVm["crafting_template"];
        var bladeId = rowVm["blade_id"];
        var handleId = rowVm["handle_id"];
        var guardId = rowVm["guard_id"];
        var pommelId = rowVm["pommel_id"];

        var bladeScale = int.TryParse(rowVm["blade_scale"], out var bs) ? bs : 100;
        var handleScale = int.TryParse(rowVm["handle_scale"], out var hs) ? hs : 100;
        var guardScale = int.TryParse(rowVm["guard_scale"], out var gs) ? gs : 100;
        var pommelScale = int.TryParse(rowVm["pommel_scale"], out var ps) ? ps : 100;

        // Create services
        var catalogService = new CraftingPieceCatalogService();
        var fbxLoaderService = new FbxLoaderService();

        // Derive paths from the file path
        // FilePath is like: C:\...\TOR_Armory\ModuleData\tor_items\tor_meleeweapons.xml
        // We need to find the ModuleData folder (may be parent or grandparent depending on file location)
        // AssetSources is at the same level as ModuleData (sibling folder in module root)
        var fileDir = Path.GetDirectoryName(vm.FilePath) ?? "";

        // Find the ModuleData folder by walking up the path
        var moduleDataPath = fileDir;
        while (!string.IsNullOrEmpty(moduleDataPath) && !moduleDataPath.EndsWith("ModuleData", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(moduleDataPath);
            if (parent == moduleDataPath) break; // Reached root
            moduleDataPath = parent ?? "";
        }

        // If we couldn't find ModuleData, fall back to the file's directory
        if (string.IsNullOrEmpty(moduleDataPath) || !moduleDataPath.EndsWith("ModuleData", StringComparison.OrdinalIgnoreCase))
        {
            moduleDataPath = fileDir;
        }

        var moduleRootPath = Path.GetDirectoryName(moduleDataPath) ?? "";
        var assetSourcesPath = Path.Combine(moduleRootPath, "AssetSources");

        // Create and show editor
        Console.WriteLine($"[WeaponPartsEditor] Creating editor window...");
        var editor = new WeaponPartsEditorView();
        editor.Initialize(catalogService, fbxLoaderService, moduleDataPath, assetSourcesPath);
        editor.SetInitialSelection(templateId, bladeId, handleId, guardId, pommelId,
            bladeScale, handleScale, guardScale, pommelScale);

        // Get the parent window from the anchor control
        var parentWindow = TopLevel.GetTopLevel(anchor) as Window;
        Console.WriteLine($"[WeaponPartsEditor] Parent window found: {parentWindow != null}");

        if (parentWindow != null)
        {
            Console.WriteLine($"[WeaponPartsEditor] Showing dialog...");
            await editor.ShowDialog(parentWindow);
            Console.WriteLine($"[WeaponPartsEditor] Dialog closed");
        }
        else
        {
            Console.WriteLine($"[WeaponPartsEditor] No parent window, showing as regular window");
            editor.Show();
            return;
        }

        // If user applied changes, update the row
        if (editor.DialogResult && editor.Selection.HasValue)
        {
            var selection = editor.Selection.Value;

            // Update piece IDs
            if (selection.bladeId != null)
                rowVm["blade_id"] = selection.bladeId;
            if (selection.handleId != null)
                rowVm["handle_id"] = selection.handleId;
            if (selection.guardId != null)
                rowVm["guard_id"] = selection.guardId;
            if (selection.pommelId != null)
                rowVm["pommel_id"] = selection.pommelId;

            // Update scales
            rowVm["blade_scale"] = selection.bladeScale.ToString();
            rowVm["handle_scale"] = selection.handleScale.ToString();
            rowVm["guard_scale"] = selection.guardScale.ToString();
            rowVm["pommel_scale"] = selection.pommelScale.ToString();
        }

        // Clean up
        fbxLoaderService.Dispose();
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
            PlaceholderText = "Type to filter icons...",
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
