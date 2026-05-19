using System.Collections.ObjectModel;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.Input;
using TORTools.App.Models;
using TORTools.App.Services;
using TORTools.Core.Commands;
using TORTools.Core.Models;
using TORTools.Core.Services;

namespace TORTools.App.ViewModels;

/// <summary>
/// ViewModel for equipment set files (files with nested variations like EquipmentRosters).
/// Handles equipment-specific operations like variation management.
/// </summary>
public partial class EquipmentFileTabViewModel : FileTabViewModel
{
    public EquipmentFileTabViewModel(string filePath) : base(filePath)
    {
    }

    public EquipmentFileTabViewModel(
        string filePath,
        FileEditManager fileEditManager,
        IUndoRedoService undoRedoService,
        CrossReferenceService crossRefService,
        TupleListService tupleListService,
        FilePathResolver filePathResolver)
        : base(filePath, fileEditManager, undoRedoService, crossRefService, tupleListService, filePathResolver)
    {
    }

    /// <summary>
    /// Handles cell value changes for equipment set files.
    /// Overrides base to handle equipment-specific columns.
    /// </summary>
    protected override void OnCellValueChanged(object? sender, CellValueChangedEventArgs e)
    {
        if (sender is not EntryRowViewModel rowVm) return;
        if (Context.Document == null) return;

        // Handle equipment set variations specially
        if (rowVm.IsEquipmentSetVariation)
        {
            HandleEquipmentCellChange(rowVm, e);
            MarkAsModified();
            return;
        }

        // For non-equipment rows, use base implementation
        base.OnCellValueChanged(sender, e);
    }

    /// <summary>
    /// Handles cell changes for equipment set variations.
    /// Updates the nested XML structure accordingly.
    /// </summary>
    private void HandleEquipmentCellChange(EntryRowViewModel rowVm, CellValueChangedEventArgs e)
    {
        var variation = rowVm.VariationEntry;
        var roster = rowVm.XmlEntry;
        var columnName = e.ColumnName;
        var newValue = e.NewValue;

        // Equipment slot columns
        var equipmentSlots = Schema?.EquipmentSlots?.Select(s => s.Slot).ToHashSet() ?? new HashSet<string>();

        if (columnName == "id")
        {
            // Update roster-level id attribute
            var oldId = roster.Id;
            roster.SetAttributeValue("id", newValue);

            // Update RosterId on all variation rows belonging to this roster
            foreach (var row in Rows.Where(r => r.RosterId == oldId))
            {
                row.RosterId = newValue;
            }
            Console.WriteLine($"[EquipmentSet] Renamed roster from '{oldId}' to: {newValue}");
        }
        else if (columnName == "culture")
        {
            // Update roster-level culture attribute
            roster.SetAttributeValue("culture", newValue);
            Console.WriteLine($"[EquipmentSet] Updated roster culture to: {newValue}");
        }
        else if (columnName == "civilian" && variation != null)
        {
            // Update variation's civilian attribute
            if (newValue == "true")
            {
                variation.OriginalElement.SetAttributeValue("civilian", "true");
            }
            else
            {
                // Remove the attribute if false (default)
                variation.OriginalElement.SetAttributeValue("civilian", null);
            }
            Console.WriteLine($"[EquipmentSet] Updated civilian to: {newValue}");
        }
        else if (equipmentSlots.Contains(columnName) && variation != null)
        {
            // Update equipment slot
            UpdateEquipmentSlot(variation, columnName, newValue);
        }
    }

    /// <summary>
    /// Updates an equipment slot in a variation element.
    /// </summary>
    private void UpdateEquipmentSlot(XmlEntry variation, string slotName, string? itemId)
    {
        var equipmentElementName = Schema?.EquipmentItemElement ?? "Equipment";

        // Find existing equipment element for this slot
        var existingEquip = variation.Children
            .FirstOrDefault(c => c.ElementName == equipmentElementName &&
                                 c.GetAttributeValue("slot") == slotName);

        if (string.IsNullOrWhiteSpace(itemId))
        {
            // Remove the equipment element if value is cleared
            if (existingEquip != null)
            {
                existingEquip.OriginalElement.Remove();
                variation.Children.Remove(existingEquip);
                Console.WriteLine($"[EquipmentSet] Removed {slotName}");
            }
        }
        else
        {
            if (existingEquip != null)
            {
                // Update existing equipment element
                existingEquip.SetAttributeValue("id", itemId);
                Console.WriteLine($"[EquipmentSet] Updated {slotName} = {itemId}");
            }
            else
            {
                // Create new equipment element
                var newElement = new XElement(equipmentElementName,
                    new XAttribute("slot", slotName),
                    new XAttribute("id", itemId));
                variation.OriginalElement.Add(newElement);
                variation.Children.Add(new XmlEntry(newElement));
                Console.WriteLine($"[EquipmentSet] Added {slotName} = {itemId}");
            }
        }
    }

    /// <summary>
    /// Handles deletion for equipment set variation rows.
    /// </summary>
    protected override bool HandleRowDeletion(EntryRowViewModel rowToDelete)
    {
        if (rowToDelete.IsEquipmentSetVariation && rowToDelete.VariationEntry != null)
        {
            DeleteSelectedVariation();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Deletes the currently selected equipment set variation.
    /// If this is the first variation (index 1) or last variation, deletes the entire roster instead.
    /// </summary>
    [RelayCommand]
    public void DeleteSelectedVariation()
    {
        if (!HasNestedVariations || Context.Document == null || SelectedIndex < 0 || SelectedIndex >= Rows.Count)
            return;

        var rowToDelete = Rows[SelectedIndex];
        if (rowToDelete.VariationEntry == null)
        {
            Console.WriteLine("[DeleteVariation] No variation entry on selected row");
            return;
        }

        var rosterEntry = rowToDelete.XmlEntry;
        var variationEntry = rowToDelete.VariationEntry;
        var rosterId = rowToDelete.RosterId;

        // Count how many variations this roster has
        var variationsInRoster = Rows.Count(r => r.RosterId == rosterId && r.IsEquipmentSetVariation);

        // If this is the first variation (shows roster ID, not └) or last variation, delete the entire roster
        if (rowToDelete.IsFirstVariation || variationsInRoster <= 1)
        {
            Console.WriteLine($"[DeleteVariation] First/last variation in roster '{rosterId}', deleting entire roster");
            DeleteSelectedRoster();
            return;
        }

        // Delete just the variation
        Console.WriteLine($"[DeleteVariation] Deleting variation {rowToDelete.VariationIndex} from roster '{rosterId}'");

        var deleteCommand = new DeleteVariationCommand(Context.Document, rosterEntry, variationEntry);
        UndoRedoService.Execute(deleteCommand);

        // Remove the row from UI
        Rows.RemoveAt(SelectedIndex);

        // Update variation indices for remaining rows in this roster
        UpdateVariationIndicesForRoster(rosterId);

        // Update row numbers
        UpdateRowNumbers();

        // Notify cells to refresh styling
        RequestCellRefresh();
        MarkAsModified();
    }

    /// <summary>
    /// Deletes the entire roster (including all its variations).
    /// </summary>
    [RelayCommand]
    public void DeleteSelectedRoster()
    {
        Console.WriteLine($"[DeleteRoster] Called! HasNestedVariations={HasNestedVariations}, Document={Context.Document != null}, SelectedIndex={SelectedIndex}, RowsCount={Rows.Count}");

        if (!HasNestedVariations || Context.Document == null || SelectedIndex < 0 || SelectedIndex >= Rows.Count)
        {
            Console.WriteLine($"[DeleteRoster] Early return due to precondition");
            return;
        }

        var selectedRow = Rows[SelectedIndex];
        var rosterEntry = selectedRow.XmlEntry;
        var rosterId = selectedRow.RosterId;

        Console.WriteLine($"[DeleteRoster] Deleting roster '{rosterId}' and all its variations");

        // Find the roster index in XmlEntries
        var rosterIndex = XmlEntries.IndexOf(rosterEntry);
        if (rosterIndex < 0)
        {
            Console.WriteLine($"[DeleteRoster] Roster not found in XmlEntries");
            return;
        }

        // Delete the roster from XML
        var xmlEntryCollection = new ObservableCollection<XmlEntry>(XmlEntries);
        var command = new DeleteRowCommand(Context.Document, xmlEntryCollection, rosterEntry);
        UndoRedoService.Execute(command);

        // Sync collections
        XmlEntries.Clear();
        XmlEntries.AddRange(xmlEntryCollection);

        // Remove all rows for this roster from UI
        var rowsToRemove = Rows.Where(r => r.RosterId == rosterId).ToList();
        foreach (var row in rowsToRemove)
        {
            Rows.Remove(row);
        }

        UpdateRowNumbers();
        RequestCellRefresh();
        MarkAsModified();
    }

    /// <summary>
    /// Adds a new empty variation to the currently selected roster.
    /// </summary>
    [RelayCommand]
    public void AddVariation()
    {
        if (!HasNestedVariations || Context.Document == null || SelectedIndex < 0 || SelectedIndex >= Rows.Count)
            return;

        var selectedRow = Rows[SelectedIndex];
        var rosterEntry = selectedRow.XmlEntry;
        var variationElementName = Schema?.VariationElement ?? "EquipmentSet";

        var rosterId = rosterEntry.Id ?? "(no id)";
        var existingVariationCount = rosterEntry.OriginalElement.Elements(variationElementName).Count();
        Console.WriteLine($"[AddVariation] Adding variation to roster '{rosterId}' (currently has {existingVariationCount} variations in XElement)");

        var command = new AddVariationCommand(Context.Document, rosterEntry, variationElementName);
        UndoRedoService.Execute(command);

        if (command.AddedVariation != null)
        {
            var newVariationCount = rosterEntry.OriginalElement.Elements(variationElementName).Count();
            Console.WriteLine($"[AddVariation] After add: roster '{rosterId}' now has {newVariationCount} variations in XElement");

            var newVariationIndex = rosterEntry.Children
                .Count(c => c.ElementName == variationElementName &&
                       c.GetAttributeValue("civilian")?.Equals("true", StringComparison.OrdinalIgnoreCase) != true);

            var newRow = CreateVariationRow(rosterEntry, command.AddedVariation, newVariationIndex);

            var lastRosterRowIndex = FindLastRowIndexForRoster(selectedRow.RosterId);
            Rows.Insert(lastRosterRowIndex + 1, newRow);

            UpdateRowNumbers();
            SelectedIndex = lastRosterRowIndex + 1;
            MarkAsModified();

            Console.WriteLine($"[AddVariation] New row inserted at index {lastRosterRowIndex + 1}, variation index {newVariationIndex}");
        }
    }

    /// <summary>
    /// Duplicates the currently selected variation within the same roster.
    /// </summary>
    [RelayCommand]
    public void DuplicateVariation()
    {
        if (!HasNestedVariations || Context.Document == null || SelectedIndex < 0 || SelectedIndex >= Rows.Count)
            return;

        var selectedRow = Rows[SelectedIndex];
        if (selectedRow.VariationEntry == null)
            return;

        var rosterEntry = selectedRow.XmlEntry;
        var variationEntry = selectedRow.VariationEntry;

        var command = new DuplicateVariationCommand(Context.Document, rosterEntry, variationEntry);
        UndoRedoService.Execute(command);

        if (command.DuplicatedVariation != null)
        {
            var newVariationIndex = selectedRow.VariationIndex + 1;
            var newRow = CreateVariationRow(rosterEntry, command.DuplicatedVariation, newVariationIndex);

            CopyEquipmentValues(selectedRow, newRow);

            Rows.Insert(SelectedIndex + 1, newRow);

            UpdateVariationIndicesForRoster(selectedRow.RosterId);
            UpdateRowNumbers();

            SelectedIndex = SelectedIndex + 1;
            MarkAsModified();
        }
    }

    /// <summary>
    /// Duplicates the entire roster including all its variations.
    /// </summary>
    [RelayCommand]
    public void DuplicateRoster()
    {
        if (!HasNestedVariations || Context.Document == null || SelectedIndex < 0 || SelectedIndex >= Rows.Count)
            return;

        var selectedRow = Rows[SelectedIndex];
        var rosterEntry = selectedRow.XmlEntry;

        var xmlEntryCollection = new ObservableCollection<XmlEntry>(XmlEntries);
        var rosterIndex = xmlEntryCollection.IndexOf(rosterEntry);
        if (rosterIndex < 0) return;

        var command = new DuplicateRowCommand(Context.Document, xmlEntryCollection, rosterEntry);
        UndoRedoService.Execute(command);

        // Sync XmlEntries
        XmlEntries.Clear();
        foreach (var entry in xmlEntryCollection)
        {
            XmlEntries.Add(entry);
        }

        var duplicatedRoster = xmlEntryCollection[rosterIndex + 1];
        var variationElementName = Schema?.VariationElement ?? "EquipmentSet";

        var lastRosterRowIndex = FindLastRowIndexForRoster(selectedRow.RosterId);

        var variations = duplicatedRoster.Children
            .Where(c => c.ElementName == variationElementName &&
                   c.GetAttributeValue("civilian")?.Equals("true", StringComparison.OrdinalIgnoreCase) != true)
            .ToList();

        var insertIndex = lastRosterRowIndex + 1;
        var variationIndex = 1;
        foreach (var variation in variations)
        {
            var newRow = CreateVariationRow(duplicatedRoster, variation, variationIndex);
            CopyRosterValuesFromEntry(duplicatedRoster, newRow);
            CopyEquipmentValuesFromVariation(variation, newRow);
            Rows.Insert(insertIndex, newRow);
            insertIndex++;
            variationIndex++;
        }

        if (variations.Count == 0)
        {
            var emptyRow = CreateVariationRow(duplicatedRoster, null, 1);
            CopyRosterValuesFromEntry(duplicatedRoster, emptyRow);
            Rows.Insert(insertIndex, emptyRow);
        }

        UpdateRowNumbers();
        SelectedIndex = lastRosterRowIndex + 1;
        MarkAsModified();
    }

    /// <summary>
    /// Adds a new empty roster with one empty variation.
    /// </summary>
    [RelayCommand]
    public void AddRoster()
    {
        if (!HasNestedVariations || Context.Document == null)
            return;

        var xmlEntryCollection = new ObservableCollection<XmlEntry>(XmlEntries);

        int xmlInsertIndex = xmlEntryCollection.Count;
        int rowInsertIndex = Rows.Count;

        if (SelectedIndex >= 0 && SelectedIndex < Rows.Count)
        {
            var selectedRow = Rows[SelectedIndex];
            var rosterEntry = selectedRow.XmlEntry;
            var rosterIndex = xmlEntryCollection.IndexOf(rosterEntry);
            if (rosterIndex >= 0)
            {
                xmlInsertIndex = rosterIndex + 1;
                rowInsertIndex = FindLastRowIndexForRoster(selectedRow.RosterId) + 1;
            }
        }

        var command = new AddRowCommand(Context.Document, xmlEntryCollection, xmlInsertIndex);
        UndoRedoService.Execute(command);

        // Sync XmlEntries
        XmlEntries.Clear();
        foreach (var entry in xmlEntryCollection)
        {
            XmlEntries.Add(entry);
        }

        var newRoster = xmlEntryCollection[xmlInsertIndex];
        var variationElementName = Schema?.VariationElement ?? "EquipmentSet";

        var addVariationCmd = new AddVariationCommand(Context.Document, newRoster, variationElementName);
        UndoRedoService.Execute(addVariationCmd);

        var newRow = CreateVariationRow(newRoster, addVariationCmd.AddedVariation, 1);
        Rows.Insert(rowInsertIndex, newRow);

        UpdateRowNumbers();
        SelectedIndex = rowInsertIndex;
        MarkAsModified();
    }

    /// <summary>
    /// Creates a row for an equipment set variation.
    /// </summary>
    private EntryRowViewModel CreateVariationRow(XmlEntry roster, XmlEntry? variation, int variationIndex)
    {
        var row = new EntryRowViewModel(roster, ColumnNames.ToList(), null);
        row.VariationEntry = variation;
        row.VariationIndex = variationIndex;
        row.RosterId = roster.Id;
        row.IsNew = true;
        row.IsIdLocked = false;

        row.SetValueWithoutNotify("id", roster.Id ?? "");
        row.SetValueWithoutNotify("culture", roster.GetAttributeValue("culture") ?? "");
        row.SetValueWithoutNotify("_variation", variationIndex.ToString());

        if (variation != null)
        {
            var civilianValue = variation.GetAttributeValue("civilian");
            if (!string.IsNullOrEmpty(civilianValue))
            {
                row.SetValueWithoutNotify("civilian", civilianValue);
            }
        }

        row.CellValueChanged += OnCellValueChangedHandler;

        return row;
    }

    /// <summary>
    /// Copies equipment values from a source row to a target row.
    /// </summary>
    private void CopyEquipmentValues(EntryRowViewModel source, EntryRowViewModel target)
    {
        var equipmentSlots = Schema?.EquipmentSlots?.Select(s => s.Slot).ToHashSet() ?? new HashSet<string>();
        foreach (var slot in equipmentSlots)
        {
            var value = source[slot];
            if (!string.IsNullOrEmpty(value))
            {
                target.SetValueWithoutNotify(slot, value);
            }
        }
    }

    /// <summary>
    /// Copies roster-level values from an entry to a row.
    /// </summary>
    private void CopyRosterValuesFromEntry(XmlEntry roster, EntryRowViewModel row)
    {
        row.SetValueWithoutNotify("id", roster.Id ?? "");
        row.SetValueWithoutNotify("culture", roster.GetAttributeValue("culture") ?? "");
    }

    /// <summary>
    /// Copies equipment values from a variation entry to a row.
    /// </summary>
    private void CopyEquipmentValuesFromVariation(XmlEntry variation, EntryRowViewModel row)
    {
        var equipmentItemElementName = Schema?.EquipmentItemElement ?? "Equipment";
        var equipmentSlots = Schema?.EquipmentSlots?.Select(s => s.Slot).ToHashSet() ?? new HashSet<string>();

        foreach (var equipItem in variation.Children.Where(c => c.ElementName == equipmentItemElementName))
        {
            var slot = equipItem.GetAttributeValue("slot");
            var itemId = equipItem.GetAttributeValue("id");
            if (!string.IsNullOrEmpty(slot) && equipmentSlots.Contains(slot) && !string.IsNullOrEmpty(itemId))
            {
                row.SetValueWithoutNotify(slot, itemId);
            }
        }
    }

    /// <summary>
    /// Finds the last row index for a given roster ID.
    /// </summary>
    private int FindLastRowIndexForRoster(string? rosterId)
    {
        if (string.IsNullOrEmpty(rosterId)) return Rows.Count - 1;

        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            if (Rows[i].RosterId == rosterId)
                return i;
        }
        return Rows.Count - 1;
    }

    /// <summary>
    /// Updates variation indices for all rows in a roster.
    /// </summary>
    private void UpdateVariationIndicesForRoster(string? rosterId)
    {
        if (string.IsNullOrEmpty(rosterId)) return;

        var variationIndex = 1;
        foreach (var row in Rows.Where(r => r.RosterId == rosterId))
        {
            row.VariationIndex = variationIndex;
            row.SetValueWithoutNotify("_variation", variationIndex.ToString());
            variationIndex++;
        }
    }

    /// <summary>
    /// Syncs rows for equipment sets with nested variations.
    /// Overrides base to handle roster/variation structure.
    /// </summary>
    protected override void SyncRowsWithEntries()
    {
        var variationElementName = Schema?.VariationElement ?? "EquipmentSet";

        // Build set of current variation elements from XML
        var currentVariationElements = new HashSet<XElement>();
        foreach (var roster in XmlEntries)
        {
            roster.RefreshChildren();
            foreach (var variation in roster.Children.Where(c => c.ElementName == variationElementName))
            {
                if (variation.GetAttributeValue("civilian")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
                    continue;
                currentVariationElements.Add(variation.OriginalElement);
            }
        }

        // Remove rows whose variations no longer exist
        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            var row = Rows[i];
            if (row.IsRemoved) continue;

            if (row.VariationEntry != null)
            {
                if (!currentVariationElements.Contains(row.VariationEntry.OriginalElement))
                {
                    Console.WriteLine($"[SyncEquipmentSetRows] Removing row for deleted variation in roster {row.RosterId}");
                    Rows.RemoveAt(i);
                }
            }
        }

        // Build set of variation elements that have rows
        var rowVariationElements = new HashSet<XElement>(
            Rows.Where(r => !r.IsRemoved && r.VariationEntry != null)
                .Select(r => r.VariationEntry!.OriginalElement));

        // Add rows for variations that don't have rows yet
        foreach (var roster in XmlEntries)
        {
            var rosterId = roster.GetAttributeValue("id") ?? "";

            var variations = roster.Children
                .Where(c => c.ElementName == variationElementName &&
                       c.GetAttributeValue("civilian")?.Equals("true", StringComparison.OrdinalIgnoreCase) != true)
                .ToList();

            int variationIndex = 1;
            foreach (var variation in variations)
            {
                if (!rowVariationElements.Contains(variation.OriginalElement))
                {
                    Console.WriteLine($"[SyncEquipmentSetRows] Creating row for restored variation {variationIndex} in roster {rosterId}");
                    var newRow = CreateVariationRow(roster, variation, variationIndex);
                    CopyEquipmentValuesFromVariation(variation, newRow);

                    var lastRosterRowIndex = FindLastRowIndexForRoster(rosterId);
                    if (lastRosterRowIndex >= 0)
                    {
                        Rows.Insert(lastRosterRowIndex + 1, newRow);
                    }
                    else
                    {
                        Rows.Add(newRow);
                    }
                }
                variationIndex++;
            }
        }

        // Update row numbers and variation indices
        int rowNum = 1;
        string? currentRosterId = null;
        int currentVariationIndex = 0;

        for (int i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            row.RowNumber = rowNum++;

            if (row.RosterId != currentRosterId)
            {
                currentRosterId = row.RosterId;
                currentVariationIndex = 1;
            }
            row.VariationIndex = currentVariationIndex;
            row.SetValueWithoutNotify("_variation", currentVariationIndex.ToString());
            currentVariationIndex++;
        }

        RequestCellRefresh();
    }
}
