using TORTools.Core.Models;

namespace TORTools.Core.Workspace;

/// <summary>
/// Service for managing the TOR workspace configuration and file discovery.
/// </summary>
public interface IWorkspaceService
{
    /// <summary>
    /// Auto-detects the Bannerlord installation and TOR module paths.
    /// </summary>
    /// <returns>A workspace config with detected paths, or empty paths if not found.</returns>
    WorkspaceConfig AutoDetect();

    /// <summary>
    /// Loads the saved workspace configuration.
    /// </summary>
    /// <returns>The saved config, or a new config if none exists.</returns>
    WorkspaceConfig LoadConfig();

    /// <summary>
    /// Saves the workspace configuration.
    /// </summary>
    void SaveConfig(WorkspaceConfig config);

    /// <summary>
    /// Gets all XML files in the workspace.
    /// </summary>
    IReadOnlyList<XmlFileInfo> GetXmlFiles(WorkspaceConfig config);

    /// <summary>
    /// Gets XML files organized by catalog (spanning across repositories).
    /// </summary>
    IReadOnlyList<CatalogGroup> GetCatalogs(WorkspaceConfig config);

    /// <summary>
    /// Validates the workspace configuration.
    /// </summary>
    WorkspaceValidationResult ValidateWorkspace(WorkspaceConfig config);

    /// <summary>
    /// Gets the path to the workspace configuration file.
    /// </summary>
    string ConfigFilePath { get; }
}

/// <summary>
/// Result of workspace validation.
/// </summary>
public record WorkspaceValidationResult
{
    public bool IsValid { get; init; }
    public List<string> Errors { get; init; } = new();
    public List<string> Warnings { get; init; } = new();

    public bool TorCoreFound { get; init; }
    public bool TorArmoryFound { get; init; }
    public bool TorEnvironmentFound { get; init; }
}
