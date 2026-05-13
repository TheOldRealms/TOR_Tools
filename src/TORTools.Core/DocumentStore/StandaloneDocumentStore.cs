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

            Log($"SaveDocument({fileKey}) -> saving to {doc.FilePath} (compact={compactFormat})");
            _xmlService.Save(doc, compactFormat: compactFormat);
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

        foreach (var (name, value) in attributes)
        {
            Log($"UpdateEntry -> set {name}={value ?? "(null)"}");
            entry.SetAttributeValue(name, value);
        }

        // Auto-save
        SaveDocument(fileKey);

        EntryChanged?.Invoke(this, new EntryChangedEventArgs
        {
            FileKey = fileKey,
            EntryId = entryId,
            ChangeType = EntryChangeType.Updated
        });

        return true;
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
