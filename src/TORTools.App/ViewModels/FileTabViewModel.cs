using System.Collections.ObjectModel;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TORTools.App.Services;
using TORTools.Core.Commands;
using TORTools.Core.Models;
using TORTools.Core.Schema;
using TORTools.Core.Services;
using TORTools.Core.Validation;

namespace TORTools.App.ViewModels;

public partial class FileTabViewModel : ViewModelBase, IDisposable
{
    private readonly IXmlDocumentService _xmlService;
    private readonly IUndoRedoService _undoRedoService;
    private readonly ISchemaService _schemaService;
    private readonly IValidationService _validationService;
    private readonly CrossReferenceService _crossRefService;
    private readonly TupleListService _tupleListService;
    private XmlDocumentWrapper? _document;
    private FileSystemWatcher? _fileWatcher;
    private bool _isReloading;
    private bool _isSaving;

    /// <summary>
    /// Cross-reference data loaded from other XML files.
    /// Key is the cross-reference field name, value is a dictionary mapping local keys to referenced values.
    /// </summary>
    private readonly Dictionary<string, Dictionary<string, List<string>>> _crossRefData = new();

    /// <summary>
    /// Available IDs for autocomplete in editable cross-reference fields.
    /// Key is the field name, value is the list of available IDs.
    /// </summary>
    private readonly Dictionary<string, List<string>> _availableIds = new();

    /// <summary>
    /// Source file paths for cross-reference fields.
    /// Key is the field name, value is the resolved path to the source file.
    /// </summary>
    private readonly Dictionary<string, string> _crossRefSourcePaths = new();

    /// <summary>
    /// Descriptions for cross-reference target items (e.g., attribute descriptions).
    /// Key is the field name, value is a dictionary mapping ID to description.
    /// </summary>
    private readonly Dictionary<string, Dictionary<string, string>> _crossRefDescriptions = new();

    /// <summary>
    /// Tuple list data loaded from external XML files.
    /// Key is the field name, value is a dictionary mapping local keys to lists of tuple dictionaries.
    /// </summary>
    private readonly Dictionary<string, Dictionary<string, List<Dictionary<string, string>>>> _tupleListData = new();

    /// <summary>
    /// Source file paths for tuple list fields.
    /// Key is the field name, value is the resolved path to the source file.
    /// </summary>
    private readonly Dictionary<string, string> _tupleListSourcePaths = new();

    /// <summary>
    /// Git committed values for comparison.
    /// Key is entryId, value is dictionary of fieldName -> committedValue.
    /// </summary>
    private readonly Dictionary<string, Dictionary<string, string>> _gitCommittedValues = new();

    /// <summary>
    /// Entries that have been removed during this session.
    /// These can be shown/hidden via the ShowRemovedEntries toggle.
    /// </summary>
    private readonly List<EntryRowViewModel> _removedEntries = new();

    /// <summary>
    /// Central validation manager - cells register their errors here.
    /// </summary>
    public ValidationManager ValidationManager { get; } = new();

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
    /// Whether ID editing is locked for all rows (default true).
    /// Toggle via the lock icon in the ID column header.
    /// </summary>
    [ObservableProperty]
    private bool _isIdColumnLocked = true;

    /// <summary>
    /// Whether to show entries that were removed (exist in git but not in current file).
    /// </summary>
    [ObservableProperty]
    private bool _showRemovedEntries = false;

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
    /// The schema definition for this file type, if available.
    /// </summary>
    public SchemaDefinition? Schema { get; private set; }

    /// <summary>
    /// Gets the field definition for a column, if schema is available.
    /// </summary>
    public FieldDefinition? GetFieldDefinition(string columnName)
    {
        return Schema?.GetField(columnName);
    }

    /// <summary>
    /// Gets available IDs for autocomplete in a cross-reference field.
    /// </summary>
    public IEnumerable<string> GetAvailableIds(string fieldName)
    {
        if (_availableIds.TryGetValue(fieldName, out var ids))
            return ids;
        return Enumerable.Empty<string>();
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

    public FileTabViewModel(string filePath) : this(filePath, new XmlDocumentService(), new UndoRedoService(), new SchemaService(), new ValidationService(), new CrossReferenceService(), new TupleListService())
    {
    }

    public FileTabViewModel(string filePath, IXmlDocumentService xmlService, IUndoRedoService undoRedoService, ISchemaService schemaService, IValidationService validationService, CrossReferenceService crossRefService, TupleListService tupleListService)
    {
        _xmlService = xmlService;
        _undoRedoService = undoRedoService;
        _schemaService = schemaService;
        _validationService = validationService;
        _crossRefService = crossRefService;
        _tupleListService = tupleListService;
        FilePath = filePath;
        Title = Path.GetFileName(filePath);

        // Load schema for this file type
        Schema = _schemaService.GetSchema(Title);

        // Load cross-reference data if schema defines any
        LoadCrossReferences();

        // Load tuple list data if schema defines any
        LoadTupleListData();

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
                    var targetFilePath = FindSourceFile(baseDir, config.TargetFile);
                    if (targetFilePath != null && !string.IsNullOrEmpty(config.TargetKeyField))
                    {
                        var availableIds = _crossRefService.LoadTargetKeys(targetFilePath, config.TargetKeyField);
                        _availableIds[fieldName] = availableIds;
                        Console.WriteLine($"[CrossRef] Loaded {availableIds.Count} available IDs for direct crossref {fieldName} from {config.TargetFile}");
                    }
                    else
                    {
                        Console.WriteLine($"[CrossRef] Target file not found for direct crossref: {config.TargetFile}");
                    }
                }
                else
                {
                    // Indirect cross-reference: load from mapping file
                    var sourceFilePath = FindSourceFile(baseDir, config.SourceFile);
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
                            var targetFilePath = FindSourceFile(baseDir, config.TargetFile);
                            if (targetFilePath != null && !string.IsNullOrEmpty(config.TargetKeyField))
                            {
                                var availableIds = _crossRefService.LoadTargetKeys(targetFilePath, config.TargetKeyField);
                                _availableIds[fieldName] = availableIds;
                                Console.WriteLine($"[CrossRef] Loaded {availableIds.Count} available IDs for {fieldName} autocomplete");

                                // Also load descriptions from the target file if available
                                var descriptions = LoadTargetDescriptions(targetFilePath, config.TargetKeyField);
                                if (descriptions.Count > 0)
                                {
                                    _crossRefDescriptions[fieldName] = descriptions;
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
                var sourceFilePath = FindSourceFile(baseDir, config.SourceFile);
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

        // Update the source file
        var success = _crossRefService.UpdateCrossReference(sourceFilePath, fieldDef.CrossReference, localKey, valueList);

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
    /// Finds a source file by searching in the base directory and parent directories.
    /// </summary>
    private string? FindSourceFile(string baseDir, string fileName)
    {
        Console.WriteLine($"[FindSourceFile] Looking for {fileName} from base {baseDir}");

        // FIRST: Check TORTools/data for tool-specific data files (highest priority)
        // This allows tool-specific files like tor_attributes.xml to live in TORTools
        var torToolsDataPath = FindTorToolsDataPath(baseDir, fileName);
        if (torToolsDataPath != null)
        {
            Console.WriteLine($"[FindSourceFile] Found in TORTools/data: {torToolsDataPath}");
            return torToolsDataPath;
        }

        // Check same directory
        var path = Path.Combine(baseDir, fileName);
        if (File.Exists(path))
        {
            Console.WriteLine($"[FindSourceFile] Found at: {path}");
            return path;
        }

        // Check tor_custom_xmls subdirectory (common location)
        path = Path.Combine(baseDir, "tor_custom_xmls", fileName);
        if (File.Exists(path))
        {
            Console.WriteLine($"[FindSourceFile] Found at: {path}");
            return path;
        }

        // Navigate up to find Modules directory
        // Structure: Modules/TOR_Armory/ModuleData/tor_armors.xml
        // We need: Modules/TOR_Core/ModuleData/tor_custom_xmls/tor_extendeditemproperties.xml
        var current = baseDir;
        for (int i = 0; i < 5; i++) // Safety limit
        {
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent)) break;

            var parentName = Path.GetFileName(parent);
            if (parentName?.Equals("Modules", StringComparison.OrdinalIgnoreCase) == true)
            {
                // Check TOR_Core/ModuleData/tor_custom_xmls
                var torCorePath = Path.Combine(parent, "TOR_Core", "ModuleData", "tor_custom_xmls", fileName);
                Console.WriteLine($"[FindSourceFile] Checking TOR_Core path: {torCorePath}");
                if (File.Exists(torCorePath))
                {
                    Console.WriteLine($"[FindSourceFile] Found at: {torCorePath}");
                    return torCorePath;
                }

                // Also check TOR_Core/ModuleData directly
                torCorePath = Path.Combine(parent, "TOR_Core", "ModuleData", fileName);
                if (File.Exists(torCorePath))
                {
                    Console.WriteLine($"[FindSourceFile] Found at: {torCorePath}");
                    return torCorePath;
                }
                break;
            }
            current = parent;
        }

        Console.WriteLine($"[FindSourceFile] Not found: {fileName}");
        return null;
    }

    /// <summary>
    /// Helper to find TORTools/data path for tool-specific data files.
    /// </summary>
    private string? FindTorToolsDataPath(string baseDir, string fileName)
    {
        var current = baseDir;
        for (int i = 0; i < 5; i++) // Safety limit
        {
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent)) break;

            var parentName = Path.GetFileName(parent);
            if (parentName?.Equals("Modules", StringComparison.OrdinalIgnoreCase) == true)
            {
                // Found Modules directory - check TORTools/data
                var torToolsPath = Path.Combine(parent, "TORTools", "data", fileName);
                if (File.Exists(torToolsPath))
                {
                    return torToolsPath;
                }
                break;
            }
            current = parent;
        }
        return null;
    }

    /// <summary>
    /// Loads descriptions from a target XML file.
    /// Looks for "display_description" first (player-friendly), then falls back to "description".
    /// </summary>
    private Dictionary<string, string> LoadTargetDescriptions(string filePath, string keyField)
    {
        var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var doc = XDocument.Load(filePath);
            foreach (var element in doc.Root?.Elements() ?? Enumerable.Empty<XElement>())
            {
                var key = element.Attribute(keyField)?.Value;
                // Prefer display_description (player-friendly), fallback to description
                var description = element.Attribute("display_description")?.Value
                               ?? element.Attribute("description")?.Value;

                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(description))
                {
                    descriptions[key] = description;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LoadTargetDescriptions] Error loading {filePath}: {ex.Message}");
        }

        return descriptions;
    }

    /// <summary>
    /// Gets the description for a cross-reference target item.
    /// </summary>
    public string? GetCrossRefDescription(string fieldName, string itemId)
    {
        if (_crossRefDescriptions.TryGetValue(fieldName, out var descriptions))
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
        var targetFiles = config.GetAllTargetFiles().ToList();
        Console.WriteLine($"[Navigate] Using config: targetFiles=[{string.Join(", ", targetFiles)}], targetKey={config.TargetKeyField}");

        NavigateToCrossReference?.Invoke(this, new CrossReferenceNavigationEventArgs(
            targetFiles,
            config.TargetKeyField,
            referenceId
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
        Task.Run(() => RunValidationAsync());
    }

    /// <summary>
    /// Runs full file validation asynchronously on a background thread.
    /// </summary>
    private async Task RunValidationAsync()
    {
        Console.WriteLine($"[Validation] Starting validation of {Rows.Count} entries...");

        // Clear previous validation issues on UI thread
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            ValidationManager.ClearByPrefix("basic_");
            ValidationManager.ClearByPrefix("upgrade_");
            ValidationManager.ClearByPrefix("crossref_");
        });

        // Capture row data for thread-safe processing
        var rowData = new List<(int index, string id, Dictionary<string, string> values)>();
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            for (int i = 0; i < Rows.Count; i++)
            {
                var row = Rows[i];
                var values = new Dictionary<string, string>();
                foreach (var col in ColumnNames)
                {
                    values[col] = row[col] ?? "";
                }
                rowData.Add((i, row["id"] ?? "", values));
            }
        });

        // Run basic validation
        // Skip duplicate ID check for equipment sets (variations share same roster ID)
        var skipDuplicateIdCheck = Schema?.HasNestedVariations == true;
        var entries = rowData.Select(r => (IDictionary<string, string>)r.values).ToList();
        var result = _validationService.ValidateAll(entries, Schema, skipDuplicateIdCheck);

        // Register basic validation issues
        foreach (var issue in result.Issues)
        {
            var key = $"basic_{issue.RowIndex}_{issue.AttributeName}_{issue.CurrentValue ?? "empty"}";
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                ValidationManager.RegisterError(key, issue);
            });
        }

        // Run upgrade target validation
        await ValidateUpgradeTargetsAsync(rowData);

        // Run skill template tier validation
        await ValidateSkillTemplateTiersAsync(rowData);

        // Run cross-reference validation for all crossRef fields
        await ValidateCrossReferencesAsync(rowData);

        Console.WriteLine($"[Validation] Completed validation. Errors: {ValidationManager.ErrorCount}, Warnings: {ValidationManager.WarningCount}");
    }

    /// <summary>
    /// Validates upgrade targets asynchronously.
    /// </summary>
    private async Task ValidateUpgradeTargetsAsync(List<(int index, string id, Dictionary<string, string> values)> rowData)
    {
        // Check if this file has upgrade target fields
        var hasUpgradeTargets = Schema?.Fields.ContainsKey("upgrade_target_1") == true;
        if (!hasUpgradeTargets) return;

        // Build lookups
        var idToLevel = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (index, id, values) in rowData)
        {
            if (!string.IsNullOrEmpty(id) && int.TryParse(values.GetValueOrDefault("level", "0"), out var level))
            {
                idToLevel[id] = level;
            }
        }

        var problemTargets = new Dictionary<string, (int sourceRowIndex, string fieldName, string sourceId, int sourceLevel)>(StringComparer.OrdinalIgnoreCase);

        // Process all rows
        foreach (var (rowIndex, sourceId, values) in rowData)
        {
            var sourceLevel = idToLevel.GetValueOrDefault(sourceId, 0);

            for (int i = 1; i <= 3; i++)
            {
                var fieldName = $"upgrade_target_{i}";
                var targetId = values.GetValueOrDefault(fieldName, "");

                var fieldDef = GetFieldDefinition(fieldName);
                if (fieldDef?.PrefixToStrip != null && targetId.StartsWith(fieldDef.PrefixToStrip, StringComparison.OrdinalIgnoreCase))
                {
                    targetId = targetId.Substring(fieldDef.PrefixToStrip.Length);
                }

                if (!string.IsNullOrEmpty(targetId))
                {
                    if (!idToLevel.ContainsKey(targetId))
                    {
                        var key = $"upgrade_{rowIndex}_{fieldName}_notfound";
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            ValidationManager.RegisterError(key, new TORTools.Core.Validation.ValidationIssue
                            {
                                Severity = TORTools.Core.Validation.ValidationSeverity.Error,
                                RowIndex = rowIndex,
                                AttributeName = fieldName,
                                Message = $"Upgrade target '{targetId}' not found in this file",
                                EntryId = sourceId,
                                CurrentValue = targetId
                            });
                        });
                    }
                    else
                    {
                        var targetLevel = idToLevel[targetId];
                        if (targetLevel <= sourceLevel)
                        {
                            if (!problemTargets.TryGetValue(targetId, out var existing) || sourceLevel > existing.sourceLevel)
                            {
                                problemTargets[targetId] = (rowIndex, fieldName, sourceId, sourceLevel);
                            }
                        }
                    }
                }
            }
        }

        // Register tier warnings
        foreach (var kvp in problemTargets)
        {
            var targetId = kvp.Key;
            var (sourceRowIndex, fieldName, sourceId, sourceLevel) = kvp.Value;
            var targetLevel = idToLevel.GetValueOrDefault(targetId, 0);

            var key = $"upgrade_{sourceRowIndex}_{fieldName}_tier";
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                ValidationManager.RegisterError(key, new TORTools.Core.Validation.ValidationIssue
                {
                    Severity = TORTools.Core.Validation.ValidationSeverity.Warning,
                    RowIndex = sourceRowIndex,
                    AttributeName = fieldName,
                    Message = $"'{targetId}' has level {targetLevel}, should be higher than {sourceLevel}",
                    EntryId = sourceId,
                    CurrentValue = targetId
                });
            });
        }
    }

    /// <summary>
    /// Validates skill template tiers asynchronously.
    /// </summary>
    private async Task ValidateSkillTemplateTiersAsync(List<(int index, string id, Dictionary<string, string> values)> rowData)
    {
        var hasSkillTemplate = Schema?.Fields.ContainsKey("skill_template") == true;
        if (!hasSkillTemplate) return;

        int checkedCount = 0;
        int skippedNoLevel = 0;
        int mismatchCount = 0;

        foreach (var (rowIndex, entryId, values) in rowData)
        {
            var levelStr = values.GetValueOrDefault("level", "1");
            var skillTemplate = values.GetValueOrDefault("skill_template", "");

            if (string.IsNullOrEmpty(skillTemplate)) continue;
            if (!int.TryParse(levelStr, out var level)) continue;

            var expectedTier = (level - 1) / 5;

            // Try to extract tier from skill template name
            // Pattern 1: tor_skills_levelNN (e.g., tor_skills_level11)
            // Pattern 2: _tN_ (e.g., _t2_)
            int? templateTier = null;

            var levelMatch = System.Text.RegularExpressions.Regex.Match(skillTemplate, @"level(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (levelMatch.Success && int.TryParse(levelMatch.Groups[1].Value, out var templateLevel))
            {
                templateTier = (templateLevel - 1) / 5;
            }
            else
            {
                var tierMatch = System.Text.RegularExpressions.Regex.Match(skillTemplate, @"_t(\d+)_");
                if (tierMatch.Success && int.TryParse(tierMatch.Groups[1].Value, out var parsedTier))
                {
                    templateTier = parsedTier;
                }
            }

            if (!templateTier.HasValue)
            {
                skippedNoLevel++;
                continue;
            }

            checkedCount++;

            if (templateTier.Value != expectedTier)
            {
                mismatchCount++;
                var key = $"upgrade_{rowIndex}_skill_template_tier";
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ValidationManager.RegisterError(key, new TORTools.Core.Validation.ValidationIssue
                    {
                        Severity = TORTools.Core.Validation.ValidationSeverity.Warning,
                        RowIndex = rowIndex,
                        AttributeName = "skill_template",
                        Message = $"Skill template is Tier {templateTier.Value} but troop is Tier {expectedTier} (level {level})",
                        EntryId = entryId,
                        CurrentValue = skillTemplate
                    });
                });
            }
        }

        Console.WriteLine($"[Validation] Skill template tier check: {checkedCount} checked, {skippedNoLevel} skipped (no level pattern), {mismatchCount} mismatches");
    }

    /// <summary>
    /// Validates cross-reference fields asynchronously.
    /// </summary>
    private async Task ValidateCrossReferencesAsync(List<(int index, string id, Dictionary<string, string> values)> rowData)
    {
        if (Schema == null) return;

        // Find all crossReference fields
        var crossRefFields = Schema.Fields
            .Where(f => f.Value.Type == "crossReference" && f.Value.CrossReference != null)
            .ToList();

        if (!crossRefFields.Any()) return;

        Console.WriteLine($"[Validation] Cross-ref fields to validate: {crossRefFields.Count}");

        foreach (var (fieldName, fieldDef) in crossRefFields)
        {
            var crossRef = fieldDef.CrossReference!;

            // Get available IDs from the already-loaded cache
            if (!_availableIds.TryGetValue(fieldName, out var availableIdsList) || availableIdsList.Count == 0)
            {
                Console.WriteLine($"[Validation] No available IDs for {fieldName}, skipping");
                continue;
            }

            Console.WriteLine($"[Validation] Validating {fieldName} with {availableIdsList.Count} valid IDs");
            Console.WriteLine($"[Validation] Sample valid IDs: {string.Join(", ", availableIdsList.Take(5))}");
            Console.WriteLine($"[Validation] PrefixToStrip: '{crossRef.PrefixToStrip ?? "(none)"}'");

            // Debug: check for specific IDs that might be missing
            var debugIds = new[] { "tor_skills_level21", "tor_skills_dwarf_irondrake", "tor_skills_level1", "tor_skills_level16" };
            foreach (var debugId in debugIds)
            {
                var exists = availableIdsList.Any(id => id.Equals(debugId, StringComparison.OrdinalIgnoreCase));
                Console.WriteLine($"[Validation] Debug: '{debugId}' exists in valid IDs: {exists}");
            }

            var validIdsSet = new HashSet<string>(availableIdsList, StringComparer.OrdinalIgnoreCase);

            // Debug: log some sample values from the data
            var sampleValues = rowData.Take(5).Select(r => r.values.GetValueOrDefault(fieldName, "(empty)")).ToList();
            Console.WriteLine($"[Validation] Sample data values: {string.Join(", ", sampleValues)}");

            int invalidCount = 0;
            foreach (var (rowIndex, entryId, values) in rowData)
            {
                var rawValue = values.GetValueOrDefault(fieldName, "");
                if (string.IsNullOrEmpty(rawValue)) continue;

                // Handle multi-value fields (colon-separated or comma-separated)
                var ids = rawValue.Split(new[] { ':', ',' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var id in ids)
                {
                    var cleanId = id.Trim();
                    if (string.IsNullOrEmpty(cleanId)) continue;

                    // Strip prefix if configured
                    if (!string.IsNullOrEmpty(crossRef.PrefixToStrip) &&
                        cleanId.StartsWith(crossRef.PrefixToStrip, StringComparison.OrdinalIgnoreCase))
                    {
                        cleanId = cleanId.Substring(crossRef.PrefixToStrip.Length);
                    }

                    if (!validIdsSet.Contains(cleanId))
                    {
                        invalidCount++;
                        if (invalidCount <= 5)
                        {
                            Console.WriteLine($"[Validation] Invalid crossref: row {rowIndex}, {fieldName}='{cleanId}' (original: '{id}')");
                        }
                        var key = $"crossref_{rowIndex}_{fieldName}_{cleanId}";
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            ValidationManager.RegisterError(key, new TORTools.Core.Validation.ValidationIssue
                            {
                                Severity = TORTools.Core.Validation.ValidationSeverity.Error,
                                RowIndex = rowIndex,
                                AttributeName = fieldName,
                                Message = $"'{cleanId}' not found in {crossRef.TargetFile}",
                                EntryId = entryId,
                                CurrentValue = cleanId
                            });
                        });
                    }
                }
            }

            Console.WriteLine($"[Validation] CrossRef {fieldName}: {invalidCount} invalid entries found");
        }
    }

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
        HasUnsavedChanges = false;
        OnPropertyChanged(nameof(Rows));
    }

    private void LoadFile()
    {
        try
        {
            // Check if this schema defines multiple source files to merge
            if (Schema?.AdditionalSourceFiles != null && Schema.AdditionalSourceFiles.Count > 0)
            {
                LoadMergedFiles();
            }
            else
            {
                // Standard single-file loading
                _document = _xmlService.Load(FilePath);
                var entries = _xmlService.GetEntries(_document);

                XmlEntries.Clear();
                XmlEntries.AddRange(entries);

                // Load git committed values for comparison
                LoadGitCommittedValues();

                // Check if this is an equipment set file with nested variations
                if (Schema?.HasNestedVariations == true && !string.IsNullOrEmpty(Schema.VariationElement))
                {
                    // Flatten equipment sets - each variation becomes a row
                    LoadEquipmentSetVariations(entries);
                }
                else
                {
                    // Normal loading
                    DiscoverColumns(entries);
                    CreateRows(entries);
                }

                HasError = false;
                ErrorMessage = "";

                // Run validation on file load
                RunValidation();
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Error loading file: {ex.Message}";
        }
    }

    /// <summary>
    /// Loads entries from multiple source files and merges them into a single view.
    /// </summary>
    private void LoadMergedFiles()
    {
        if (Schema == null) return;

        var allEntries = new List<XmlEntry>();
        var baseDir = Path.GetDirectoryName(FilePath);
        if (string.IsNullOrEmpty(baseDir)) return;

        // Load main file
        Console.WriteLine($"[LoadMergedFiles] Loading main file: {FilePath}");
        _document = _xmlService.Load(FilePath);
        var mainEntries = _xmlService.GetEntries(_document);
        Console.WriteLine($"[LoadMergedFiles] Loaded {mainEntries.Count} entries from main file");

        // Set source file field on main entries (if specified)
        if (!string.IsNullOrEmpty(Schema.SourceFileField))
        {
            foreach (var entry in mainEntries)
            {
                // Main file entries get the "default" value (usually "false" for is_custom_battle_lord)
                entry.SetAttributeValue(Schema.SourceFileField, "false");
            }
        }

        allEntries.AddRange(mainEntries);

        // Load additional source files
        foreach (var additionalFile in Schema.AdditionalSourceFiles)
        {
            var additionalFilePath = FindSourceFile(baseDir, additionalFile.FileName);
            if (additionalFilePath == null)
            {
                Console.WriteLine($"[LoadMergedFiles] Additional file not found: {additionalFile.FileName}");
                continue;
            }

            Console.WriteLine($"[LoadMergedFiles] Loading additional file: {additionalFilePath}");
            var additionalDoc = _xmlService.Load(additionalFilePath);
            var additionalEntries = _xmlService.GetEntries(additionalDoc);
            Console.WriteLine($"[LoadMergedFiles] Loaded {additionalEntries.Count} entries from {additionalFile.FileName}");

            // Set source file field on additional entries
            if (!string.IsNullOrEmpty(Schema.SourceFileField) && !string.IsNullOrEmpty(additionalFile.SourceValue))
            {
                foreach (var entry in additionalEntries)
                {
                    entry.SetAttributeValue(Schema.SourceFileField, additionalFile.SourceValue);
                }
            }

            allEntries.AddRange(additionalEntries);
        }

        Console.WriteLine($"[LoadMergedFiles] Total merged entries: {allEntries.Count}");

        // Merge data from merged data file (e.g., tor_heroes.xml)
        if (Schema.MergedDataFile != null)
        {
            MergeDataFromFile(allEntries, baseDir);
        }

        XmlEntries.Clear();
        XmlEntries.AddRange(allEntries);

        // Load git committed values for comparison
        LoadGitCommittedValues();

        // Normal loading
        DiscoverColumns(allEntries);
        CreateRows(allEntries);

        HasError = false;
        ErrorMessage = "";

        // Run validation on file load
        RunValidation();
    }

    /// <summary>
    /// Merges data from a separate data file into the loaded entries.
    /// Used for merging hero data (faction, text) from tor_heroes.xml into lords.
    /// </summary>
    private void MergeDataFromFile(List<XmlEntry> entries, string baseDir)
    {
        if (Schema?.MergedDataFile == null) return;

        var mergedConfig = Schema.MergedDataFile;
        var mergedFilePath = FindSourceFile(baseDir, mergedConfig.FileName);
        if (mergedFilePath == null)
        {
            Console.WriteLine($"[MergeData] Merged data file not found: {mergedConfig.FileName}");
            return;
        }

        Console.WriteLine($"[MergeData] Loading merged data from: {mergedFilePath}");

        try
        {
            var mergedDoc = XDocument.Load(mergedFilePath);
            var mergedRoot = mergedDoc.Root;
            if (mergedRoot == null) return;

            // Find the entry element name
            var entryElementName = mergedConfig.EntryElement ?? "Hero";
            var matchField = mergedConfig.MatchField ?? "id";

            // Build a dictionary of merged data keyed by match field
            var mergedData = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var element in mergedRoot.Elements(entryElementName))
            {
                var key = element.Attribute(matchField)?.Value;
                if (!string.IsNullOrEmpty(key))
                {
                    mergedData[key] = element;
                }
            }

            Console.WriteLine($"[MergeData] Loaded {mergedData.Count} entries from {mergedConfig.FileName}");

            // Merge data into entries
            foreach (var entry in entries)
            {
                var entryId = entry.GetAttributeValue(matchField);
                if (string.IsNullOrEmpty(entryId)) continue;

                if (mergedData.TryGetValue(entryId, out var mergedElement))
                {
                    // Apply field mappings
                    if (mergedConfig.FieldMappings != null)
                    {
                        foreach (var mapping in mergedConfig.FieldMappings)
                        {
                            var sourceField = mapping.Value; // "faction" or "text"
                            var targetField = mapping.Key;   // "clan" or "encyclopedia_text"

                            var sourceValue = mergedElement.Attribute(sourceField)?.Value;
                            if (!string.IsNullOrEmpty(sourceValue))
                            {
                                entry.SetAttributeValue(targetField, sourceValue);
                            }
                        }
                    }
                }
            }

            Console.WriteLine($"[MergeData] Merged data complete");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MergeData] Error loading {mergedFilePath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads equipment sets by flattening variations into rows.
    /// Each EquipmentSet variation becomes its own row with equipment slots as columns.
    /// </summary>
    /// <remarks>
    /// NOTE: Vanilla Bannerlord supports a Flags element on EquipmentRoster with attributes like
    /// IsCivilianTemplate, IsCombatantTemplate, IsWandererEquipment. TOR doesn't use this element
    /// (XmlGenerator doesn't support it). Only the 'civilian' attribute on EquipmentSet is used.
    /// If TOR adopts the vanilla Flags element in future, we'll need to add support here.
    /// </remarks>
    private void LoadEquipmentSetVariations(IReadOnlyList<XmlEntry> rosterEntries)
    {
        // Build column list: roster fields + variation attributes + equipment slots
        // Note: civilian column removed - civilian sets are auto-generated on save
        ColumnNames.Clear();
        ColumnNames.Add("id");           // Roster ID
        ColumnNames.Add("culture");      // Roster culture
        ColumnNames.Add("_variation");   // Variation index (internal tracking)

        // Add equipment slots from schema
        if (Schema?.EquipmentSlots != null)
        {
            foreach (var slot in Schema.EquipmentSlots.OrderBy(s => s.Order))
            {
                ColumnNames.Add(slot.Slot);
            }
        }

        // Unsubscribe from old rows
        foreach (var row in Rows)
        {
            row.CellValueChanged -= OnCellValueChanged;
        }
        Rows.Clear();

        int rowNum = 1;
        var variationElementName = Schema!.VariationElement!;
        var equipmentElementName = Schema.EquipmentItemElement ?? "Equipment";

        foreach (var roster in rosterEntries)
        {
            var rosterId = roster.GetAttributeValue("id") ?? "";
            var rosterCulture = roster.GetAttributeValue("culture") ?? "";

            // Get all EquipmentSet variations
            var variations = roster.Children.Where(c => c.ElementName == variationElementName).ToList();

            if (variations.Count == 0)
            {
                // No variations - create a single row for the roster
                var emptyRow = CreateEquipmentRow(roster, null, rosterId, rosterCulture, 0, false, new Dictionary<string, string>(), rowNum++);
                Rows.Add(emptyRow);
            }
            else
            {
                int variationIndex = 0;
                foreach (var variation in variations)
                {
                    var isCivilian = variation.GetAttributeValue("civilian")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

                    // TODO: Future consideration - allow editing separate civilian equipment.
                    // Currently civilian sets are always clones of combat sets (per XmlGenerator behavior).
                    // Skip civilian variations - they will be auto-generated on save.
                    if (isCivilian) continue;

                    // Extract equipment from this variation
                    var equipment = new Dictionary<string, string>();
                    foreach (var equipItem in variation.Children.Where(c => c.ElementName == equipmentElementName))
                    {
                        var slot = equipItem.GetAttributeValue("slot");
                        var itemId = equipItem.GetAttributeValue("id");
                        if (!string.IsNullOrEmpty(slot) && !string.IsNullOrEmpty(itemId))
                        {
                            equipment[slot] = itemId;
                        }
                    }

                    var row = CreateEquipmentRow(roster, variation, rosterId, rosterCulture, variationIndex, false, equipment, rowNum++);
                    Rows.Add(row);
                    variationIndex++;
                }
            }
        }

        Console.WriteLine($"[EquipmentSets] Loaded {Rows.Count} variations from {rosterEntries.Count} rosters");
    }

    /// <summary>
    /// Creates a row for an equipment set variation.
    /// </summary>
    private EntryRowViewModel CreateEquipmentRow(
        XmlEntry roster,
        XmlEntry? variation,
        string rosterId,
        string rosterCulture,
        int variationIndex,
        bool isCivilian,
        Dictionary<string, string> equipment,
        int rowNum)
    {
        // Get git committed values for this entry
        var gitKey = $"{rosterId}_{variationIndex}";
        var gitValues = GetGitCommittedValues(gitKey);

        var row = new EntryRowViewModel(roster, ColumnNames, gitValues);
        row.RowNumber = rowNum;
        row.VariationEntry = variation;
        row.VariationIndex = variationIndex;
        row.RosterId = rosterId;

        // NOTE: Do NOT subscribe to CellValueChanged yet - we're just populating initial values
        // Set roster-level values (these are display-only, not persisted to roster attributes)
        row.SetValueWithoutNotify("id", rosterId);
        row.SetValueWithoutNotify("culture", rosterCulture);
        row.SetValueWithoutNotify("_variation", variationIndex.ToString());
        row.SetValueWithoutNotify("civilian", isCivilian ? "true" : "false");

        // Set equipment slot values
        if (Schema?.EquipmentSlots != null)
        {
            foreach (var slot in Schema.EquipmentSlots)
            {
                if (equipment.TryGetValue(slot.Slot, out var itemId))
                {
                    row.SetValueWithoutNotify(slot.Slot, itemId);
                }
                else
                {
                    row.SetValueWithoutNotify(slot.Slot, "");
                }
            }
        }

        // NOW subscribe to changes - after initial values are set
        row.CellValueChanged += OnCellValueChanged;

        return row;
    }

    /// <summary>
    /// Loads the git committed version of the file for comparison.
    /// </summary>
    private void LoadGitCommittedValues()
    {
        _gitCommittedValues.Clear();

        try
        {
            // Find the git repository root
            var directory = Path.GetDirectoryName(FilePath);
            if (string.IsNullOrEmpty(directory)) return;

            // Get relative path from git root
            var relativePath = GetGitRelativePath(directory, FilePath);
            if (string.IsNullOrEmpty(relativePath)) return;

            // Run git show HEAD:<path> to get committed content
            var gitContent = RunGitShow(directory, relativePath);
            if (string.IsNullOrEmpty(gitContent)) return;

            // Parse the XML content and extract values
            ParseGitContent(gitContent);

            Console.WriteLine($"[Git] Loaded {_gitCommittedValues.Count} entries from git for comparison");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Git] Failed to load git committed values: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the relative path of a file from the git repository root.
    /// </summary>
    private static string? GetGitRelativePath(string workingDir, string filePath)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse --show-toplevel",
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return null;

            var gitRoot = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (process.ExitCode != 0 || string.IsNullOrEmpty(gitRoot))
                return null;

            // Normalize paths for comparison
            gitRoot = gitRoot.Replace('/', Path.DirectorySeparatorChar);
            var normalizedFilePath = Path.GetFullPath(filePath);

            if (normalizedFilePath.StartsWith(gitRoot, StringComparison.OrdinalIgnoreCase))
            {
                var relative = normalizedFilePath.Substring(gitRoot.Length).TrimStart(Path.DirectorySeparatorChar);
                // Git uses forward slashes
                return relative.Replace(Path.DirectorySeparatorChar, '/');
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Runs git show HEAD:<path> and returns the content.
    /// </summary>
    private static string? RunGitShow(string workingDir, string relativePath)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"show HEAD:{relativePath}",
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return null;

            var content = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                return null;

            return content;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses git XML content and extracts values into _gitCommittedValues.
    /// </summary>
    private void ParseGitContent(string xmlContent)
    {
        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(xmlContent);
            var root = doc.Root;
            if (root == null) return;

            // Find all entry elements (usually direct children of root)
            foreach (var element in root.Elements())
            {
                // Get the ID attribute to use as the key
                var idAttr = element.Attribute("id");
                if (idAttr == null) continue;

                var entryId = idAttr.Value;
                var values = new Dictionary<string, string>();

                // Extract all attributes
                foreach (var attr in element.Attributes())
                {
                    // Store display value (unwrap localization if present)
                    var rawValue = attr.Value;
                    var (_, displayValue) = LocalizationHelper.Unwrap(rawValue);
                    values[attr.Name.LocalName] = displayValue;
                }

                _gitCommittedValues[entryId] = values;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Git] Failed to parse git content: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the git committed values for an entry, if available.
    /// </summary>
    public Dictionary<string, string>? GetGitCommittedValues(string entryId)
    {
        return _gitCommittedValues.TryGetValue(entryId, out var values) ? values : null;
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

        // Add cross-reference, nested, and tupleList columns from schema (these may not be direct attributes)
        if (Schema != null)
        {
            foreach (var kvp in Schema.Fields)
            {
                if (kvp.Value.CrossReference != null && !ColumnNames.Contains(kvp.Key))
                {
                    ColumnNames.Add(kvp.Key);
                }
                else if (kvp.Value.TupleList != null && !ColumnNames.Contains(kvp.Key))
                {
                    ColumnNames.Add(kvp.Key);
                }
                else if (kvp.Value.Nested && !string.IsNullOrEmpty(kvp.Value.NestedPath) && !ColumnNames.Contains(kvp.Key))
                {
                    ColumnNames.Add(kvp.Key);
                }
            }
        }
    }

    private void CreateRows(IReadOnlyList<XmlEntry> entries)
    {
        // Unsubscribe from old rows
        foreach (var row in Rows)
        {
            row.CellValueChanged -= OnCellValueChanged;
        }

        Rows.Clear();
        int rowNum = 1;
        foreach (var entry in entries)
        {
            var isNew = _newEntries.Contains(entry);

            // Get git committed values for this entry (by ID)
            var entryId = entry.GetAttribute("id")?.DisplayValue ?? "";
            var gitValues = GetGitCommittedValues(entryId);

            var row = new EntryRowViewModel(entry, ColumnNames, gitValues);
            row.IsNew = isNew;
            row.IsIdLocked = !isNew; // New entries have unlocked ID
            row.RowNumber = rowNum++;
            row.CellValueChanged += OnCellValueChanged;

            // Populate cross-reference values
            PopulateCrossReferenceValues(row, entry);

            // Populate nested field values
            PopulateNestedValues(row, entry);

            Rows.Add(row);
        }
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
    /// Populates nested field values for a row based on schema nested paths.
    /// </summary>
    private void PopulateNestedValues(EntryRowViewModel row, XmlEntry entry)
    {
        if (Schema == null) return;

        foreach (var kvp in Schema.Fields)
        {
            var fieldName = kvp.Key;
            var fieldDef = kvp.Value;

            if (fieldDef.Nested && !string.IsNullOrEmpty(fieldDef.NestedPath))
            {
                var value = entry.GetNestedValue(fieldDef.NestedPath);
                if (!string.IsNullOrEmpty(value))
                {
                    row.SetOriginalValue(fieldName, value);
                }
            }
        }
    }

    private void OnCellValueChanged(object? sender, CellValueChangedEventArgs e)
    {
        if (sender is not EntryRowViewModel rowVm) return;
        if (_document == null) return;

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
        var command = new CellEditUndoCommand(rowVm, e.ColumnName, e.OldValue, e.NewValue, nestedPath);

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
                var newElement = new System.Xml.Linq.XElement(equipmentElementName,
                    new System.Xml.Linq.XAttribute("slot", slotName),
                    new System.Xml.Linq.XAttribute("id", itemId));
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
            return ConvertPascalToSnakeCase(sourceValue);
        }

        // subtype → Type: convert snake_case to PascalCase
        // e.g., "head_armor" → "HeadArmor"
        if (targetField.Equals("Type", StringComparison.OrdinalIgnoreCase) ||
            sourceField.Equals("subtype", StringComparison.OrdinalIgnoreCase))
        {
            return ConvertSnakeToPascalCase(sourceValue);
        }

        // Default: use source value as-is
        return sourceValue;
    }

    /// <summary>
    /// Converts PascalCase to snake_case.
    /// E.g., "HeadArmor" → "head_armor"
    /// </summary>
    private static string ConvertPascalToSnakeCase(string value)
    {
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                    result.Append('_');
                result.Append(char.ToLower(c));
            }
            else
            {
                result.Append(c);
            }
        }
        return result.ToString();
    }

    /// <summary>
    /// Converts snake_case to PascalCase.
    /// E.g., "head_armor" → "HeadArmor"
    /// </summary>
    private static string ConvertSnakeToPascalCase(string value)
    {
        var result = new System.Text.StringBuilder();
        bool capitalizeNext = true;
        foreach (char c in value)
        {
            if (c == '_')
            {
                capitalizeNext = true;
            }
            else
            {
                result.Append(capitalizeNext ? char.ToUpper(c) : c);
                capitalizeNext = false;
            }
        }
        return result.ToString();
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

            // For equipment sets: auto-generate civilian clones before saving
            if (Schema?.HasNestedVariations == true)
            {
                GenerateCivilianClones();
            }

            // Check if this is a multi-file schema that needs split saving
            if (Schema?.AdditionalSourceFiles != null && Schema.AdditionalSourceFiles.Count > 0)
            {
                SaveMergedFiles();
            }
            else
            {
                // Standard single-file save
                var compactFormat = Schema?.CompactFormat ?? true;
                _xmlService.Save(_document, null, compactFormat);
            }

            HasUnsavedChanges = false;
            HasError = false;
            ErrorMessage = "";

            // After save, all entries are no longer "new" - they're in the file now
            // Also mark modified fields as "saved" (for orange/green text indicator)
            _newEntries.Clear();
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
        }
        finally
        {
            // Delay resetting flag to avoid catching our own save event
            Task.Delay(500).ContinueWith(_ => _isSaving = false);
        }
    }

    /// <summary>
    /// Saves multi-file schemas by splitting entries back to their source files.
    /// Also saves merged data (e.g., hero data) back to separate files.
    /// </summary>
    private void SaveMergedFiles()
    {
        if (Schema == null) return;

        var baseDir = Path.GetDirectoryName(FilePath);
        if (string.IsNullOrEmpty(baseDir)) return;

        var compactFormat = Schema.CompactFormat;

        // Group entries by source file field (e.g., is_custom_battle_lord)
        var mainEntries = new List<XmlEntry>();
        var additionalFileEntries = new Dictionary<string, List<XmlEntry>>();

        foreach (var entry in XmlEntries)
        {
            if (!string.IsNullOrEmpty(Schema.SourceFileField))
            {
                var sourceValue = entry.GetAttributeValue(Schema.SourceFileField);

                // Check if this entry belongs to an additional file
                var additionalFile = Schema.AdditionalSourceFiles.FirstOrDefault(f => f.SourceValue == sourceValue);
                if (additionalFile != null)
                {
                    if (!additionalFileEntries.ContainsKey(additionalFile.FileName))
                    {
                        additionalFileEntries[additionalFile.FileName] = new List<XmlEntry>();
                    }
                    additionalFileEntries[additionalFile.FileName].Add(entry);
                }
                else
                {
                    mainEntries.Add(entry);
                }
            }
            else
            {
                mainEntries.Add(entry);
            }
        }

        // Save main file
        Console.WriteLine($"[SaveMergedFiles] Saving {mainEntries.Count} entries to main file: {FilePath}");
        var mainXDoc = CreateDocumentFromEntries(mainEntries, Schema.RootElement ?? "NPCCharacters");
        var mainDoc = new XmlDocumentWrapper(mainXDoc, FilePath, _document!.HasBom, _document.Encoding, _document.IndentString);
        _xmlService.Save(mainDoc, FilePath, compactFormat);

        // Save additional files
        foreach (var kvp in additionalFileEntries)
        {
            var fileName = kvp.Key;
            var entries = kvp.Value;
            var filePath = FindSourceFile(baseDir, fileName);

            if (filePath != null)
            {
                Console.WriteLine($"[SaveMergedFiles] Saving {entries.Count} entries to: {filePath}");
                var xdoc = CreateDocumentFromEntries(entries, Schema.RootElement ?? "NPCCharacters");
                var doc = new XmlDocumentWrapper(xdoc, filePath, _document!.HasBom, _document.Encoding, _document.IndentString);
                _xmlService.Save(doc, filePath, compactFormat);
            }
        }

        // Save merged data file if configured (e.g., tor_heroes.xml)
        if (Schema.MergedDataFile != null)
        {
            SaveMergedDataFile(baseDir);
        }
    }

    /// <summary>
    /// Creates an XDocument from a list of XmlEntry objects.
    /// </summary>
    private XDocument CreateDocumentFromEntries(List<XmlEntry> entries, string rootElementName)
    {
        var root = new XElement(rootElementName);
        foreach (var entry in entries)
        {
            // Remove the source file field attribute before saving (it's only for internal tracking)
            if (!string.IsNullOrEmpty(Schema?.SourceFileField))
            {
                entry.OriginalElement.Attribute(Schema.SourceFileField)?.Remove();
            }
            root.Add(entry.OriginalElement);
        }
        return new XDocument(root);
    }

    /// <summary>
    /// Saves merged data fields back to the merged data file (e.g., tor_heroes.xml).
    /// Extracts clan → faction and encyclopedia_text → text mappings.
    /// </summary>
    private void SaveMergedDataFile(string baseDir)
    {
        if (Schema?.MergedDataFile == null) return;

        var mergedConfig = Schema.MergedDataFile;
        var mergedFilePath = FindSourceFile(baseDir, mergedConfig.FileName);
        if (mergedFilePath == null)
        {
            Console.WriteLine($"[SaveMergedData] Merged data file not found: {mergedConfig.FileName}");
            return;
        }

        Console.WriteLine($"[SaveMergedData] Updating merged data file: {mergedFilePath}");

        try
        {
            // Load existing merged data file
            var mergedDoc = XDocument.Load(mergedFilePath);
            var mergedRoot = mergedDoc.Root;
            if (mergedRoot == null) return;

            var entryElementName = mergedConfig.EntryElement ?? "Hero";
            var matchField = mergedConfig.MatchField ?? "id";

            // Build a dictionary of existing entries
            var existingEntries = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var element in mergedRoot.Elements(entryElementName))
            {
                var key = element.Attribute(matchField)?.Value;
                if (!string.IsNullOrEmpty(key))
                {
                    existingEntries[key] = element;
                }
            }

            // Update entries with data from our rows
            int updatedCount = 0;
            foreach (var entry in XmlEntries)
            {
                var entryId = entry.GetAttributeValue(matchField);
                if (string.IsNullOrEmpty(entryId)) continue;

                if (existingEntries.TryGetValue(entryId, out var heroElement))
                {
                    // Apply reverse field mappings (targetField → sourceField)
                    if (mergedConfig.FieldMappings != null)
                    {
                        foreach (var mapping in mergedConfig.FieldMappings)
                        {
                            var targetField = mapping.Key;   // "clan" or "encyclopedia_text"
                            var sourceField = mapping.Value; // "faction" or "text"

                            var value = entry.GetAttributeValue(targetField);
                            if (!string.IsNullOrEmpty(value))
                            {
                                var oldValue = heroElement.Attribute(sourceField)?.Value;
                                if (oldValue != value)
                                {
                                    heroElement.SetAttributeValue(sourceField, value);
                                    updatedCount++;
                                }
                            }
                        }
                    }
                }
            }

            Console.WriteLine($"[SaveMergedData] Updated {updatedCount} fields in {mergedFilePath}");

            // Save the merged data file with compact format
            var compactFormat = Schema.CompactFormat;
            var mergedDocWrapper = new XmlDocumentWrapper(mergedDoc, mergedFilePath, _document!.HasBom, _document.Encoding, _document.IndentString);
            _xmlService.Save(mergedDocWrapper, mergedFilePath, compactFormat);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SaveMergedData] Error saving {mergedFilePath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Auto-generates civilian clone EquipmentSets for each combat EquipmentSet.
    /// This matches XmlGenerator behavior where civilian sets are identical clones.
    /// TODO: Future consideration - allow editing separate civilian equipment.
    /// </summary>
    private void GenerateCivilianClones()
    {
        if (XmlEntries.Count == 0) return;

        var variationElementName = Schema?.VariationElement ?? "EquipmentSet";
        var cloneCount = 0;

        foreach (var roster in XmlEntries)
        {
            // Remove existing civilian clones first
            var civilianSets = roster.Children
                .Where(c => c.ElementName == variationElementName &&
                            c.GetAttributeValue("civilian")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            foreach (var civilianSet in civilianSets)
            {
                civilianSet.OriginalElement.Remove();
                roster.Children.Remove(civilianSet);
            }

            // Get all combat sets (no civilian attribute)
            var combatSets = roster.Children
                .Where(c => c.ElementName == variationElementName &&
                            c.GetAttributeValue("civilian")?.Equals("true", StringComparison.OrdinalIgnoreCase) != true)
                .ToList();

            // Clone each combat set as civilian
            foreach (var combatSet in combatSets)
            {
                var civilianClone = new System.Xml.Linq.XElement(combatSet.OriginalElement);
                civilianClone.SetAttributeValue("civilian", "true");

                // Add after the combat set
                combatSet.OriginalElement.AddAfterSelf(civilianClone);
                cloneCount++;
            }
        }

        Console.WriteLine($"[EquipmentSets] Generated {cloneCount} civilian clones");
    }

    private void SyncChangesToXml()
    {
        foreach (var rowVm in Rows)
        {
            // Skip equipment set variation rows - their changes are applied directly
            // via HandleEquipmentSetCellChange() during editing
            if (rowVm.IsEquipmentSetVariation)
                continue;

            var xmlEntry = rowVm.XmlEntry;

            foreach (var columnName in ColumnNames)
            {
                // Skip cross-reference columns - they're virtual and stored in other files
                var fieldDef = GetFieldDefinition(columnName);
                if (fieldDef?.CrossReference != null)
                    continue;

                var currentValue = rowVm[columnName];

                // Handle nested fields
                if (fieldDef?.Nested == true && !string.IsNullOrEmpty(fieldDef.NestedPath))
                {
                    var existingValue = xmlEntry.GetNestedValue(fieldDef.NestedPath) ?? "";
                    var normalizedCurrent = currentValue ?? "";
                    if (existingValue != normalizedCurrent)
                    {
                        xmlEntry.SetNestedValue(fieldDef.NestedPath, currentValue);
                        _document!.HasUnsavedChanges = true;
                    }
                    continue;
                }

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

    private CancellationTokenSource? _validationDebounceToken;

    public void MarkAsModified()
    {
        HasUnsavedChanges = true;
        if (_document != null)
        {
            _document.HasUnsavedChanges = true;
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

        var indexToDelete = SelectedIndex;

        // Store the row for "removed entries" display (only if not a new entry)
        var rowToDelete = Rows[indexToDelete];
        if (!rowToDelete.IsNew)
        {
            rowToDelete.IsRemoved = true;
            _removedEntries.Add(rowToDelete);
            Console.WriteLine($"[Removed] Stored removed entry: {rowToDelete["id"]} (total: {_removedEntries.Count})");
        }

        var xmlEntryCollection = new ObservableCollection<XmlEntry>(XmlEntries);
        var entryToDelete = xmlEntryCollection[indexToDelete];

        // Remove from new entries tracking
        _newEntries.Remove(entryToDelete);

        var command = new DeleteRowCommand(_document, xmlEntryCollection, entryToDelete);
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
        // Rediscover columns in case new entries have different attributes
        DiscoverColumns(XmlEntries);

        // Recreate the rows (CreateRows handles IsNew tracking via _newEntries)
        CreateRows(XmlEntries);

        // Add removed entries at the end if toggle is on
        if (ShowRemovedEntries)
        {
            foreach (var removedRow in _removedEntries)
            {
                if (!Rows.Contains(removedRow))
                {
                    Rows.Add(removedRow);
                }
            }
        }
    }

    /// <summary>
    /// Refreshes rows to show/hide removed entries based on toggle.
    /// </summary>
    private void RefreshRowsWithRemovedEntries()
    {
        Console.WriteLine($"[Removed] RefreshRowsWithRemovedEntries called, ShowRemovedEntries={ShowRemovedEntries}, count={_removedEntries.Count}");
        if (ShowRemovedEntries)
        {
            // Insert removed entries at their original positions
            // Sort by RowNumber to insert in correct order
            foreach (var removedRow in _removedEntries.OrderBy(r => r.RowNumber))
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
            foreach (var removedRow in _removedEntries.ToList())
            {
                Rows.Remove(removedRow);
            }
        }
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
    private readonly string? _nestedPath;

    public string Description => $"Edit {_columnName}";

    public CellEditUndoCommand(EntryRowViewModel rowVm, string columnName, string oldValue, string newValue, string? nestedPath = null)
    {
        _rowVm = rowVm;
        _columnName = columnName;
        _oldValue = oldValue;
        _newValue = newValue;
        _nestedPath = nestedPath;
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
        // Handle nested fields
        if (!string.IsNullOrEmpty(_nestedPath))
        {
            _rowVm.XmlEntry.SetNestedValue(_nestedPath, value);
            return;
        }

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
