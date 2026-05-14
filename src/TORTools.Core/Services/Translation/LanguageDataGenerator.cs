using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TORTools.Core.Models.Translation;

namespace TORTools.Core.Services.Translation;

/// <summary>
/// Generates language_data.xml and translation stub files for new languages.
/// </summary>
public partial class LanguageDataGenerator
{
    private readonly string _modulesBasePath;
    private readonly TranslationService _translationService;

    /// <summary>
    /// Pattern to detect localizable attributes.
    /// </summary>
    [GeneratedRegex(@"\{=([^}]+)\}", RegexOptions.Singleline)]
    private static partial Regex LocalizationTagPattern();

    /// <summary>
    /// Modules to scan for translatable files.
    /// </summary>
    private static readonly string[] ModulesToScan = { "TOR_Core", "TOR_Armory", "TOR_Environment" };

    public LanguageDataGenerator(string modulesBasePath)
    {
        _modulesBasePath = modulesBasePath;
        _translationService = new TranslationService(modulesBasePath);
    }

    /// <summary>
    /// Generates a complete language folder structure for a new language.
    /// </summary>
    public LanguageConfig GenerateLanguageFolder(string targetFolderPath, string languageCode, string languageName)
    {
        // Ensure target folder exists
        Directory.CreateDirectory(targetFolderPath);

        // Discover all translatable files across modules
        var translatableFiles = DiscoverTranslatableFiles();

        // Generate language_data.xml
        var languageDataPath = Path.Combine(targetFolderPath, "language_data.xml");
        GenerateLanguageDataXml(languageDataPath, languageCode, languageName, translatableFiles);

        // Generate stub translation files
        foreach (var file in translatableFiles)
        {
            var stubPath = Path.Combine(targetFolderPath, file.RelativePath);
            GenerateTranslationStub(file.SourcePath, stubPath, languageName);
        }

        return new LanguageConfig
        {
            LanguageCode = languageCode,
            LanguageName = languageName,
            FolderPath = targetFolderPath,
            HasLanguageData = true,
            TranslationFiles = translatableFiles.Select(f => $"{languageCode}/{f.RelativePath}").ToList(),
            LastSyncedAt = DateTime.Now
        };
    }

    /// <summary>
    /// Discovers all XML files that contain localizable strings.
    /// </summary>
    public List<TranslatableFile> DiscoverTranslatableFiles()
    {
        var results = new List<TranslatableFile>();

        foreach (var moduleName in ModulesToScan)
        {
            var moduleDataPath = Path.Combine(_modulesBasePath, moduleName, "ModuleData");
            if (!Directory.Exists(moduleDataPath))
                continue;

            // Scan all XML files in ModuleData and subdirectories
            var xmlFiles = Directory.GetFiles(moduleDataPath, "*.xml", SearchOption.AllDirectories);

            foreach (var xmlFile in xmlFiles)
            {
                // Skip files in Languages folder
                if (xmlFile.Contains(Path.DirectorySeparatorChar + "Languages" + Path.DirectorySeparatorChar))
                    continue;

                // Check if file contains localizable strings
                if (ContainsLocalizableStrings(xmlFile))
                {
                    var relativePath = GetRelativeModulePath(xmlFile, moduleName);
                    results.Add(new TranslatableFile
                    {
                        SourcePath = xmlFile,
                        ModuleName = moduleName,
                        RelativePath = relativePath,
                        FileName = Path.GetFileName(xmlFile)
                    });
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Checks if an XML file contains localizable strings.
    /// </summary>
    private bool ContainsLocalizableStrings(string xmlFilePath)
    {
        try
        {
            var content = File.ReadAllText(xmlFilePath);
            return LocalizationTagPattern().IsMatch(content);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Generates language_data.xml file.
    /// </summary>
    private void GenerateLanguageDataXml(
        string outputPath,
        string languageCode,
        string languageName,
        List<TranslatableFile> files)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine($"<LanguageData id=\"{EscapeXml(languageName)}\">");

        foreach (var file in files.OrderBy(f => f.RelativePath))
        {
            var xmlPath = $"{languageCode}/{file.RelativePath}";
            sb.AppendLine($"  <LanguageFile xml_path=\"{xmlPath}\" />");
        }

        sb.AppendLine("</LanguageData>");

        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// Generates a translation stub file with TODO entries for all localizable strings.
    /// </summary>
    private void GenerateTranslationStub(string sourcePath, string outputPath, string languageName)
    {
        // Ensure directory exists
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Extract all localization IDs from source
        var entries = _translationService.ExtractLocalizationIds(sourcePath);

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine();
        sb.AppendLine("<base xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" type=\"string\">");
        sb.AppendLine("  <tags>");
        sb.AppendLine($"    <tag language=\"{EscapeXml(languageName)}\" />");
        sb.AppendLine("  </tags>");
        sb.AppendLine("  <strings>");

        foreach (var (locId, englishText) in entries.OrderBy(e => e.Key))
        {
            var todoText = $"TODO [{EscapeXml(englishText)}]";
            sb.AppendLine($"    <string id=\"{EscapeXml(locId)}\" text=\"{todoText}\"/>");
        }

        sb.AppendLine("  </strings>");
        sb.AppendLine("</base>");

        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// Gets the relative path within a module (e.g., "TOR_Core/ModuleData/tor_strings.xml").
    /// </summary>
    private string GetRelativeModulePath(string fullPath, string moduleName)
    {
        var modulePath = Path.Combine(_modulesBasePath, moduleName);
        var relativePath = Path.GetRelativePath(modulePath, fullPath);
        return $"{moduleName}/{relativePath.Replace('\\', '/')}";
    }

    private static string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}

/// <summary>
/// Represents a file that can be translated.
/// </summary>
public class TranslatableFile
{
    public string SourcePath { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
}
