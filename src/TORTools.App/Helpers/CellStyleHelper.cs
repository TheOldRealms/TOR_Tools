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
    // CSS class names (must match CellStyles.axaml selectors)
    private static readonly string ClassRemoved = "removed";
    private static readonly string ClassError = "error";
    private static readonly string ClassWarning = "warning";
    private static readonly string ClassNew = "new";
    private static readonly string ClassModified = "modified";
    private static readonly string ClassWasNew = "wasNew";
    private static readonly string ClassSaved = "saved";
    private static readonly string ClassCellSelected = "cellSelected";

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
            SetClass(border, ClassRemoved, true);
            return;
        }

        var hasError = issues.Any(i => i.Severity == ValidationSeverity.Error);
        var hasWarning = issues.Any(i => i.Severity == ValidationSeverity.Warning);

        if (hasError)
        {
            SetClass(border, ClassError, true);
            SetTooltip(border, issues.Where(i => i.Severity == ValidationSeverity.Error));
        }
        else if (hasWarning)
        {
            SetClass(border, ClassWarning, true);
            SetTooltip(border, issues.Where(i => i.Severity == ValidationSeverity.Warning));
        }
        else if (vm.HasUnsavedChanges && rowVm.IsNew)
        {
            SetClass(border, ClassNew, true);
        }
        else if (vm.HasUnsavedChanges && rowVm.IsFieldModified(attributeName))
        {
            SetClass(border, ClassModified, true);
        }
        else if (rowVm.WasNew)
        {
            SetClass(border, ClassWasNew, true);
        }
        else if (rowVm.IsFieldSaved(attributeName))
        {
            SetClass(border, ClassSaved, true);
        }
        else
        {
            // Default state - clear tooltip
            ToolTip.SetTip(border, null);
        }
    }

    /// <summary>
    /// Clears all cell state classes from a border.
    /// </summary>
    private static void ClearAllStates(Border border)
    {
        SetClass(border, ClassRemoved, false);
        SetClass(border, ClassError, false);
        SetClass(border, ClassWarning, false);
        SetClass(border, ClassNew, false);
        SetClass(border, ClassModified, false);
        SetClass(border, ClassWasNew, false);
        SetClass(border, ClassSaved, false);
        ToolTip.SetTip(border, null);
    }

    /// <summary>
    /// Sets or removes a CSS class on a control.
    /// </summary>
    private static void SetClass(StyledElement element, string className, bool isSet)
    {
        if (isSet)
        {
            if (!element.Classes.Contains(className))
            {
                element.Classes.Add(className);
            }
        }
        else
        {
            element.Classes.Remove(className);
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

    /// <summary>
    /// Updates the cell selection state based on the current SelectedIndex and SelectedColumn.
    /// Call this when the cell selection changes.
    /// </summary>
    /// <param name="border">The cell border to style</param>
    /// <param name="rowVm">The row view model</param>
    /// <param name="attributeName">The column/attribute name</param>
    /// <param name="vm">The file tab view model</param>
    public static void UpdateCellSelection(
        Border border,
        EntryRowViewModel rowVm,
        string attributeName,
        FileTabViewModel vm)
    {
        var rowIndex = rowVm.RowNumber - 1;

        // Check if this specific cell is selected (not entire row)
        var isCellSelected = vm.SelectedIndex == rowIndex &&
                            vm.SelectedColumn != null &&
                            vm.SelectedColumn.Equals(attributeName, StringComparison.OrdinalIgnoreCase);

        SetClass(border, ClassCellSelected, isCellSelected);
    }

    /// <summary>
    /// Clears cell selection state from a border.
    /// </summary>
    public static void ClearCellSelection(Border border)
    {
        SetClass(border, ClassCellSelected, false);
    }
}
