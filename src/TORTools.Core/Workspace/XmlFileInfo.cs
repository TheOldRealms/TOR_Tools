namespace TORTools.Core.Workspace;

/// <summary>
/// Metadata about a single XML file in the workspace.
/// </summary>
public class XmlFileInfo
{
    /// <summary>
    /// Full path to the XML file.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// File name without path.
    /// </summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>
    /// Human-readable display name (e.g., "Armors", "Melee Weapons").
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Catalog this file belongs to (e.g., "Item Catalog", "Unit Catalog").
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// Which TOR repository this file belongs to.
    /// </summary>
    public required string Repository { get; init; }

    /// <summary>
    /// Relative path from the repository root.
    /// </summary>
    public required string RelativePath { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long FileSize { get; init; }

    /// <summary>
    /// Last modified time.
    /// </summary>
    public DateTime LastModified { get; init; }
}
