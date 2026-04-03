using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TORTools.App.ViewModels;
using TORTools.Core.Services;

namespace TORTools.App.Views.Dialogs;

/// <summary>
/// Dialog for editing abilities with icon display.
/// </summary>
public class AbilityEditorDialog : BaseEditorDialog
{
    private readonly List<string> _availableAbilities;
    private readonly FileTabViewModel _viewModel;
    private readonly List<string> _selectedAbilities;
    private ListBox? _selectedListBox;
    private TextBox? _searchBox;

    public AbilityEditorDialog(string currentValue, List<string> availableAbilities, FileTabViewModel viewModel)
        : base("Edit Abilities", 550, 550)
    {
        _availableAbilities = availableAbilities;
        _viewModel = viewModel;
        _selectedAbilities = string.IsNullOrEmpty(currentValue)
            ? new List<string>()
            : currentValue.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrEmpty(x))
                .ToList();

        // Initialize after fields are set
        Initialize();
    }

    protected override void BuildContent()
    {
        // Selected abilities section
        MainStack.Children.Add(CreateLabel("Selected Abilities:"));

        _selectedListBox = new ListBox
        {
            Height = 120,
            ItemTemplate = CreateAbilityItemTemplate(),
            ItemsSource = _selectedAbilities.ToList()
        };
        MainStack.Children.Add(_selectedListBox);

        // Remove button
        var removeButton = new Button
        {
            Content = "Remove Selected",
            Padding = new Thickness(12, 4),
            Margin = new Thickness(0, 4, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(180, 60, 60)),
            Foreground = Brushes.White
        };
        removeButton.Click += (s, e) =>
        {
            if (_selectedListBox.SelectedItem is string selected)
            {
                _selectedAbilities.Remove(selected);
                _selectedListBox.ItemsSource = _selectedAbilities.ToList();
            }
        };
        MainStack.Children.Add(removeButton);

        // Available abilities section
        MainStack.Children.Add(CreateSectionLabel("Available Abilities (double-click to add):"));

        _searchBox = CreateSearchBox();
        MainStack.Children.Add(_searchBox);

        var availableListBox = new ListBox
        {
            Height = 200,
            ItemTemplate = CreateAbilityItemTemplate(),
            ItemsSource = GetFilteredAvailable("")
        };
        MainStack.Children.Add(availableListBox);

        // Filter on search
        _searchBox.TextChanged += (s, e) =>
        {
            availableListBox.ItemsSource = GetFilteredAvailable(_searchBox.Text ?? "");
        };

        // Double-click to add
        availableListBox.DoubleTapped += (s, e) =>
        {
            if (availableListBox.SelectedItem is string selected && !_selectedAbilities.Contains(selected))
            {
                _selectedAbilities.Add(selected);
                _selectedListBox.ItemsSource = _selectedAbilities.ToList();
                availableListBox.ItemsSource = GetFilteredAvailable(_searchBox?.Text ?? "");
            }
        };
    }

    private List<string> GetFilteredAvailable(string searchText)
    {
        var selectedSet = new HashSet<string>(_selectedAbilities, StringComparer.OrdinalIgnoreCase);
        return _availableAbilities
            .Where(id => !selectedSet.Contains(id))
            .Where(id => string.IsNullOrEmpty(searchText) ||
                         id.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            .Take(50)
            .ToList();
    }

    private IDataTemplate CreateAbilityItemTemplate()
    {
        return new FuncDataTemplate<string>((abilityId, _) =>
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(4, 2)
            };

            // Try to get ability icon
            string? iconPath = null;
            string displayName = abilityId ?? "";

            if (!string.IsNullOrEmpty(abilityId) && _viewModel.AbilityCatalogService != null && _viewModel.IconService != null)
            {
                var abilityInfo = _viewModel.AbilityCatalogService.GetAbility(abilityId);
                if (abilityInfo != null)
                {
                    // Get icon
                    if (!string.IsNullOrEmpty(abilityInfo.SpriteName))
                    {
                        iconPath = _viewModel.IconService.GetIconPath(abilityInfo.SpriteName);
                    }
                    // Use ability name if available
                    if (!string.IsNullOrEmpty(abilityInfo.Name))
                    {
                        // Strip localization key if present
                        var name = abilityInfo.Name;
                        if (name.StartsWith("{=") && name.Contains("}"))
                        {
                            name = name.Substring(name.IndexOf("}") + 1);
                        }
                        displayName = $"{name} ({abilityId})";
                    }
                }
            }

            // Add icon if available
            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
            {
                try
                {
                    var icon = new Image
                    {
                        Width = 24,
                        Height = 24,
                        Source = new Bitmap(iconPath),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    panel.Children.Add(icon);
                }
                catch
                {
                    // Icon loading failed, continue without it
                }
            }
            else
            {
                // Placeholder for alignment
                var placeholder = new Border
                {
                    Width = 24,
                    Height = 24,
                    Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                    CornerRadius = new CornerRadius(4)
                };
                panel.Children.Add(placeholder);
            }

            // Add text
            var text = new TextBlock
            {
                Text = displayName,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White
            };
            panel.Children.Add(text);

            return panel;
        });
    }

    protected override string? GetResultValue()
    {
        return _selectedAbilities.Count > 0
            ? string.Join(", ", _selectedAbilities)
            : "";
    }
}
