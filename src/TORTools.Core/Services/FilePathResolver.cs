namespace TORTools.Core.Services;

/// <summary>
/// Resolves file paths across the TOR mod directory structure.
/// Handles locating XML files in various mod directories (TOR_Core, TOR_Armory, etc.)
/// and the tool's data directory.
/// </summary>
public class FilePathResolver
{
    private static string? _cachedDataDirectory;

    /// <summary>
    /// Gets the path to the tool's data directory.
    /// Uses relative path from app location - works regardless of folder name.
    /// </summary>
    public static string? GetDataDirectory()
    {
        if (_cachedDataDirectory != null)
            return _cachedDataDirectory;

        var appDir = AppDomain.CurrentDomain.BaseDirectory;

        // Release: exe is in release/, data is in ../data/
        var path = Path.GetFullPath(Path.Combine(appDir, "..", "data"));
        if (Directory.Exists(path))
        {
            _cachedDataDirectory = path;
            return path;
        }

        // Dev/copied: data might be in same folder as exe
        path = Path.Combine(appDir, "data");
        if (Directory.Exists(path))
        {
            _cachedDataDirectory = path;
            return path;
        }

        // Dev mode: Walk up from bin/Debug/net10.0 to find repo root's data folder
        // Similar logic to SchemaService.FindSchemasPath()
        var dir = new DirectoryInfo(appDir);
        while (dir != null)
        {
            var dataDir = Path.Combine(dir.FullName, "data");
            if (Directory.Exists(dataDir))
            {
                _cachedDataDirectory = dataDir;
                return dataDir;
            }

            // Also check if we're in bin/Debug/net10.0 etc (go up 4+ levels to repo root)
            var parentData = Path.Combine(dir.FullName, "..", "..", "..", "..", "data");
            if (Directory.Exists(parentData))
            {
                _cachedDataDirectory = Path.GetFullPath(parentData);
                return _cachedDataDirectory;
            }

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// Gets the full path to a file in the tool's data directory.
    /// Returns null if the file doesn't exist.
    /// </summary>
    public static string? GetDataFile(string fileName)
    {
        var dataDir = GetDataDirectory();
        if (dataDir == null) return null;

        var path = Path.Combine(dataDir, fileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Finds a source file by searching in the base directory and parent directories.
    /// Search order:
    /// 1. TORTools/data (tool-specific data files - highest priority)
    /// 2. Same directory as baseDir
    /// 3. tor_custom_xmls subdirectory
    /// 4. Navigate up to find Modules directory, then check TOR_Core/ModuleData
    /// </summary>
    public string? FindSourceFile(string baseDir, string fileName)
    {
        Console.WriteLine($"[FindSourceFile] Looking for {fileName} from base {baseDir}");

        // FIRST: Check TORTools/data for tool-specific data files (highest priority)
        // This allows tool-specific files like tor_attributes.xml to live in TORTools
        var torToolsDataPath = FindTorToolsDataPath(baseDir, fileName);
        if (torToolsDataPath != null)
        {
            Console.WriteLine($"[FindSourceFile] Found in TORTools/data: {torToolsDataPath}");
            return torToolsDataPath;
        }

        // Check same directory
        var path = Path.Combine(baseDir, fileName);
        if (File.Exists(path))
        {
            Console.WriteLine($"[FindSourceFile] Found at: {path}");
            return path;
        }

        // Check tor_custom_xmls subdirectory (common location)
        path = Path.Combine(baseDir, "tor_custom_xmls", fileName);
        if (File.Exists(path))
        {
            Console.WriteLine($"[FindSourceFile] Found at: {path}");
            return path;
        }

        // Navigate up to find Modules directory
        // Structure: Modules/TOR_Armory/ModuleData/tor_armors.xml
        // We need: Modules/TOR_Core/ModuleData/tor_custom_xmls/tor_extendeditemproperties.xml
        var current = baseDir;
        for (int i = 0; i < 5; i++) // Safety limit
        {
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent)) break;

            var parentName = Path.GetFileName(parent);
            if (parentName?.Equals("Modules", StringComparison.OrdinalIgnoreCase) == true)
            {
                // Check TOR_Core/ModuleData/tor_custom_xmls
                var torCorePath = Path.Combine(parent, "TOR_Core", "ModuleData", "tor_custom_xmls", fileName);
                Console.WriteLine($"[FindSourceFile] Checking TOR_Core path: {torCorePath}");
                if (File.Exists(torCorePath))
                {
                    Console.WriteLine($"[FindSourceFile] Found at: {torCorePath}");
                    return torCorePath;
                }

                // Also check TOR_Core/ModuleData directly
                torCorePath = Path.Combine(parent, "TOR_Core", "ModuleData", fileName);
                if (File.Exists(torCorePath))
                {
                    Console.WriteLine($"[FindSourceFile] Found at: {torCorePath}");
                    return torCorePath;
                }
                break;
            }
            current = parent;
        }

        Console.WriteLine($"[FindSourceFile] Not found: {fileName}");
        return null;
    }

    /// <summary>
    /// Helper to find data files relative to the app's location.
    /// Works regardless of folder name - just uses ../data/ from the exe.
    /// </summary>
    public string? FindTorToolsDataPath(string baseDir, string fileName)
    {
        return GetDataFile(fileName);
    }
}
