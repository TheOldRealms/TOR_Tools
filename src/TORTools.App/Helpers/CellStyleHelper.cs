using Avalonia;
using Avalonia.Controls;
using TORTools.App.ViewModels;
using TORTools.Core.Validation;

namespace TORTools.App.Helpers;

/// <summary>
/// Helper class for managing cell styling using pseudo-classes.
/// This allows styles to be defined in AXAML while logic remains in C#.
/// </summary>
public static class CellStyleHelper
{
    // Pseudo-class names (must match CellStyles.axaml selectors)
    private static readonly string PseudoRemoved = ":removed";
    private static readonly string PseudoError = ":error";
    private static readonly string PseudoWarning = ":warning";
    private static readonly string PseudoNew = ":new";
    private static readonly string PseudoModified = ":modified";
    private static readonly string PseudoWasNew = ":wasNew";
    private static readonly string PseudoSaved = ":saved";

    /// <summary>
    /// Updates the pseudo-classes on a cell border based on the current state.
    /// The actual styling is defined in CellStyles.axaml.
    /// </summary>
    /// <param name="border">The cell border to style</param>
    /// <param name="rowVm">The row view model</param>
    /// <param name="attributeName">The column/attribute name</param>
    /// <param name="vm">The file tab view model</param>
    /// <param name="validationIssues">Optional pre-fetched validation issues</param>
    public static void UpdateCellState(
        Border border,
        EntryRowViewModel rowVm,
        string attributeName,
        FileTabViewModel vm,
        IReadOnlyList<ValidationIssue>? validationIssues = null)
    {
        // Get validation issues if not provided
        var rowIndex = rowVm.RowNumber - 1;
        var issues = validationIssues ?? vm.ValidationManager.Issues
            .Where(i => i.RowIndex == rowIndex && i.AttributeName == attributeName)
            .ToList();

        // Clear all state pseudo-classes first
        ClearAllStates(border);

        // Determine state and set appropriate pseudo-class
        // Priority order: removed > error > warning > new > modified > wasNew > saved

        if (rowVm.IsRemoved)
        {
            SetPseudoClass(border, PseudoRemoved, true);
            return;
        }

        var hasError = issues.Any(i => i.Severity == ValidationSeverity.Error);
        var hasWarning = issues.Any(i => i.Severity == ValidationSeverity.Warning);

        if (hasError)
        {
            SetPseudoClass(border, PseudoError, true);
            SetTooltip(border, issues.Where(i => i.Severity == ValidationSeverity.Error));
        }
        else if (hasWarning)
        {
            SetPseudoClass(border, PseudoWarning, true);
            SetTooltip(border, issues.Where(i => i.Severity == ValidationSeverity.Warning));
        }
        else if (vm.HasUnsavedChanges && rowVm.IsNew)
        {
            SetPseudoClass(border, PseudoNew, true);
        }
        else if (vm.HasUnsavedChanges && rowVm.IsFieldModified(attributeName))
        {
            SetPseudoClass(border, PseudoModified, true);
        }
        else if (rowVm.WasNew)
        {
            SetPseudoClass(border, PseudoWasNew, true);
        }
        else if (rowVm.IsFieldSaved(attributeName))
        {
            SetPseudoClass(border, PseudoSaved, true);
        }
        else
        {
            // Default state - clear tooltip
            ToolTip.SetTip(border, null);
        }
    }

    /// <summary>
    /// Clears all cell state pseudo-classes from a border.
    /// </summary>
    private static void ClearAllStates(Border border)
    {
        SetPseudoClass(border, PseudoRemoved, false);
        SetPseudoClass(border, PseudoError, false);
        SetPseudoClass(border, PseudoWarning, false);
        SetPseudoClass(border, PseudoNew, false);
        SetPseudoClass(border, PseudoModified, false);
        SetPseudoClass(border, PseudoWasNew, false);
        SetPseudoClass(border, PseudoSaved, false);
        ToolTip.SetTip(border, null);
    }

    /// <summary>
    /// Sets or removes a pseudo-class on a control.
    /// </summary>
    private static void SetPseudoClass(StyledElement element, string pseudoClass, bool isSet)
    {
        // Remove the leading colon for the pseudo-classes API
        var className = pseudoClass.TrimStart(':');

        if (isSet)
        {
            ((IPseudoClasses)element.Classes).Add(className);
        }
        else
        {
            ((IPseudoClasses)element.Classes).Remove(className);
        }
    }

    /// <summary>
    /// Sets tooltip text from validation issues.
    /// </summary>
    private static void SetTooltip(Border border, IEnumerable<ValidationIssue> issues)
    {
        var message = string.Join("\n", issues.Select(i => i.Message));
        if (!string.IsNullOrEmpty(message))
        {
            ToolTip.SetTip(border, message);
        }
    }
}
