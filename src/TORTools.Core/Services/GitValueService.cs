using System.Diagnostics;
using System.Xml.Linq;

namespace TORTools.Core.Services;

/// <summary>
/// Service for loading git-committed values for file comparison.
/// </summary>
public class GitValueService : IGitValueService
{
    /// <inheritdoc />
    public Dictionary<string, Dictionary<string, string>> LoadGitCommittedValues(string filePath)
    {
        var gitValues = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Find the git repository root
            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(directory)) return gitValues;

            // Get relative path from git root
            var relativePath = GetGitRelativePath(directory, filePath);
            if (string.IsNullOrEmpty(relativePath)) return gitValues;

            // Run git show HEAD:<path> to get committed content
            var gitContent = RunGitShow(directory, relativePath);
            if (string.IsNullOrEmpty(gitContent)) return gitValues;

            // Parse the XML content and extract values
            ParseGitContent(gitContent, gitValues);

            Console.WriteLine($"[Git] Loaded {gitValues.Count} entries from git for comparison");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Git] Failed to load git committed values: {ex.Message}");
        }

        return gitValues;
    }

    /// <summary>
    /// Gets the relative path of a file from the git repository root.
    /// </summary>
    private static string? GetGitRelativePath(string workingDir, string filePath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse --show-toplevel",
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
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
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"show HEAD:{relativePath}",
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
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
    /// Parses git XML content and extracts values into the provided dictionary.
    /// </summary>
    private static void ParseGitContent(string xmlContent, Dictionary<string, Dictionary<string, string>> gitValues)
    {
        try
        {
            var doc = XDocument.Parse(xmlContent);
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

                gitValues[entryId] = values;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Git] Failed to parse git content: {ex.Message}");
        }
    }
}
