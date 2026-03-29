using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TORTools.Core.Commands;
using TORTools.Core.Models;
using TORTools.Core.Services;

namespace TORTools.App.ViewModels;

public partial class FileTabViewModel : ViewModelBase, IDisposable
{
    private readonly IXmlDocumentService _xmlService;
    private readonly IUndoRedoService _undoRedoService;
    private XmlDocumentWrapper? _document;
    private FileSystemWatcher? _fileWatcher;
    private bool _isReloading;
    private bool _isSaving;

    [ObservableProperty]
    private string _title = "Untitled";

    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _hasError;

    /// <summary>
    /// Observable rows for DataGrid binding.
    /// </summary>
    public ObservableCollection<EntryRowViewModel> Rows { get; } = new();

    /// <summary>
    /// The raw XmlEntry objects (for internal use).
    /// </summary>
    public List<XmlEntry> XmlEntries { get; } = new();

    /// <summary>
    /// Column names discovered from the XML.
    /// </summary>
    public List<string> ColumnNames { get; } = new();

    /// <summary>
    /// The currently selected entry index (for row operations).
    /// </summary>
    [ObservableProperty]
    private int _selectedIndex = -1;

    /// <summary>
    /// The undo/redo service for this tab.
    /// </summary>
    public IUndoRedoService UndoRedoService => _undoRedoService;

    public FileTabViewModel(string filePath) : this(filePath, new XmlDocumentService(), new UndoRedoService())
    {
    }

    public FileTabViewModel(string filePath, IXmlDocumentService xmlService, IUndoRedoService undoRedoService)
    {
        _xmlService = xmlService;
        _undoRedoService = undoRedoService;
        FilePath = filePath;
        Title = Path.GetFileName(filePath);

        LoadFile();
        SetupFileWatcher();
    }

    private void SetupFileWatcher()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            var fileName = Path.GetFileName(FilePath);

            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
                return;

            _fileWatcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _fileWatcher.Changed += OnFileChangedExternally;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FileWatcher] Failed to setup watcher: {ex.Message}");
        }
    }

    private void OnFileChangedExternally(object sender, FileSystemEventArgs e)
    {
        // Ignore if we're currently saving or already reloading
        if (_isSaving || _isReloading)
            return;

        // Debounce - file system events can fire multiple times
        _isReloading = true;

        // Use Dispatcher to reload on UI thread
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                ReloadFile();
            }
            finally
            {
                _isReloading = false;
            }
        });
    }

    /// <summary>
    /// Reloads the file from disk, discarding any unsaved changes.
    /// </summary>
    public void ReloadFile()
    {
        LoadFile();
        _undoRedoService.Clear();
        HasUnsavedChanges = false;
        OnPropertyChanged(nameof(Rows));
    }

    private void LoadFile()
    {
        try
        {
            _document = _xmlService.Load(FilePath);
            var entries = _xmlService.GetEntries(_document);

            XmlEntries.Clear();
            XmlEntries.AddRange(entries);

            // Discover all unique column names from all entries
            DiscoverColumns(entries);

            // Create row view models for DataGrid binding
            CreateRows(entries);

            HasError = false;
            ErrorMessage = "";
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Error loading file: {ex.Message}";
        }
    }

    private void DiscoverColumns(IReadOnlyList<XmlEntry> entries)
    {
        ColumnNames.Clear();
        var columnSet = new HashSet<string>();

        // Always put 'id' and 'name' first if they exist
        var priorityColumns = new[] { "id", "name" };

        foreach (var entry in entries)
        {
            foreach (var attr in entry.Attributes)
            {
                columnSet.Add(attr.Name);
            }
        }

        // Add priority columns first
        foreach (var col in priorityColumns)
        {
            if (columnSet.Contains(col))
            {
                ColumnNames.Add(col);
                columnSet.Remove(col);
            }
        }

        // Add remaining columns in alphabetical order
        ColumnNames.AddRange(columnSet.OrderBy(c => c));
    }

    private void CreateRows(IReadOnlyList<XmlEntry> entries)
    {
        // Unsubscribe from old rows
        foreach (var row in Rows)
        {
            row.CellValueChanged -= OnCellValueChanged;
        }

        Rows.Clear();
        foreach (var entry in entries)
        {
            var row = new EntryRowViewModel(entry, ColumnNames);
            row.IsNew = _newEntries.Contains(entry);
            row.CellValueChanged += OnCellValueChanged;
            Rows.Add(row);
        }
    }

    private void OnCellValueChanged(object? sender, CellValueChangedEventArgs e)
    {
        if (sender is not EntryRowViewModel rowVm) return;
        if (_document == null) return;

        // Create and execute an edit command
        var command = new CellEditUndoCommand(rowVm, e.ColumnName, e.OldValue, e.NewValue);

        // Don't use Execute() here since the value is already changed
        // Just push to undo stack
        _undoRedoService.Execute(new AlreadyExecutedCommand(command));

        MarkAsModified();
    }

    public void Save()
    {
        if (_document == null)
            return;

        _isSaving = true;
        try
        {
            // Sync changes from dynamic entries back to XmlEntries
            SyncChangesToXml();

            _xmlService.Save(_document);
            HasUnsavedChanges = false;
            HasError = false;
            ErrorMessage = "";

            // After save, all entries are no longer "new" - they're in the file now
            _newEntries.Clear();
            foreach (var row in Rows)
            {
                row.IsNew = false;
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Error saving file: {ex.Message}";
        }
        finally
        {
            // Delay resetting flag to avoid catching our own save event
            Task.Delay(500).ContinueWith(_ => _isSaving = false);
        }
    }

    private void SyncChangesToXml()
    {
        foreach (var rowVm in Rows)
        {
            var xmlEntry = rowVm.XmlEntry;

            foreach (var columnName in ColumnNames)
            {
                var currentValue = rowVm[columnName];
                var attr = xmlEntry.GetAttribute(columnName);

                if (attr != null)
                {
                    // Existing attribute - update if changed
                    if (attr.DisplayValue != currentValue)
                    {
                        xmlEntry.SetAttributeValue(columnName,
                            LocalizationHelper.Wrap(attr.LocalizationKey, currentValue));
                        _document!.HasUnsavedChanges = true;
                    }
                }
                else if (!string.IsNullOrEmpty(currentValue))
                {
                    // New attribute on new entry - add it
                    xmlEntry.SetAttributeValue(columnName, currentValue);
                    _document!.HasUnsavedChanges = true;
                }
            }
        }

        HasUnsavedChanges = _document?.HasUnsavedChanges ?? false;
    }

    public void MarkAsModified()
    {
        HasUnsavedChanges = true;
        if (_document != null)
        {
            _document.HasUnsavedChanges = true;
        }
    }

    /// <summary>
    /// Tracks which XmlEntry objects are new (for IsNew styling).
    /// </summary>
    private readonly HashSet<XmlEntry> _newEntries = new();

    /// <summary>
    /// Stores the copied row data (column name -> value).
    /// </summary>
    private Dictionary<string, string>? _copiedRowData;

    /// <summary>
    /// The row currently selected for copy.
    /// </summary>
    private EntryRowViewModel? _copiedRow;

    /// <summary>
    /// Adds a new row after the current selection.
    /// </summary>
    [RelayCommand]
    public void AddRow()
    {
        AddRowAtIndex(SelectedIndex >= 0 ? SelectedIndex + 1 : XmlEntries.Count);
    }

    /// <summary>
    /// Adds a new row before the current selection.
    /// </summary>
    [RelayCommand]
    public void InsertRowAbove()
    {
        AddRowAtIndex(SelectedIndex >= 0 ? SelectedIndex : 0);
    }

    /// <summary>
    /// Adds a new row at a specific index.
    /// </summary>
    public void AddRowAtIndex(int insertIndex)
    {
        if (_document == null) return;

        var xmlEntryCollection = new ObservableCollection<XmlEntry>(XmlEntries);

        var command = new AddRowCommand(_document, xmlEntryCollection, insertIndex);
        _undoRedoService.Execute(command);

        // Sync collections
        XmlEntries.Clear();
        XmlEntries.AddRange(xmlEntryCollection);

        // Mark the new entry as new
        if (insertIndex < XmlEntries.Count)
        {
            _newEntries.Add(XmlEntries[insertIndex]);
        }

        // Recreate dynamic entries
        RefreshRows();
        MarkAsModified();

        // Select the new row
        SelectedIndex = insertIndex;
    }

    /// <summary>
    /// Deletes the currently selected row.
    /// </summary>
    [RelayCommand]
    public void DeleteRow()
    {
        if (_document == null || SelectedIndex < 0 || SelectedIndex >= XmlEntries.Count)
            return;

        var xmlEntryCollection = new ObservableCollection<XmlEntry>(XmlEntries);
        var entryToDelete = xmlEntryCollection[SelectedIndex];

        // Remove from new entries tracking
        _newEntries.Remove(entryToDelete);

        var command = new DeleteRowCommand(_document, xmlEntryCollection, entryToDelete);
        _undoRedoService.Execute(command);

        // Sync collections
        XmlEntries.Clear();
        XmlEntries.AddRange(xmlEntryCollection);

        // Recreate dynamic entries
        RefreshRows();
        MarkAsModified();
    }

    /// <summary>
    /// Duplicates the currently selected row.
    /// </summary>
    [RelayCommand]
    public void DuplicateRow()
    {
        if (_document == null || SelectedIndex < 0 || SelectedIndex >= XmlEntries.Count)
            return;

        var xmlEntryCollection = new ObservableCollection<XmlEntry>(XmlEntries);
        var entryToDuplicate = xmlEntryCollection[SelectedIndex];
        var insertIndex = SelectedIndex + 1;

        var command = new DuplicateRowCommand(_document, xmlEntryCollection, entryToDuplicate);
        _undoRedoService.Execute(command);

        // Sync collections
        XmlEntries.Clear();
        XmlEntries.AddRange(xmlEntryCollection);

        // Mark the duplicated entry as new
        if (insertIndex < XmlEntries.Count)
        {
            _newEntries.Add(XmlEntries[insertIndex]);
        }

        // Recreate dynamic entries
        RefreshRows();
        MarkAsModified();

        // Select the new row
        SelectedIndex = insertIndex;
    }

    /// <summary>
    /// Selects a row for copy operation (highlights it).
    /// </summary>
    public void SelectRowForCopy(EntryRowViewModel row)
    {
        // Clear previous selection
        if (_copiedRow != null)
        {
            _copiedRow.IsSelectedForCopy = false;
        }

        // Set new selection
        _copiedRow = row;
        row.IsSelectedForCopy = true;

        // Store the data
        _copiedRowData = new Dictionary<string, string>();
        foreach (var col in ColumnNames)
        {
            _copiedRowData[col] = row[col];
        }
    }

    /// <summary>
    /// Copies the currently selected row's data.
    /// </summary>
    [RelayCommand]
    public void CopyRow()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Rows.Count)
            return;

        SelectRowForCopy(Rows[SelectedIndex]);
    }

    /// <summary>
    /// Pastes copied row data onto the currently selected row.
    /// </summary>
    [RelayCommand]
    public void PasteRow()
    {
        if (_copiedRowData == null)
            return;

        if (SelectedIndex < 0 || SelectedIndex >= Rows.Count)
            return;

        var targetRow = Rows[SelectedIndex];

        foreach (var kvp in _copiedRowData)
        {
            // Skip ID for existing (non-new) entries
            if (kvp.Key.Equals("id", StringComparison.OrdinalIgnoreCase) && !targetRow.IsNew)
                continue;

            // Set the value (this will trigger CellValueChanged for undo support)
            targetRow[kvp.Key] = kvp.Value;
        }

        MarkAsModified();
        ForceRowsRefresh();
    }

    /// <summary>
    /// Whether a row has been copied and is ready to paste.
    /// </summary>
    public bool HasCopiedRow => _copiedRowData != null;

    /// <summary>
    /// Undoes the last operation.
    /// </summary>
    public void Undo()
    {
        if (!_undoRedoService.CanUndo) return;
        _undoRedoService.Undo();
        MarkAsModified();
        // Force DataGrid to refresh by triggering collection reset
        ForceRowsRefresh();
    }

    /// <summary>
    /// Redoes the last undone operation.
    /// </summary>
    public void Redo()
    {
        if (!_undoRedoService.CanRedo) return;
        _undoRedoService.Redo();
        MarkAsModified();
        // Force DataGrid to refresh by triggering collection reset
        ForceRowsRefresh();
    }

    /// <summary>
    /// Forces the DataGrid to refresh by re-adding all rows.
    /// This works around Avalonia DataGrid not responding to indexed property changes.
    /// </summary>
    private void ForceRowsRefresh()
    {
        var items = Rows.ToList();
        Rows.Clear();
        foreach (var item in items)
        {
            Rows.Add(item);
        }
    }

    private void RefreshRows()
    {
        // Rediscover columns in case new entries have different attributes
        DiscoverColumns(XmlEntries);

        // Recreate the rows (CreateRows handles IsNew tracking via _newEntries)
        CreateRows(XmlEntries);
    }

    private void RefreshFromXmlEntries()
    {
        // Reload XmlEntries from document
        if (_document == null) return;

        var entries = _xmlService.GetEntries(_document);
        XmlEntries.Clear();
        XmlEntries.AddRange(entries);

        RefreshRows();
        HasUnsavedChanges = _document.HasUnsavedChanges;
    }

    public void Dispose()
    {
        if (_fileWatcher != null)
        {
            _fileWatcher.Changed -= OnFileChangedExternally;
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Dispose();
            _fileWatcher = null;
        }
    }
}

/// <summary>
/// Command for undoing/redoing cell edits in the UI.
/// </summary>
internal class CellEditUndoCommand : IEditCommand
{
    private readonly EntryRowViewModel _rowVm;
    private readonly string _columnName;
    private readonly string _oldValue;
    private readonly string _newValue;

    public string Description => $"Edit {_columnName}";

    public CellEditUndoCommand(EntryRowViewModel rowVm, string columnName, string oldValue, string newValue)
    {
        _rowVm = rowVm;
        _columnName = columnName;
        _oldValue = oldValue;
        _newValue = newValue;
    }

    public void Execute()
    {
        _rowVm.SetValueSilent(_columnName, _newValue);
        UpdateXmlEntry(_newValue);
    }

    public void Undo()
    {
        _rowVm.SetValueSilent(_columnName, _oldValue);
        UpdateXmlEntry(_oldValue);
    }

    private void UpdateXmlEntry(string value)
    {
        var attr = _rowVm.XmlEntry.GetAttribute(_columnName);
        if (attr != null)
        {
            var rawValue = LocalizationHelper.Wrap(attr.LocalizationKey, value);
            _rowVm.XmlEntry.SetAttributeValue(_columnName, rawValue);
        }
        else
        {
            // New attribute - add it directly without localization wrapping
            _rowVm.XmlEntry.SetAttributeValue(_columnName, value);
        }
    }
}

/// <summary>
/// Wrapper for a command that has already been executed on first call.
/// First Execute() does nothing, subsequent calls delegate to inner.
/// </summary>
internal class AlreadyExecutedCommand : IEditCommand
{
    private readonly IEditCommand _inner;
    private bool _firstExecute = true;

    public string Description => _inner.Description;

    public AlreadyExecutedCommand(IEditCommand inner)
    {
        _inner = inner;
    }

    public void Execute()
    {
        if (_firstExecute)
        {
            // First time - already executed by the UI
            _firstExecute = false;
            return;
        }
        // Subsequent calls (redo) - actually execute
        _inner.Execute();
    }

    public void Undo()
    {
        _inner.Undo();
    }
}
