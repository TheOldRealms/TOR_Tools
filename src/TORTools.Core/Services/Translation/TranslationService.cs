using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TORTools.Core.Models.Translation;

namespace TORTools.Core.Services.Translation;

/// <summary>
/// Service for managing translations - loading, merging, and exporting translation files.
/// </summary>
public partial class TranslationService
{
    /// <summary>
    /// Pattern to extract localization IDs from attribute values.
    /// Matches: {=localization_id}text
    /// </summary>
    [GeneratedRegex(@"\{=([^}]+)\}(.*)$", RegexOptions.Singleline)]
    private static partial Regex LocalizationPattern();

    /// <summary>
    /// Attributes that should be scanned for localization tags.
    /// </summary>
    private static readonly HashSet<string> LocalizableAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text", "name", "Name", "title", "short_name", "ruler_title",
        "RegimentName", "RegimentHQName", "MenuHeaderText", "TooltipDescription",
        "encyclopedia_text", "Description", "description"
    };

    /// <summary>
    /// Elements that should never be scanned for localization.
    /// </summary>
    private static readonly HashSet<string> ExcludedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mesh", "Material", "face", "EquipmentSet", "BodyProperties",
        "face_key_template", "Component", "Flags", "hair_tag", "beard_tag",
        "hair_tags", "beard_tags", "template", "upgrade_targets", "Equipments", "skills"
    };

    private readonly string _modulesBasePath;

    public TranslationService(string modulesBasePath)
    {
        _modulesBasePath = modulesBasePath;
    }

    /// <summary>
    /// Loads a language configuration from a folder path.
    /// </summary>
    public LanguageConfig? LoadLanguageConfig(string languageFolderPath)
    {
        // Trim trailing slashes to ensure Path.GetFileName works correctly
        languageFolderPath = languageFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!Directory.Exists(languageFolderPath))
            return null;

        var languageDataPath = Path.Combine(languageFolderPath, "language_data.xml");
        if (!File.Exists(languageDataPath))
        {
            // Try to infer from folder name
            var folderName = Path.GetFileName(languageFolderPath);
            return new LanguageConfig
            {
                LanguageCode = folderName,
                LanguageName = GetLanguageName(folderName),
                FolderPath = languageFolderPath,
                HasLanguageData = false,
                TranslationFiles = new List<string>()
            };
        }

        try
        {
            var doc = XDocument.Load(languageDataPath);
            var root = doc.Root;
            if (root == null)
                return null;

            var languageName = root.Attribute("id")?.Value ?? "Unknown";
            var languageCode = Path.GetFileName(languageFolderPath);

            var files = root.Descendants("LanguageFile")
                .Select(e => e.Attribute("xml_path")?.Value)
                .Where(p => !string.IsNullOrEmpty(p))
                .Cast<string>()
                .ToList();

            return new LanguageConfig
            {
                LanguageCode = languageCode,
                LanguageName = languageName,
                FolderPath = languageFolderPath,
                HasLanguageData = true,
                TranslationFiles = files,
                LastSyncedAt = DateTime.Now
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts all localization IDs and their English text from a source XML file.
    /// </summary>
    public Dictionary<string, string> ExtractLocalizationIds(string sourceXmlPath)
    {
        var results = new Dictionary<string, string>();

        if (!File.Exists(sourceXmlPath))
            return results;

        try
        {
            var doc = XDocument.Load(sourceXmlPath);
            ExtractFromElement(doc.Root, results);
        }
        catch
        {
            // Failed to parse - return empty
        }

        return results;
    }

    private void ExtractFromElement(XElement? element, Dictionary<string, string> results)
    {
        if (element == null)
            return;

        // Skip excluded elements
        if (ExcludedElements.Contains(element.Name.LocalName))
            return;

        // Check attributes for localization patterns
        foreach (var attr in element.Attributes())
        {
            var value = attr.Value;
            if (string.IsNullOrEmpty(value))
                continue;

            var match = LocalizationPattern().Match(value);
            if (match.Success)
            {
                var locId = match.Groups[1].Value;
                var text = match.Groups[2].Value.Trim();

                // First occurrence wins
                if (!results.ContainsKey(locId))
                {
                    results[locId] = text;
                }
            }
        }

        // Recurse into child elements
        foreach (var child in element.Elements())
        {
            ExtractFromElement(child, results);
        }
    }

    /// <summary>
    /// Loads translations from a translation XML file.
    /// Returns a dictionary of localization ID -> translated text.
    /// </summary>
    public Dictionary<string, string> LoadTranslationFile(string translationFilePath)
    {
        var results = new Dictionary<string, string>();

        if (!File.Exists(translationFilePath))
            return results;

        try
        {
            var doc = XDocument.Load(translationFilePath);
            var strings = doc.Descendants("string");

            foreach (var str in strings)
            {
                var id = str.Attribute("id")?.Value;
                var text = str.Attribute("text")?.Value;

                if (!string.IsNullOrEmpty(id) && text != null)
                {
                    results[id] = text;
                }
            }
        }
        catch
        {
            // Failed to parse - return empty
        }

        return results;
    }

    /// <summary>
    /// Creates a merged translation sheet for a single file.
    /// </summary>
    public TranslationSheet CreateTranslationSheet(
        string sourceXmlPath,
        string? translationFilePath,
        string languageCode,
        string relativePath)
    {
        var sheet = new TranslationSheet
        {
            FileName = Path.GetFileName(sourceXmlPath),
            RelativePath = relativePath,
            LanguageCode = languageCode
        };

        // Extract English source entries
        var englishEntries = ExtractLocalizationIds(sourceXmlPath);

        // Load existing translations
        var translations = translationFilePath != null
            ? LoadTranslationFile(translationFilePath)
            : new Dictionary<string, string>();

        // Track which translation IDs we've seen
        var seenTranslationIds = new HashSet<string>();

        // Create entries for all English source entries
        foreach (var (locId, englishText) in englishEntries)
        {
            var entry = new TranslationEntry
            {
                LocalizationId = locId,
                EnglishText = englishText,
                SourceFile = sheet.FileName,
                RelativePath = relativePath
            };

            if (translations.TryGetValue(locId, out var translatedText))
            {
                seenTranslationIds.Add(locId);
                entry.TranslatedText = translatedText;

                // Check if it's a TODO entry (format: "TODO [english text]")
                // Must match exact format to avoid false positives (e.g., Spanish "todo" meaning "all")
                if (translatedText.StartsWith("TODO [", StringComparison.Ordinal))
                {
                    entry.Status = TranslationStatus.Todo;
                }
                else
                {
                    entry.Status = TranslationStatus.Translated;
                }
            }
            else
            {
                // Missing translation
                entry.Status = TranslationStatus.Missing;
                entry.TranslatedText = $"TODO [{englishText}]";
            }

            sheet.Entries.Add(entry);
        }

        // Add orphaned entries (in translation but not in English source)
        foreach (var (locId, translatedText) in translations)
        {
            if (!seenTranslationIds.Contains(locId) && !englishEntries.ContainsKey(locId))
            {
                sheet.Entries.Add(new TranslationEntry
                {
                    LocalizationId = locId,
                    EnglishText = "???",
                    TranslatedText = translatedText,
                    Status = TranslationStatus.Orphaned,
                    SourceFile = sheet.FileName,
                    RelativePath = relativePath
                });
            }
        }

        return sheet;
    }

    /// <summary>
    /// Exports a translation sheet to an XML file.
    /// </summary>
    public void ExportTranslationSheet(TranslationSheet sheet, string outputPath, string languageName)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine();
        sb.AppendLine("<base xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" type=\"string\">");
        sb.AppendLine("  <tags>");
        sb.AppendLine($"    <tag language=\"{EscapeXml(languageName)}\" />");
        sb.AppendLine("  </tags>");
        sb.AppendLine("  <strings>");

        // Sort entries by localization ID for consistency
        var sortedEntries = sheet.Entries
            .Where(e => e.Status != TranslationStatus.Orphaned || !string.IsNullOrEmpty(e.TranslatedText))
            .OrderBy(e => e.LocalizationId);

        foreach (var entry in sortedEntries)
        {
            var text = entry.TranslatedText ?? entry.EnglishText;
            sb.AppendLine($"    <string id=\"{EscapeXml(entry.LocalizationId)}\" text=\"{EscapeXml(text)}\"/>");
        }

        sb.AppendLine("  </strings>");
        sb.AppendLine("</base>");

        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// Resolves the English source path for a translation file path.
    /// </summary>
    public string? ResolveEnglishSourcePath(string translationRelativePath)
    {
        var (sourcePath, _) = ResolveEnglishSourcePathWithExpected(translationRelativePath);
        return sourcePath;
    }

    /// <summary>
    /// Resolves the English source path for a translation file path.
    /// Returns both the resolved path (if exists) and the expected path (for error messages).
    /// </summary>
    public (string? SourcePath, string ExpectedPath) ResolveEnglishSourcePathWithExpected(string translationRelativePath)
    {
        // Translation path format: DE/TOR_Core/ModuleData/tor_strings.xml
        // English source: TOR_Core/ModuleData/tor_strings.xml
        var parts = translationRelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 4)
        {
            return (null, $"Invalid path format: {translationRelativePath}");
        }

        // Skip language code, reconstruct path (following PowerShell tool pattern)
        var moduleName = parts[1]; // TOR_Core, TOR_Armory, etc.
        var relativeModulePath = string.Join("/", parts.Skip(3)); // path after ModuleData/

        // Construct expected path: Modules/MODULE_NAME/ModuleData/relative_path
        var expectedPath = Path.Combine(_modulesBasePath, moduleName, "ModuleData", relativeModulePath);
        expectedPath = expectedPath.Replace('/', Path.DirectorySeparatorChar);

        if (File.Exists(expectedPath))
        {
            return (expectedPath, expectedPath);
        }

        return (null, expectedPath);
    }

    private static string GetLanguageName(string code) => code.ToUpperInvariant() switch
    {
        "DE" => "Deutsch",
        "FR" => "Français",
        "SP" => "Español",
        "IT" => "Italiano",
        "RU" => "Русский",
        "PT" => "Português",
        "PL" => "Polski",
        "TR" => "Türkçe",
        "CN" => "简体中文",
        "JP" => "日本語",
        "KR" => "한국어",
        "EN" => "English",
        _ => code
    };

    private static string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    /// <summary>
    /// Validates a language configuration against actual source files.
    /// Returns a list of invalid entries (files that don't have a matching source).
    /// </summary>
    public LanguageValidationResult ValidateLanguageConfig(LanguageConfig config)
    {
        var result = new LanguageValidationResult
        {
            LanguageCode = config.LanguageCode,
            LanguageName = config.LanguageName
        };

        foreach (var translationFile in config.TranslationFiles)
        {
            // Translation file format: DE/TOR_Core/ModuleData/file.xml
            var (sourcePath, expectedPath) = ResolveEnglishSourcePathWithExpected(translationFile);

            if (sourcePath == null)
            {
                result.InvalidEntries.Add(new InvalidTranslationEntry
                {
                    RelativePath = translationFile,
                    ExpectedSourcePath = expectedPath,
                    Reason = "Source file not found"
                });
            }
            else
            {
                result.ValidEntries.Add(translationFile);
            }
        }

        return result;
    }

    /// <summary>
    /// Repairs a language_data.xml by removing invalid entries.
    /// </summary>
    public bool RepairLanguageData(LanguageConfig config, List<string> entriesToRemove)
    {
        var languageDataPath = Path.Combine(config.FolderPath, "language_data.xml");
        if (!File.Exists(languageDataPath))
            return false;

        try
        {
            var doc = XDocument.Load(languageDataPath);
            var root = doc.Root;
            if (root == null)
                return false;

            var toRemoveSet = new HashSet<string>(entriesToRemove, StringComparer.OrdinalIgnoreCase);

            // Find and remove invalid LanguageFile elements
            var elementsToRemove = root.Descendants("LanguageFile")
                .Where(e =>
                {
                    var xmlPath = e.Attribute("xml_path")?.Value;
                    return xmlPath != null && toRemoveSet.Contains(xmlPath);
                })
                .ToList();

            foreach (var element in elementsToRemove)
            {
                element.Remove();
            }

            // Save the repaired file
            doc.Save(languageDataPath);

            // Update the config
            config.TranslationFiles = config.TranslationFiles
                .Where(f => !toRemoveSet.Contains(f))
                .ToList();

            Console.WriteLine($"[TranslationService] Repaired language_data.xml - removed {elementsToRemove.Count} invalid entries");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TranslationService] Failed to repair language_data.xml: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Adds missing translation files to language_data.xml and creates empty template files.
    /// </summary>
    public bool AddMissingTranslationFiles(LanguageConfig config, List<string> filesToAdd)
    {
        var languageDataPath = Path.Combine(config.FolderPath, "language_data.xml");
        if (!File.Exists(languageDataPath))
            return false;

        try
        {
            var doc = XDocument.Load(languageDataPath);
            var root = doc.Root;
            if (root == null)
                return false;

            // Find the LanguageData element (or create one if needed)
            var languageDataElement = root.Element("LanguageData") ?? root;

            int addedCount = 0;
            int templatesCreated = 0;

            foreach (var filePath in filesToAdd)
            {
                // Add to language_data.xml
                var newElement = new XElement("LanguageFile",
                    new XAttribute("xml_path", filePath));
                languageDataElement.Add(newElement);
                addedCount++;

                // Create empty template file
                // filePath format: DE/TOR_Core/ModuleData/file.xml
                var parts = filePath.Split('/');
                if (parts.Length >= 2)
                {
                    // Build the actual file path: LanguageFolder/MODULE/ModuleData/...
                    var templatePath = Path.Combine(config.FolderPath, string.Join(Path.DirectorySeparatorChar.ToString(), parts.Skip(1)));

                    var templateDir = Path.GetDirectoryName(templatePath);
                    if (!string.IsNullOrEmpty(templateDir) && !Directory.Exists(templateDir))
                    {
                        Directory.CreateDirectory(templateDir);
                    }

                    if (!File.Exists(templatePath))
                    {
                        var template = $"""
                            <?xml version="1.0" encoding="utf-8"?>
                            <base xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema" type="string">
                              <tags>
                                <tag language="{config.LanguageName}" />
                              </tags>
                              <strings>
                                <!-- Translation entries will be populated when you open and export this file -->
                              </strings>
                            </base>
                            """;
                        File.WriteAllText(templatePath, template, Encoding.UTF8);
                        templatesCreated++;
                        Console.WriteLine($"[TranslationService] Created template: {templatePath}");
                    }
                }

                // Update the config's file list
                config.TranslationFiles.Add(filePath);
            }

            // Save the updated language_data.xml
            doc.Save(languageDataPath);

            Console.WriteLine($"[TranslationService] Added {addedCount} entries to language_data.xml, created {templatesCreated} template files");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TranslationService] Failed to add missing files: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Finds missing translation files by comparing against the master template.
    /// </summary>
    public List<string> FindMissingTranslationFiles(LanguageConfig config)
    {
        var missing = new List<string>();
        var existingFiles = new HashSet<string>(config.TranslationFiles, StringComparer.OrdinalIgnoreCase);

        // Load required files from template
        var templateFiles = LoadTemplateFiles(config.LanguageCode);

        foreach (var templatePath in templateFiles)
        {
            if (!existingFiles.Contains(templatePath))
            {
                missing.Add(templatePath);
            }
        }

        return missing;
    }

    /// <summary>
    /// Loads the list of required translation files from the template.
    /// </summary>
    private List<string> LoadTemplateFiles(string languageCode)
    {
        var results = new List<string>();

        // Look for template in TORTools/templates/
        var templatePath = Path.Combine(_modulesBasePath, "TORTools", "templates", "language_data_template.xml");

        if (!File.Exists(templatePath))
        {
            Console.WriteLine($"[TranslationService] Template not found: {templatePath}");
            return results;
        }

        try
        {
            var doc = XDocument.Load(templatePath);
            var files = doc.Descendants("LanguageFile")
                .Select(e => e.Attribute("xml_path")?.Value)
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p!.Replace("{LANG}", languageCode))
                .ToList();

            return files;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TranslationService] Failed to load template: {ex.Message}");
            return results;
        }
    }
}

/// <summary>
/// Result of validating a language configuration.
/// </summary>
public class LanguageValidationResult
{
    public string LanguageCode { get; set; } = "";
    public string LanguageName { get; set; } = "";
    public List<string> ValidEntries { get; } = new();
    public List<InvalidTranslationEntry> InvalidEntries { get; } = new();

    public bool HasInvalidEntries => InvalidEntries.Count > 0;
    public int TotalEntries => ValidEntries.Count + InvalidEntries.Count;
}

/// <summary>
/// An invalid translation entry that references a non-existent source file.
/// </summary>
public class InvalidTranslationEntry
{
    public string RelativePath { get; set; } = "";
    public string ExpectedSourcePath { get; set; } = "";
    public string Reason { get; set; } = "";
}
