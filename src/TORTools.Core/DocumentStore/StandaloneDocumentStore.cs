using System.Xml.Linq;
using TORTools.Core.Models;
using TORTools.Core.Schema;
using TORTools.Core.Services;
using TORTools.Core.Workspace;

namespace TORTools.Core.DocumentStore;

/// <summary>
/// Standalone document store that loads XML files directly from disk.
/// Used by the MCP server when running independently of the UI.
/// </summary>
public class StandaloneDocumentStore : IDocumentStore
{
    private readonly IXmlDocumentService _xmlService;
    private readonly IWorkspaceService _workspaceService;
    private readonly ISchemaService _schemaService;
    private readonly CrossReferenceService _crossRefService = new();
    private readonly FilePathResolver _pathResolver = new();

    private WorkspaceConfig _workspaceConfig = new();
    private readonly Dictionary<string, XmlDocumentWrapper> _loadedDocuments = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, XmlFileInfo> _fileInfoCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>
    /// Enable verbose logging.
    /// </summary>
    public static bool VerboseLogging { get; set; } = false;

    /// <summary>
    /// Path to log file. If null, logs go to stderr.
    /// </summary>
    public static string? LogFilePath { get; set; }

    private static StreamWriter? _logWriter;
    private static readonly object _logLock = new();

    public event EventHandler<DocumentChangedEventArgs>? DocumentChanged;
    public event EventHandler<EntryChangedEventArgs>? EntryChanged;

    /// <summary>
    /// Initialize file logging. Call once at startup if LogFilePath is set.
    /// </summary>
    public static void InitializeLogging()
    {
        if (string.IsNullOrEmpty(LogFilePath))
            return;

        try
        {
            var logDir = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);

            _logWriter = new StreamWriter(LogFilePath, append: true) { AutoFlush = true };
            _logWriter.WriteLine($"\n=== TORTools MCP Log Started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DocumentStore] Failed to initialize log file: {ex.Message}");
        }
    }

    /// <summary>
    /// Close the log file.
    /// </summary>
    public static void CloseLogging()
    {
        lock (_logLock)
        {
            _logWriter?.WriteLine($"=== TORTools MCP Log Ended {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            _logWriter?.Dispose();
            _logWriter = null;
        }
    }

    private static void Log(string message)
    {
        Log("DocumentStore", message);
    }

    /// <summary>
    /// Public logging method for tools to use.
    /// </summary>
    public static void Log(string source, string message)
    {
        if (!VerboseLogging)
            return;

        var logLine = $"[{source}] {DateTime.Now:HH:mm:ss.fff} {message}";

        lock (_logLock)
        {
            if (_logWriter != null)
            {
                _logWriter.WriteLine(logLine);
            }
            else
            {
                Console.Error.WriteLine(logLine);
            }
        }
    }

    public StandaloneDocumentStore(
        IXmlDocumentService xmlService,
        IWorkspaceService workspaceService)
    {
        _xmlService = xmlService;
        _workspaceService = workspaceService;
        _schemaService = new SchemaService();
    }

    public InitializeResult Initialize()
    {
        Log("Initializing workspace...");
        try
        {
            // Try to load saved config, fall back to auto-detect
            _workspaceConfig = _workspaceService.LoadConfig();
            Log($"  Loaded config: TOR_Core={_workspaceConfig.TorCorePath ?? "(not set)"}");

            if (!_workspaceConfig.IsConfigured)
            {
                Log("  Config not set, auto-detecting...");
                _workspaceConfig = _workspaceService.AutoDetect();
            }

            var validation = _workspaceService.ValidateWorkspace(_workspaceConfig);
            Log($"  Validation: Core={validation.TorCoreFound}, Armory={validation.TorArmoryFound}, Env={validation.TorEnvironmentFound}");

            if (!validation.IsValid)
            {
                Log($"  ERROR: {string.Join("; ", validation.Errors)}");
                return new InitializeResult
                {
                    Success = false,
                    Error = string.Join("; ", validation.Errors),
                    ValidationResult = validation
                };
            }

            // Cache file info for quick lookups
            var files = _workspaceService.GetXmlFiles(_workspaceConfig);
            foreach (var file in files)
            {
                _fileInfoCache[file.FileName] = file;
                _fileInfoCache[file.FilePath] = file;
            }
            Log($"  Cached {files.Count} XML files");

            return new InitializeResult
            {
                Success = true,
                ValidationResult = validation
            };
        }
        catch (Exception ex)
        {
            Log($"  EXCEPTION: {ex.Message}");
            return new InitializeResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    public WorkspaceConfig GetWorkspaceConfig() => _workspaceConfig;

    public IReadOnlyList<XmlFileInfo> GetAvailableFiles()
    {
        return _workspaceService.GetXmlFiles(_workspaceConfig);
    }

    public XmlDocumentWrapper? GetDocument(string fileKey)
    {
        lock (_lock)
        {
            // Check if already loaded
            if (_loadedDocuments.TryGetValue(fileKey, out var doc))
            {
                Log($"GetDocument({fileKey}) -> cached");
                return doc;
            }

            // Resolve file path
            var filePath = ResolveFilePath(fileKey);
            if (filePath == null)
            {
                Log($"GetDocument({fileKey}) -> NOT FOUND");
                return null;
            }

            // Check if loaded by different key
            if (_loadedDocuments.TryGetValue(filePath, out doc))
            {
                _loadedDocuments[fileKey] = doc; // Cache by both keys
                Log($"GetDocument({fileKey}) -> cached by path");
                return doc;
            }

            // Load the document
            try
            {
                Log($"GetDocument({fileKey}) -> loading from {filePath}");
                doc = _xmlService.Load(filePath);
                _loadedDocuments[fileKey] = doc;
                _loadedDocuments[filePath] = doc;
                Log($"GetDocument({fileKey}) -> loaded {_xmlService.GetEntries(doc).Count} entries");

                // Merge linked data (e.g. tor_strings_metadata.xml) into in-memory entries
                var schema = GetSchema(fileKey);
                if (schema?.MergedDataFile != null)
                {
                    MergeDataFromFile(doc, schema);
                }

                // Merge crossReference values (e.g. RaceLock from tor_extendeditemproperties.xml)
                // so queries see them and so UpdateEntry can round-trip them correctly.
                if (schema != null)
                {
                    MergeCrossReferences(doc, schema);
                }

                DocumentChanged?.Invoke(this, new DocumentChangedEventArgs
                {
                    FileKey = fileKey,
                    ChangeType = DocumentChangeType.Loaded
                });

                return doc;
            }
            catch (Exception ex)
            {
                Log($"GetDocument({fileKey}) -> ERROR: {ex.Message}");
                Console.Error.WriteLine($"[DocumentStore] Failed to load {filePath}: {ex.Message}");
                return null;
            }
        }
    }

    public void SaveDocument(string fileKey)
    {
        lock (_lock)
        {
            if (!_loadedDocuments.TryGetValue(fileKey, out var doc))
            {
                Log($"SaveDocument({fileKey}) -> not loaded, skipping");
                return;
            }

            var schema = GetSchema(fileKey);
            var compactFormat = schema?.CompactFormat ?? true;

            // Fields that live in a sidecar file (e.g. tor_strings_metadata.xml) OR in a
            // crossReference source file (e.g. tor_extendeditemproperties.xml for RaceLock).
            // Both must be stripped from the main document on save.
            // IMPORTANT: only crossReference fields with a SourceFile live elsewhere.
            // Pure foreign-key crossRefs (TargetFile only, like `culture`) live inline on
            // the main element and must NOT be excluded.
            var excludeFields = schema?.Fields
                .Where(f => f.Value.LinkedField == true
                            || (f.Value.Type == "crossReference"
                                && f.Value.CrossReference != null
                                && !string.IsNullOrEmpty(f.Value.CrossReference.SourceFile)))
                .Select(f => f.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (excludeFields != null && excludeFields.Count == 0) excludeFields = null;

            Log($"SaveDocument({fileKey}) -> saving to {doc.FilePath} (compact={compactFormat}, linkedFields={excludeFields?.Count ?? 0})");

            // tor_strings uses groupByField="category" where category is itself a linked field,
            // so we need the special save path that reads the group value from XmlEntry attrs
            // (which include the merged-in linked values) rather than from the raw XML.
            var groupByField = schema?.GroupByField;
            var groupFieldDef = !string.IsNullOrEmpty(groupByField) ? schema!.GetField(groupByField) : null;

            if (groupFieldDef?.LinkedField == true)
            {
                var entries = _xmlService.GetEntries(doc);
                _xmlService.SaveWithCategoryComments(
                    doc,
                    entries,
                    doc.FilePath,
                    schema!.RootElement ?? "strings",
                    compactFormat,
                    excludeFields);
            }
            else
            {
                _xmlService.Save(doc, null, compactFormat, groupByField, excludeFields);
            }

            // Write linked-field values back to the sidecar file.
            if (schema?.MergedDataFile != null)
            {
                var baseDir = Path.GetDirectoryName(doc.FilePath);
                if (!string.IsNullOrEmpty(baseDir))
                {
                    SaveMergedDataFile(doc, schema, baseDir);
                }
            }

            Log($"SaveDocument({fileKey}) -> saved successfully");

            DocumentChanged?.Invoke(this, new DocumentChangedEventArgs
            {
                FileKey = fileKey,
                ChangeType = DocumentChangeType.Saved
            });
        }
    }

    public bool IsDocumentLoaded(string fileKey)
    {
        lock (_lock)
        {
            return _loadedDocuments.ContainsKey(fileKey);
        }
    }

    public IReadOnlyList<XmlEntry> GetEntries(string fileKey)
    {
        var doc = GetDocument(fileKey);
        if (doc == null)
            return Array.Empty<XmlEntry>();

        return _xmlService.GetEntries(doc);
    }

    public XmlEntry? GetEntry(string fileKey, string entryId)
    {
        var entries = GetEntries(fileKey);
        return entries.FirstOrDefault(e =>
            string.Equals(e.Id, entryId, StringComparison.OrdinalIgnoreCase));
    }

    public XmlEntry? CreateEntry(string fileKey, string? templateId = null)
    {
        Log($"CreateEntry({fileKey}, template={templateId ?? "none"})");
        var doc = GetDocument(fileKey);
        if (doc == null)
        {
            Log($"CreateEntry -> file not found");
            return null;
        }

        XmlEntry? template = null;
        if (!string.IsNullOrEmpty(templateId))
        {
            template = GetEntry(fileKey, templateId);
            Log($"CreateEntry -> using template: {template?.Id ?? "NOT FOUND"}");
        }

        var newEntry = _xmlService.AddEntry(doc, template);
        Log($"CreateEntry -> created entry id={newEntry.Id}");

        // Auto-save
        SaveDocument(fileKey);

        EntryChanged?.Invoke(this, new EntryChangedEventArgs
        {
            FileKey = fileKey,
            EntryId = newEntry.Id ?? "",
            ChangeType = EntryChangeType.Created
        });

        return newEntry;
    }

    public bool UpdateEntry(string fileKey, string entryId, Dictionary<string, string?> attributes)
    {
        Log($"UpdateEntry({fileKey}, {entryId}, {attributes.Count} attrs)");
        var entry = GetEntry(fileKey, entryId);
        if (entry == null)
        {
            Log($"UpdateEntry -> entry not found");
            return false;
        }

        var schema = GetSchema(fileKey);
        var doc = GetDocument(fileKey);
        var baseDir = doc != null ? Path.GetDirectoryName(doc.FilePath) : null;

        foreach (var (name, value) in attributes)
        {
            var fieldDef = schema?.GetField(name);
            var crossRef = fieldDef?.CrossReference;

            // Route crossReference writes to the source file (e.g. RaceLock -> tor_extendeditemproperties.xml).
            // Matches the UI's FileTabViewModel.UpdateCrossReferenceValue path.
            // Only applies to crossRefs with a SourceFile; pure foreign-key crossRefs
            // (TargetFile only, like `culture`) live inline and get the regular path.
            if (fieldDef?.Type == "crossReference" && crossRef != null
                && !string.IsNullOrEmpty(crossRef.SourceFile) && !string.IsNullOrEmpty(baseDir))
            {
                var sourcePath = _pathResolver.FindSourceFile(baseDir, crossRef.SourceFile);
                if (sourcePath != null)
                {
                    var localKey = string.IsNullOrEmpty(crossRef.LocalKeyField)
                        ? entry.Id ?? entryId
                        : entry.GetAttributeValue(crossRef.LocalKeyField) ?? entry.Id ?? entryId;
                    var newValues = ParseCrossRefValueList(value);
                    Log($"UpdateEntry -> crossRef {name}={value ?? "(null)"} -> {Path.GetFileName(sourcePath)} key={localKey}");
                    _crossRefService.UpdateCrossReference(sourcePath, crossRef, localKey, newValues, compactFormat: true);
                    // Also mirror onto the in-memory XElement so GetEntry reflects the write.
                    entry.SetAttributeValue(name, value);
                    continue;
                }
                Log($"UpdateEntry -> crossRef {name}: source file '{crossRef.SourceFile}' not found; falling back to inline");
            }

            Log($"UpdateEntry -> set {name}={value ?? "(null)"}");
            entry.SetAttributeValue(name, value);
        }

        // Auto-save (strips linkedField/crossRef attrs from main doc).
        SaveDocument(fileKey);

        EntryChanged?.Invoke(this, new EntryChangedEventArgs
        {
            FileKey = fileKey,
            EntryId = entryId,
            ChangeType = EntryChangeType.Updated
        });

        return true;
    }

    private static List<string> ParseCrossRefValueList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new List<string>();
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    public bool DeleteEntry(string fileKey, string entryId)
    {
        Log($"DeleteEntry({fileKey}, {entryId})");
        var doc = GetDocument(fileKey);
        if (doc == null)
        {
            Log($"DeleteEntry -> file not found");
            return false;
        }

        var entry = GetEntry(fileKey, entryId);
        if (entry == null)
        {
            Log($"DeleteEntry -> entry not found");
            return false;
        }

        _xmlService.RemoveEntry(doc, entry);
        Log($"DeleteEntry -> removed entry");

        // Auto-save
        SaveDocument(fileKey);

        EntryChanged?.Invoke(this, new EntryChangedEventArgs
        {
            FileKey = fileKey,
            EntryId = entryId,
            ChangeType = EntryChangeType.Deleted
        });

        return true;
    }

    public XmlEntry? DuplicateEntry(string fileKey, string entryId)
    {
        Log($"DuplicateEntry({fileKey}, {entryId})");
        var doc = GetDocument(fileKey);
        if (doc == null)
        {
            Log($"DuplicateEntry -> file not found");
            return null;
        }

        var entry = GetEntry(fileKey, entryId);
        if (entry == null)
        {
            Log($"DuplicateEntry -> entry not found");
            return null;
        }

        var newEntry = _xmlService.DuplicateEntry(doc, entry);
        Log($"DuplicateEntry -> created {newEntry.Id} from {entryId}");

        // Auto-save
        SaveDocument(fileKey);

        EntryChanged?.Invoke(this, new EntryChangedEventArgs
        {
            FileKey = fileKey,
            EntryId = newEntry.Id ?? "",
            ChangeType = EntryChangeType.Created
        });

        return newEntry;
    }

    public SchemaDefinition? GetSchema(string fileKey)
    {
        // Try by file key directly
        var schema = _schemaService.GetSchema(fileKey);
        if (schema != null)
            return schema;

        // Try by resolved file name
        if (_fileInfoCache.TryGetValue(fileKey, out var fileInfo))
        {
            return _schemaService.GetSchema(fileInfo.FileName);
        }

        return null;
    }

    public List<ReferenceResult> FindReferences(string targetId, string? sourceFileKey = null)
    {
        var results = new List<ReferenceResult>();
        var filesToSearch = sourceFileKey != null
            ? new[] { sourceFileKey }
            : _fileInfoCache.Values.Select(f => f.FileName).Distinct().ToArray();

        foreach (var fileKey in filesToSearch)
        {
            var schema = GetSchema(fileKey);
            if (schema == null) continue;

            var entries = GetEntries(fileKey);
            foreach (var entry in entries)
            {
                foreach (var attr in entry.Attributes)
                {
                    // Check if this attribute references the target ID
                    if (ContainsReference(attr.RawValue, targetId))
                    {
                        results.Add(new ReferenceResult
                        {
                            File = fileKey,
                            EntryId = entry.Id ?? "",
                            Field = attr.Name
                        });
                    }
                }
            }
        }

        return results;
    }

    private static bool ContainsReference(string? value, string targetId)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(targetId))
            return false;

        // Direct match
        if (value.Equals(targetId, StringComparison.OrdinalIgnoreCase))
            return true;

        // Check for ID in a colon-separated list
        var parts = value.Split(':');
        return parts.Any(p => p.Trim().Equals(targetId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reads a merged data file (e.g. tor_strings_metadata.xml) and copies its mapped
    /// attributes onto the in-memory XmlEntry elements. Mirrors FileLoaderService.MergeDataFromFile.
    /// </summary>
    private void MergeDataFromFile(XmlDocumentWrapper doc, SchemaDefinition schema)
    {
        var mergedConfig = schema.MergedDataFile;
        if (mergedConfig == null) return;

        var baseDir = Path.GetDirectoryName(doc.FilePath);
        if (string.IsNullOrEmpty(baseDir)) return;

        var mergedFilePath = FindMergedDataFile(baseDir, mergedConfig.FileName);
        if (mergedFilePath == null)
        {
            Log($"MergeData -> sidecar '{mergedConfig.FileName}' not found; skipping");
            return;
        }

        var entryElementName = mergedConfig.EntryElement ?? "String";
        var matchField = mergedConfig.MatchField ?? "id";

        XDocument mergedDoc;
        try
        {
            mergedDoc = XDocument.Load(mergedFilePath);
        }
        catch (Exception ex)
        {
            Log($"MergeData -> failed to load {mergedFilePath}: {ex.Message}");
            return;
        }

        var mergedRoot = mergedDoc.Root;
        if (mergedRoot == null) return;

        var mergedData = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in mergedRoot.Elements(entryElementName))
        {
            var key = element.Attribute(matchField)?.Value;
            if (!string.IsNullOrEmpty(key))
                mergedData[key] = element;
        }

        var entries = _xmlService.GetEntries(doc);
        int mergedCount = 0;
        foreach (var entry in entries)
        {
            var entryId = entry.GetAttributeValue(matchField);
            if (string.IsNullOrEmpty(entryId)) continue;
            if (!mergedData.TryGetValue(entryId, out var mergedElement)) continue;
            if (mergedConfig.FieldMappings == null) continue;

            foreach (var mapping in mergedConfig.FieldMappings)
            {
                var sourceValue = mergedElement.Attribute(mapping.Value)?.Value;
                if (!string.IsNullOrEmpty(sourceValue))
                    entry.SetAttributeValue(mapping.Key, sourceValue);
            }
            mergedCount++;
        }

        // Match UI behavior: auto-infer category from string ID for entries that don't have one.
        if (mergedConfig.FieldMappings?.ContainsKey("category") == true)
        {
            foreach (var entry in entries)
            {
                if (!string.IsNullOrEmpty(entry.GetAttributeValue("category"))) continue;
                var inferred = StringCategoryService.InferCategory(entry.GetAttributeValue("id"));
                if (!string.IsNullOrEmpty(inferred))
                    entry.SetAttributeValue("category", inferred);
            }
        }

        Log($"MergeData -> merged {mergedCount} entries from {mergedFilePath}");
    }

    /// <summary>
    /// Loads crossReference values (e.g. RaceLock from tor_extendeditemproperties.xml) and
    /// sets them as attributes on the in-memory XmlEntry elements. Mirrors the UI's
    /// FileTabViewModel load path. On save, these attributes are stripped out again
    /// (see excludeFields in SaveDocument).
    /// </summary>
    private void MergeCrossReferences(XmlDocumentWrapper doc, SchemaDefinition schema)
    {
        var baseDir = Path.GetDirectoryName(doc.FilePath);
        if (string.IsNullOrEmpty(baseDir)) return;

        // Only merge crossRefs that have a SourceFile (data lives elsewhere). Pure foreign-key
        // crossRefs (`culture` → tor_cultures.xml) already live inline on the main element.
        var crossRefFields = schema.Fields
            .Where(kvp => kvp.Value.Type == "crossReference"
                          && kvp.Value.CrossReference != null
                          && !string.IsNullOrEmpty(kvp.Value.CrossReference.SourceFile))
            .ToList();
        if (crossRefFields.Count == 0) return;

        var entries = _xmlService.GetEntries(doc);
        foreach (var kvp in crossRefFields)
        {
            var fieldName = kvp.Key;
            var config = kvp.Value.CrossReference!;
            var sourcePath = _pathResolver.FindSourceFile(baseDir, config.SourceFile);
            if (sourcePath == null)
            {
                Log($"MergeCrossRef -> {fieldName}: source '{config.SourceFile}' not found; skipping");
                continue;
            }

            var crossRefs = _crossRefService.LoadCrossReferences(sourcePath, config);
            if (crossRefs.Count == 0) continue;

            var keyField = string.IsNullOrEmpty(config.LocalKeyField) ? "id" : config.LocalKeyField;
            int applied = 0;
            foreach (var entry in entries)
            {
                var localKey = entry.GetAttributeValue(keyField);
                if (string.IsNullOrEmpty(localKey)) continue;
                if (!crossRefs.TryGetValue(localKey, out var values) || values.Count == 0) continue;

                var joined = string.Join(", ", values);
                entry.SetAttributeValue(fieldName, joined);
                applied++;
            }
            Log($"MergeCrossRef -> {fieldName}: applied {applied} values from {Path.GetFileName(sourcePath)}");
        }
    }

    /// <summary>
    /// Writes linked-field values back to the sidecar metadata file, upserting entries
    /// by match field. Mirrors FileSaverService.SaveMergedDataFile.
    /// </summary>
    private void SaveMergedDataFile(XmlDocumentWrapper doc, SchemaDefinition schema, string baseDir)
    {
        var mergedConfig = schema.MergedDataFile;
        if (mergedConfig == null) return;

        var entryElementName = mergedConfig.EntryElement ?? "String";
        var matchField = mergedConfig.MatchField ?? "id";
        var rootElementName = mergedConfig.RootElement ?? "StringMetadata";

        var mergedFilePath = FindMergedDataFile(baseDir, mergedConfig.FileName);

        XDocument mergedDoc;
        XElement mergedRoot;
        if (mergedFilePath == null)
        {
            mergedFilePath = GetMetadataFilePath(baseDir, mergedConfig.FileName);
            Log($"SaveMergedData -> creating new sidecar: {mergedFilePath}");
            mergedRoot = new XElement(rootElementName);
            mergedDoc = new XDocument(new XDeclaration("1.0", "UTF-8", null), mergedRoot);
        }
        else
        {
            try
            {
                mergedDoc = XDocument.Load(mergedFilePath);
                mergedRoot = mergedDoc.Root ?? new XElement(rootElementName);
            }
            catch (Exception ex)
            {
                Log($"SaveMergedData -> failed to load {mergedFilePath}, recreating: {ex.Message}");
                mergedRoot = new XElement(rootElementName);
                mergedDoc = new XDocument(new XDeclaration("1.0", "UTF-8", null), mergedRoot);
            }
        }

        var existingEntries = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in mergedRoot.Elements(entryElementName))
        {
            var key = element.Attribute(matchField)?.Value;
            if (!string.IsNullOrEmpty(key))
                existingEntries[key] = element;
        }

        int updated = 0, created = 0;
        foreach (var entry in _xmlService.GetEntries(doc))
        {
            var entryId = entry.GetAttributeValue(matchField);
            if (string.IsNullOrEmpty(entryId)) continue;
            if (mergedConfig.FieldMappings == null) continue;

            bool hasLinkedData = mergedConfig.FieldMappings
                .Any(m => !string.IsNullOrEmpty(entry.GetAttributeValue(m.Key)));
            if (!hasLinkedData) continue;

            if (existingEntries.TryGetValue(entryId, out var element))
            {
                foreach (var mapping in mergedConfig.FieldMappings)
                {
                    var value = entry.GetAttributeValue(mapping.Key);
                    if (!string.IsNullOrEmpty(value) && element.Attribute(mapping.Value)?.Value != value)
                    {
                        element.SetAttributeValue(mapping.Value, value);
                        updated++;
                    }
                }
            }
            else
            {
                var newElement = new XElement(entryElementName);
                newElement.SetAttributeValue(matchField, entryId);
                foreach (var mapping in mergedConfig.FieldMappings)
                {
                    var value = entry.GetAttributeValue(mapping.Key);
                    if (!string.IsNullOrEmpty(value))
                        newElement.SetAttributeValue(mapping.Value, value);
                }
                mergedRoot.Add(newElement);
                created++;
            }
        }

        Log($"SaveMergedData -> {updated} updated / {created} created in {mergedFilePath}");

        try
        {
            var wrapper = new XmlDocumentWrapper(mergedDoc, mergedFilePath, doc.HasBom, doc.Encoding, doc.IndentString);
            _xmlService.Save(wrapper, mergedFilePath, mergedConfig.CompactFormat);
        }
        catch (Exception ex)
        {
            Log($"SaveMergedData -> error saving {mergedFilePath}: {ex.Message}");
        }
    }

    private static string? FindMergedDataFile(string baseDir, string fileName)
    {
        var torToolsDataPath = FilePathResolver.GetDataDirectory();
        if (!string.IsNullOrEmpty(torToolsDataPath))
        {
            var toolsDataFile = Path.Combine(torToolsDataPath, fileName);
            if (File.Exists(toolsDataFile)) return toolsDataFile;
        }

        var customPath = Path.Combine(baseDir, "tor_custom_xmls", fileName);
        if (File.Exists(customPath)) return customPath;

        var basePath = Path.Combine(baseDir, fileName);
        if (File.Exists(basePath)) return basePath;

        var parentDir = Path.GetDirectoryName(baseDir);
        if (!string.IsNullOrEmpty(parentDir))
        {
            var parentPath = Path.Combine(parentDir, fileName);
            if (File.Exists(parentPath)) return parentPath;
        }

        return null;
    }

    private static string GetMetadataFilePath(string baseDir, string fileName)
    {
        var torToolsDataPath = FilePathResolver.GetDataDirectory();
        if (!string.IsNullOrEmpty(torToolsDataPath))
            return Path.Combine(torToolsDataPath, fileName);
        return Path.Combine(baseDir, fileName);
    }

    private string? ResolveFilePath(string fileKey)
    {
        // If it's already a full path and exists, use it
        if (Path.IsPathRooted(fileKey) && File.Exists(fileKey))
            return fileKey;

        // Look up in cache
        if (_fileInfoCache.TryGetValue(fileKey, out var fileInfo))
            return fileInfo.FilePath;

        // Try to find by file name
        var fileName = Path.GetFileName(fileKey);
        if (_fileInfoCache.TryGetValue(fileName, out fileInfo))
            return fileInfo.FilePath;

        return null;
    }
}
