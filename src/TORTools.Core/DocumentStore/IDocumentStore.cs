using TORTools.Core.Models;
using TORTools.Core.Schema;
using TORTools.Core.Workspace;

namespace TORTools.Core.DocumentStore;

/// <summary>
/// Abstraction for document storage operations.
/// Enables both standalone file-based storage and in-app shared document state.
/// </summary>
public interface IDocumentStore
{
    /// <summary>
    /// Initializes the document store (auto-detects workspace, etc.).
    /// </summary>
    InitializeResult Initialize();

    /// <summary>
    /// Gets the current workspace configuration.
    /// </summary>
    WorkspaceConfig GetWorkspaceConfig();

    /// <summary>
    /// Gets all available XML files in the workspace.
    /// </summary>
    IReadOnlyList<XmlFileInfo> GetAvailableFiles();

    /// <summary>
    /// Gets a document by file key (file name or full path).
    /// Loads the document if not already loaded.
    /// </summary>
    XmlDocumentWrapper? GetDocument(string fileKey);

    /// <summary>
    /// Saves a document to disk.
    /// </summary>
    void SaveDocument(string fileKey);

    /// <summary>
    /// Checks if a document is currently loaded.
    /// </summary>
    bool IsDocumentLoaded(string fileKey);

    /// <summary>
    /// Gets all entries from a document.
    /// </summary>
    IReadOnlyList<XmlEntry> GetEntries(string fileKey);

    /// <summary>
    /// Gets a single entry by ID.
    /// </summary>
    XmlEntry? GetEntry(string fileKey, string entryId);

    /// <summary>
    /// Creates a new entry, optionally based on a template.
    /// </summary>
    XmlEntry? CreateEntry(string fileKey, string? templateId = null);

    /// <summary>
    /// Updates an entry's attributes.
    /// </summary>
    bool UpdateEntry(string fileKey, string entryId, Dictionary<string, string?> attributes);

    /// <summary>
    /// Deletes an entry by ID.
    /// </summary>
    bool DeleteEntry(string fileKey, string entryId);

    /// <summary>
    /// Duplicates an entry with a new ID.
    /// </summary>
    XmlEntry? DuplicateEntry(string fileKey, string entryId);

    /// <summary>
    /// Gets the schema definition for a file.
    /// </summary>
    SchemaDefinition? GetSchema(string fileKey);

    /// <summary>
    /// Finds all entries that reference a given ID.
    /// </summary>
    List<ReferenceResult> FindReferences(string targetId, string? sourceFileKey = null);

    /// <summary>
    /// Raised when a document is modified.
    /// </summary>
    event EventHandler<DocumentChangedEventArgs>? DocumentChanged;

    /// <summary>
    /// Raised when an entry is created, updated, or deleted.
    /// </summary>
    event EventHandler<EntryChangedEventArgs>? EntryChanged;
}

/// <summary>
/// Result of initialization.
/// </summary>
public record InitializeResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public WorkspaceValidationResult? ValidationResult { get; init; }
}

/// <summary>
/// Result of finding a reference.
/// </summary>
public record ReferenceResult
{
    public required string File { get; init; }
    public required string EntryId { get; init; }
    public required string Field { get; init; }
}

/// <summary>
/// Event args for document changes.
/// </summary>
public class DocumentChangedEventArgs : EventArgs
{
    public required string FileKey { get; init; }
    public required DocumentChangeType ChangeType { get; init; }
}

/// <summary>
/// Event args for entry changes.
/// </summary>
public class EntryChangedEventArgs : EventArgs
{
    public required string FileKey { get; init; }
    public required string EntryId { get; init; }
    public required EntryChangeType ChangeType { get; init; }
}

/// <summary>
/// Type of document change.
/// </summary>
public enum DocumentChangeType
{
    Loaded,
    Saved,
    Modified
}

/// <summary>
/// Type of entry change.
/// </summary>
public enum EntryChangeType
{
    Created,
    Updated,
    Deleted
}
