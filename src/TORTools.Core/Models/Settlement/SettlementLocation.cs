namespace TORTools.Core.Models.Settlement;

/// <summary>
/// Represents a location within a settlement (center, tavern, lordshall, etc.)
/// with scene assignments.
/// </summary>
public class SettlementLocation
{
    /// <summary>
    /// Location identifier (e.g., "center", "tavern", "lordshall", "prison", "village_center").
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Primary scene name for this location.
    /// </summary>
    public string SceneName { get; set; } = "";

    /// <summary>
    /// Scene name for level 1 (used for lordshall/keep).
    /// </summary>
    public string SceneName1 { get; set; } = "";

    /// <summary>
    /// Scene name for level 2 (used for lordshall/keep).
    /// </summary>
    public string SceneName2 { get; set; } = "";

    /// <summary>
    /// Scene name for level 3 (used for lordshall/keep).
    /// </summary>
    public string SceneName3 { get; set; } = "";

    /// <summary>
    /// Returns true if this location has any scene assigned.
    /// </summary>
    public bool HasScene => !string.IsNullOrEmpty(SceneName)
                         || !string.IsNullOrEmpty(SceneName1)
                         || !string.IsNullOrEmpty(SceneName2)
                         || !string.IsNullOrEmpty(SceneName3);

    /// <summary>
    /// Returns the primary scene name, falling back to level 1 if not set.
    /// </summary>
    public string PrimaryScene => !string.IsNullOrEmpty(SceneName) ? SceneName : SceneName1;

    /// <summary>
    /// Returns true if this location uses level-based scenes (lordshall pattern).
    /// </summary>
    public bool UsesLevelScenes => !string.IsNullOrEmpty(SceneName1)
                                 || !string.IsNullOrEmpty(SceneName2)
                                 || !string.IsNullOrEmpty(SceneName3);
}
