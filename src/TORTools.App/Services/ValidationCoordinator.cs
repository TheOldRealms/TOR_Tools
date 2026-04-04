using System.Text.RegularExpressions;
using Avalonia.Threading;
using TORTools.App.Models;
using TORTools.Core.Validation;

namespace TORTools.App.Services;

/// <summary>
/// Coordinates all validation operations for file editing.
/// Handles upgrade targets, skill template tiers, and cross-reference validation.
/// </summary>
public class ValidationCoordinator
{
    private readonly IValidationService _validationService;

    public ValidationCoordinator(IValidationService validationService)
    {
        _validationService = validationService;
    }

    /// <summary>
    /// Runs full validation asynchronously.
    /// </summary>
    public async Task RunValidationAsync(FileEditContext context)
    {
        Console.WriteLine($"[Validation] Starting validation of {context.Rows.Count} entries...");

        // Clear previous validation issues on UI thread
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            context.ValidationManager.ClearByPrefix("basic_");
            context.ValidationManager.ClearByPrefix("upgrade_");
            context.ValidationManager.ClearByPrefix("crossref_");
        });

        // Capture row data for thread-safe processing
        var rowData = new List<(int index, string id, Dictionary<string, string> values)>();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            for (int i = 0; i < context.Rows.Count; i++)
            {
                var row = context.Rows[i];
                var values = new Dictionary<string, string>();
                foreach (var col in context.ColumnNames)
                {
                    values[col] = row[col] ?? "";
                }
                rowData.Add((i, row["id"] ?? "", values));
            }
        });

        // Run basic validation
        var skipDuplicateIdCheck = context.Schema?.HasNestedVariations == true;
        var entries = rowData.Select(r => (IDictionary<string, string>)r.values).ToList();
        var result = _validationService.ValidateAll(entries, context.Schema, skipDuplicateIdCheck);

        // Register basic validation issues
        foreach (var issue in result.Issues)
        {
            var key = $"basic_{issue.RowIndex}_{issue.AttributeName}_{issue.CurrentValue ?? "empty"}";
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                context.ValidationManager.RegisterError(key, issue);
            });
        }

        // Run upgrade target validation
        await ValidateUpgradeTargetsAsync(context, rowData);

        // Run skill template tier validation
        await ValidateSkillTemplateTiersAsync(context, rowData);

        // Run cross-reference validation
        await ValidateCrossReferencesAsync(context, rowData);

        Console.WriteLine($"[Validation] Completed validation. Errors: {context.ValidationManager.ErrorCount}, Warnings: {context.ValidationManager.WarningCount}");
    }

    /// <summary>
    /// Validates upgrade targets asynchronously.
    /// </summary>
    private async Task ValidateUpgradeTargetsAsync(
        FileEditContext context,
        List<(int index, string id, Dictionary<string, string> values)> rowData)
    {
        // Check if this file has upgrade target fields
        var hasUpgradeTargets = context.Schema?.Fields.ContainsKey("upgrade_target_1") == true;
        if (!hasUpgradeTargets) return;

        // Build lookups
        var idToLevel = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (index, id, values) in rowData)
        {
            if (!string.IsNullOrEmpty(id) && int.TryParse(values.GetValueOrDefault("level", "0"), out var level))
            {
                idToLevel[id] = level;
            }
        }

        var problemTargets = new Dictionary<string, (int sourceRowIndex, string fieldName, string sourceId, int sourceLevel)>(StringComparer.OrdinalIgnoreCase);

        // Process all rows
        foreach (var (rowIndex, sourceId, values) in rowData)
        {
            var sourceLevel = idToLevel.GetValueOrDefault(sourceId, 0);

            for (int i = 1; i <= 3; i++)
            {
                var fieldName = $"upgrade_target_{i}";
                var targetId = values.GetValueOrDefault(fieldName, "");

                var fieldDef = context.Schema?.GetField(fieldName);
                if (fieldDef?.PrefixToStrip != null && targetId.StartsWith(fieldDef.PrefixToStrip, StringComparison.OrdinalIgnoreCase))
                {
                    targetId = targetId.Substring(fieldDef.PrefixToStrip.Length);
                }

                if (!string.IsNullOrEmpty(targetId))
                {
                    if (!idToLevel.ContainsKey(targetId))
                    {
                        var key = $"upgrade_{rowIndex}_{fieldName}_notfound";
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            context.ValidationManager.RegisterError(key, new ValidationIssue
                            {
                                Severity = ValidationSeverity.Error,
                                RowIndex = rowIndex,
                                AttributeName = fieldName,
                                Message = $"Upgrade target '{targetId}' not found in this file",
                                EntryId = sourceId,
                                CurrentValue = targetId
                            });
                        });
                    }
                    else
                    {
                        var targetLevel = idToLevel[targetId];
                        if (targetLevel <= sourceLevel)
                        {
                            if (!problemTargets.TryGetValue(targetId, out var existing) || sourceLevel > existing.sourceLevel)
                            {
                                problemTargets[targetId] = (rowIndex, fieldName, sourceId, sourceLevel);
                            }
                        }
                    }
                }
            }
        }

        // Register tier warnings
        foreach (var kvp in problemTargets)
        {
            var targetId = kvp.Key;
            var (sourceRowIndex, fieldName, sourceId, sourceLevel) = kvp.Value;
            var targetLevel = idToLevel.GetValueOrDefault(targetId, 0);

            var key = $"upgrade_{sourceRowIndex}_{fieldName}_tier";
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                context.ValidationManager.RegisterError(key, new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    RowIndex = sourceRowIndex,
                    AttributeName = fieldName,
                    Message = $"'{targetId}' has level {targetLevel}, should be higher than {sourceLevel}",
                    EntryId = sourceId,
                    CurrentValue = targetId
                });
            });
        }
    }

    /// <summary>
    /// Validates skill template tiers asynchronously.
    /// </summary>
    private async Task ValidateSkillTemplateTiersAsync(
        FileEditContext context,
        List<(int index, string id, Dictionary<string, string> values)> rowData)
    {
        var hasSkillTemplate = context.Schema?.Fields.ContainsKey("skill_template") == true;
        if (!hasSkillTemplate) return;

        int checkedCount = 0;
        int skippedNoLevel = 0;
        int mismatchCount = 0;

        foreach (var (rowIndex, entryId, values) in rowData)
        {
            var levelStr = values.GetValueOrDefault("level", "1");
            var skillTemplate = values.GetValueOrDefault("skill_template", "");

            if (string.IsNullOrEmpty(skillTemplate)) continue;
            if (!int.TryParse(levelStr, out var level)) continue;

            var expectedTier = (level - 1) / 5;

            // Try to extract tier from skill template name
            int? templateTier = null;

            var levelMatch = Regex.Match(skillTemplate, @"level(\d+)", RegexOptions.IgnoreCase);
            if (levelMatch.Success && int.TryParse(levelMatch.Groups[1].Value, out var templateLevel))
            {
                templateTier = (templateLevel - 1) / 5;
            }
            else
            {
                var tierMatch = Regex.Match(skillTemplate, @"_t(\d+)_");
                if (tierMatch.Success && int.TryParse(tierMatch.Groups[1].Value, out var parsedTier))
                {
                    templateTier = parsedTier;
                }
            }

            if (!templateTier.HasValue)
            {
                skippedNoLevel++;
                continue;
            }

            checkedCount++;

            if (templateTier.Value != expectedTier)
            {
                mismatchCount++;
                var key = $"upgrade_{rowIndex}_skill_template_tier";
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    context.ValidationManager.RegisterError(key, new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        RowIndex = rowIndex,
                        AttributeName = "skill_template",
                        Message = $"Skill template is Tier {templateTier.Value} but troop is Tier {expectedTier} (level {level})",
                        EntryId = entryId,
                        CurrentValue = skillTemplate
                    });
                });
            }
        }

        Console.WriteLine($"[Validation] Skill template tier check: {checkedCount} checked, {skippedNoLevel} skipped (no level pattern), {mismatchCount} mismatches");
    }

    /// <summary>
    /// Validates cross-reference fields asynchronously.
    /// </summary>
    private async Task ValidateCrossReferencesAsync(
        FileEditContext context,
        List<(int index, string id, Dictionary<string, string> values)> rowData)
    {
        if (context.Schema == null) return;

        // Find all crossReference fields
        var crossRefFields = context.Schema.Fields
            .Where(f => f.Value.Type == "crossReference" && f.Value.CrossReference != null)
            .ToList();

        if (!crossRefFields.Any()) return;

        Console.WriteLine($"[Validation] Cross-ref fields to validate: {crossRefFields.Count}");

        foreach (var (fieldName, fieldDef) in crossRefFields)
        {
            var crossRef = fieldDef.CrossReference!;

            // Get available IDs from the cache
            if (!context.AvailableIds.TryGetValue(fieldName, out var availableIdsList) || availableIdsList.Count == 0)
            {
                Console.WriteLine($"[Validation] No available IDs for {fieldName}, skipping");
                continue;
            }

            Console.WriteLine($"[Validation] Validating {fieldName} with {availableIdsList.Count} valid IDs");

            var validIdsSet = new HashSet<string>(availableIdsList, StringComparer.OrdinalIgnoreCase);

            int invalidCount = 0;
            foreach (var (rowIndex, entryId, values) in rowData)
            {
                var rawValue = values.GetValueOrDefault(fieldName, "");
                if (string.IsNullOrEmpty(rawValue)) continue;

                // Handle multi-value fields (colon-separated or comma-separated)
                var ids = rawValue.Split(new[] { ':', ',' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var id in ids)
                {
                    var cleanId = id.Trim();
                    if (string.IsNullOrEmpty(cleanId)) continue;

                    // Strip prefix if configured
                    if (!string.IsNullOrEmpty(crossRef.PrefixToStrip) &&
                        cleanId.StartsWith(crossRef.PrefixToStrip, StringComparison.OrdinalIgnoreCase))
                    {
                        cleanId = cleanId.Substring(crossRef.PrefixToStrip.Length);
                    }

                    if (!validIdsSet.Contains(cleanId))
                    {
                        invalidCount++;
                        var key = $"crossref_{rowIndex}_{fieldName}_{cleanId}";
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            context.ValidationManager.RegisterError(key, new ValidationIssue
                            {
                                Severity = ValidationSeverity.Error,
                                RowIndex = rowIndex,
                                AttributeName = fieldName,
                                Message = $"'{cleanId}' not found in {crossRef.TargetFile}",
                                EntryId = entryId,
                                CurrentValue = cleanId
                            });
                        });
                    }
                }
            }

            Console.WriteLine($"[Validation] CrossRef {fieldName}: {invalidCount} invalid entries found");
        }
    }
}
