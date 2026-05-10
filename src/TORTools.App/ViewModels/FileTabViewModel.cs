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

    /// <summary>
    /// Convenience accessor for the file edit context.
    /// </summary>
    private FileEditContext Context => _fileEditManager.Context;

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
    /// Triggers a refresh of all cell content and styling.
    /// Call this after any operation that changes data or state (save, undo, redo, etc.).
    /// </summary>
    public void RequestCellRefresh()
    {
        CellRefreshRequested?.Invoke(this, EventArgs.Empty);
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
    /// Whether ID editing is locked for all rows (default true).
    /// Toggle via the lock icon in the ID column header.
    /// </summary>
    [ObservableProperty]
    private bool _isIdColumnLocked = true;

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
    /// Filtered rows based on FilterText. If empty, returns null.
    /// </summary>
    public ObservableCollection<EntryRowViewModel>? FilteredRows { get; private set; }

    /// <summary>
    /// Rows to display in the grid - returns FilteredRows if filtering, otherwise Rows.
    /// </summary>
    public ObservableCollection<EntryRowViewModel> DisplayRows => FilteredRows ?? Rows;

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(FilterText))
        {
            FilteredRows = null; // Use Rows directly
            OnPropertyChanged(nameof(FilteredRows));
            OnPropertyChanged(nameof(DisplayRows));
            return;
        }

        var searchTerms = FilterText.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var filtered = Rows.Where(row =>
        {
            // Check if any cell contains all search terms
            foreach (var term in searchTerms)
            {
                bool termFound = false;
                foreach (var colName in ColumnNames)
                {
                    var cellValue = row[colName]?.ToLowerInvariant() ?? "";
                    if (cellValue.Contains(term))
                    {
                        termFound = true;
                        break;
                    }
                }
                if (!termFound) return false;
            }
            return true;
        }).ToList();

        FilteredRows = new ObservableCollection<EntryRowViewModel>(filtered);
        OnPropertyChanged(nameof(FilteredRows));
        OnPropertyChanged(nameof(DisplayRows));
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
    /// The schema definition for this file type - now accessed through Context.
    /// </summary>
    public SchemaDefinition? Schema => Context.Schema;

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
        if (Schema == null) return;

        var baseDir = Path.GetDirectoryName(FilePath);
        if (string.IsNullOrEmpty(baseDir)) return;

        // Find all fields with crossReference configuration
        foreach (var kvp in Schema.Fields)
        {
            var fieldName = kvp.Key;
            var fieldDef = kvp.Value;

            if (fieldDef.CrossReference != null)
            {
                var config = fieldDef.CrossReference;

                // Check if this is a "direct" cross-reference (value stored on entry, no source file)
                // vs "indirect" cross-reference (values in a separate mapping file)
                if (string.IsNullOrEmpty(config.SourceFile))
                {
                    // Direct cross-reference: load available IDs from target file for validation/autocomplete
                    var targetFilePath = _filePathResolver.FindSourceFile(baseDir, config.TargetFile);
                    if (targetFilePath != null && !string.IsNullOrEmpty(config.TargetKeyField))
                    {
                        var availableIds = _crossRefService.LoadTargetKeys(targetFilePath, config.TargetKeyField);
                        Context.AvailableIds[fieldName] = availableIds;
                        Console.WriteLine($"[CrossRef] Loaded {availableIds.Count} available IDs for direct crossref {fieldName} from {config.TargetFile}");

                        // Load display names if configured
                        if (!string.IsNullOrEmpty(config.TargetDisplayField))
                        {
                            var displayNames = _crossRefService.LoadTargetDisplayNames(targetFilePath, config.TargetKeyField, config.TargetDisplayField);
                            if (displayNames.Count > 0)
                            {
                                Context.CrossRefDisplayNames[fieldName] = displayNames;
                                Console.WriteLine($"[CrossRef] Loaded {displayNames.Count} display names for {fieldName}");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[CrossRef] Target file not found for direct crossref: {config.TargetFile}");
                    }
                }
                else
                {
                    // Indirect cross-reference: load from mapping file
                    var sourceFilePath = _filePathResolver.FindSourceFile(baseDir, config.SourceFile);
                    if (sourceFilePath != null)
                    {
                        Dictionary<string, List<string>> crossRefData;

                        if (fieldDef.Type == "reverseCrossReference")
                        {
                            // Reverse lookup: trait ID -> list of item IDs that use it
                            crossRefData = _crossRefService.LoadReverseCrossReferences(sourceFilePath, config);
                            Console.WriteLine($"[CrossRef] Loaded {crossRefData.Count} reverse references for {fieldName} from {config.SourceFile}");
                        }
                        else
                        {
                            // Forward lookup: item ID -> list of trait IDs
                            crossRefData = _crossRefService.LoadCrossReferences(sourceFilePath, config);
                            Console.WriteLine($"[CrossRef] Loaded {crossRefData.Count} references for {fieldName} from {config.SourceFile}");

                            // For editable cross-references, load available target IDs for autocomplete
                            var targetFilePath = _filePathResolver.FindSourceFile(baseDir, config.TargetFile);
                            if (targetFilePath != null && !string.IsNullOrEmpty(config.TargetKeyField))
                            {
                                var availableIds = _crossRefService.LoadTargetKeys(targetFilePath, config.TargetKeyField);
                                Context.AvailableIds[fieldName] = availableIds;
                                Console.WriteLine($"[CrossRef] Loaded {availableIds.Count} available IDs for {fieldName} autocomplete");

                                // Also load descriptions from the target file if available
                                var descriptions = _crossRefService.LoadTargetDescriptions(targetFilePath, config.TargetKeyField);
                                if (descriptions.Count > 0)
                                {
                                    Context.CrossRefDescriptions[fieldName] = descriptions;
                                    Console.WriteLine($"[CrossRef] Loaded {descriptions.Count} descriptions for {fieldName}");
                                }
                            }
                        }

                        _crossRefData[fieldName] = crossRefData;
                        _crossRefSourcePaths[fieldName] = sourceFilePath;
                    }
                    else
                    {
                        Console.WriteLine($"[CrossRef] Source file not found: {config.SourceFile}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Loads tuple list data based on schema definitions.
    /// </summary>
    private void LoadTupleListData()
    {
        if (Schema == null) return;

        var baseDir = Path.GetDirectoryName(FilePath);
        if (string.IsNullOrEmpty(baseDir)) return;

        // Find all fields with tupleList configuration
        foreach (var kvp in Schema.Fields)
        {
            var fieldName = kvp.Key;
            var fieldDef = kvp.Value;

            if (fieldDef.TupleList != null)
            {
                var config = fieldDef.TupleList;

                // Find the source file
                var sourceFilePath = _filePathResolver.FindSourceFile(baseDir, config.SourceFile);
                if (sourceFilePath != null)
                {
                    var tupleData = _tupleListService.LoadTupleData(sourceFilePath, config);
                    _tupleListData[fieldName] = tupleData;
                    _tupleListSourcePaths[fieldName] = sourceFilePath;
                    Console.WriteLine($"[TupleList] Loaded {tupleData.Count} entries for {fieldName} from {config.SourceFile}");
                }
                else
                {
                    Console.WriteLine($"[TupleList] Source file not found: {config.SourceFile}");
                }
            }
        }
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
            Console.WriteLine($"[CrossRef] No source file path cached for field: {fieldName}");
            return false;
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
        LoadFile();
        _undoRedoService.Clear();
        Context.HasUnsavedChanges = false;
        HasUnsavedChanges = false;
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

    private void OnCellValueChanged(object? sender, CellValueChangedEventArgs e)
    {
        if (sender is not EntryRowViewModel rowVm) return;
        if (Context.Document == null) return;

        // Handle equipment set variations specially
        if (rowVm.IsEquipmentSetVariation)
        {
            HandleEquipmentSetCellChange(rowVm, e);
            MarkAsModified();
            return;
        }

        // Check if this is a nested field
        var fieldDef = GetFieldDefinition(e.ColumnName);
        var nestedPath = (fieldDef?.Nested == true) ? fieldDef.NestedPath : null;

        // Create and execute an edit command
        var command = new CellEditCommand(rowVm, e.ColumnName, e.OldValue, e.NewValue, nestedPath);

        // Don't use Execute() here since the value is already changed
        // Just push to undo stack
        _undoRedoService.Execute(new AlreadyExecutedCommand(command));

        // Handle auto-fill fields
        ApplyAutoFill(rowVm, e.ColumnName, e.NewValue);

        MarkAsModified();
    }

    /// <summary>
    /// Handles cell changes for equipment set variations.
    /// Updates the nested XML structure accordingly.
    /// </summary>
    private void HandleEquipmentSetCellChange(EntryRowViewModel rowVm, CellValueChangedEventArgs e)
    {
        var variation = rowVm.VariationEntry;
        var roster = rowVm.XmlEntry;
        var columnName = e.ColumnName;
        var newValue = e.NewValue;

        // Equipment slot columns
        var equipmentSlots = Schema?.EquipmentSlots?.Select(s => s.Slot).ToHashSet() ?? new HashSet<string>();

        if (columnName == "culture")
        {
            // Update roster-level culture attribute
            roster.SetAttributeValue("culture", newValue);
            Console.WriteLine($"[EquipmentSet] Updated roster culture to: {newValue}");
        }
        else if (columnName == "civilian" && variation != null)
        {
            // Update variation's civilian attribute
            if (newValue == "true")
            {
                variation.OriginalElement.SetAttributeValue("civilian", "true");
            }
            else
            {
                // Remove the attribute if false (default)
                variation.OriginalElement.SetAttributeValue("civilian", null);
            }
            Console.WriteLine($"[EquipmentSet] Updated civilian to: {newValue}");
        }
        else if (equipmentSlots.Contains(columnName) && variation != null)
        {
            // Update equipment slot
            UpdateEquipmentSlot(variation, columnName, newValue);
        }
    }

    /// <summary>
    /// Updates an equipment slot in a variation element.
    /// </summary>
    private void UpdateEquipmentSlot(XmlEntry variation, string slotName, string? itemId)
    {
        var equipmentElementName = Schema?.EquipmentItemElement ?? "Equipment";

        // Find existing equipment element for this slot
        var existingEquip = variation.Children
            .FirstOrDefault(c => c.ElementName == equipmentElementName &&
                                 c.GetAttributeValue("slot") == slotName);

        if (string.IsNullOrWhiteSpace(itemId))
        {
            // Remove the equipment element if value is cleared
            if (existingEquip != null)
            {
                existingEquip.OriginalElement.Remove();
                variation.Children.Remove(existingEquip);
                Console.WriteLine($"[EquipmentSet] Removed {slotName}");
            }
        }
        else
        {
            if (existingEquip != null)
            {
                // Update existing equipment element
                existingEquip.SetAttributeValue("id", itemId);
                Console.WriteLine($"[EquipmentSet] Updated {slotName} = {itemId}");
            }
            else
            {
                // Create new equipment element
                var newElement = new XElement(equipmentElementName,
                    new XAttribute("slot", slotName),
                    new XAttribute("id", itemId));
                variation.OriginalElement.Add(newElement);
                variation.Children.Add(new XmlEntry(newElement));
                Console.WriteLine($"[EquipmentSet] Added {slotName} = {itemId}");
            }
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

        // Clear undo/redo history since we're starting fresh
        _undoRedoService.Clear();

        // Clear the context
        Context.Clear();
        Context.FilePath = FilePath;
        Context.Schema = Schema;

        // Reload the file (this also reloads merged data files)
        LoadFile();

        Console.WriteLine($"[Reload] Reload complete, {Rows.Count} rows loaded");
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

        // Sync collections
        XmlEntries.Clear();
        XmlEntries.AddRange(xmlEntryCollection);

        // Mark the new entry as new
        if (insertIndex < XmlEntries.Count)
        {
            var newEntryId = XmlEntries[insertIndex].GetAttribute("id")?.DisplayValue ?? "";
            if (!string.IsNullOrEmpty(newEntryId))
            {
                Context.NewEntries.Add(newEntryId);
            }
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
        if (Context.Document == null || SelectedIndex < 0 || SelectedIndex >= XmlEntries.Count)
            return;

        var indexToDelete = SelectedIndex;

        // Store the row for "removed entries" display (only if not a new entry)
        var rowToDelete = Rows[indexToDelete];
        if (!rowToDelete.IsNew)
        {
            rowToDelete.IsRemoved = true;
            Context.RemovedEntries.Add(rowToDelete);
            Console.WriteLine($"[Removed] Stored removed entry: {rowToDelete["id"]} (total: {Context.RemovedEntries.Count})");
        }

        var xmlEntryCollection = new ObservableCollection<XmlEntry>(XmlEntries);
        var entryToDelete = xmlEntryCollection[indexToDelete];

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

        // Remove just this row from the UI (preserves scroll position)
        Rows.RemoveAt(indexToDelete);

        // Update row numbers for remaining rows
        for (int i = indexToDelete; i < Rows.Count; i++)
        {
            Rows[i].RowNumber = i + 1;
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
    public void DuplicateRow()
    {
        if (Context.Document == null || SelectedIndex < 0 || SelectedIndex >= XmlEntries.Count)
            return;

        var xmlEntryCollection = new ObservableCollection<XmlEntry>(XmlEntries);
        var entryToDuplicate = xmlEntryCollection[SelectedIndex];
        var insertIndex = SelectedIndex + 1;

        var command = new DuplicateRowCommand(Context.Document, xmlEntryCollection, entryToDuplicate);
        _undoRedoService.Execute(command);

        // Sync collections
        XmlEntries.Clear();
        XmlEntries.AddRange(xmlEntryCollection);

        // Mark the duplicated entry as new
        if (insertIndex < XmlEntries.Count)
        {
            var newEntryId = XmlEntries[insertIndex].GetAttribute("id")?.DisplayValue ?? "";
            if (!string.IsNullOrEmpty(newEntryId))
            {
                Context.NewEntries.Add(newEntryId);
            }
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

        // Notify that copied row data is available
        OnPropertyChanged(nameof(HasCopiedRow));
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

        // Force UI update by removing and re-adding the row at the same position
        var index = SelectedIndex;
        Rows.RemoveAt(index);
        Rows.Insert(index, targetRow);
        SelectedIndex = index;

        MarkAsModified();
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
        // Sync entries from document and refresh rows
        SyncEntriesFromDocument();
        RecreateRowsFromEntries();
        SubscribeRowEvents();
    }

    /// <summary>
    /// Redoes the last undone operation.
    /// </summary>
    public void Redo()
    {
        if (!_undoRedoService.CanRedo) return;
        _undoRedoService.Redo();
        MarkAsModified();
        // Sync entries from document and refresh rows
        SyncEntriesFromDocument();
        RecreateRowsFromEntries();
        SubscribeRowEvents();
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
