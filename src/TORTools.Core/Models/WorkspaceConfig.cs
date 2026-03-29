namespace TORTools.Core.Models;

/// <summary>
/// Configuration for the TOR workspace, storing paths to TOR repositories.
/// </summary>
public class WorkspaceConfig
{
    /// <summary>
    /// Path to the Bannerlord installation directory.
    /// </summary>
    public string? BannerlordPath { get; set; }

    /// <summary>
    /// Path to the TOR_Core module.
    /// </summary>
    public string? TorCorePath { get; set; }

    /// <summary>
    /// Path to the TOR_Armory module.
    /// </summary>
    public string? TorArmoryPath { get; set; }

    /// <summary>
    /// Path to the TOR_Environment module.
    /// </summary>
    public string? TorEnvironmentPath { get; set; }

    /// <summary>
    /// List of recently opened files.
    /// </summary>
    public List<string> RecentFiles { get; set; } = new();

    /// <summary>
    /// Whether the workspace has been configured.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrEmpty(TorCorePath) ||
                                 !string.IsNullOrEmpty(TorArmoryPath) ||
                                 !string.IsNullOrEmpty(TorEnvironmentPath);
}
