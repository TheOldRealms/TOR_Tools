using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public FileTabViewModel(string filePath) : this(filePath, new XmlDocumentService(), new UndoRedoService(), new SchemaService(), new ValidationService(), new CrossReferenceService())
    {
    }

    public FileTabViewModel(string filePath, IXmlDocumentService xmlService, IUndoRedoService undoRedoService, ISchemaService schemaService, IValidationService validationService, CrossReferenceService crossRefService)
    {
        _xmlService = xmlService;
        _undoRedoService = undoRedoService;
        _schemaService = schemaService;
        _validationService = validationService;
        _crossRefService = crossRefService;
        FilePath = filePath;
        Title = Path.GetFileName(filePath);

        // Load schema for this file type
        Schema = _schemaService.GetSchema(Title);

        // Load cross-reference data if schema defines any
        LoadCrossReferences();

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

                // Find the source file - look in same directory and parent directories
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
                // Found the Modules directory - check TOR_Core
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
        // Clear only basic validation issues (preserve cell-registered cross-ref errors)
        ValidationManager.ClearByPrefix("basic_");

        // Convert rows to dictionaries for basic validation
        var entries = Rows.Select(r => (IDictionary<string, string>)ColumnNames.ToDictionary(
            col => col,
            col => r[col] ?? ""
        )).ToList();

        // Run basic validation (required fields, duplicate IDs)
        var result = _validationService.ValidateAll(entries, Schema);
        foreach (var issue in result.Issues)
        {
            var key = $"basic_{issue.RowIndex}_{issue.AttributeName}_{issue.CurrentValue ?? "empty"}";
            ValidationManager.RegisterError(key, issue);
        }

        // Note: Cross-reference validation is done by cells themselves
        // when they render and call RegisterError/UnregisterErrors
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
            _document = _xmlService.Load(FilePath);
            var entries = _xmlService.GetEntries(_document);

            XmlEntries.Clear();
            XmlEntries.AddRange(entries);

            // Load git committed values for comparison
            LoadGitCommittedValues();

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

        // Add cross-reference and nested columns from schema (these may not be direct attributes)
        if (Schema != null)
        {
            foreach (var kvp in Schema.Fields)
            {
                if (kvp.Value.CrossReference != null && !ColumnNames.Contains(kvp.Key))
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

            _xmlService.Save(_document);
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

    private void SyncChangesToXml()
    {
        foreach (var rowVm in Rows)
        {
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
