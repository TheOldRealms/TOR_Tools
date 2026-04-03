namespace TORTools.Core.Services;

/// <summary>
/// Service for loading git-committed values for file comparison.
/// </summary>
public interface IGitValueService
{
    /// <summary>
    /// Loads the git-committed values for a file.
    /// Returns a dictionary mapping entry ID to attribute values.
    /// </summary>
    /// <param name="filePath">The absolute path to the file.</param>
    /// <returns>Dictionary of entry ID -> (attribute name -> value)</returns>
    Dictionary<string, Dictionary<string, string>> LoadGitCommittedValues(string filePath);
}
