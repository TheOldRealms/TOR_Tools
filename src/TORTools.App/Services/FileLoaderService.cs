using System.Xml.Linq;
using TORTools.App.Models;
using TORTools.App.ViewModels;
using TORTools.Core.Models;
using TORTools.Core.Services;

namespace TORTools.App.Services;

/// <summary>
/// Service responsible for loading XML files and populating the FileEditContext.
/// Handles single-file loading, multi-file merging, equipment set variations, and cross-references.
/// </summary>
public class FileLoaderService
{
    private readonly IXmlDocumentService _xmlService;
    private readonly IGitValueService _gitValueService;
    private readonly CrossReferenceService _crossRefService;

    public FileLoaderService(
        IXmlDocumentService xmlService,
        IGitValueService gitValueService,
        CrossReferenceService crossRefService)
    {
        _xmlService = xmlService;
        _gitValueService = gitValueService;
        _crossRefService = crossRefService;
    }

    /// <summary>
    /// Loads a file into the context.
    /// </summary>
    public void LoadFile(FileEditContext context)
    {
        if (string.IsNullOrEmpty(context.FilePath))
            throw new ArgumentException("FilePath must be set in context");

        if (context.Schema == null)
            throw new ArgumentException("Schema must be set in context");

        try
        {
            // Check if this schema defines multiple source files to merge
            if (context.Schema.AdditionalSourceFiles != null && context.Schema.AdditionalSourceFiles.Count > 0)
            {
                LoadMergedFiles(context);
            }
            else
            {
                // Standard single-file loading
                context.Document = _xmlService.Load(context.FilePath);
                var entries = _xmlService.GetEntries(context.Document);

                context.XmlEntries.Clear();
                context.XmlEntries.AddRange(entries);

                // Load git committed values for comparison
                context.GitCommittedValues = _gitValueService.LoadGitCommittedValues(context.FilePath);

                // Check if this is an equipment set file with nested variations
                if (context.Schema.HasNestedVariations && !string.IsNullOrEmpty(context.Schema.VariationElement))
                {
                    // Flatten equipment sets - each variation becomes a row
                    LoadEquipmentSetVariations(context, entries);
                }
                else
                {
                    // Normal loading
                    DiscoverColumns(context, entries);
                    CreateRows(context, entries);
                }

                context.HasError = false;
                context.ErrorMessage = "";
            }
        }
        catch (Exception ex)
        {
            context.HasError = true;
            context.ErrorMessage = $"Error loading file: {ex.Message}";
            throw;
        }
    }

    /// <summary>
    /// Loads entries from multiple source files and merges them into a single view.
    /// </summary>
    private void LoadMergedFiles(FileEditContext context)
    {
        if (context.Schema == null) return;

        var allEntries = new List<XmlEntry>();
        var baseDir = Path.GetDirectoryName(context.FilePath);
        if (string.IsNullOrEmpty(baseDir)) return;

        // Load main file
        Console.WriteLine($"[LoadMergedFiles] Loading main file: {context.FilePath}");
        context.Document = _xmlService.Load(context.FilePath);
        var mainEntries = _xmlService.GetEntries(context.Document);
        Console.WriteLine($"[LoadMergedFiles] Loaded {mainEntries.Count} entries from main file");

        // Set source file field on main entries (if specified)
        if (!string.IsNullOrEmpty(context.Schema.SourceFileField))
        {
            foreach (var entry in mainEntries)
            {
                entry.SetAttributeValue(context.Schema.SourceFileField, "false");
            }
        }

        allEntries.AddRange(mainEntries);

        // Load additional source files
        foreach (var additionalFile in context.Schema.AdditionalSourceFiles)
        {
            var additionalFilePath = FindSourceFile(baseDir, additionalFile.FileName);
            if (additionalFilePath == null)
            {
                Console.WriteLine($"[LoadMergedFiles] Additional file not found: {additionalFile.FileName}");
                continue;
            }

            Console.WriteLine($"[LoadMergedFiles] Loading additional file: {additionalFilePath}");
            var additionalDoc = _xmlService.Load(additionalFilePath);
            var additionalEntries = _xmlService.GetEntries(additionalDoc);
            Console.WriteLine($"[LoadMergedFiles] Loaded {additionalEntries.Count} entries from {additionalFile.FileName}");

            // Set source file field on additional entries
            if (!string.IsNullOrEmpty(context.Schema.SourceFileField) && !string.IsNullOrEmpty(additionalFile.SourceValue))
            {
                foreach (var entry in additionalEntries)
                {
                    entry.SetAttributeValue(context.Schema.SourceFileField, additionalFile.SourceValue);
                }
            }

            allEntries.AddRange(additionalEntries);
        }

        Console.WriteLine($"[LoadMergedFiles] Total merged entries: {allEntries.Count}");

        // Merge data from merged data file (e.g., tor_heroes.xml)
        if (context.Schema.MergedDataFile != null)
        {
            MergeDataFromFile(context, allEntries, baseDir);
        }

        context.XmlEntries.Clear();
        context.XmlEntries.AddRange(allEntries);

        // Load git committed values for comparison
        context.GitCommittedValues = _gitValueService.LoadGitCommittedValues(context.FilePath);

        // Normal loading
        DiscoverColumns(context, allEntries);
        CreateRows(context, allEntries);

        context.HasError = false;
        context.ErrorMessage = "";
    }

    /// <summary>
    /// Merges data from a separate data file into the loaded entries.
    /// </summary>
    private void MergeDataFromFile(FileEditContext context, List<XmlEntry> entries, string baseDir)
    {
        if (context.Schema?.MergedDataFile == null) return;

        var mergedConfig = context.Schema.MergedDataFile;
        var mergedFilePath = FindSourceFile(baseDir, mergedConfig.FileName);
        if (mergedFilePath == null)
        {
            Console.WriteLine($"[MergeData] Merged data file not found: {mergedConfig.FileName}");
            return;
        }

        Console.WriteLine($"[MergeData] Loading merged data from: {mergedFilePath}");

        try
        {
            var mergedDoc = XDocument.Load(mergedFilePath);
            var mergedRoot = mergedDoc.Root;
            if (mergedRoot == null) return;

            var entryElementName = mergedConfig.EntryElement ?? "Hero";
            var matchField = mergedConfig.MatchField ?? "id";

            // Build dictionary of merged data
            var mergedData = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var element in mergedRoot.Elements(entryElementName))
            {
                var key = element.Attribute(matchField)?.Value;
                if (!string.IsNullOrEmpty(key))
                {
                    mergedData[key] = element;
                }
            }

            Console.WriteLine($"[MergeData] Loaded {mergedData.Count} entries from {mergedConfig.FileName}");

            // Merge data into entries
            int mergedCount = 0;
            foreach (var entry in entries)
            {
                var entryId = entry.GetAttributeValue(matchField);
                if (string.IsNullOrEmpty(entryId)) continue;

                if (mergedData.TryGetValue(entryId, out var mergedElement))
                {
                    if (mergedConfig.FieldMappings != null)
                    {
                        foreach (var mapping in mergedConfig.FieldMappings)
                        {
                            var targetField = mapping.Key;   // "clan" or "encyclopedia_text"
                            var sourceField = mapping.Value; // "faction" or "text"

                            var sourceValue = mergedElement.Attribute(sourceField)?.Value;
                            if (!string.IsNullOrEmpty(sourceValue))
                            {
                                entry.SetAttributeValue(targetField, sourceValue);
                                if (mergedCount < 3)
                                {
                                    Console.WriteLine($"[MergeData] Set {targetField}='{sourceValue.Substring(0, Math.Min(50, sourceValue.Length))}...' for {entryId}");
                                }
                            }
                        }
                        mergedCount++;
                    }
                }
            }
            Console.WriteLine($"[MergeData] Merged {mergedCount} entries with data");

            Console.WriteLine($"[MergeData] Merged data complete");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MergeData] Error merging data: {ex.Message}");
        }
    }

    /// <summary>
    /// Finds a source file relative to the base directory.
    /// Searches in tor_custom_xmls first, then in TORTools/data, then in base directory.
    /// </summary>
    private string? FindSourceFile(string baseDir, string fileName)
    {
        // Check tor_custom_xmls subdirectory (for lords, heroes, abilities)
        var customPath = Path.Combine(baseDir, "tor_custom_xmls", fileName);
        if (File.Exists(customPath))
        {
            Console.WriteLine($"[FindSourceFile] Found at: {customPath}");
            return customPath;
        }

        // Check TORTools/data directory (for reference files like attributes, skillsets)
        var torToolsDataPath = FindTorToolsDataPath(baseDir);
        if (torToolsDataPath != null)
        {
            var toolsDataFile = Path.Combine(torToolsDataPath, fileName);
            if (File.Exists(toolsDataFile))
            {
                Console.WriteLine($"[FindSourceFile] Found in TORTools/data: {toolsDataFile}");
                return toolsDataFile;
            }
        }

        // Check base directory
        var basePath = Path.Combine(baseDir, fileName);
        if (File.Exists(basePath))
        {
            Console.WriteLine($"[FindSourceFile] Found at: {basePath}");
            return basePath;
        }

        // Check parent directory (for settlements.xml)
        var parentDir = Directory.GetParent(baseDir)?.FullName;
        if (parentDir != null)
        {
            var parentPath = Path.Combine(parentDir, fileName);
            if (File.Exists(parentPath))
            {
                Console.WriteLine($"[FindSourceFile] Found at: {parentPath}");
                return parentPath;
            }
        }

        Console.WriteLine($"[FindSourceFile] File not found: {fileName}");
        return null;
    }

    /// <summary>
    /// Gets the tool's data directory using the centralized FilePathResolver.
    /// </summary>
    private string? FindTorToolsDataPath(string baseDir)
    {
        return TORTools.Core.Services.FilePathResolver.GetDataDirectory();
    }

    /// <summary>
    /// Loads equipment set variations and flattens them into rows.
    /// </summary>
    private void LoadEquipmentSetVariations(FileEditContext context, IReadOnlyList<XmlEntry> rosterEntries)
    {
        if (context.Schema == null) return;

        var variationElementName = context.Schema.VariationElement ?? "EquipmentSet";
        var equipmentItemElementName = context.Schema.EquipmentItemElement ?? "Equipment";

        Console.WriteLine($"[EquipmentSets] Loading {rosterEntries.Count} rosters with nested variations");

        // Set up columns for equipment sets
        context.ColumnNames.Clear();
        context.ColumnNames.Add("id");
        context.ColumnNames.Add("culture");
        context.ColumnNames.Add("_variation");

        if (context.Schema.EquipmentSlots != null)
        {
            foreach (var slot in context.Schema.EquipmentSlots.OrderBy(s => s.Order))
            {
                context.ColumnNames.Add(slot.Slot);
            }
        }

        // Add reverse cross-reference fields (e.g., UsedBy)
        var reverseCrossRefFields = context.Schema.Fields
            .Where(f => f.Value.Type == "reverseCrossReference" && f.Value.Hidden != true)
            .OrderBy(f => f.Value.Order)
            .Select(f => f.Key)
            .ToList();
        foreach (var field in reverseCrossRefFields)
        {
            context.ColumnNames.Add(field);
        }

        context.Rows.Clear();

        int rowNum = 1;
        foreach (var roster in rosterEntries)
        {
            var rosterId = roster.GetAttributeValue("id") ?? "";
            var rosterCulture = roster.GetAttributeValue("culture") ?? "";

            var variations = roster.Children
                .Where(c => c.ElementName == variationElementName)
                .ToList();

            if (variations.Count == 0)
            {
                // No variations - create single row for roster
                var emptyRow = CreateEquipmentRow(context, roster, null, rosterId, rosterCulture, 1, rowNum++);
                context.Rows.Add(emptyRow);
            }
            else
            {
                int variationIndex = 1;
                foreach (var variation in variations)
                {
                    var isCivilian = variation.GetAttributeValue("civilian")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

                    // Skip civilian variations - they will be auto-generated on save
                    if (isCivilian) continue;

                    // Extract equipment from this variation
                    var equipment = new Dictionary<string, string>();
                    foreach (var equipItem in variation.Children.Where(c => c.ElementName == equipmentItemElementName))
                    {
                        var slot = equipItem.GetAttributeValue("slot");
                        var itemId = equipItem.GetAttributeValue("id");
                        if (!string.IsNullOrEmpty(slot) && !string.IsNullOrEmpty(itemId))
                        {
                            equipment[slot] = itemId;
                        }
                    }

                    var row = CreateEquipmentRow(context, roster, variation, rosterId, rosterCulture, variationIndex, rowNum++);

                    // Set equipment slot values
                    if (context.Schema.EquipmentSlots != null)
                    {
                        foreach (var slot in context.Schema.EquipmentSlots)
                        {
                            if (equipment.TryGetValue(slot.Slot, out var itemId))
                            {
                                row.SetValueWithoutNotify(slot.Slot, itemId);
                            }
                        }
                    }

                    context.Rows.Add(row);
                    variationIndex++;
                }
            }
        }

        Console.WriteLine($"[EquipmentSets] Created {context.Rows.Count} variation rows");
    }

    /// <summary>
    /// Creates a row for an equipment set variation.
    /// </summary>
    private EntryRowViewModel CreateEquipmentRow(
        FileEditContext context,
        XmlEntry roster,
        XmlEntry? variation,
        string rosterId,
        string rosterCulture,
        int variationIndex,
        int rowNum)
    {
        // Get git committed values for this entry
        var gitKey = $"{rosterId}_{variationIndex}";
        var gitValues = context.GitCommittedValues.TryGetValue(gitKey, out var values) ? values : null;

        var row = new EntryRowViewModel(roster, context.ColumnNames, gitValues);
        row.RowNumber = rowNum;
        row.VariationEntry = variation;
        row.VariationIndex = variationIndex;
        row.RosterId = rosterId;

        // Set roster-level values
        row.SetValueWithoutNotify("id", rosterId);
        row.SetValueWithoutNotify("culture", rosterCulture);
        row.SetValueWithoutNotify("_variation", variationIndex.ToString());

        Console.WriteLine($"[EquipmentRow] Created row for {rosterId}, variation index: {variationIndex}");

        return row;
    }

    /// <summary>
    /// Discovers columns from the entries.
    /// </summary>
    private void DiscoverColumns(FileEditContext context, IReadOnlyList<XmlEntry> entries)
    {
        if (context.Schema == null || entries.Count == 0) return;

        // Use schema-defined field order
        var orderedFields = context.Schema.Fields
            .Where(f => !f.Value.Hidden)
            .OrderBy(f => f.Value.Order)
            .Select(f => f.Key)
            .ToList();

        context.ColumnNames.Clear();
        context.ColumnNames.AddRange(orderedFields);

        Console.WriteLine($"[DiscoverColumns] Discovered {context.ColumnNames.Count} columns from schema");
    }

    /// <summary>
    /// Creates row view models from XML entries.
    /// </summary>
    private void CreateRows(FileEditContext context, IReadOnlyList<XmlEntry> entries)
    {
        Console.WriteLine($"[CreateRows] Creating rows for {entries.Count} entries");

        context.Rows.Clear();

        int rowNum = 1;
        foreach (var entry in entries)
        {
            var rowVm = CreateRow(context, entry, rowNum++);
            context.Rows.Add(rowVm);
        }

        Console.WriteLine($"[CreateRows] Created {context.Rows.Count} rows");
    }

    /// <summary>
    /// Creates a single row view model from an XML entry.
    /// </summary>
    private EntryRowViewModel CreateRow(FileEditContext context, XmlEntry entry, int rowNum)
    {
        // Get git committed values for this entry
        var entryId = entry.GetAttributeValue("id") ?? "";
        var gitValues = context.GitCommittedValues.TryGetValue(entryId, out var values) ? values : null;

        var row = new EntryRowViewModel(entry, context.ColumnNames, gitValues);
        row.RowNumber = rowNum;

        // Set values for all columns (visible)
        foreach (var columnName in context.ColumnNames)
        {
            var fieldDef = context.Schema?.GetField(columnName);
            string? value = null;

            // Check if this is a nested field
            if (fieldDef?.Nested == true && !string.IsNullOrEmpty(fieldDef.NestedPath))
            {
                value = entry.GetNestedValue(fieldDef.NestedPath);
            }
            else
            {
                // Regular attribute
                var attr = entry.GetAttribute(columnName);
                value = attr?.DisplayValue;
            }

            row.SetValueWithoutNotify(columnName, value ?? "");
        }

        // Also populate hidden fields (needed for banner color display, etc.)
        if (context.Schema != null)
        {
            var hiddenFields = context.Schema.Fields
                .Where(f => f.Value.Hidden == true)
                .Select(f => f.Key);

            foreach (var fieldName in hiddenFields)
            {
                var fieldDef = context.Schema.GetField(fieldName);
                string? value = null;

                if (fieldDef?.Nested == true && !string.IsNullOrEmpty(fieldDef.NestedPath))
                {
                    value = entry.GetNestedValue(fieldDef.NestedPath);
                }
                else
                {
                    var attr = entry.GetAttribute(fieldName);
                    value = attr?.DisplayValue;
                }

                if (!string.IsNullOrEmpty(value))
                {
                    row.SetValueWithoutNotify(fieldName, value);
                }
            }
        }

        return row;
    }
}
