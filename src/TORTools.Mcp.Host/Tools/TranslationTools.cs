using System.ComponentModel;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using TORTools.Core.Models.Translation;
using TORTools.Core.Services.Translation;
using TORTools.Core.Workspace;

using static TORTools.Core.DocumentStore.StandaloneDocumentStore;

namespace TORTools.Mcp.Host.Tools;

/// <summary>
/// MCP tools for working with translations/localizations.
/// </summary>
[McpServerToolType]
public class TranslationTools
{
    private readonly TranslationService _translationService;
    private readonly string _modulesBasePath;

    public TranslationTools(IWorkspaceService workspaceService)
    {
        var config = workspaceService.LoadConfig();
        _modulesBasePath = Path.Combine(config.BannerlordPath ?? "", "Modules");
        _translationService = new TranslationService(_modulesBasePath);
    }

    [McpServerTool, Description("List all available language folders in the Languages directory.")]
    public TranslationLanguagesResult translation_list_languages()
    {
        Log("Tool", "translation_list_languages()");

        var languagesPath = Path.Combine(_modulesBasePath, "TOR_Core", "ModuleData", "Languages");
        if (!Directory.Exists(languagesPath))
        {
            return new TranslationLanguagesResult
            {
                Success = false,
                Error = $"Languages directory not found: {languagesPath}"
            };
        }

        var languages = new List<LanguageDto>();
        foreach (var dir in Directory.GetDirectories(languagesPath))
        {
            var config = _translationService.LoadLanguageConfig(dir);
            if (config != null)
            {
                languages.Add(new LanguageDto
                {
                    Code = config.LanguageCode,
                    Name = config.LanguageName,
                    FileCount = config.TranslationFiles.Count,
                    HasLanguageData = config.HasLanguageData
                });
            }
        }

        return new TranslationLanguagesResult
        {
            Success = true,
            Languages = languages
        };
    }

    [McpServerTool, Description("List all translation files for a specific language.")]
    public TranslationFilesResult translation_list_files(
        [Description("Language code (e.g., 'DE', 'FR')")]
        string language_code)
    {
        Log("Tool", $"translation_list_files(language_code={language_code})");

        var languagePath = Path.Combine(_modulesBasePath, "TOR_Core", "ModuleData", "Languages", language_code);
        var config = _translationService.LoadLanguageConfig(languagePath);

        if (config == null)
        {
            return new TranslationFilesResult
            {
                Success = false,
                Error = $"Language '{language_code}' not found."
            };
        }

        var files = config.TranslationFiles
            .Select(f => new TranslationFileDto
            {
                RelativePath = f,
                FileName = Path.GetFileName(f)
            })
            .ToList();

        return new TranslationFilesResult
        {
            Success = true,
            LanguageCode = config.LanguageCode,
            LanguageName = config.LanguageName,
            Files = files
        };
    }

    [McpServerTool, Description("Get translation entries for a specific file, showing English source and translation status.")]
    public TranslationSheetResult translation_get_sheet(
        [Description("Language code (e.g., 'DE', 'FR')")]
        string language_code,
        [Description("Relative path from language_data.xml (e.g., 'DE/TOR_Core/ModuleData/tor_strings.xml')")]
        string relative_path,
        [Description("Filter by status: 'all', 'missing', 'todo', 'translated', 'orphaned'")]
        string status_filter = "all",
        [Description("Maximum entries to return")]
        int limit = 100)
    {
        Log("Tool", $"translation_get_sheet(language_code={language_code}, relative_path={relative_path}, status_filter={status_filter}, limit={limit})");

        var languagePath = Path.Combine(_modulesBasePath, "TOR_Core", "ModuleData", "Languages", language_code);
        var config = _translationService.LoadLanguageConfig(languagePath);

        if (config == null)
        {
            return new TranslationSheetResult
            {
                Success = false,
                Error = $"Language '{language_code}' not found."
            };
        }

        // Resolve paths
        var (sourcePath, expectedPath) = _translationService.ResolveEnglishSourcePathWithExpected(relative_path);
        if (sourcePath == null)
        {
            return new TranslationSheetResult
            {
                Success = false,
                Error = $"English source file not found. Expected: {expectedPath}"
            };
        }

        // Build translation file path
        var parts = relative_path.Split('/');
        var translationPath = Path.Combine(config.FolderPath, string.Join(Path.DirectorySeparatorChar.ToString(), parts.Skip(1)));

        // Create translation sheet
        var sheet = _translationService.CreateTranslationSheet(sourcePath, translationPath, language_code, relative_path);

        // Filter by status
        var entries = sheet.Entries.AsEnumerable();
        if (status_filter != "all")
        {
            entries = status_filter.ToLowerInvariant() switch
            {
                "missing" => entries.Where(e => e.Status == TranslationStatus.Missing),
                "todo" => entries.Where(e => e.Status == TranslationStatus.Todo),
                "translated" => entries.Where(e => e.Status == TranslationStatus.Translated),
                "orphaned" => entries.Where(e => e.Status == TranslationStatus.Orphaned),
                _ => entries
            };
        }

        var resultEntries = entries.Take(limit).Select(e => new TranslationEntryDto
        {
            LocalizationId = e.LocalizationId,
            EnglishText = e.EnglishText,
            TranslatedText = e.TranslatedText,
            Status = e.Status.ToString().ToLowerInvariant()
        }).ToList();

        var stats = new TranslationStatsDto
        {
            Total = sheet.Entries.Count,
            Translated = sheet.Entries.Count(e => e.Status == TranslationStatus.Translated),
            Todo = sheet.Entries.Count(e => e.Status == TranslationStatus.Todo),
            Missing = sheet.Entries.Count(e => e.Status == TranslationStatus.Missing),
            Orphaned = sheet.Entries.Count(e => e.Status == TranslationStatus.Orphaned)
        };

        return new TranslationSheetResult
        {
            Success = true,
            FileName = sheet.FileName,
            Stats = stats,
            Entries = resultEntries
        };
    }

    [McpServerTool, Description("Validate a language configuration, checking for invalid entries (source files that don't exist) and missing files (source files not in config).")]
    public TranslationValidateResult translation_validate(
        [Description("Language code (e.g., 'DE', 'FR')")]
        string language_code)
    {
        Log("Tool", $"translation_validate(language_code={language_code})");

        var languagePath = Path.Combine(_modulesBasePath, "TOR_Core", "ModuleData", "Languages", language_code);
        var config = _translationService.LoadLanguageConfig(languagePath);

        if (config == null)
        {
            return new TranslationValidateResult
            {
                Success = false,
                Error = $"Language '{language_code}' not found."
            };
        }

        var validationResult = _translationService.ValidateLanguageConfig(config);
        var missingFiles = _translationService.FindMissingTranslationFiles(config);

        return new TranslationValidateResult
        {
            Success = true,
            LanguageCode = config.LanguageCode,
            LanguageName = config.LanguageName,
            ValidCount = validationResult.ValidEntries.Count,
            InvalidEntries = validationResult.InvalidEntries.Select(e => e.RelativePath).ToList(),
            MissingFiles = missingFiles,
            NeedsRepair = validationResult.HasInvalidEntries || missingFiles.Count > 0
        };
    }

    [McpServerTool, Description("Repair a language configuration by removing invalid entries and adding missing files.")]
    public TranslationRepairResult translation_repair(
        [Description("Language code (e.g., 'DE', 'FR')")]
        string language_code,
        [Description("Remove invalid entries from language_data.xml")]
        bool remove_invalid = true,
        [Description("Add missing files to language_data.xml and create templates")]
        bool add_missing = true)
    {
        Log("Tool", $"translation_repair(language_code={language_code}, remove_invalid={remove_invalid}, add_missing={add_missing})");

        var languagePath = Path.Combine(_modulesBasePath, "TOR_Core", "ModuleData", "Languages", language_code);
        var config = _translationService.LoadLanguageConfig(languagePath);

        if (config == null)
        {
            return new TranslationRepairResult
            {
                Success = false,
                Error = $"Language '{language_code}' not found."
            };
        }

        var validationResult = _translationService.ValidateLanguageConfig(config);
        var missingFiles = _translationService.FindMissingTranslationFiles(config);

        int removedCount = 0;
        int addedCount = 0;

        if (remove_invalid && validationResult.HasInvalidEntries)
        {
            var entriesToRemove = validationResult.InvalidEntries.Select(e => e.RelativePath).ToList();
            var removeSuccess = _translationService.RepairLanguageData(config, entriesToRemove);
            if (removeSuccess)
            {
                removedCount = entriesToRemove.Count;
            }
        }

        if (add_missing && missingFiles.Count > 0)
        {
            var addSuccess = _translationService.AddMissingTranslationFiles(config, missingFiles);
            if (addSuccess)
            {
                addedCount = missingFiles.Count;
            }
        }

        return new TranslationRepairResult
        {
            Success = true,
            RemovedCount = removedCount,
            AddedCount = addedCount,
            Message = $"Removed {removedCount} invalid entries, added {addedCount} missing files."
        };
    }

    [McpServerTool, Description("Search for translation entries containing specific text across all files in a language.")]
    public TranslationSearchResult translation_search(
        [Description("Language code (e.g., 'DE', 'FR')")]
        string language_code,
        [Description("Search query (searches in English text and translated text)")]
        string query,
        [Description("Maximum results to return")]
        int limit = 50)
    {
        Log("Tool", $"translation_search(language_code={language_code}, query={query}, limit={limit})");

        var languagePath = Path.Combine(_modulesBasePath, "TOR_Core", "ModuleData", "Languages", language_code);
        var config = _translationService.LoadLanguageConfig(languagePath);

        if (config == null)
        {
            return new TranslationSearchResult
            {
                Success = false,
                Error = $"Language '{language_code}' not found."
            };
        }

        var queryLower = query.ToLowerInvariant();
        var results = new List<TranslationSearchEntryDto>();

        foreach (var relativePath in config.TranslationFiles)
        {
            if (results.Count >= limit) break;

            var (sourcePath, _) = _translationService.ResolveEnglishSourcePathWithExpected(relativePath);
            if (sourcePath == null) continue;

            var parts = relativePath.Split('/');
            var translationPath = Path.Combine(config.FolderPath, string.Join(Path.DirectorySeparatorChar.ToString(), parts.Skip(1)));

            var sheet = _translationService.CreateTranslationSheet(sourcePath, translationPath, language_code, relativePath);

            foreach (var entry in sheet.Entries)
            {
                if (results.Count >= limit) break;

                var englishLower = entry.EnglishText?.ToLowerInvariant() ?? "";
                var translatedLower = entry.TranslatedText?.ToLowerInvariant() ?? "";

                if (englishLower.Contains(queryLower) || translatedLower.Contains(queryLower))
                {
                    results.Add(new TranslationSearchEntryDto
                    {
                        LocalizationId = entry.LocalizationId,
                        EnglishText = entry.EnglishText,
                        TranslatedText = entry.TranslatedText,
                        Status = entry.Status.ToString().ToLowerInvariant(),
                        SourceFile = relativePath
                    });
                }
            }
        }

        return new TranslationSearchResult
        {
            Success = true,
            Query = query,
            MatchCount = results.Count,
            Entries = results
        };
    }
}

// DTOs

public class LanguageDto
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("file_count")]
    public int FileCount { get; set; }

    [JsonPropertyName("has_language_data")]
    public bool HasLanguageData { get; set; }
}

public class TranslationLanguagesResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    [JsonPropertyName("languages")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LanguageDto>? Languages { get; set; }
}

public class TranslationFileDto
{
    [JsonPropertyName("relative_path")]
    public string RelativePath { get; set; } = "";

    [JsonPropertyName("file_name")]
    public string FileName { get; set; } = "";
}

public class TranslationFilesResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    [JsonPropertyName("language_code")]
    public string LanguageCode { get; set; } = "";

    [JsonPropertyName("language_name")]
    public string LanguageName { get; set; } = "";

    [JsonPropertyName("files")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TranslationFileDto>? Files { get; set; }
}

public class TranslationEntryDto
{
    [JsonPropertyName("localization_id")]
    public string LocalizationId { get; set; } = "";

    [JsonPropertyName("english_text")]
    public string EnglishText { get; set; } = "";

    [JsonPropertyName("translated_text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TranslatedText { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";
}

public class TranslationStatsDto
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("translated")]
    public int Translated { get; set; }

    [JsonPropertyName("todo")]
    public int Todo { get; set; }

    [JsonPropertyName("missing")]
    public int Missing { get; set; }

    [JsonPropertyName("orphaned")]
    public int Orphaned { get; set; }
}

public class TranslationSheetResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    [JsonPropertyName("file_name")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("stats")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TranslationStatsDto? Stats { get; set; }

    [JsonPropertyName("entries")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TranslationEntryDto>? Entries { get; set; }
}

public class TranslationValidateResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    [JsonPropertyName("language_code")]
    public string LanguageCode { get; set; } = "";

    [JsonPropertyName("language_name")]
    public string LanguageName { get; set; } = "";

    [JsonPropertyName("valid_count")]
    public int ValidCount { get; set; }

    [JsonPropertyName("invalid_entries")]
    public List<string> InvalidEntries { get; set; } = new();

    [JsonPropertyName("missing_files")]
    public List<string> MissingFiles { get; set; } = new();

    [JsonPropertyName("needs_repair")]
    public bool NeedsRepair { get; set; }
}

public class TranslationRepairResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    [JsonPropertyName("removed_count")]
    public int RemovedCount { get; set; }

    [JsonPropertyName("added_count")]
    public int AddedCount { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

public class TranslationSearchEntryDto
{
    [JsonPropertyName("localization_id")]
    public string LocalizationId { get; set; } = "";

    [JsonPropertyName("english_text")]
    public string EnglishText { get; set; } = "";

    [JsonPropertyName("translated_text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TranslatedText { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("source_file")]
    public string SourceFile { get; set; } = "";
}

public class TranslationSearchResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    [JsonPropertyName("match_count")]
    public int MatchCount { get; set; }

    [JsonPropertyName("entries")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TranslationSearchEntryDto>? Entries { get; set; }
}
