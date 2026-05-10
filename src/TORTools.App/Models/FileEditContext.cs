using System.Collections.ObjectModel;
using TORTools.App.ViewModels;
using TORTools.Core.Models;
using TORTools.Core.Schema;
using TORTools.Core.Validation;

namespace TORTools.App.Models;

/// <summary>
/// Context object that holds all shared state for editing a file.
/// This centralizes state management and is passed between services.
/// </summary>
public class FileEditContext
{
    /// <summary>
    /// The absolute path to the file being edited.
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// The schema definition for this file type.
    /// </summary>
    public SchemaDefinition? Schema { get; set; }

    /// <summary>
    /// The XML entries loaded from the file (data source).
    /// </summary>
    public List<XmlEntry> XmlEntries { get; } = new();

    /// <summary>
    /// The rows displayed in the UI (view models).
    /// </summary>
    public ObservableCollection<EntryRowViewModel> Rows { get; } = new();

    /// <summary>
    /// The column names for the table.
    /// </summary>
    public List<string> ColumnNames { get; } = new();

    /// <summary>
    /// The underlying XML document wrapper.
    /// </summary>
    public XmlDocumentWrapper? Document { get; set; }

    /// <summary>
    /// Validation manager for tracking issues.
    /// </summary>
    public ValidationManager ValidationManager { get; } = new();

    /// <summary>
    /// Git committed values for comparison (entry ID -> field values).
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> GitCommittedValues { get; set; } = new();

    /// <summary>
    /// Available IDs for cross-reference validation (field name -> list of valid IDs).
    /// </summary>
    public Dictionary<string, List<string>> AvailableIds { get; } = new();

    /// <summary>
    /// Cross-reference descriptions (field name -> ID -> description).
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> CrossRefDescriptions { get; } = new();

    /// <summary>
    /// Cross-reference display names (field name -> ID -> display name).
    /// Used for showing friendly names in dropdowns while storing IDs.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> CrossRefDisplayNames { get; } = new();

    /// <summary>
    /// Tracks which entries are new (not yet saved).
    /// </summary>
    public HashSet<string> NewEntries { get; } = new();

    /// <summary>
    /// Tracks removed entries that can be shown/hidden.
    /// </summary>
    public ObservableCollection<EntryRowViewModel> RemovedEntries { get; } = new();

    /// <summary>
    /// Whether there are unsaved changes.
    /// </summary>
    public bool HasUnsavedChanges { get; set; }

    /// <summary>
    /// Whether the file has an error.
    /// </summary>
    public bool HasError { get; set; }

    /// <summary>
    /// Error message if HasError is true.
    /// </summary>
    public string ErrorMessage { get; set; } = "";

    /// <summary>
    /// Clears all state (for reload scenarios).
    /// </summary>
    public void Clear()
    {
        XmlEntries.Clear();
        Rows.Clear();
        ColumnNames.Clear();
        Document = null;
        ValidationManager.Clear();
        GitCommittedValues.Clear();
        AvailableIds.Clear();
        CrossRefDescriptions.Clear();
        CrossRefDisplayNames.Clear();
        NewEntries.Clear();
        RemovedEntries.Clear();
        HasUnsavedChanges = false;
        HasError = false;
        ErrorMessage = "";
    }
}
