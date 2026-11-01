using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Dialogs;
using NMSE.UI.Avalonia.Platform;

namespace NMSE.UI.Avalonia.Panels.Player;

/// <summary>Edits the game's ten quick-menu bindings in each of its three contexts.</summary>
public sealed class HotkeysPanel : UserControl
{
    private sealed record ActionChoice(string Name, string Label, HotActionParameterKind Kind)
    {
        public override string ToString() => Label;
    }

    private readonly IDesktopServices? _desktop;
    private readonly ComboBox[,] _actions = new ComboBox[3, 10];
    private readonly Button[,] _parameters = new Button[3, 10];
    private readonly TextBlock[] _contextLabels = new TextBlock[3];
    private readonly Button _export = new() { Name = "ExportHotkeys" };
    private readonly Button _import = new() { Name = "ImportHotkeys" };
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private JsonObject? _player;
    private bool _loading;
    public LocalisationService? Localisation { get; set; }
    public event EventHandler? DataModified;

    public HotkeysPanel(IDesktopServices? desktop = null)
    {
        _desktop = desktop;
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 16 };
        for (int c = 0; c < 3; c++)
        {
            var rows = new StackPanel { Spacing = 6 };
            rows.Children.Add(_contextLabels[c] = new TextBlock { FontWeight = FontWeight.SemiBold });
            for (int s = 0; s < 10; s++)
            {
                int context = c, slot = s;
                var combo = _actions[c, s] = new ComboBox { Name = $"HotkeyAction{c}_{s}", HorizontalAlignment = HorizontalAlignment.Stretch };
                var parameters = _parameters[c, s] = new Button { Name = $"HotkeyParameters{c}_{s}", Content = "…", IsEnabled = false };
                combo.SelectionChanged += (_, _) => OnActionChanged(context, slot);
                parameters.Click += async (_, _) => await EditParametersAsync(context, slot);
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6 };
                row.Children.Add(new TextBlock { Text = $"Ctrl+{s}", VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(combo); Grid.SetColumn(combo, 1);
                row.Children.Add(parameters); Grid.SetColumn(parameters, 2);
                rows.Children.Add(row);
            }
            columns.Children.Add(rows); Grid.SetColumn(rows, c);
        }
        _export.Click += async (_, _) => await ExportAsync();
        _import.Click += async (_, _) => await ImportAsync();
        Content = new ScrollViewer
        {
            Content = new StackPanel { Margin = new Thickness(12), Spacing = 12, Children =
            {
                columns,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _export, _import } },
                _hint, _status,
            } },
        };
        ApplyUiLocalisation();
    }

    public void LoadData(JsonObject? player)
    {
        _player = player;
        _status.Text = string.Empty;
        ApplyUiLocalisation();
    }

    public void ApplyUiLocalisation()
    {
        _loading = true;
        try
        {
            string[] labels = ["on_foot", "in_ship", "in_exocraft"];
            for (int c = 0; c < 3; c++)
            {
                _contextLabels[c].Text = UiStrings.Get("hotkeys.context_" + labels[c]);
                for (int s = 0; s < 10; s++)
                {
                    string current = HotActionsLogic.GetActionName(GetSlot(c, s));
                    var choices = HotActionsLogic.Actions.Select(action => new ActionChoice(action.Name,
                        HotActionsLogic.GetDisplayName(action, Localisation), action.ParameterKind)).ToList();
                    // An action added by a later game version must survive viewing and saving.
                    if (!choices.Any(choice => choice.Name == current))
                        choices.Add(new(current, current, HotActionParameterKind.None));
                    _actions[c, s].ItemsSource = choices;
                    _actions[c, s].SelectedItem = choices.First(choice => choice.Name == current);
                    UpdateParameters(c, s);
                }
            }
            _export.Content = UiStrings.Get("common.export");
            _import.Content = UiStrings.Get("common.import");
            _export.IsEnabled = _import.IsEnabled = _player != null && _desktop != null;
            _hint.Text = UiStrings.Get("hotkeys.hint");
        }
        finally { _loading = false; }
    }

    private JsonObject? GetSlot(int context, int slot)
    {
        var hot = _player?.GetArray("HotActions");
        var keys = hot != null && context < hot.Length ? hot.GetObject(context)?.GetArray("KeyActions") : null;
        return keys != null && slot < keys.Length ? keys.Get(slot) as JsonObject : null;
    }

    private void OnActionChanged(int context, int slot)
    {
        if (_loading || _player == null || _actions[context, slot].SelectedItem is not ActionChoice choice) return;
        if (HotActionsLogic.GetActionName(GetSlot(context, slot)) == choice.Name) return;
        var slots = HotActionsLogic.EnsureSlots(HotActionsLogic.EnsureContext(_player, context));
        HotActionsLogic.SetSlot(slots.GetObject(slot), choice.Name);
        UpdateParameters(context, slot);
        DataModified?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateParameters(int context, int slot)
    {
        var choice = _actions[context, slot].SelectedItem as ActionChoice;
        var button = _parameters[context, slot];
        button.IsEnabled = _player != null && choice?.Kind is not null and not HotActionParameterKind.None;
        var current = GetSlot(context, slot);
        int number = HotActionsLogic.GetNumber(current);
        var (x, y) = HotActionsLogic.GetInventoryIndex(current);
        string summary = choice?.Kind switch
        {
            HotActionParameterKind.InventoryItem => UiStrings.Format("hotkeys.summary_slot", number, x, y),
            HotActionParameterKind.EmoteId => HotActionsLogic.GetId(current),
            HotActionParameterKind.CreatureFood => UiStrings.Format("hotkeys.summary_food", number, HotActionsLogic.GetId(current)),
            HotActionParameterKind.Index or HotActionParameterKind.WeaponMode => number.ToString(CultureInfo.CurrentCulture),
            _ => string.Empty,
        };
        ToolTip.SetTip(button, summary);
    }

    private async Task EditParametersAsync(int context, int slot)
    {
        if (_player == null || _actions[context, slot].SelectedItem is not ActionChoice choice
            || TopLevel.GetTopLevel(this) is not Window owner) return;
        var current = GetSlot(context, slot);
        var value = await HotActionParametersWindow.ShowAsync(owner, choice.Label, choice.Kind, current);
        if (value == null) return;
        var slots = HotActionsLogic.EnsureSlots(HotActionsLogic.EnsureContext(_player, context));
        HotActionsLogic.SetSlot(slots.GetObject(slot), choice.Name, value.Id, value.Number, value.X, value.Y);
        UpdateParameters(context, slot);
        DataModified?.Invoke(this, EventArgs.Empty);
    }

    private async Task ExportAsync()
    {
        if (_player == null || _desktop == null) return;
        var config = ExportConfig.Instance;
        string name = ExportConfig.BuildFileName(config.HotkeysTemplate, config.HotkeysExt, new Dictionary<string, string> { ["name"] = "Hotkeys" });
        string? path = await _desktop.PickSaveFileAsync(UiStrings.Get("common.export"), name, config.HotkeysExt.TrimStart('.'),
            new DesktopFileType("Hotkeys", "*" + config.HotkeysExt));
        if (path == null) return;
        try { HotActionsLogic.BuildExport(_player).ExportToFile(path); }
        catch (Exception ex) { await _desktop.ShowErrorAsync(UiStrings.Get("common.error"), UiStrings.Format("discovery.export_failed", ex.Message)); }
    }

    private async Task ImportAsync()
    {
        if (_player == null || _desktop == null) return;
        string? path = (await _desktop.PickFilesAsync(UiStrings.Get("common.import"), false,
            new DesktopFileType("Hotkeys", "*" + ExportConfig.Instance.HotkeysExt, "*.json"))).FirstOrDefault();
        if (path == null) return;
        try
        {
            int applied = HotActionsLogic.ApplyImport(_player, JsonObject.ImportFromFile(path));
            LoadData(_player);
            _status.Text = UiStrings.Get("discovery.import_no_items");
            if (applied > 0)
            {
                _status.Text = UiStrings.Format("hotkeys.import_success", applied);
                DataModified?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex) { await _desktop.ShowErrorAsync(UiStrings.Get("common.error"), UiStrings.Format("discovery.import_failed", ex.Message)); }
    }
}
