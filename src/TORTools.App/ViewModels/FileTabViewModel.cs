using System.Collections.ObjectModel;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TORTools.App.Commands;
using TORTools.App.Helpers;
using TORTools.App.Models;
using TORTools.App.Services;
using TORTools.Core.Commands;
using TORTools.Core.Models;
using TORTools.Core.Schema;
using TORTools.Core.Services;
using TORTools.Core.Validation;

namespace TORTools.App.ViewModels;

public partial class FileTabViewModel : ViewModelBase, IDisposable
{
    private readonly FileEditManager _fileEditManager;
    private readonly IUndoRedoService _undoRedoService;
    private readonly CrossReferenceService _crossRefService;
    private readonly TupleListService _tupleListService;
    private readonly FilePathResolver _filePathResolver;
    private FileSystemWatcher? _fileWatcher;
    private bool _isReloading;
    private bool _isSaving;
    private CancellationTokenSource? _filterCts;
    private const int FilterDebounceMs = 300;

    /// <summary>
    /// Convenience accessor for the file edit context.
    /// </summary>
    protected FileEditContext Context => _fileEditManager.Context;

    /// <summary>
    /// Cross-reference data loaded from other XML files (local to ViewModel, not in Context).
    /// Key is the cross-reference field name, value is a dictionary mapping local keys to referenced values.
    /// </summary>
    private readonly Dictionary<string, Dictionary<string, List<string>>> _crossRefData = new();

    /// <summary>
    /// Source file paths for cross-reference fields (local to ViewModel).
    /// Key is the field name, value is the resolved path to the source file.
    /// </summary>
    private readonly Dictionary<string, string> _crossRefSourcePaths = new();

    /// <summary>
    /// Tuple list data loaded from external XML files (local to ViewModel).
    /// Key is the field name, value is a dictionary mapping local keys to lists of tuple dictionaries.
    /// </summary>
    private readonly Dictionary<string, Dictionary<string, List<Dictionary<string, string>>>> _tupleListData = new();

    /// <summary>
    /// Source file paths for tuple list fields (local to ViewModel).
    /// Key is the field name, value is the resolved path to the source file.
    /// </summary>
    private readonly Dictionary<string, string> _tupleListSourcePaths = new();

    /// <summary>
    /// Central validation manager - now accessed through Context.
    /// </summary>
    public ValidationManager ValidationManager => Context.ValidationManager;

    /// <summary>
    /// Event raised when user wants to navigate to a cross-referenced entry.
    /// </summary>
    public event EventHandler<CrossReferenceNavigationEventArgs>? NavigateToCrossReference;

    /// <summary>
    /// Event raised when cells need to refresh their content and styling.
    /// Subscribe to this in cell templates for centralized refresh handling.
    /// </summary>
    public event EventHandler? CellRefreshRequested;

    /// <summary>
    /// Event raised when columns need to be regenerated (e.g., after reload).
    /// Subscribe to this in views to reset column generation state.
    /// </summary>
    public event EventHandler? ColumnsInvalidated;

    /// <summary>
    /// Triggers a refresh of all cell content and styling.
    /// Call this after any operation that changes data or state (save, undo, redo, etc.).
    /// </summary>
    public void RequestCellRefresh()
    {
        CellRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Triggers column regeneration in subscribed views.
    /// </summary>
    public void InvalidateColumns()
    {
        ColumnsInvalidated?.Invoke(this, EventArgs.Empty);
    }

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

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Observable rows for DataGrid binding - now accessed through Context.
    /// </summary>
    public ObservableCollection<EntryRowViewModel> Rows => Context.Rows;

    /// <summary>
    /// The raw XmlEntry objects - now accessed through Context.
    /// </summary>
    public List<XmlEntry> XmlEntries => Context.XmlEntries;

    /// <summary>
    /// Column names discovered from the XML - now accessed through Context.
    /// </summary>
    public List<string> ColumnNames => Context.ColumnNames;

    /// <summary>
    /// The currently selected entry index (for row operations).
    /// </summary>
    [ObservableProperty]
    private int _selectedIndex = -1;

    /// <summary>
    /// The currently selected column name (for cell-level selection).
    /// When null, the entire row is selected. When set, only the specific cell is selected.
    /// </summary>
    [ObservableProperty]
    private string? _selectedColumn;

    /// <summary>
    /// Event raised when a cell is selected (for visual highlighting).
    /// </summary>
    public event EventHandler<CellSelectionEventArgs>? CellSelected;

    /// <summary>
    /// Selects a specific cell.
    /// </summary>
    public void SelectCell(int rowIndex, string? columnName)
    {
        SelectedIndex = rowIndex;
        SelectedColumn = columnName;
        CellSelected?.Invoke(this, new CellSelectionEventArgs(rowIndex, columnName));
    }

    /// <summary>
    /// Selects an entire row (clears column selection).
    /// </summary>
    public void SelectRow(int rowIndex)
    {
        SelectCell(rowIndex, null);
    }

    /// <summary>
    /// Whether ID editing is locked for all rows (default false - disabled for now to allow renaming).
    /// Toggle via the lock icon in the ID column header.
    /// </summary>
    [ObservableProperty]
    private bool _isIdColumnLocked = false;

    /// <summary>
    /// Whether to show entries that were removed (exist in git but not in current file).
    /// </summary>
    [ObservableProperty]
    private bool _showRemovedEntries;

    /// <summary>
    /// Text used to filter entries in the grid.
    /// </summary>
    [ObservableProperty]
    private string _filterText = "";

    /// <summary>
    /// Whether a filter/loading operation is in progress.
    /// </summary>
    [ObservableProperty]
    private bool _isFiltering;

    /// <summary>
    /// Whether to suppress scroll-into-view behavior (set during undo/redo/row operations).
    /// </summary>
    public bool SuppressScrollIntoView { get; set; }

    /// <summary>
    /// Filtered rows based on FilterText. If empty, returns null.
    /// </summary>
    public ObservableCollection<EntryRowViewModel>? FilteredRows { get; private set; }

    /// <summary>
    /// Rows to display in the grid - returns FilteredRows if filtering, otherwise Rows.
    /// </summary>
    public ObservableCollection<EntryRowViewModel> DisplayRows => FilteredRows ?? Rows;

    partial void OnFilterTextChanged(string value)
    {
        // Cancel any pending filter operation
        _filterCts?.Cancel();
        _filterCts = new CancellationTokenSource();
        var token = _filterCts.Token;

        // Debounce: wait before applying filter
        _ = ApplyFilterDebouncedAsync(value, token);
    }

    private async Task ApplyFilterDebouncedAsync(string filterText, CancellationToken token)
    {
        try
        {
            // Show loading indicator immediately if there's text
            if (!string.IsNullOrWhiteSpace(filterText))
            {
                IsFiltering = true;
            }

            // Wait for debounce period
            await Task.Delay(FilterDebounceMs, token);

            // Check if cancelled
            if (token.IsCancellationRequested) return;

            // Apply filter on background thread for large datasets
            await Task.Run(() => ApplyFilterCore(filterText, token), token);
        }
        catch (OperationCanceledException)
        {
            // Filter was cancelled by new input, ignore
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsFiltering = false;
            }
        }
    }

    private void ApplyFilterCore(string filterText, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(filterText))
        {
            // Clear filter on UI thread
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                FilteredRows = null;
                OnPropertyChanged(nameof(FilteredRows));
                OnPropertyChanged(nameof(DisplayRows));
            });
            return;
        }

        var searchTerms = filterText.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var columnNamesList = ColumnNames.ToList(); // Cache to avoid repeated enumeration

        var filtered = new List<EntryRowViewModel>();
        foreach (var row in Rows)
        {
            if (token.IsCancellationRequested) return;

            // Check if any cell contains all search terms
            bool allTermsFound = true;
            foreach (var term in searchTerms)
            {
                bool termFound = false;
                foreach (var colName in columnNamesList)
                {
                    var cellValue = row[colName]?.ToLowerInvariant() ?? "";
                    if (cellValue.Contains(term))
                    {
                        termFound = true;
                        break;
                    }
                }
                if (!termFound)
                {
                    allTermsFound = false;
                    break;
                }
            }
            if (allTermsFound)
            {
                filtered.Add(row);
            }
        }

        if (token.IsCancellationRequested) return;

        // Update UI on UI thread
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            FilteredRows = new ObservableCollection<EntryRowViewModel>(filtered);
            OnPropertyChanged(nameof(FilteredRows));
            OnPropertyChanged(nameof(DisplayRows));
        });
    }

    partial void OnShowRemovedEntriesChanged(bool value)
    {
        RefreshRowsWithRemovedEntries();
    }

    partial void OnIsIdColumnLockedChanged(bool value)
    {
        // Update all rows' IsIdLocked state
        foreach (var row in Rows)
        {
            // Only lock existing entries; new entries stay unlocked
            if (!row.IsNew)
            {
                row.IsIdLocked = value;
            }
        }
    }

    /// <summary>
    /// The undo/redo service for this tab.
    /// </summary>
    public IUndoRedoService UndoRedoService => _undoRedoService;

    /// <summary>
    /// Icon service for icon picker functionality. Set after construction by MainWindowViewModel.
    /// </summary>
    public IIconService? IconService { get; set; }

    /// <summary>
    /// Item catalog service for equipment set validation. Set after construction by MainWindowViewModel.
    /// </summary>
    public ItemCatalogService? ItemCatalogService { get; set; }

    /// <summary>
    /// Banner image service for faction banner display. Set after construction by MainWindowViewModel.
    /// </summary>
    public BannerImageService? BannerImageService { get; set; }

    /// <summary>
    /// Ability catalog service for ability icons and info. Set after construction by MainWindowViewModel.
    /// </summary>
    public AbilityCatalogService? AbilityCatalogService { get; set; }

    /// <summary>
    /// Item trait catalog service for trait icons and info. Set after construction by MainWindowViewModel.
    /// </summary>
    public ItemTraitCatalogService? ItemTraitCatalogService { get; set; }

    /// <summary>
    /// Faction catalog service for faction lookups and kingdom color inheritance. Set after construction by MainWindowViewModel.
    /// </summary>
    public FactionCatalogService? FactionCatalogService { get; set; }

    /// <summary>
    /// XML document service for file path resolution. Set after construction by MainWindowViewModel.
    /// </summary>
    public IXmlDocumentService? XmlDocumentService { get; set; }

    /// <summary>
    /// The schema definition for this file type - now accessed through Context.
    /// </summary>
    public SchemaDefinition? Schema => Context.Schema;

    /// <summary>
    /// Whether this file type has nested variations (e.g., equipment sets with multiple variations per roster).
    /// Used to show/hide variation-specific menu items.
    /// </summary>
    public bool HasNestedVariations => Schema?.HasNestedVariations == true;

    /// <summary>
    /// Gets the field definition for a column, if schema is available.
    /// </summary>
    public FieldDefinition? GetFieldDefinition(string columnName)
    {
        return Schema?.GetField(columnName);
    }

    /// <summary>
    /// Gets available IDs for autocomplete in a cross-reference field - now accessed through Context.
    /// </summary>
    public IEnumerable<string> GetAvailableIds(string fieldName)
    {
        if (Context.AvailableIds.TryGetValue(fieldName, out var ids))
            return ids;
        return Enumerable.Empty<string>();
    }

    /// <summary>
    /// Gets the display name for a cross-reference ID (e.g., culture name instead of ID).
    /// </summary>
    public string GetDisplayName(string fieldName, string id)
    {
        if (Context.CrossRefDisplayNames.TryGetValue(fieldName, out var displayNames))
        {
            if (displayNames.TryGetValue(id, out var displayName))
                return displayName;
        }
        return id; // Fall back to ID if no display name
    }

    /// <summary>
    /// Gets all display names for a field (ID -> display name mapping).
    /// </summary>
    public Dictionary<string, string>? GetDisplayNames(string fieldName)
    {
        if (Context.CrossRefDisplayNames.TryGetValue(fieldName, out var displayNames))
            return displayNames;
        return null;
    }

    /// <summary>
    /// Collection of validation issues.
    /// </summary>
    public ObservableCollection<ValidationIssue> ValidationIssues { get; } = new();

    /// <summary>
    /// Whether the validation panel is expanded.
    /// </summary>
    [ObservableProperty]
    private bool _isValidationPanelExpanded = false;

    /// <summary>
    /// Number of validation errors.
    /// </summary>
    [ObservableProperty]
    private int _validationErrorCount;

    /// <summary>
    /// Number of validation warnings.
    /// </summary>
    [ObservableProperty]
    private int _validationWarningCount;

    /// <summary>
    /// Summary text for validation status.
    /// </summary>
    public string ValidationSummary
    {
        get
        {
            if (ValidationErrorCount == 0 && ValidationWarningCount == 0)
                return "No issues";
            var parts = new List<string>();
            if (ValidationErrorCount > 0)
                parts.Add($"{ValidationErrorCount} error{(ValidationErrorCount > 1 ? "s" : "")}");
            if (ValidationWarningCount > 0)
                parts.Add($"{ValidationWarningCount} warning{(ValidationWarningCount > 1 ? "s" : "")}");
            return string.Join(", ", parts);
        }
    }

    public FileTabViewModel(string filePath) : this(
        filePath,
        CreateFileEditManager(filePath),
        new UndoRedoService(),
        new CrossReferenceService(),
        new TupleListService(),
        new FilePathResolver())
    {
    }

    private static FileEditManager CreateFileEditManager(string filePath)
    {
        var xmlService = new XmlDocumentService();
        var schemaService = new SchemaService();
        var validationService = new ValidationService();
        var gitValueService = new GitValueService();
        var crossRefService = new CrossReferenceService();
        var tupleListService = new TupleListService();

        var context = new FileEditContext { FilePath = filePath };
        var fileLoaderService = new FileLoaderService(xmlService, gitValueService, crossRefService);
        var fileSaverService = new FileSaverService(xmlService);
        var validationCoordinator = new ValidationCoordinator(validationService);

        return new FileEditManager(
            context,
            schemaService,
            new UndoRedoService(),
            fileLoaderService,
            fileSaverService,
            validationCoordinator,
            crossRefService,
            tupleListService);
    }

    public FileTabViewModel(
        string filePath,
        FileEditManager fileEditManager,
        IUndoRedoService undoRedoService,
        CrossReferenceService crossRefService,
        TupleListService tupleListService,
        FilePathResolver filePathResolver)
    {
        _fileEditManager = fileEditManager;
        _undoRedoService = undoRedoService;
        _crossRefService = crossRefService;
        _tupleListService = tupleListService;
        _filePathResolver = filePathResolver;
        FilePath = filePath;
        Title = Path.GetFileName(filePath);

        // Subscribe to validation manager changes
        ValidationManager.IssuesChanged += OnValidationIssuesChanged;

        LoadFile();
        SetupFileWatcher();
    }

    /// <summary>
    /// Loads cross-reference data based on schema definitions.
    /// </summary>
    private void LoadCrossReferences()
    {
        var result = _crossRefService.LoadAllCrossReferences(FilePath, Schema, _filePathResolver);

        // Copy results to local storage and context
        foreach (var kvp in result.CrossRefData)
            _crossRefData[kvp.Key] = kvp.Value;

        foreach (var kvp in result.SourcePaths)
            _crossRefSourcePaths[kvp.Key] = kvp.Value;

        foreach (var kvp in result.AvailableIds)
            Context.AvailableIds[kvp.Key] = kvp.Value;

        foreach (var kvp in result.DisplayNames)
            Context.CrossRefDisplayNames[kvp.Key] = kvp.Value;

        foreach (var kvp in result.Descriptions)
            Context.CrossRefDescriptions[kvp.Key] = kvp.Value;
    }

    /// <summary>
    /// Refreshes cross-reference data from disk.
    /// Call this when another file that this tab references has been saved.
    /// This allows newly added entries in other files to appear in autocomplete.
    /// </summary>
    public void RefreshCrossReferences()
    {
        // Clear existing data
        Context.AvailableIds.Clear();
        Context.CrossRefDisplayNames.Clear();
        Context.CrossRefDescriptions.Clear();
        _crossRefData.Clear();
        _crossRefSourcePaths.Clear();

        // Use service to refresh (clears cache and reloads)
        var result = _crossRefService.RefreshAllCrossReferences(FilePath, Schema, _filePathResolver);

        // Copy results
        foreach (var kvp in result.CrossRefData)
            _crossRefData[kvp.Key] = kvp.Value;

        foreach (var kvp in result.SourcePaths)
            _crossRefSourcePaths[kvp.Key] = kvp.Value;

        foreach (var kvp in result.AvailableIds)
            Context.AvailableIds[kvp.Key] = kvp.Value;

        foreach (var kvp in result.DisplayNames)
            Context.CrossRefDisplayNames[kvp.Key] = kvp.Value;

        foreach (var kvp in result.Descriptions)
            Context.CrossRefDescriptions[kvp.Key] = kvp.Value;

        Console.WriteLine($"[CrossRef] Refreshed cross-references for {Title}");
    }

    /// <summary>
    /// Loads tuple list data based on schema definitions.
    /// </summary>
    private void LoadTupleListData()
    {
        var result = _tupleListService.LoadAllTupleData(FilePath, Schema, _filePathResolver);

        // Copy results to local storage
        foreach (var kvp in result.TupleData)
            _tupleListData[kvp.Key] = kvp.Value;

        foreach (var kvp in result.SourcePaths)
            _tupleListSourcePaths[kvp.Key] = kvp.Value;
    }

    /// <summary>
    /// Gets the tuple list data for a specific field.
    /// </summary>
    public Dictionary<string, List<Dictionary<string, string>>>? GetTupleListData(string fieldName)
    {
        return _tupleListData.TryGetValue(fieldName, out var data) ? data : null;
    }

    /// <summary>
    /// Gets formatted tuple display text for a cell.
    /// </summary>
    public string GetTupleDisplayText(string fieldName, string localKey)
    {
        var fieldDef = Schema?.GetField(fieldName);
        if (fieldDef?.TupleList == null) return "-";

        if (!_tupleListData.TryGetValue(fieldName, out var data)) return "-";

        var tuples = _tupleListService.GetTuples(data, localKey);
        return _tupleListService.FormatTuplesForDisplay(tuples, fieldDef.TupleList);
    }

    /// <summary>
    /// Gets the tuples for a specific entry and field.
    /// </summary>
    public List<Dictionary<string, string>> GetTuples(string fieldName, string localKey)
    {
        if (!_tupleListData.TryGetValue(fieldName, out var data))
            return new List<Dictionary<string, string>>();

        return _tupleListService.GetTuples(data, localKey);
    }

    /// <summary>
    /// Saves tuple data to the source XML file.
    /// </summary>
    /// <param name="fieldName">The tuple list field name (e.g., "ext_DamageProportions")</param>
    /// <param name="localKey">The local key value (e.g., troop ID)</param>
    /// <param name="tuples">The tuple data to save</param>
    /// <returns>True if save was successful</returns>
    public bool SaveTupleData(string fieldName, string localKey, List<Dictionary<string, string>> tuples)
    {
        var fieldDef = Schema?.GetField(fieldName);
        if (fieldDef?.TupleList == null)
        {
            Console.WriteLine($"[TupleList] No tuple list config for field: {fieldName}");
            return false;
        }

        if (!_tupleListSourcePaths.TryGetValue(fieldName, out var sourceFilePath))
        {
            Console.WriteLine($"[TupleList] No source file path cached for field: {fieldName}");
            return false;
        }

        // Save to XML
        var success = _tupleListService.SaveTupleData(sourceFilePath, fieldDef.TupleList, localKey, tuples);

        if (success)
        {
            // Update the in-memory cache
            if (_tupleListData.TryGetValue(fieldName, out var data))
            {
                data[localKey] = tuples;
            }
        }

        return success;
    }

    /// <summary>
    /// Updates a cross-reference field value and writes back to the source file.
    /// </summary>
    /// <param name="fieldName">The cross-reference field name (e.g., "ItemTraits")</param>
    /// <param name="localKey">The local key value (e.g., item ID)</param>
    /// <param name="newValues">The new values (comma-separated will be split)</param>
    /// <returns>True if update was successful</returns>
    public bool UpdateCrossReferenceValue(string fieldName, string localKey, string newValues)
    {
        var fieldDef = Schema?.GetField(fieldName);
        if (fieldDef?.CrossReference == null)
        {
            Console.WriteLine($"[CrossRef] No cross-reference config for field: {fieldName}");
            return false;
        }

        if (!_crossRefSourcePaths.TryGetValue(fieldName, out var sourceFilePath))
        {
            // Try to find the source file dynamically if not cached
            var baseDir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(baseDir) && !string.IsNullOrEmpty(fieldDef.CrossReference.SourceFile))
            {
                sourceFilePath = _filePathResolver.FindSourceFile(baseDir, fieldDef.CrossReference.SourceFile);
                if (sourceFilePath != null)
                {
                    _crossRefSourcePaths[fieldName] = sourceFilePath;
                    Console.WriteLine($"[CrossRef] Dynamically resolved source file for {fieldName}: {sourceFilePath}");
                }
            }

            if (sourceFilePath == null)
            {
                Console.WriteLine($"[CrossRef] No source file path cached or found for field: {fieldName}");
                return false;
            }
        }

        // Parse the comma-separated values
        var valueList = (newValues ?? "")
            .Split(new[] { ',', ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.Trim())
            .Where(v => !string.IsNullOrEmpty(v))
            .ToList();

        Console.WriteLine($"[CrossRef] Updating {fieldName} for {localKey}: {string.Join(", ", valueList)}");

        // Look up the schema for the source file to get its compactFormat setting
        var sourceFileName = Path.GetFileName(sourceFilePath);
        var schemaService = new SchemaService();
        var sourceSchema = schemaService.GetSchema(sourceFileName);
        var compactFormat = sourceSchema?.CompactFormat ?? true; // Default to compact if schema not found

        // Update the source file
        var success = _crossRefService.UpdateCrossReference(sourceFilePath, fieldDef.CrossReference, localKey, valueList, compactFormat);

        if (success)
        {
            // Update the in-memory cache
            // Ensure the field entry exists in the cache
            if (!_crossRefData.ContainsKey(fieldName))
            {
                _crossRefData[fieldName] = new Dictionary<string, List<string>>();
            }

            if (valueList.Count > 0)
            {
                _crossRefData[fieldName][localKey] = valueList;
            }
            else
            {
                _crossRefData[fieldName].Remove(localKey);
            }
        }

        return success;
    }

    /// <summary>
    /// Gets the description for a cross-reference target item.
    /// </summary>
    public string? GetCrossRefDescription(string fieldName, string itemId)
    {
        if (Context.CrossRefDescriptions.TryGetValue(fieldName, out var descriptions))
        {
            if (descriptions.TryGetValue(itemId, out var description))
            {
                return description;
            }
        }
        return null;
    }

    /// <summary>
    /// Navigates to a cross-referenced entry in another file.
    /// </summary>
    [RelayCommand]
    public void NavigateToReference(string? referenceId)
    {
        if (string.IsNullOrEmpty(referenceId)) return;

        // Find which cross-reference field this belongs to
        foreach (var kvp in Schema?.Fields ?? new Dictionary<string, FieldDefinition>())
        {
            if (kvp.Value.CrossReference != null)
            {
                var config = kvp.Value.CrossReference;
                NavigateToCrossReference?.Invoke(this, new CrossReferenceNavigationEventArgs(
                    config.GetAllTargetFiles(),
                    config.TargetKeyField,
                    referenceId
                ));
                return;
            }
        }
    }

    /// <summary>
    /// Navigates to a cross-referenced entry using the specific field's configuration.
    /// </summary>
    public void NavigateToReferenceForField(string fieldName, string referenceId)
    {
        if (string.IsNullOrEmpty(referenceId)) return;

        var fieldDef = Schema?.GetField(fieldName);
        if (fieldDef?.CrossReference == null)
        {
            Console.WriteLine($"[Navigate] No cross-reference config for field: {fieldName}");
            return;
        }

        var config = fieldDef.CrossReference;
        var isReverseCrossRef = fieldDef.Type == "reverseCrossReference";

        // For reverse cross-references, navigate to the SOURCE file (e.g., lords file)
        // For forward cross-references, navigate to the TARGET file (e.g., traits file)
        List<string> navigationFiles;
        string keyField;

        if (isReverseCrossRef && !string.IsNullOrEmpty(config.SourceFile))
        {
            // Reverse: UsedBy shows lord IDs, clicking should go to the lords file
            navigationFiles = new List<string> { config.SourceFile };
            keyField = config.SourceKeyField ?? "id";
            Console.WriteLine($"[Navigate] Reverse cross-ref: sourceFile={config.SourceFile}, sourceKeyField={keyField}");
        }
        else
        {
            // Forward: normal cross-reference to target file
            navigationFiles = config.GetAllTargetFiles().ToList();
            keyField = config.TargetKeyField;
        }

        // Strip the prefix before navigating (e.g., "SkillSet.tor_skills_level21" -> "tor_skills_level21")
        var targetValue = referenceId;
        if (!string.IsNullOrEmpty(config.PrefixToStrip) &&
            targetValue.StartsWith(config.PrefixToStrip, StringComparison.OrdinalIgnoreCase))
        {
            targetValue = targetValue.Substring(config.PrefixToStrip.Length);
        }

        Console.WriteLine($"[Navigate] Using config: files=[{string.Join(", ", navigationFiles)}], keyField={keyField}, value={targetValue}");

        NavigateToCrossReference?.Invoke(this, new CrossReferenceNavigationEventArgs(
            navigationFiles,
            keyField,
            targetValue
        ));
    }

    /// <summary>
    /// Runs validation on all rows and updates the ValidationIssues collection.
    /// </summary>
    /// <summary>
    /// Called when ValidationManager issues change - updates the UI.
    /// </summary>
    private void OnValidationIssuesChanged(object? sender, EventArgs e)
    {
        // Update the observable collection from the manager
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            ValidationIssues.Clear();
            foreach (var issue in ValidationManager.Issues)
            {
                ValidationIssues.Add(issue);
            }

            ValidationErrorCount = ValidationManager.ErrorCount;
            ValidationWarningCount = ValidationManager.WarningCount;
            OnPropertyChanged(nameof(ValidationSummary));
        });
    }

    [RelayCommand]
    public void RunValidation()
    {
        // Run validation asynchronously on background thread
        Task.Run(() => _fileEditManager.RunValidationAsync());
    }

    // RunValidationAsync, ValidateUpgradeTargetsAsync, ValidateSkillTemplateTiersAsync, and
    // ValidateCrossReferencesAsync are now handled by ValidationCoordinator


    /// <summary>
    /// Navigates to the row with the specified validation issue.
    /// </summary>
    [RelayCommand]
    public void NavigateToIssue(ValidationIssue? issue)
    {
        if (issue == null) return;

        SelectedIndex = issue.RowIndex;
        // The DataGrid should auto-scroll to the selected row
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
        // Clear all caches to force fresh reload
        _crossRefData.Clear();
        _crossRefSourcePaths.Clear();
        _crossRefService.ClearCache();
        FilePathResolver.ClearCache();

        LoadFile();
        _undoRedoService.Clear();
        Context.HasUnsavedChanges = false;
        HasUnsavedChanges = false;

        // Force column regeneration and UI refresh
        InvalidateColumns();
        OnPropertyChanged(nameof(Rows));
    }

    /// <summary>
    /// Updates the Title to use the schema display name with all related filenames in parenthesis.
    /// </summary>
    private void UpdateTitle()
    {
        var fileName = Path.GetFileName(FilePath);

        if (Schema != null && !string.IsNullOrEmpty(Schema.DisplayName))
        {
            // Collect all related files from the schema
            var relatedFiles = new List<string> { fileName };

            // Add additional source files
            if (Schema.AdditionalSourceFiles != null)
            {
                foreach (var sourceFile in Schema.AdditionalSourceFiles)
                {
                    if (!string.IsNullOrEmpty(sourceFile.FileName) && !relatedFiles.Contains(sourceFile.FileName))
                    {
                        relatedFiles.Add(sourceFile.FileName);
                    }
                }
            }

            // Add merged data file
            if (Schema.MergedDataFile != null && !string.IsNullOrEmpty(Schema.MergedDataFile.FileName))
            {
                if (!relatedFiles.Contains(Schema.MergedDataFile.FileName))
                {
                    relatedFiles.Add(Schema.MergedDataFile.FileName);
                }
            }

            // Add linked file
            if (Schema.LinkedFile != null && !string.IsNullOrEmpty(Schema.LinkedFile.FileName))
            {
                if (!relatedFiles.Contains(Schema.LinkedFile.FileName))
                {
                    relatedFiles.Add(Schema.LinkedFile.FileName);
                }
            }

            // Use schema display name with all filenames in parenthesis
            Title = $"{Schema.DisplayName} ({string.Join(", ", relatedFiles)})";
        }
        else
        {
            // Fallback to just the filename
            Title = fileName;
        }
    }

    private void LoadFile()
    {
        try
        {
            // Delegate to FileEditManager for loading
            _fileEditManager.LoadFileAsync(FilePath).Wait();

            // Copy state from Context to ViewModel properties
            HasError = Context.HasError;
            ErrorMessage = Context.ErrorMessage;
            HasUnsavedChanges = Context.HasUnsavedChanges;

            // Update title to use display name from schema
            UpdateTitle();

            if (!HasError)
            {
                // Load cross-reference data now that schema and file are loaded
                LoadCrossReferences();

                // Load tuple list data
                LoadTupleListData();

                // Populate cross-reference values for all rows
                foreach (var row in Rows)
                {
                    var entry = row.XmlEntry;
                    PopulateCrossReferenceValues(row, entry);
                }

                // Subscribe rows to cell change events for undo/redo and auto-fill
                SubscribeRowEvents();

                // Run validation on file load
                RunValidation();
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            // Unwrap AggregateException from .Wait() to get the real error
            var actualException = ex is AggregateException aggEx ? aggEx.InnerException ?? ex : ex;
            ErrorMessage = $"Error loading file: {actualException.Message}";
            Context.HasError = true;
            Context.ErrorMessage = actualException.Message;
        }
    }

    /// <summary>
    /// Subscribes all rows to the CellValueChanged event for undo/redo tracking.
    /// </summary>
    private void SubscribeRowEvents()
    {
        foreach (var row in Rows)
        {
            row.CellValueChanged -= OnCellValueChanged; // Unsubscribe first to avoid duplicates
            row.CellValueChanged += OnCellValueChanged;
        }
    }

    // LoadMergedFiles, MergeDataFromFile, LoadEquipmentSetVariations, CreateEquipmentRow,
    // DiscoverColumns, and CreateRows are now handled by FileLoaderService

    /// <summary>
    /// Gets the git committed values for an entry, if available - now accessed through Context.
    /// </summary>
    public Dictionary<string, string>? GetGitCommittedValues(string entryId)
    {
        return Context.GitCommittedValues.TryGetValue(entryId, out var values) ? values : null;
    }

    /// <summary>
    /// Populates cross-reference column values for a row.
    /// </summary>
    private void PopulateCrossReferenceValues(EntryRowViewModel row, XmlEntry entry)
    {
        if (Schema == null) return;

        foreach (var kvp in Schema.Fields)
        {
            var fieldName = kvp.Key;
            var fieldDef = kvp.Value;

            if (fieldDef.CrossReference != null && _crossRefData.TryGetValue(fieldName, out var crossRefLookup))
            {
                var config = fieldDef.CrossReference;

                // Get the local key value (e.g., the item's id)
                var localKeyAttr = entry.GetAttribute(config.LocalKeyField);
                var localKey = localKeyAttr?.RawValue ?? "";

                if (!string.IsNullOrEmpty(localKey) && crossRefLookup.TryGetValue(localKey, out var values))
                {
                    // Format the values as a comma-separated string
                    var displayValue = string.Join(", ", values);
                    // Use SetOriginalValue to track modifications correctly
                    row.SetOriginalValue(fieldName, displayValue);
                }
            }
        }
    }

    /// <summary>
    /// Handles cell value changes. Override in subclasses for custom behavior.
    /// </summary>
    protected virtual void OnCellValueChanged(object? sender, CellValueChangedEventArgs e)
    {
        if (sender is not EntryRowViewModel rowVm) return;
        if (Context.Document == null) return;

        // Check if this is a nested field
        var fieldDef = GetFieldDefinition(e.ColumnName);
        var nestedPath = (fieldDef?.Nested == true) ? fieldDef.NestedPath : null;

        // Create and execute an edit command
        var command = new CellEditCommand(rowVm, e.ColumnName, e.OldValue, e.NewValue, nestedPath);

        // Sync the XmlEntry with the new value immediately
        // (AlreadyExecutedCommand skips Execute() on first call, so we must sync here)
        SyncXmlEntry(rowVm, e.ColumnName, e.NewValue, nestedPath);

        // Don't use Execute() here since the value is already changed in the UI
        // Just push to undo stack for undo/redo support
        _undoRedoService.Execute(new AlreadyExecutedCommand(command));

        // Handle auto-fill fields
        ApplyAutoFill(rowVm, e.ColumnName, e.NewValue);

        MarkAsModified();
    }

    /// <summary>
    /// Protected handler wrapper for subclasses to subscribe to cell changes.
    /// </summary>
    protected void OnCellValueChangedHandler(object? sender, CellValueChangedEventArgs e)
    {
        OnCellValueChanged(sender, e);
    }

    /// <summary>
    /// Virtual method for handling special row deletion logic.
    /// Override in subclasses for custom behavior (e.g., equipment set variations).
    /// </summary>
    /// <returns>True if the deletion was handled, false to use default deletion logic.</returns>
    protected virtual bool HandleRowDeletion(EntryRowViewModel rowToDelete)
    {
        // Base implementation does nothing - override in subclasses
        return false;
    }

    /// <summary>
    /// Syncs a cell value change to the underlying XmlEntry.
    /// This is needed because AlreadyExecutedCommand skips Execute() on first call,
    /// so we must manually update the XmlEntry when the UI changes a value.
    /// </summary>
    private void SyncXmlEntry(EntryRowViewModel rowVm, string columnName, string value, string? nestedPath)
    {
        // Handle nested fields
        if (!string.IsNullOrEmpty(nestedPath))
        {
            rowVm.XmlEntry.SetNestedValue(nestedPath, value);
            return;
        }

        // Check if this is a linked field (stored in metadata, not main XML)
        var fieldDef = GetFieldDefinition(columnName);
        var isLinkedField = fieldDef?.LinkedField == true;

        var attr = rowVm.XmlEntry.GetAttribute(columnName);
        if (attr != null)
        {
            var rawValue = LocalizationHelper.Wrap(attr.LocalizationKey, value);
            rowVm.XmlEntry.SetAttributeValue(columnName, rawValue);
            if (isLinkedField)
            {
                Console.WriteLine($"[SyncXmlEntry] Updated linked field {rowVm.XmlEntry.Id}.{columnName} = '{value}' (had attr)");
            }
        }
        else
        {
            // New attribute - add it directly without localization wrapping
            rowVm.XmlEntry.SetAttributeValue(columnName, value);
            if (isLinkedField)
            {
                Console.WriteLine($"[SyncXmlEntry] Created linked field {rowVm.XmlEntry.Id}.{columnName} = '{value}' (new attr)");
            }
        }

        // Verify the value was set
        if (isLinkedField)
        {
            var verify = rowVm.XmlEntry.GetAttributeValue(columnName);
            Console.WriteLine($"[SyncXmlEntry] Verify: {rowVm.XmlEntry.Id}.{columnName} = '{verify}'");
        }
    }

    /// <summary>
    /// Auto-fills dependent fields based on schema autoFillFrom rules.
    /// </summary>
    private void ApplyAutoFill(EntryRowViewModel rowVm, string changedColumn, string? newValue)
    {
        if (Schema == null) return;

        // Find fields that depend on the changed column
        foreach (var kvp in Schema.Fields)
        {
            var fieldDef = kvp.Value;
            if (fieldDef.AutoFillFrom != null &&
                fieldDef.AutoFillFrom.Equals(changedColumn, StringComparison.OrdinalIgnoreCase))
            {
                // Auto-fill this field based on the source value
                var autoValue = ConvertToAutoFillValue(newValue, kvp.Key, changedColumn);
                if (autoValue != null)
                {
                    var oldValue = rowVm[kvp.Key];
                    rowVm[kvp.Key] = autoValue;
                    Console.WriteLine($"[AutoFill] {kvp.Key} = {autoValue} (from {changedColumn}={newValue})");
                }
            }
        }
    }

    /// <summary>
    /// Converts a source value to the auto-fill target value.
    /// Handles bidirectional conversion between Type and subtype.
    /// </summary>
    private static string? ConvertToAutoFillValue(string? sourceValue, string targetField, string sourceField)
    {
        if (string.IsNullOrEmpty(sourceValue))
            return null;

        // Type → subtype: convert PascalCase to snake_case
        // e.g., "HeadArmor" → "head_armor"
        if (targetField.Equals("subtype", StringComparison.OrdinalIgnoreCase))
        {
            return StringCaseConverter.ConvertPascalToSnakeCase(sourceValue);
        }

        // subtype → Type: convert snake_case to PascalCase
        // e.g., "head_armor" → "HeadArmor"
        if (targetField.Equals("Type", StringComparison.OrdinalIgnoreCase) ||
            sourceField.Equals("subtype", StringComparison.OrdinalIgnoreCase))
        {
            return StringCaseConverter.ConvertSnakeToPascalCase(sourceValue);
        }

        // Default: use source value as-is
        return sourceValue;
    }

    public void Save()
    {
        if (Context.Document == null)
            return;

        _isSaving = true;
        try
        {
            // Delegate to FileEditManager for save logic
            _fileEditManager.Save();

            // Copy state from Context to ViewModel properties
            HasUnsavedChanges = Context.HasUnsavedChanges;
            HasError = Context.HasError;
            ErrorMessage = Context.ErrorMessage;

            // After save, all entries are no longer "new" - they're in the file now
            // Also mark modified fields as "saved" (for orange/green text indicator)
            Context.NewEntries.Clear();
            foreach (var row in Rows)
            {
                // MarkFieldsAsSaved must be called BEFORE IsNew = false
                // so that WasNew gets set correctly
                row.MarkFieldsAsSaved();
                row.IsNew = false;
            }

            // Force UI refresh to update cell colors
            ForceRowsRefresh();
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Error saving file: {ex.Message}";
            Context.HasError = true;
            Context.ErrorMessage = ex.Message;
        }
        finally
        {
            // Delay resetting flag to avoid catching our own save event
            Task.Delay(500).ContinueWith(_ => _isSaving = false);
        }
    }

    // SaveMergedFiles, CreateDocumentFromEntries, SaveMergedDataFile, GenerateCivilianClones,
    // and SyncChangesToXml are now handled by FileSaverService

    /// <summary>
    /// Reloads the file from disk, picking up any external changes (e.g., git discards).
    /// This also reloads all merged data files like tor_heroes.xml.
    /// </summary>
    [RelayCommand]
    public void Reload()
    {
        Console.WriteLine($"[Reload] Reloading {FilePath}");
        IsLoading = true;

        try
        {
            // Clear undo/redo history since we're starting fresh
            _undoRedoService.Clear();

            // Clear all caches to force fresh reload
            _crossRefData.Clear();
            _crossRefSourcePaths.Clear();
            _crossRefService.ClearCache();
            FilePathResolver.ClearCache();

            // Clear the context
            Context.Clear();
            Context.FilePath = FilePath;
            Context.Schema = Schema;

            // Reload the file (this also reloads merged data files)
            LoadFile();

            // Force column regeneration and UI refresh
            InvalidateColumns();
            OnPropertyChanged(nameof(Rows));
            OnPropertyChanged(nameof(ColumnNames));

            Console.WriteLine($"[Reload] Reload complete, {Rows.Count} rows loaded");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private CancellationTokenSource? _validationDebounceToken;

    public void MarkAsModified()
    {
        HasUnsavedChanges = true;
        if (Context.Document != null)
        {
            Context.Document.HasUnsavedChanges = true;
        }

        // Debounced validation - wait 500ms after last edit before validating
        _validationDebounceToken?.Cancel();
        _validationDebounceToken = new CancellationTokenSource();
        var token = _validationDebounceToken.Token;

        Task.Delay(500, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(RunValidation);
            }
        }, token);
    }

    /// <summary>
    /// Stores the copied row data (column name -> value).
    /// </summary>
    private Dictionary<string, string>? _copiedRowData;

    /// <summary>
    /// The row currently selected for copy.
    /// </summary>
    private EntryRowViewModel? _copiedRow;

    /// <summary>
    /// Stores a single copied cell value (for cell-level copy/paste).
    /// </summary>
    private string? _copiedCellValue;

    /// <summary>
    /// The column name of the copied cell (null = row mode, non-null = cell mode).
    /// </summary>
    private string? _copiedCellColumn;

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
        if (Context.Document == null) return;

        var xmlEntryCollection = new ObservableCollection<XmlEntry>(XmlEntries);

        var command = new AddRowCommand(Context.Document, xmlEntryCollection, insertIndex);
        _undoRedoService.Execute(command);

        // Sync and insert the new row
        InsertNewRowFromCommand(xmlEntryCollection, insertIndex);
    }

    /// <summary>
    /// Common logic for inserting a newly created row after add/duplicate commands.
    /// Syncs the XmlEntries collection and inserts a row directly (preserves scroll position).
    /// </summary>
    private void InsertNewRowFromCommand(ObservableCollection<XmlEntry> xmlEntryCollection, int insertIndex)
    {
        // Sync collections
        XmlEntries.Clear();
        XmlEntries.AddRange(xmlEntryCollection);

        // Mark the new entry as new
        string newEntryId = "";
        if (insertIndex < XmlEntries.Count)
        {
            newEntryId = XmlEntries[insertIndex].GetAttribute("id")?.DisplayValue ?? "";
            if (!string.IsNullOrEmpty(newEntryId))
            {
                Context.NewEntries.Add(newEntryId);
            }
        }

        // Create and insert the new row directly (preserves scroll position)
        var newEntry = XmlEntries[insertIndex];
        var gitValues = GetGitCommittedValues(newEntryId);
        var newRow = new EntryRowViewModel(newEntry, ColumnNames, gitValues);
        newRow.IsNew = true;
        newRow.IsIdLocked = false; // New entries can have their ID edited
        newRow.RowNumber = insertIndex + 1;

        // Populate values from entry
        foreach (var columnName in ColumnNames)
        {
            var fieldDef = GetFieldDefinition(columnName);
            string? value = null;

            if (fieldDef?.Nested == true && !string.IsNullOrEmpty(fieldDef.NestedPath))
            {
                value = newEntry.GetNestedValue(fieldDef.NestedPath);
            }
            else if (fieldDef?.CrossReference == null)
            {
                var attr = newEntry.GetAttribute(columnName);
                value = attr?.DisplayValue;
            }

            if (value != null)
            {
                newRow.SetValueWithoutNotify(columnName, value);
            }
        }

        // Populate cross-reference values
        PopulateCrossReferenceValues(newRow, newEntry);

        // Subscribe to change events
        newRow.CellValueChanged += OnCellValueChanged;

        // Suppress scroll-into-view - the new row is already visible next to the selected row
        SuppressScrollIntoView = true;
        try
        {
            // Insert the row directly instead of recreating all rows
            Rows.Insert(insertIndex, newRow);

            // Update row numbers for subsequent rows
            for (int i = insertIndex + 1; i < Rows.Count; i++)
            {
                Rows[i].RowNumber = i + 1;
            }

            MarkAsModified();

            // Select the new row
            SelectedIndex = insertIndex;
        }
        finally
        {
            SuppressScrollIntoView = false;
        }
    }

    /// <summary>
    /// Deletes the currently selected row.
    /// For equipment set variations, this deletes just the variation, not the entire roster.
    /// </summary>
    [RelayCommand]
    public void DeleteRow()
    {
        if (Context.Document == null || SelectedIndex < 0 || SelectedIndex >= DisplayRows.Count)
            return;

        // Get row from DisplayRows (works correctly whether filtered or not)
        var rowToDelete = DisplayRows[SelectedIndex];

        // Handle special row deletion (virtual, overridden in EquipmentFileTabViewModel)
        if (HandleRowDeletion(rowToDelete))
        {
            return;
        }

        // Find actual indices in the source collections (different from SelectedIndex when filtered)
        var actualRowIndex = Rows.IndexOf(rowToDelete);
        if (actualRowIndex < 0)
            return;

        var xmlEntryCollection = new ObservableCollection<XmlEntry>(XmlEntries);
        var actualXmlIndex = xmlEntryCollection.IndexOf(rowToDelete.XmlEntry);
        if (actualXmlIndex < 0 || actualXmlIndex >= xmlEntryCollection.Count)
            return;

        // Store the row for "removed entries" display (only if not a new entry)
        if (!rowToDelete.IsNew)
        {
            rowToDelete.IsRemoved = true;
            Context.RemovedEntries.Add(rowToDelete);
            Console.WriteLine($"[Removed] Stored removed entry: {rowToDelete["id"]} (total: {Context.RemovedEntries.Count})");
        }

        var entryToDelete = xmlEntryCollection[actualXmlIndex];

        // Remove from new entries tracking
        var entryId = entryToDelete.GetAttribute("id")?.DisplayValue ?? "";
        if (!string.IsNullOrEmpty(entryId))
        {
            Context.NewEntries.Remove(entryId);
        }

        var command = new DeleteRowCommand(Context.Document, xmlEntryCollection, entryToDelete);
        _undoRedoService.Execute(command);

        // Sync collections
        XmlEntries.Clear();
        XmlEntries.AddRange(xmlEntryCollection);

        // Remove from the source Rows collection using actual index
        Rows.RemoveAt(actualRowIndex);

        // Update row numbers for remaining rows
        for (int i = actualRowIndex; i < Rows.Count; i++)
        {
            Rows[i].RowNumber = i + 1;
        }

        // Also remove from FilteredRows if filtering is active
        if (FilteredRows != null)
        {
            FilteredRows.Remove(rowToDelete);
        }

        // If ShowRemovedEntries is true, immediately re-insert at original position
        if (ShowRemovedEntries && !rowToDelete.IsNew)
        {
            RefreshRowsWithRemovedEntries();
        }

        // Notify cells to refresh styling
        RequestCellRefresh();
        MarkAsModified();
    }

    /// <summary>
    /// Duplicates the currently selected row.
    /// </summary>
    [RelayCommand]
    public virtual void DuplicateRow()
    {
        if (Context.Document == null || SelectedIndex < 0 || SelectedIndex >= DisplayRows.Count)
            return;

        // Get row from DisplayRows (works correctly whether filtered or not)
        var rowToDuplicate = DisplayRows[SelectedIndex];

        // Find actual index in XmlEntries (different from SelectedIndex when filtered)
        var xmlEntryCollection = new ObservableCollection<XmlEntry>(XmlEntries);
        var actualXmlIndex = xmlEntryCollection.IndexOf(rowToDuplicate.XmlEntry);
        if (actualXmlIndex < 0)
            return;

        var entryToDuplicate = xmlEntryCollection[actualXmlIndex];
        var insertIndex = actualXmlIndex + 1;

        var command = new DuplicateRowCommand(Context.Document, xmlEntryCollection, entryToDuplicate);
        _undoRedoService.Execute(command);

        // Sync and insert the new row
        InsertNewRowFromCommand(xmlEntryCollection, insertIndex);
    }

    /// <summary>
    /// Updates row numbers for all rows after modifications.
    /// </summary>
    protected void UpdateRowNumbers()
    {
        for (int i = 0; i < Rows.Count; i++)
        {
            Rows[i].RowNumber = i + 1;
        }
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

        // Notify that copied row data is available
        OnPropertyChanged(nameof(HasCopiedRow));
        OnPropertyChanged(nameof(HasCopiedData));
    }

    /// <summary>
    /// Copies the currently selected row's data, or just the selected cell if in cell mode.
    /// </summary>
    [RelayCommand]
    public void CopyRow()
    {
        if (SelectedIndex < 0 || SelectedIndex >= DisplayRows.Count)
            return;

        var row = DisplayRows[SelectedIndex];

        // Check if a single cell is selected (cell mode) vs entire row (row mode)
        if (!string.IsNullOrEmpty(SelectedColumn))
        {
            // Cell mode: copy just this cell's value
            _copiedCellColumn = SelectedColumn;
            _copiedCellValue = row[SelectedColumn];
            _copiedRowData = null; // Clear row data

            // Clear previous row copy selection
            if (_copiedRow != null)
            {
                _copiedRow.IsSelectedForCopy = false;
                _copiedRow = null;
            }

            Console.WriteLine($"[Copy] Cell mode: {SelectedColumn} = {_copiedCellValue}");
        }
        else
        {
            // Row mode: copy entire row
            _copiedCellColumn = null;
            _copiedCellValue = null;
            SelectRowForCopy(row);
            Console.WriteLine($"[Copy] Row mode: {_copiedRowData?.Count} columns");
        }

        OnPropertyChanged(nameof(HasCopiedData));
    }

    /// <summary>
    /// Pastes copied data onto the currently selected row or cell.
    /// In cell mode: pastes to the selected cell only.
    /// In row mode: pastes all columns from the copied row.
    /// </summary>
    [RelayCommand]
    public void PasteRow()
    {
        if (SelectedIndex < 0 || SelectedIndex >= DisplayRows.Count)
            return;

        var targetRow = DisplayRows[SelectedIndex];

        // Check if we have cell data to paste
        if (_copiedCellColumn != null && _copiedCellValue != null)
        {
            // Cell mode: paste to the currently selected cell
            var targetColumn = SelectedColumn ?? _copiedCellColumn;

            // Skip ID for existing (non-new) entries
            if (targetColumn.Equals("id", StringComparison.OrdinalIgnoreCase) && !targetRow.IsNew)
            {
                Console.WriteLine($"[Paste] Skipped - cannot paste to ID column of existing entry");
                return;
            }

            targetRow[targetColumn] = _copiedCellValue;
            Console.WriteLine($"[Paste] Cell mode: {targetColumn} = {_copiedCellValue}");
        }
        else if (_copiedRowData != null)
        {
            // Row mode: paste all columns
            foreach (var kvp in _copiedRowData)
            {
                // Skip ID for existing (non-new) entries
                if (kvp.Key.Equals("id", StringComparison.OrdinalIgnoreCase) && !targetRow.IsNew)
                    continue;

                // Set the value (this will trigger CellValueChanged for undo support)
                targetRow[kvp.Key] = kvp.Value;
            }
            Console.WriteLine($"[Paste] Row mode: {_copiedRowData.Count} columns");
        }
        else
        {
            // Nothing to paste
            return;
        }

        // Force UI update by removing and re-adding the row at the same position
        // Find actual index in Rows (different from SelectedIndex when filtered)
        var actualRowIndex = Rows.IndexOf(targetRow);
        if (actualRowIndex >= 0)
        {
            Rows.RemoveAt(actualRowIndex);
            Rows.Insert(actualRowIndex, targetRow);
        }

        // Also update FilteredRows if filtering is active
        if (FilteredRows != null)
        {
            var filteredIndex = FilteredRows.IndexOf(targetRow);
            if (filteredIndex >= 0)
            {
                FilteredRows.RemoveAt(filteredIndex);
                FilteredRows.Insert(filteredIndex, targetRow);
            }
        }

        MarkAsModified();
    }

    /// <summary>
    /// Whether a row has been copied and is ready to paste.
    /// </summary>
    public bool HasCopiedRow => _copiedRowData != null;

    /// <summary>
    /// Whether any data (cell or row) has been copied and is ready to paste.
    /// </summary>
    public bool HasCopiedData => _copiedRowData != null || _copiedCellValue != null;

    /// <summary>
    /// Undoes the last operation.
    /// </summary>
    public void Undo()
    {
        if (!_undoRedoService.CanUndo) return;

        // Suppress scroll-into-view during undo to preserve scroll position
        SuppressScrollIntoView = true;
        try
        {
            _undoRedoService.Undo();
            MarkAsModified();
            // Sync entries from document and refresh rows incrementally (preserves scroll)
            SyncEntriesFromDocument();
            SyncRowsWithEntries();
        }
        finally
        {
            SuppressScrollIntoView = false;
        }
    }

    /// <summary>
    /// Redoes the last undone operation.
    /// </summary>
    public void Redo()
    {
        if (!_undoRedoService.CanRedo) return;

        // Suppress scroll-into-view during redo to preserve scroll position
        SuppressScrollIntoView = true;
        try
        {
            _undoRedoService.Redo();
            MarkAsModified();
            // Sync entries from document and refresh rows incrementally (preserves scroll)
            SyncEntriesFromDocument();
            SyncRowsWithEntries();
        }
        finally
        {
            SuppressScrollIntoView = false;
        }
    }

    /// <summary>
    /// Syncs the Rows collection with XmlEntries incrementally.
    /// Only adds/removes rows that changed, preserving scroll position.
    /// Override in subclasses for specialized sync (e.g., equipment sets).
    /// </summary>
    protected virtual void SyncRowsWithEntries()
    {
        // Build a set of current entry elements for fast lookup
        var entryElements = new HashSet<XElement>(XmlEntries.Select(e => e.OriginalElement));

        // Remove rows whose entries no longer exist
        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            var row = Rows[i];
            if (row.IsRemoved) continue; // Skip removed entries display

            if (!entryElements.Contains(row.XmlEntry.OriginalElement))
            {
                Rows.RemoveAt(i);
            }
        }

        // Build a set of row elements for fast lookup
        var rowElements = new HashSet<XElement>(
            Rows.Where(r => !r.IsRemoved).Select(r => r.XmlEntry.OriginalElement));

        // Add rows for new entries at correct positions
        for (int i = 0; i < XmlEntries.Count; i++)
        {
            var entry = XmlEntries[i];
            if (!rowElements.Contains(entry.OriginalElement))
            {
                // Create a new row for this entry
                var entryId = entry.GetAttribute("id")?.DisplayValue ?? "";
                var isNew = Context.NewEntries.Contains(entryId);
                var gitValues = GetGitCommittedValues(entryId);

                var newRow = new EntryRowViewModel(entry, ColumnNames, gitValues);
                newRow.IsNew = isNew;
                newRow.IsIdLocked = !isNew;

                // Populate values
                foreach (var columnName in ColumnNames)
                {
                    var fieldDef = GetFieldDefinition(columnName);
                    string? value = null;

                    if (fieldDef?.Nested == true && !string.IsNullOrEmpty(fieldDef.NestedPath))
                    {
                        value = entry.GetNestedValue(fieldDef.NestedPath);
                    }
                    else if (fieldDef?.CrossReference == null)
                    {
                        var attr = entry.GetAttribute(columnName);
                        value = attr?.DisplayValue;
                    }

                    if (value != null)
                    {
                        newRow.SetValueWithoutNotify(columnName, value);
                    }
                }

                PopulateCrossReferenceValues(newRow, entry);
                newRow.CellValueChanged += OnCellValueChanged;

                // Insert at correct position
                Rows.Insert(i, newRow);
            }
        }

        // Update row numbers
        for (int i = 0; i < Rows.Count; i++)
        {
            Rows[i].RowNumber = i + 1;
        }

        RequestCellRefresh();
    }

    /// <summary>
    /// Synchronizes XmlEntries from the actual XML document.
    /// Called after undo/redo to ensure the entries collection matches the document.
    /// </summary>
    private void SyncEntriesFromDocument()
    {
        if (Context.Document == null) return;

        var root = Context.Document.Document.Root;
        if (root == null) return;

        var entryElementName = Context.Document.EntryElementName;

        // Rebuild XmlEntries from the current XML elements
        XmlEntries.Clear();
        foreach (var element in root.Elements(entryElementName))
        {
            XmlEntries.Add(new XmlEntry(element));
        }
    }

    /// <summary>
    /// Notifies all cells to refresh their content and styling.
    /// This preserves scroll position unlike clearing/re-adding rows.
    /// </summary>
    private void ForceRowsRefresh()
    {
        // Just trigger cell refresh - cells update their own content via the event
        RequestCellRefresh();
    }

    private void RefreshRows()
    {
        // After undo/redo operations, we need to recreate rows from XmlEntries
        // This is a simplified version that doesn't reload from disk
        RecreateRowsFromEntries();

        // Add removed entries at the end if toggle is on
        if (ShowRemovedEntries)
        {
            foreach (var removedRow in Context.RemovedEntries)
            {
                if (!Rows.Contains(removedRow))
                {
                    Rows.Add(removedRow);
                }
            }
        }

        // Ensure all rows are subscribed to change events
        SubscribeRowEvents();

        // Notify UI of changes
        OnPropertyChanged(nameof(Rows));
    }

    /// <summary>
    /// Recreates row view models from the current XmlEntries collection.
    /// Used after add/delete/duplicate operations.
    /// </summary>
    private void RecreateRowsFromEntries()
    {
        Rows.Clear();
        int rowNum = 1;

        foreach (var entry in XmlEntries)
        {
            var entryId = entry.GetAttribute("id")?.DisplayValue ?? "";
            var isNew = Context.NewEntries.Contains(entryId);
            var gitValues = GetGitCommittedValues(entryId);

            var row = new EntryRowViewModel(entry, ColumnNames, gitValues);
            row.IsNew = isNew;
            row.IsIdLocked = !isNew;
            row.RowNumber = rowNum++;

            // Populate values from entry
            foreach (var columnName in ColumnNames)
            {
                var fieldDef = GetFieldDefinition(columnName);
                string? value = null;

                // Handle nested fields
                if (fieldDef?.Nested == true && !string.IsNullOrEmpty(fieldDef.NestedPath))
                {
                    value = entry.GetNestedValue(fieldDef.NestedPath);
                }
                else if (fieldDef?.CrossReference == null) // Skip cross-ref fields
                {
                    var attr = entry.GetAttribute(columnName);
                    value = attr?.DisplayValue;
                }

                if (value != null)
                {
                    row.SetValueWithoutNotify(columnName, value);
                }
            }

            // Populate cross-reference values
            PopulateCrossReferenceValues(row, entry);

            Rows.Add(row);
        }
    }

    /// <summary>
    /// Refreshes rows to show/hide removed entries based on toggle.
    /// </summary>
    private void RefreshRowsWithRemovedEntries()
    {
        Console.WriteLine($"[Removed] RefreshRowsWithRemovedEntries called, ShowRemovedEntries={ShowRemovedEntries}, count={Context.RemovedEntries.Count}");
        if (ShowRemovedEntries)
        {
            // Insert removed entries at their original positions
            // Sort by RowNumber to insert in correct order
            foreach (var removedRow in Context.RemovedEntries.OrderBy(r => r.RowNumber))
            {
                if (!Rows.Contains(removedRow))
                {
                    // Find insert position: count how many rows have lower RowNumber
                    var insertIndex = 0;
                    for (int i = 0; i < Rows.Count; i++)
                    {
                        if (Rows[i].RowNumber < removedRow.RowNumber)
                            insertIndex = i + 1;
                        else
                            break;
                    }
                    insertIndex = Math.Min(insertIndex, Rows.Count);
                    Rows.Insert(insertIndex, removedRow);
                    Console.WriteLine($"[Removed] Inserted removed entry at {insertIndex}: {removedRow["id"]} (RowNumber={removedRow.RowNumber})");
                }
            }
        }
        else
        {
            // Remove the removed entries from display
            foreach (var removedRow in Context.RemovedEntries.ToList())
            {
                Rows.Remove(removedRow);
            }
        }
    }

    public void Dispose()
    {
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        _filterCts = null;

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
/// Event args for navigating to a cross-referenced entry.
/// </summary>
public class CrossReferenceNavigationEventArgs : EventArgs
{
    /// <summary>
    /// The target XML file names to search (e.g., ["tor_armors.xml", "tor_meleeweapons.xml"]).
    /// Files are searched in order until the entry is found.
    /// </summary>
    public IReadOnlyList<string> TargetFiles { get; }

    /// <summary>
    /// The key field in the target file (e.g., "ItemTraitStringId").
    /// </summary>
    public string TargetKeyField { get; }

    /// <summary>
    /// The value to search for in the target file.
    /// </summary>
    public string TargetValue { get; }

    public CrossReferenceNavigationEventArgs(IEnumerable<string> targetFiles, string targetKeyField, string targetValue)
    {
        TargetFiles = targetFiles.ToList();
        TargetKeyField = targetKeyField;
        TargetValue = targetValue;
    }
}

/// <summary>
/// Event args for cell selection changes.
/// </summary>
public class CellSelectionEventArgs : EventArgs
{
    /// <summary>
    /// The selected row index (-1 if no selection).
    /// </summary>
    public int RowIndex { get; }

    /// <summary>
    /// The selected column name (null if entire row is selected).
    /// </summary>
    public string? ColumnName { get; }

    /// <summary>
    /// Whether an entire row is selected (ColumnName is null).
    /// </summary>
    public bool IsRowSelection => ColumnName == null;

    public CellSelectionEventArgs(int rowIndex, string? columnName)
    {
        RowIndex = rowIndex;
        ColumnName = columnName;
    }
}
