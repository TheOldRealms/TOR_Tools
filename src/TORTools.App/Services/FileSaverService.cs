using System.Xml.Linq;
using TORTools.App.Models;
using TORTools.Core.Models;
using TORTools.Core.Services;

namespace TORTools.App.Services;

/// <summary>
/// Service responsible for saving file changes.
/// Handles single-file saving, multi-file splitting, and equipment set cloning.
/// </summary>
public class FileSaverService
{
    private readonly IXmlDocumentService _xmlService;

    public FileSaverService(IXmlDocumentService xmlService)
    {
        _xmlService = xmlService;
    }

    /// <summary>
    /// Saves the file(s).
    /// </summary>
    public void Save(FileEditContext context)
    {
        if (context.Document == null)
            throw new InvalidOperationException("No document loaded");

        if (context.Schema == null)
            throw new InvalidOperationException("No schema loaded");

        try
        {
            // Sync changes from row view models back to XML entries
            SyncChangesToXml(context);

            // For equipment sets: auto-generate civilian clones before saving
            if (context.Schema.HasNestedVariations)
            {
                GenerateCivilianClones(context);
            }

            // Check if this is a multi-file schema that needs split saving
            if (context.Schema.AdditionalSourceFiles != null && context.Schema.AdditionalSourceFiles.Count > 0)
            {
                SaveMergedFiles(context);
            }
            else
            {
                // Standard single-file save
                var compactFormat = context.Schema.CompactFormat;
                var groupByField = context.Schema.GroupByField;

                // Get linked fields that should not be written to the main file
                var linkedFields = context.Schema.Fields
                    .Where(f => f.Value.LinkedField == true)
                    .Select(f => f.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Check if we need to use category comments (groupByField is a linked field)
                var groupFieldDef = !string.IsNullOrEmpty(groupByField)
                    ? context.Schema.GetField(groupByField)
                    : null;

                if (groupFieldDef?.LinkedField == true)
                {
                    // Use special save method that reads category from XmlEntry values
                    _xmlService.SaveWithCategoryComments(
                        context.Document,
                        context.XmlEntries,
                        context.FilePath,
                        context.Schema.RootElement ?? "strings",
                        compactFormat,
                        linkedFields.Count > 0 ? linkedFields : null);
                }
                else
                {
                    // Regular save (groupByField is an actual XML attribute)
                    _xmlService.Save(context.Document, null, compactFormat, groupByField,
                        linkedFields.Count > 0 ? linkedFields : null);
                }

                // Save merged data file if configured (e.g., tor_strings_metadata.xml)
                if (context.Schema.MergedDataFile != null)
                {
                    var baseDir = Path.GetDirectoryName(context.FilePath);
                    if (!string.IsNullOrEmpty(baseDir))
                    {
                        SaveMergedDataFile(context, baseDir);
                    }
                }
            }

            context.HasUnsavedChanges = false;
            context.HasError = false;
            context.ErrorMessage = "";
        }
        catch (Exception ex)
        {
            context.HasError = true;
            context.ErrorMessage = $"Error saving file: {ex.Message}";
            throw;
        }
    }

    /// <summary>
    /// Syncs changes from row view models back to XML entries.
    /// </summary>
    private void SyncChangesToXml(FileEditContext context)
    {
        foreach (var rowVm in context.Rows)
        {
            // Skip equipment set variation rows - their changes are applied directly
            if (rowVm.IsEquipmentSetVariation)
                continue;

            var xmlEntry = rowVm.XmlEntry;

            foreach (var columnName in context.ColumnNames)
            {
                // Skip cross-reference columns - they're virtual and stored in other files
                var fieldDef = context.Schema?.GetField(columnName);
                if (fieldDef?.CrossReference != null)
                    continue;

                var currentValue = rowVm[columnName];

                // Handle tagList fields
                if (fieldDef?.TagList != null)
                {
                    var tagConfig = fieldDef.TagList;
                    var existingValue = xmlEntry.GetTagList(
                        tagConfig.ContainerElement,
                        tagConfig.ItemElement,
                        tagConfig.NameAttribute,
                        tagConfig.WeightAttribute) ?? "";
                    var normalizedCurrent = currentValue ?? "";
                    if (existingValue != normalizedCurrent)
                    {
                        xmlEntry.SetTagList(
                            currentValue,
                            tagConfig.ContainerElement,
                            tagConfig.ItemElement,
                            tagConfig.NameAttribute,
                            tagConfig.WeightAttribute);
                        context.Document!.HasUnsavedChanges = true;
                    }
                    continue;
                }

                // Handle nested fields
                if (fieldDef?.Nested == true && !string.IsNullOrEmpty(fieldDef.NestedPath))
                {
                    var existingValue = xmlEntry.GetNestedValue(fieldDef.NestedPath) ?? "";
                    var normalizedCurrent = currentValue ?? "";
                    if (existingValue != normalizedCurrent)
                    {
                        xmlEntry.SetNestedValue(fieldDef.NestedPath, currentValue);
                        context.Document!.HasUnsavedChanges = true;
                    }
                    continue;
                }

                var attr = xmlEntry.GetAttribute(columnName);

                if (attr != null)
                {
                    // Existing attribute - update if changed
                    if (attr.DisplayValue != currentValue)
                    {
                        xmlEntry.SetAttributeValue(columnName,
                            LocalizationHelper.Wrap(attr.LocalizationKey, currentValue ?? ""));
                        context.Document!.HasUnsavedChanges = true;
                    }
                }
                else if (!string.IsNullOrEmpty(currentValue))
                {
                    // New attribute on new entry - add it
                    xmlEntry.SetAttributeValue(columnName, currentValue);
                    context.Document!.HasUnsavedChanges = true;
                }
            }
        }

        context.HasUnsavedChanges = context.Document?.HasUnsavedChanges ?? false;
    }

    /// <summary>
    /// Saves multi-file schemas by splitting entries back to their source files.
    /// </summary>
    private void SaveMergedFiles(FileEditContext context)
    {
        if (context.Schema == null) return;

        var baseDir = Path.GetDirectoryName(context.FilePath);
        if (string.IsNullOrEmpty(baseDir)) return;

        var compactFormat = context.Schema.CompactFormat;

        // Group entries by source file field (e.g., is_custom_battle_lord)
        var mainEntries = new List<XmlEntry>();
        var additionalFileEntries = new Dictionary<string, List<XmlEntry>>();

        foreach (var entry in context.XmlEntries)
        {
            if (!string.IsNullOrEmpty(context.Schema.SourceFileField))
            {
                var sourceValue = entry.GetAttributeValue(context.Schema.SourceFileField);

                // Check if this entry belongs to an additional file
                var additionalFile = context.Schema.AdditionalSourceFiles?
                    .FirstOrDefault(f => f.SourceValue == sourceValue);

                if (additionalFile != null)
                {
                    if (!additionalFileEntries.ContainsKey(additionalFile.FileName))
                    {
                        additionalFileEntries[additionalFile.FileName] = new List<XmlEntry>();
                    }
                    additionalFileEntries[additionalFile.FileName].Add(entry);
                }
                else
                {
                    mainEntries.Add(entry);
                }
            }
            else
            {
                mainEntries.Add(entry);
            }
        }

        // Save main file
        Console.WriteLine($"[SaveMergedFiles] Saving {mainEntries.Count} entries to main file: {context.FilePath}");
        var mainXDoc = CreateDocumentFromEntries(context, mainEntries);
        var mainDoc = new XmlDocumentWrapper(mainXDoc, context.FilePath, context.Document!.HasBom,
            context.Document.Encoding, context.Document.IndentString);
        _xmlService.Save(mainDoc, context.FilePath, compactFormat);

        // Save additional files
        foreach (var kvp in additionalFileEntries)
        {
            var fileName = kvp.Key;
            var entries = kvp.Value;
            var filePath = FindSourceFile(baseDir, fileName);

            if (filePath != null)
            {
                Console.WriteLine($"[SaveMergedFiles] Saving {entries.Count} entries to: {filePath}");
                var xdoc = CreateDocumentFromEntries(context, entries);
                var doc = new XmlDocumentWrapper(xdoc, filePath, context.Document!.HasBom,
                    context.Document.Encoding, context.Document.IndentString);
                _xmlService.Save(doc, filePath, compactFormat);
            }
        }

        // Save merged data file if configured (e.g., tor_heroes.xml)
        if (context.Schema.MergedDataFile != null)
        {
            SaveMergedDataFile(context, baseDir);
        }
    }

    /// <summary>
    /// Creates an XDocument from a list of XmlEntry objects.
    /// </summary>
    private XDocument CreateDocumentFromEntries(FileEditContext context, List<XmlEntry> entries)
    {
        var rootElementName = context.Schema?.RootElement ?? "NPCCharacters";
        var root = new XElement(rootElementName);

        // Get list of linked fields that should NOT be saved to the main file
        var linkedFields = context.Schema?.Fields
            .Where(f => f.Value.LinkedField == true)
            .Select(f => f.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();

        foreach (var entry in entries)
        {
            // Remove the source file field attribute before saving (it's only for internal tracking)
            if (!string.IsNullOrEmpty(context.Schema?.SourceFileField))
            {
                entry.OriginalElement.Attribute(context.Schema.SourceFileField)?.Remove();
            }

            // Remove linked field attributes (they belong in a separate file like tor_heroes.xml)
            foreach (var linkedField in linkedFields)
            {
                entry.OriginalElement.Attribute(linkedField)?.Remove();
            }

            root.Add(entry.OriginalElement);
        }

        // Include XML declaration from the original document
        var declaration = context.Document?.Document.Declaration
            ?? new XDeclaration("1.0", "UTF-8", null);

        return new XDocument(declaration, root);
    }

    /// <summary>
    /// Saves merged data fields back to the merged data file (e.g., tor_heroes.xml, tor_strings_metadata.xml).
    /// Creates new entries if they don't exist in the metadata file.
    /// </summary>
    private void SaveMergedDataFile(FileEditContext context, string baseDir)
    {
        if (context.Schema?.MergedDataFile == null) return;

        var mergedConfig = context.Schema.MergedDataFile;
        var mergedFilePath = FindSourceFile(baseDir, mergedConfig.FileName);

        var entryElementName = mergedConfig.EntryElement ?? "String";
        var matchField = mergedConfig.MatchField ?? "id";
        var rootElementName = mergedConfig.RootElement ?? "StringMetadata";

        XDocument mergedDoc;
        XElement mergedRoot;

        if (mergedFilePath == null)
        {
            // Create new metadata file (prefer TORTools/data/)
            mergedFilePath = GetMetadataFilePath(baseDir, mergedConfig.FileName);
            Console.WriteLine($"[SaveMergedData] Creating new merged data file: {mergedFilePath}");
            mergedRoot = new XElement(rootElementName);
            mergedDoc = new XDocument(new XDeclaration("1.0", "UTF-8", null), mergedRoot);
        }
        else
        {
            Console.WriteLine($"[SaveMergedData] Updating merged data file: {mergedFilePath}");
            try
            {
                mergedDoc = XDocument.Load(mergedFilePath);
                mergedRoot = mergedDoc.Root ?? new XElement(rootElementName);
            }
            catch
            {
                mergedRoot = new XElement(rootElementName);
                mergedDoc = new XDocument(new XDeclaration("1.0", "UTF-8", null), mergedRoot);
            }
        }

        // Build a dictionary of existing entries
        var existingEntries = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in mergedRoot.Elements(entryElementName))
        {
            var key = element.Attribute(matchField)?.Value;
            if (!string.IsNullOrEmpty(key))
            {
                existingEntries[key] = element;
            }
        }

        // Update or create entries with data from our rows
        int updatedCount = 0;
        int createdCount = 0;
        foreach (var entry in context.XmlEntries)
        {
            var entryId = entry.GetAttributeValue(matchField);
            if (string.IsNullOrEmpty(entryId)) continue;

            // Check if any linked field has a value worth saving
            bool hasLinkedData = false;
            if (mergedConfig.FieldMappings != null)
            {
                foreach (var mapping in mergedConfig.FieldMappings)
                {
                    var value = entry.GetAttributeValue(mapping.Key);
                    if (!string.IsNullOrEmpty(value))
                    {
                        hasLinkedData = true;
                        break;
                    }
                }
            }

            if (!hasLinkedData) continue;

            if (existingEntries.TryGetValue(entryId, out var metadataElement))
            {
                // Update existing entry
                if (mergedConfig.FieldMappings != null)
                {
                    foreach (var mapping in mergedConfig.FieldMappings)
                    {
                        var targetField = mapping.Key;
                        var sourceField = mapping.Value;

                        var value = entry.GetAttributeValue(targetField);
                        if (!string.IsNullOrEmpty(value))
                        {
                            var oldValue = metadataElement.Attribute(sourceField)?.Value;
                            if (oldValue != value)
                            {
                                if (targetField == "subcategory" && updatedCount < 5)
                                {
                                    Console.WriteLine($"[SaveMergedData] Updating {entryId}.{targetField}: '{oldValue}' -> '{value}'");
                                }
                                metadataElement.SetAttributeValue(sourceField, value);
                                updatedCount++;
                            }
                        }
                        else if (targetField == "subcategory" && updatedCount < 3)
                        {
                            Console.WriteLine($"[SaveMergedData] Skipped {entryId}.{targetField}: value is empty/null (attr exists: {entry.GetAttribute(targetField) != null})");
                        }
                    }
                }
            }
            else
            {
                // Create new entry
                var newElement = new XElement(entryElementName);
                newElement.SetAttributeValue(matchField, entryId);

                if (mergedConfig.FieldMappings != null)
                {
                    foreach (var mapping in mergedConfig.FieldMappings)
                    {
                        var targetField = mapping.Key;
                        var sourceField = mapping.Value;

                        var value = entry.GetAttributeValue(targetField);
                        if (!string.IsNullOrEmpty(value))
                        {
                            newElement.SetAttributeValue(sourceField, value);
                        }
                    }
                }

                mergedRoot.Add(newElement);
                createdCount++;
            }
        }

        Console.WriteLine($"[SaveMergedData] Updated {updatedCount} fields, created {createdCount} new entries in {mergedFilePath}");

        try
        {
            // Save the merged data file with its own compact format setting (default: true)
            var compactFormat = mergedConfig.CompactFormat;
            var mergedDocWrapper = new XmlDocumentWrapper(mergedDoc, mergedFilePath,
                context.Document!.HasBom, context.Document.Encoding, context.Document.IndentString);
            _xmlService.Save(mergedDocWrapper, mergedFilePath, compactFormat);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SaveMergedData] Error saving {mergedFilePath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Auto-generates civilian clone EquipmentSets for each combat EquipmentSet.
    /// </summary>
    private void GenerateCivilianClones(FileEditContext context)
    {
        if (context.XmlEntries.Count == 0) return;

        var variationElementName = context.Schema?.VariationElement ?? "EquipmentSet";
        var cloneCount = 0;

        foreach (var roster in context.XmlEntries)
        {
            // Remove existing civilian clones first
            var civilianSets = roster.Children
                .Where(c => c.ElementName == variationElementName &&
                            c.GetAttributeValue("civilian")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            foreach (var civilianSet in civilianSets)
            {
                civilianSet.OriginalElement.Remove();
                roster.Children.Remove(civilianSet);
            }

            // Get all combat sets (no civilian attribute)
            var combatSets = roster.Children
                .Where(c => c.ElementName == variationElementName &&
                            c.GetAttributeValue("civilian")?.Equals("true", StringComparison.OrdinalIgnoreCase) != true)
                .ToList();

            // Clone each combat set as civilian
            foreach (var combatSet in combatSets)
            {
                var civilianClone = new XElement(combatSet.OriginalElement);
                civilianClone.SetAttributeValue("civilian", "true");

                // Add after the combat set
                combatSet.OriginalElement.AddAfterSelf(civilianClone);
                cloneCount++;
            }
        }

        Console.WriteLine($"[EquipmentSets] Generated {cloneCount} civilian clones");
    }

    /// <summary>
    /// Finds a source file relative to the base directory.
    /// Also checks TORTools/data/ for metadata files.
    /// </summary>
    private string? FindSourceFile(string baseDir, string fileName)
    {
        // Check TORTools/data directory first (for metadata files)
        var torToolsDataPath = TORTools.Core.Services.FilePathResolver.GetDataDirectory();
        if (torToolsDataPath != null)
        {
            var toolsDataFile = Path.Combine(torToolsDataPath, fileName);
            if (File.Exists(toolsDataFile))
            {
                Console.WriteLine($"[FindSourceFile] Found in TORTools/data: {toolsDataFile}");
                return toolsDataFile;
            }
        }

        // Check tor_custom_xmls subdirectory
        var customPath = Path.Combine(baseDir, "tor_custom_xmls", fileName);
        if (File.Exists(customPath))
            return customPath;

        // Check base directory
        var basePath = Path.Combine(baseDir, fileName);
        if (File.Exists(basePath))
            return basePath;

        // Check parent directory (e.g., tor_heroes.xml is in ModuleData, not ModuleData/tor_npccharacters)
        var parentDir = Path.GetDirectoryName(baseDir);
        if (!string.IsNullOrEmpty(parentDir))
        {
            var parentPath = Path.Combine(parentDir, fileName);
            if (File.Exists(parentPath))
                return parentPath;
        }

        return null;
    }

    /// <summary>
    /// Gets the path where a new metadata file should be created.
    /// Prefers TORTools/data/ for metadata files.
    /// </summary>
    private string GetMetadataFilePath(string baseDir, string fileName)
    {
        var torToolsDataPath = TORTools.Core.Services.FilePathResolver.GetDataDirectory();
        if (torToolsDataPath != null)
        {
            return Path.Combine(torToolsDataPath, fileName);
        }
        return Path.Combine(baseDir, fileName);
    }
}
