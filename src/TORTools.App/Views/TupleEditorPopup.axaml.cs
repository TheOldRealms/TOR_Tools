using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using TORTools.App.ViewModels;
using TORTools.Core.Schema;

namespace TORTools.App.Views;

/// <summary>
/// Popup window for editing tuple list data (e.g., DamageProportions, Resistances, Amplifiers).
/// </summary>
public partial class TupleEditorPopup : Window
{
    private readonly string _entryId;
    private readonly string _fieldName;
    private readonly TupleListConfig _config;
    private readonly FileTabViewModel _viewModel;
    private readonly ObservableCollection<TupleRowViewModel> _rows;

    public TupleEditorPopup()
    {
        InitializeComponent();
        _entryId = "";
        _fieldName = "";
        _config = new TupleListConfig();
        _viewModel = null!;
        _rows = new ObservableCollection<TupleRowViewModel>();
    }

    public TupleEditorPopup(string entryId, string fieldName, List<Dictionary<string, string>> tuples, TupleListConfig config, FileTabViewModel viewModel)
    {
        InitializeComponent();

        _entryId = entryId;
        _fieldName = fieldName;
        _config = config;
        _viewModel = viewModel;

        // Set header text
        HeaderText.Text = $"Edit {config.ElementName}";
        SubHeaderText.Text = $"Entry: {entryId}";

        // Create rows from existing tuples (convert decimal to percentage for display)
        _rows = new ObservableCollection<TupleRowViewModel>();
        foreach (var tuple in tuples)
        {
            _rows.Add(new TupleRowViewModel(tuple, config.Columns));
        }

        // Generate columns based on config
        GenerateColumns();

        // Bind to DataGrid
        TupleGrid.ItemsSource = _rows;
    }

    private void GenerateColumns()
    {
        TupleGrid.Columns.Clear();

        // Calculate proportional widths - enum columns get more space
        var totalColumns = _config.Columns.Count;

        foreach (var column in _config.Columns)
        {
            DataGridColumn dgColumn;

            // Use star sizing for flexible widths - enum gets 2*, number gets 1*
            var columnWidth = column.Type == "enum"
                ? new DataGridLength(2, DataGridLengthUnitType.Star)
                : new DataGridLength(1, DataGridLengthUnitType.Star);

            if (column.Type == "enum" && column.EnumValues?.Count > 0)
            {
                // ComboBox column for enum values using template
                var enumValues = column.EnumValues;
                var attributeName = column.Attribute;

                dgColumn = new DataGridTemplateColumn
                {
                    Header = column.DisplayName,
                    Width = columnWidth,
                    CellTemplate = new FuncDataTemplate<TupleRowViewModel>((rowVm, _) =>
                    {
                        var comboBox = new ComboBox
                        {
                            ItemsSource = enumValues,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Center
                        };

                        if (rowVm != null)
                        {
                            comboBox.SelectedItem = rowVm[attributeName];
                            comboBox.SelectionChanged += (s, e) =>
                            {
                                if (comboBox.SelectedItem is string selected)
                                {
                                    rowVm[attributeName] = selected;
                                }
                            };
                        }

                        return comboBox;
                    })
                };
            }
            else if (column.Type == "number")
            {
                // Number column with percentage conversion
                var attributeName = column.Attribute;

                dgColumn = new DataGridTemplateColumn
                {
                    Header = column.DisplayName,
                    Width = columnWidth,
                    CellTemplate = new FuncDataTemplate<TupleRowViewModel>((rowVm, _) =>
                    {
                        var textBox = new TextBox
                        {
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextAlignment = Avalonia.Media.TextAlignment.Right
                        };

                        if (rowVm != null)
                        {
                            // Display as integer percentage (e.g., "25" for 0.25)
                            textBox.Text = rowVm.GetDisplayPercent(attributeName);

                            textBox.LostFocus += (s, e) =>
                            {
                                // Save as integer percentage, will be converted to decimal on save
                                rowVm.SetDisplayPercent(attributeName, textBox.Text);
                            };
                        }

                        return textBox;
                    })
                };
            }
            else
            {
                // Text column for other types
                dgColumn = new DataGridTextColumn
                {
                    Header = column.DisplayName,
                    Width = columnWidth,
                    Binding = new Binding($"[{column.Attribute}]")
                };
            }

            TupleGrid.Columns.Add(dgColumn);
        }
    }

    private void OnAddRow(object? sender, RoutedEventArgs e)
    {
        // Create empty row with default values
        // Note: Values must be in STORAGE format (decimal) because TupleRowViewModel constructor
        // converts from storage (0.00-1.00) to display (0-100) format
        var newTuple = new Dictionary<string, string>();
        foreach (var column in _config.Columns)
        {
            if (column.Type == "enum" && column.EnumValues?.Count > 0)
            {
                newTuple[column.Attribute] = column.EnumValues[0];
            }
            else if (column.Type == "number")
            {
                newTuple[column.Attribute] = "1.00"; // Default to 100% (stored as decimal)
            }
            else
            {
                newTuple[column.Attribute] = "";
            }
        }

        _rows.Add(new TupleRowViewModel(newTuple, _config.Columns));
    }

    private void OnRemoveRow(object? sender, RoutedEventArgs e)
    {
        if (TupleGrid.SelectedItem is TupleRowViewModel selectedRow)
        {
            _rows.Remove(selectedRow);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        // Convert rows back to tuple dictionaries with decimal values for storage
        var tuples = _rows.Select(r => r.GetStorageValues(_config.Columns)).ToList();

        Console.WriteLine($"[TupleEditor] Saving {tuples.Count} tuples for {_entryId}.{_fieldName}");
        foreach (var tuple in tuples)
        {
            var parts = tuple.Select(kv => $"{kv.Key}={kv.Value}");
            Console.WriteLine($"  - {string.Join(", ", parts)}");
        }

        // Save to XML via the view model
        var success = _viewModel.SaveTupleData(_fieldName, _entryId, tuples);

        if (success)
        {
            Console.WriteLine($"[TupleEditor] Save successful");
            Close(true);
        }
        else
        {
            Console.WriteLine($"[TupleEditor] Save failed");
            // Could show error message here
            Close(false);
        }
    }
}

/// <summary>
/// ViewModel for a single tuple row in the editor.
/// Handles percentage conversion: stores as integer (25), converts to decimal (0.25) for XML.
/// </summary>
public class TupleRowViewModel
{
    private readonly Dictionary<string, string> _values;
    private readonly List<TupleColumnConfig> _columns;

    public TupleRowViewModel(Dictionary<string, string> values, List<TupleColumnConfig> columns)
    {
        _columns = columns;
        _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Convert decimal values to integer percentages for display
        foreach (var kvp in values)
        {
            var column = columns.FirstOrDefault(c => c.Attribute.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase));
            if (column?.Type == "number" && double.TryParse(kvp.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalVal))
            {
                // Convert 0.25 to 25 for display
                _values[kvp.Key] = ((int)Math.Round(decimalVal * 100)).ToString();
            }
            else
            {
                _values[kvp.Key] = kvp.Value;
            }
        }
    }

    public string this[string key]
    {
        get => _values.TryGetValue(key, out var value) ? value : "";
        set => _values[key] = value;
    }

    /// <summary>
    /// Gets the display value for a percentage field (as integer, e.g., "25").
    /// </summary>
    public string GetDisplayPercent(string key)
    {
        return _values.TryGetValue(key, out var value) ? value : "0";
    }

    /// <summary>
    /// Sets the display value for a percentage field (as integer, e.g., "25").
    /// </summary>
    public void SetDisplayPercent(string key, string? value)
    {
        if (int.TryParse(value, out var intVal))
        {
            _values[key] = intVal.ToString();
        }
        else
        {
            _values[key] = "0";
        }
    }

    /// <summary>
    /// Gets all values, converting percentage integers back to decimals for XML storage.
    /// </summary>
    public Dictionary<string, string> GetStorageValues(List<TupleColumnConfig> columns)
    {
        var result = new Dictionary<string, string>();
        foreach (var kvp in _values)
        {
            var column = columns.FirstOrDefault(c => c.Attribute.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase));
            if (column?.Type == "number" && int.TryParse(kvp.Value, out var intVal))
            {
                // Convert 25 to 0.25 for storage
                var decimalVal = intVal / 100.0;
                result[kvp.Key] = decimalVal.ToString("0.00", CultureInfo.InvariantCulture);
            }
            else
            {
                result[kvp.Key] = kvp.Value;
            }
        }
        return result;
    }

    public Dictionary<string, string> GetValues() => new Dictionary<string, string>(_values);
}
