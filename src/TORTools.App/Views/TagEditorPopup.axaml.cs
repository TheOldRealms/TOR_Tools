using Avalonia.Controls;
using Avalonia.Interactivity;
using TORTools.App.ViewModels;

namespace TORTools.App.Views;

/// <summary>
/// Popup window for editing tags on a string entry.
/// Loads available tags from tor_tags.xml and allows adding/removing tags.
/// Uses the same pattern as Unit Attributes editor - double-click to add from list.
/// </summary>
public partial class TagEditorPopup : Window
{
    private readonly string _entryId;
    private readonly string _fieldName;
    private readonly FileTabViewModel _viewModel;
    private readonly List<TagDefinition> _availableTags;

    public TagEditorPopup()
    {
        InitializeComponent();
        _entryId = "";
        _fieldName = "";
        _viewModel = null!;
        _availableTags = new List<TagDefinition>();
    }

    public TagEditorPopup(
        string entryId,
        string fieldName,
        string currentTagsValue,
        List<TagDefinition> availableTags,
        FileTabViewModel viewModel)
    {
        InitializeComponent();

        _entryId = entryId;
        _fieldName = fieldName;
        _viewModel = viewModel;
        _availableTags = availableTags;

        // Set header text
        HeaderText.Text = "Edit Tags";
        SubHeaderText.Text = $"String: {entryId}";

        // Set current tags value in the text box
        CurrentTagsBox.Text = currentTagsValue;

        // Initial list population (exclude already assigned tags)
        UpdateAvailableTagsList();

        // Setup filter
        FilterBox.TextChanged += OnFilterTextChanged;

        // Setup double-click to add
        AvailableTagsList.DoubleTapped += OnTagDoubleClick;

        // Setup selection change for description
        AvailableTagsList.SelectionChanged += OnTagSelectionChanged;
    }

    private void UpdateAvailableTagsList()
    {
        var filterText = FilterBox?.Text ?? "";
        var currentTags = GetCurrentTagsSet();

        var filtered = _availableTags
            .Where(t => !currentTags.Contains(t.Id))
            .Where(t => string.IsNullOrEmpty(filterText) ||
                        t.Id.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
                        t.Description.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
                        t.Category.Contains(filterText, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Category)
            .ThenBy(t => t.Id)
            .ToList();

        AvailableTagsList.ItemsSource = filtered;
    }

    private HashSet<string> GetCurrentTagsSet()
    {
        var currentText = CurrentTagsBox?.Text ?? "";
        return currentText
            .Split(new[] { ',', ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void OnFilterTextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateAvailableTagsList();
    }

    private void OnTagDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (AvailableTagsList.SelectedItem is TagDefinition selectedTag)
        {
            AddTagToCurrentTags(selectedTag.Id);
        }
    }

    private void AddTagToCurrentTags(string tagId)
    {
        var currentText = CurrentTagsBox.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(currentText))
        {
            CurrentTagsBox.Text = tagId;
        }
        else
        {
            // Add with comma separator
            CurrentTagsBox.Text = currentText + ", " + tagId;
        }

        // Refresh available list to remove the added tag
        UpdateAvailableTagsList();

        // Clear filter
        FilterBox.Text = "";

        Console.WriteLine($"[TagEditor] Added tag: {tagId}");
    }

    private void OnTagSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (AvailableTagsList.SelectedItem is TagDefinition selectedTag)
        {
            var descriptionText = string.IsNullOrEmpty(selectedTag.Description)
                ? $"No description available for {selectedTag.Id}"
                : selectedTag.Description;

            if (!string.IsNullOrEmpty(selectedTag.Category))
            {
                descriptionText = $"[{selectedTag.Category}] {descriptionText}";
            }

            TagDescription.Text = descriptionText;
        }
        else
        {
            TagDescription.Text = "Select a tag to see its description";
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        // Get the cleaned tags value
        var tagsValue = CurrentTagsBox.Text?.Trim() ?? "";

        // Normalize: remove extra spaces, ensure consistent comma separation
        var tags = tagsValue
            .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();

        tagsValue = string.Join(", ", tags);

        Console.WriteLine($"[TagEditor] Saving tags for {_entryId}: '{tagsValue}'");

        // Find the row and update the tags field
        var row = _viewModel.DisplayRows.FirstOrDefault(r =>
            r["id"]?.Equals(_entryId, StringComparison.OrdinalIgnoreCase) == true);

        if (row != null)
        {
            row[_fieldName] = tagsValue;
            _viewModel.MarkAsModified();
            _viewModel.RequestCellRefresh();
            Console.WriteLine($"[TagEditor] Save successful");
            Close(true);
        }
        else
        {
            Console.WriteLine($"[TagEditor] Could not find row with id '{_entryId}'");
            Close(false);
        }
    }
}

/// <summary>
/// Represents a tag assigned to a string.
/// </summary>
public class TagItem
{
    public required string Name { get; set; }
    public string Description { get; set; } = "";
}

/// <summary>
/// Represents a tag definition from tor_tags.xml.
/// </summary>
public class TagDefinition
{
    public required string Id { get; set; }
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
}