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
                _xmlService.Save(context.Document, null, compactFormat);
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

                var currentValue = rowVm[columnName]?.Trim();

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
    /// Saves merged data fields back to the merged data file (e.g., tor_heroes.xml).
    /// </summary>
    private void SaveMergedDataFile(FileEditContext context, string baseDir)
    {
        if (context.Schema?.MergedDataFile == null) return;

        var mergedConfig = context.Schema.MergedDataFile;
        var mergedFilePath = FindSourceFile(baseDir, mergedConfig.FileName);
        if (mergedFilePath == null)
        {
            Console.WriteLine($"[SaveMergedData] Merged data file not found: {mergedConfig.FileName}");
            return;
        }

        Console.WriteLine($"[SaveMergedData] Updating merged data file: {mergedFilePath}");

        try
        {
            // Load existing merged data file
            var mergedDoc = XDocument.Load(mergedFilePath);
            var mergedRoot = mergedDoc.Root;
            if (mergedRoot == null) return;

            var entryElementName = mergedConfig.EntryElement ?? "Hero";
            var matchField = mergedConfig.MatchField ?? "id";

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

            // Update entries with data from our rows
            int updatedCount = 0;
            foreach (var entry in context.XmlEntries)
            {
                var entryId = entry.GetAttributeValue(matchField);
                if (string.IsNullOrEmpty(entryId)) continue;

                if (existingEntries.TryGetValue(entryId, out var heroElement))
                {
                    // Apply reverse field mappings (targetField → sourceField)
                    if (mergedConfig.FieldMappings != null)
                    {
                        foreach (var mapping in mergedConfig.FieldMappings)
                        {
                            var targetField = mapping.Key;   // "clan" or "encyclopedia_text"
                            var sourceField = mapping.Value; // "faction" or "text"

                            var value = entry.GetAttributeValue(targetField);
                            if (!string.IsNullOrEmpty(value))
                            {
                                var oldValue = heroElement.Attribute(sourceField)?.Value;
                                if (oldValue != value)
                                {
                                    heroElement.SetAttributeValue(sourceField, value);
                                    updatedCount++;
                                }
                            }
                        }
                    }
                }
            }

            Console.WriteLine($"[SaveMergedData] Updated {updatedCount} fields in {mergedFilePath}");

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
    /// </summary>
    private string? FindSourceFile(string baseDir, string fileName)
    {
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
}
