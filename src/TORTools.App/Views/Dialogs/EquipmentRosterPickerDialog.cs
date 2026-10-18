using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TORTools.App.Views.Dialogs;

/// <summary>
/// Picker for a lord/troop's equipment roster that lets the user mirror the
/// civilian loadout against combat (default) or diverge it with its own
/// roster id. Caller reads <see cref="ResultCombatId"/> and
/// <see cref="ResultCivilianId"/> after <c>ShowDialog&lt;string?&gt;</c>
/// resolves (the Task result is combat, used as a non-null marker).
/// </summary>
public class EquipmentRosterPickerDialog : BaseEditorDialog
{
    private readonly List<string> _availableRosters;
    private readonly string _initialCombat;
    private readonly string _initialCivilian;

    private CheckBox? _mirrorCheck;
    private TextBox? _combatBox;
    private TextBox? _civilianBox;
    private ListBox? _combatList;
    private ListBox? _civilianList;
    private TextBox? _combatSearch;
    private TextBox? _civilianSearch;

    public string? ResultCombatId { get; private set; }
    public string? ResultCivilianId { get; private set; }

    public EquipmentRosterPickerDialog(
        string title,
        string? currentCombat,
        string? currentCivilian,
        List<string> availableRosters)
        : base(title, 540, 640)
    {
        _initialCombat = currentCombat ?? "";
        _initialCivilian = currentCivilian ?? "";
        _availableRosters = availableRosters;
        Initialize();
    }

    protected override void BuildContent()
    {
        bool startMirrored = string.IsNullOrEmpty(_initialCivilian)
            || _initialCivilian == _initialCombat;

        _mirrorCheck = new CheckBox
        {
            Content = "Mirror combat set to civilian",
            IsChecked = startMirrored,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = Brushes.White
        };
        _mirrorCheck.IsCheckedChanged += (_, _) => ApplyMirrorState();
        MainStack.Children.Add(_mirrorCheck);

        MainStack.Children.Add(CreateLabel("Combat Equipment Set:"));
        _combatBox = new TextBox { Text = _initialCombat, Height = 30 };
        MainStack.Children.Add(_combatBox);
        _combatSearch = new TextBox { PlaceholderText = "Filter combat rosters..." };
        MainStack.Children.Add(_combatSearch);
        _combatList = new ListBox { Height = 160, ItemsSource = _availableRosters };
        MainStack.Children.Add(_combatList);
        WireFilter(_combatSearch, _combatList);
        _combatList.DoubleTapped += (_, _) =>
        {
            if (_combatList.SelectedItem is string pick)
            {
                _combatBox.Text = pick;
                if (_mirrorCheck.IsChecked == true && _civilianBox != null)
                    _civilianBox.Text = pick;
            }
        };
        _combatBox.TextChanged += (_, _) =>
        {
            if (_mirrorCheck?.IsChecked == true && _civilianBox != null)
                _civilianBox.Text = _combatBox.Text;
        };

        MainStack.Children.Add(CreateSectionLabel("Civilian Equipment Set:"));
        _civilianBox = new TextBox
        {
            Text = startMirrored ? _initialCombat : _initialCivilian,
            Height = 30
        };
        MainStack.Children.Add(_civilianBox);
        _civilianSearch = new TextBox { PlaceholderText = "Filter civilian rosters..." };
        MainStack.Children.Add(_civilianSearch);
        _civilianList = new ListBox { Height = 160, ItemsSource = _availableRosters };
        MainStack.Children.Add(_civilianList);
        WireFilter(_civilianSearch, _civilianList);
        _civilianList.DoubleTapped += (_, _) =>
        {
            if (_civilianList.SelectedItem is string pick)
                _civilianBox.Text = pick;
        };

        ApplyMirrorState();
    }

    private void WireFilter(TextBox search, ListBox list)
    {
        search.TextChanged += (_, _) =>
        {
            var q = search.Text ?? "";
            list.ItemsSource = string.IsNullOrEmpty(q)
                ? _availableRosters
                : _availableRosters
                    .Where(id => id.Contains(q, StringComparison.OrdinalIgnoreCase))
                    .ToList();
        };
    }

    private void ApplyMirrorState()
    {
        bool mirror = _mirrorCheck?.IsChecked == true;
        if (_civilianBox != null)
        {
            _civilianBox.IsEnabled = !mirror;
            if (mirror && _combatBox != null)
                _civilianBox.Text = _combatBox.Text;
        }
        if (_civilianSearch != null) _civilianSearch.IsEnabled = !mirror;
        if (_civilianList != null) _civilianList.IsEnabled = !mirror;
    }

    protected override string? GetResultValue()
    {
        var combat = _combatBox?.Text?.Trim() ?? "";
        bool mirror = _mirrorCheck?.IsChecked == true;
        var civilian = mirror ? combat : (_civilianBox?.Text?.Trim() ?? "");

        ResultCombatId = string.IsNullOrEmpty(combat) ? null : combat;
        ResultCivilianId = string.IsNullOrEmpty(civilian) ? null : civilian;

        // Return non-null when the user intends a value so the Task<string?>
        // signals "OK pressed"; the caller reads both result properties.
        return ResultCombatId;
    }
}
