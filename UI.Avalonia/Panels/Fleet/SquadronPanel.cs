using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Contracts;

namespace NMSE.UI.Avalonia.Panels.Fleet;

public sealed class SquadronPanel : UserControl, ISaveEditorPanel, IGoToJsonSource
{
    private readonly ObservableCollection<string> _pilotNames = [];
    private readonly ListBox _list = new() { Width = 260 };
    private readonly TextBlock _title = new() { FontSize = 20, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _count = new() { Foreground = Brushes.Gray };
    private readonly ComboBox _race = new();
    private readonly ComboBox _rank = new();
    private readonly ComboBox _shipType = new();
    private readonly TextBox _npcSeed = new() { MinWidth = 220 };
    private readonly TextBox _shipSeed = new() { MinWidth = 220 };
    private readonly TextBox _traitsSeed = new() { MinWidth = 220 };
    private readonly TextBox _npcResource = new() { IsReadOnly = true };
    private readonly TextBox _shipResource = new() { IsReadOnly = true };
    private readonly CheckBox _unlocked = new();
    private readonly Button _delete = new();
    private readonly Button _export = new();
    private readonly Button _import = new();
    private readonly Button[] _seedButtons = [new(), new(), new()];
    private readonly TextBlock[] _labels = new TextBlock[10];
    private readonly Border _details = new() { Padding = new Thickness(10) };
    private readonly TextBlock _status = new() { Foreground = Brushes.Gray };
    private JsonArray? _pilots;
    private JsonArray? _unlockedSlots;
    private bool _loading;

    public event EventHandler? DataModified;
    public event EventHandler<GoToJsonRequestEventArgs>? GoToJsonRequested;
    public Func<FleetTransferRequest, CancellationToken, Task<FleetTransferContent?>>? ImportAsync { get; set; }
    public Func<FleetTransferRequest, string, CancellationToken, Task<bool>>? ExportAsync { get; set; }
    public Func<string, string, CancellationToken, Task<bool>>? ConfirmAsync { get; set; }

    public SquadronPanel()
    {
        _list.ItemsSource = _pilotNames;
        _list.SelectionChanged += (_, _) => LoadSelected();
        _race.ItemsSource = SquadronLogic.PilotRaces.Select(SquadronLogic.GetLocalisedPilotRaceName).ToArray();
        _rank.ItemsSource = SquadronLogic.PilotRanks;
        _shipType.ItemsSource = SquadronLogic.GetShipTypeItems();
        _race.SelectionChanged += (_, _) => ChangeRace();
        _rank.SelectionChanged += (_, _) => ChangeRank();
        _shipType.SelectionChanged += (_, _) => ChangeShipType();
        _unlocked.IsCheckedChanged += (_, _) => ChangeUnlocked();
        _delete.Click += async (_, _) => await DeleteAsync();
        _export.Click += async (_, _) => await ExportPilotAsync();
        _import.Click += async (_, _) => await ImportPilotAsync();
        _seedButtons[0].Click += (_, _) => { _npcSeed.Text = FleetUi.NewSeed(); CommitSeeds(); };
        _seedButtons[1].Click += (_, _) => { _shipSeed.Text = FleetUi.NewSeed(); CommitSeeds(); };
        _seedButtons[2].Click += (_, _) => { _traitsSeed.Text = FleetUi.NewSeed(); CommitSeeds(); };
        foreach (TextBox box in new[] { _npcSeed, _shipSeed, _traitsSeed }) box.LostFocus += (_, _) => CommitSeeds();
        Content = BuildLayout();
        ApplyUiLocalisation();
    }

    private Control BuildLayout()
    {
        var nav = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 4,
            Children =
            {
                FleetUi.JsonButton("Squadron JSON", () => RequestGoTo("PlayerStateData.SquadronPilots")),
                FleetUi.JsonButton("Selected pilot JSON", () =>
                {
                    if (_list.SelectedIndex >= 0) RequestGoTo($"PlayerStateData.SquadronPilots[{_list.SelectedIndex}]");
                })
            }
        };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(_title); Grid.SetColumn(nav, 1); header.Children.Add(nav);

        var form = FleetUi.FormGrid();
        _labels[0] = FleetUi.AddHeader(form, 0, "Pilot Info");
        _labels[1] = FleetUi.AddRow(form, 1, "Race:", _race);
        _labels[2] = FleetUi.AddRow(form, 2, "Rank:", _rank);
        _labels[3] = FleetUi.AddRow(form, 3, "Ship Type:", _shipType);
        _labels[4] = FleetUi.AddRow(form, 4, "NPC Seed:", FleetUi.SeedEditor(_npcSeed, _seedButtons[0]));
        _labels[5] = FleetUi.AddRow(form, 5, "Ship Seed:", FleetUi.SeedEditor(_shipSeed, _seedButtons[1]));
        _labels[6] = FleetUi.AddRow(form, 6, "Traits Seed:", FleetUi.SeedEditor(_traitsSeed, _seedButtons[2]));
        _labels[7] = FleetUi.AddRow(form, 7, "NPC Resource:", _npcResource);
        _labels[8] = FleetUi.AddRow(form, 8, "Ship Resource:", _shipResource);
        _labels[9] = FleetUi.AddRow(form, 9, "Slot Unlocked:", _unlocked);
        _details.Child = form;

        var actions = new WrapPanel { Children = { _delete, _export, _import, _count } };
        var left = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        left.Children.Add(_list); Grid.SetRow(actions, 1); left.Children.Add(actions);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        body.Children.Add(left); Grid.SetColumn(_details, 1); body.Children.Add(_details);
        var root = new Grid { Margin = new Thickness(10), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 8 };
        root.Children.Add(header); Grid.SetRow(body, 1); root.Children.Add(body); Grid.SetRow(_status, 2); root.Children.Add(_status);
        return root;
    }

    public void LoadData(JsonObject saveData)
    {
        _loading = true;
        try
        {
            JsonObject? player = saveData.GetObject("PlayerStateData");
            _pilots = player?.GetArray("SquadronPilots");
            _unlockedSlots = player?.GetArray("SquadronUnlockedPilotSlots");
            RefreshList();
            _list.SelectedIndex = _pilots is { Length: > 0 } ? 0 : -1;
            LoadSelected();
        }
        finally { _loading = false; }
    }

    public void SaveData(JsonObject saveData) => CommitSeeds(force: true);

    private JsonObject? SelectedPilot()
    {
        int index = _list.SelectedIndex;
        return _pilots != null && index >= 0 && index < _pilots.Length ? _pilots.GetObject(index) : null;
    }

    private void RefreshList()
    {
        int selected = _list.SelectedIndex;
        _pilotNames.Clear();
        if (_pilots != null)
            for (int i = 0; i < _pilots.Length; i++)
                _pilotNames.Add(SquadronLogic.GetPilotDisplayName(_pilots.GetObject(i), i));
        _count.Text = _pilots == null || _pilots.Length == 0
            ? UiStrings.Get("squadron.no_pilots_found")
            : UiStrings.Format("squadron.total_pilots", _pilots.Length);
        if (selected >= 0 && selected < _pilotNames.Count) _list.SelectedIndex = selected;
    }

    private void LoadSelected()
    {
        JsonObject? pilot = SelectedPilot();
        _details.IsVisible = pilot != null;
        if (pilot == null) return;
        _loading = true;
        try
        {
            _race.SelectedIndex = Array.IndexOf(SquadronLogic.PilotRaces, SquadronLogic.GetPilotRace(pilot));
            _rank.SelectedIndex = Math.Clamp(TryInt(pilot, "PilotRank"), 0, 3);
            string shipType = SquadronLogic.GetShipType(pilot);
            _shipType.SelectedItem = _shipType.Items.Cast<object?>().OfType<StarshipLogic.ShipTypeItem>()
                .FirstOrDefault(item => item.InternalName.Equals(shipType, StringComparison.OrdinalIgnoreCase));
            _npcSeed.Text = SquadronLogic.ReadSeed(pilot, "NPCResource");
            _shipSeed.Text = SquadronLogic.ReadSeed(pilot, "ShipResource");
            _traitsSeed.Text = pilot.GetString("TraitsSeed") ?? string.Empty;
            _npcResource.Text = pilot.GetObject("NPCResource")?.GetString("Filename") ?? string.Empty;
            _shipResource.Text = pilot.GetObject("ShipResource")?.GetString("Filename") ?? string.Empty;
            int index = _list.SelectedIndex;
            _unlocked.IsChecked = _unlockedSlots != null && index < _unlockedSlots.Length && _unlockedSlots.GetBool(index);
        }
        finally { _loading = false; }
    }

    private void ChangeRace()
    {
        if (_loading || SelectedPilot() is not { } pilot || _race.SelectedIndex < 0) return;
        SquadronLogic.SetPilotRace(pilot, SquadronLogic.PilotRaces[_race.SelectedIndex]);
        _npcResource.Text = pilot.GetObject("NPCResource")?.GetString("Filename") ?? string.Empty;
        ModifiedAndRefresh();
    }

    private void ChangeRank()
    {
        if (_loading || SelectedPilot() is not { } pilot || _rank.SelectedIndex < 0) return;
        pilot.Set("PilotRank", _rank.SelectedIndex); ModifiedAndRefresh();
    }

    private void ChangeShipType()
    {
        if (_loading || SelectedPilot() is not { } pilot || _shipType.SelectedItem is not StarshipLogic.ShipTypeItem item) return;
        if (SquadronLogic.ShipTypeToResource.TryGetValue(item.InternalName, out string? resource))
        {
            pilot.GetObject("ShipResource")?.Set("Filename", resource);
            _shipResource.Text = resource;
        }
        ModifiedAndRefresh();
    }

    private void ChangeUnlocked()
    {
        int index = _list.SelectedIndex;
        if (_loading || _unlockedSlots == null || index < 0 || index >= _unlockedSlots.Length) return;
        _unlockedSlots.Set(index, _unlocked.IsChecked == true); RaiseModified();
    }

    private void CommitSeeds(bool force = false)
    {
        if ((_loading && !force) || SelectedPilot() is not { } pilot) return;
        SquadronLogic.WriteSeed(pilot, "NPCResource", _npcSeed.Text ?? string.Empty);
        SquadronLogic.WriteSeed(pilot, "ShipResource", _shipSeed.Text ?? string.Empty);
        string? traits = SeedHelper.NormalizeSeed(_traitsSeed.Text);
        if (traits != null) pilot.Set("TraitsSeed", traits);
        if (!force) RaiseModified();
    }

    private async Task DeleteAsync()
    {
        JsonObject? pilot = SelectedPilot();
        if (pilot == null || !await ConfirmOperationAsync(UiStrings.Get("squadron.delete_title"), UiStrings.Get("squadron.delete_confirm"))) return;
        SquadronLogic.DeletePilot(pilot); ModifiedAndRefresh(); LoadSelected();
    }

    private async Task ExportPilotAsync()
    {
        JsonObject? pilot = SelectedPilot();
        if (pilot == null || ExportAsync == null) { await ShowErrorAsync(UiStrings.Get("common.error")); return; }
        string extension = ExportConfig.Instance.SquadronExt;
        var vars = new Dictionary<string, string>
        {
            ["race"] = _race.SelectedIndex >= 0 ? SquadronLogic.PilotRaces[_race.SelectedIndex] : UiStrings.Get("common.unknown"),
            ["type"] = (_shipType.SelectedItem as StarshipLogic.ShipTypeItem)?.InternalName ?? UiStrings.Get("common.unknown"),
            ["rank"] = _rank.SelectedItem?.ToString() ?? "C", ["seed"] = _npcSeed.Text ?? string.Empty
        };
        string file = ExportConfig.BuildFileName(ExportConfig.Instance.SquadronTemplate, extension, vars);
        await ExportAsync(new FleetTransferRequest(FleetTransferKind.SquadronPilot, UiStrings.Get("squadron.export"), file, [extension, ".sqd"]),
            pilot.ToDisplayString(), CancellationToken.None);
    }

    private async Task ImportPilotAsync()
    {
        if (_pilots == null || ImportAsync == null) { await ShowErrorAsync(UiStrings.Get("common.error")); return; }
        string extension = ExportConfig.Instance.SquadronExt;
        FleetTransferContent? content = await ImportAsync(new FleetTransferRequest(FleetTransferKind.SquadronPilot,
            UiStrings.Get("squadron.import_title"), "pilot" + extension, [extension, ".sqd"]), CancellationToken.None);
        if (content == null) return;
        try
        {
            JsonObject imported = InventoryImportHelper.UnwrapNomNomPilot(JsonObject.Parse(content.JsonText));
            int targetIndex = Enumerable.Range(0, _pilots.Length).FirstOrDefault(i =>
            {
                string value = _pilots.GetObject(i).GetObject("NPCResource")?.GetString("Filename") ?? string.Empty;
                return string.IsNullOrEmpty(value) || value == "^";
            }, -1);
            if (targetIndex < 0) { await ShowErrorAsync(UiStrings.Get("squadron.no_empty_slot")); return; }
            JsonObject target = _pilots.GetObject(targetIndex);
            foreach (string key in imported.Names()) target.Set(key, imported.Get(key));
            RefreshList(); _list.SelectedIndex = targetIndex; LoadSelected(); RaiseModified();
        }
        catch (Exception ex) { await ShowErrorAsync(UiStrings.Format("common.import_failed", ex.Message)); }
    }

    public void ApplyUiLocalisation()
    {
        bool wasLoading = _loading;
        _loading = true;
        try
        {
        _title.Text = UiStrings.Get("squadron.pilots_title");
        _delete.Content = UiStrings.Get("common.delete"); _export.Content = UiStrings.Get("squadron.export"); _import.Content = UiStrings.Get("squadron.import");
        string[] keys = ["squadron.pilot_info", "squadron.race", "squadron.rank", "squadron.ship_type", "squadron.npc_seed",
            "squadron.ship_seed", "squadron.traits_seed", "squadron.npc_resource", "squadron.ship_resource", "squadron.slot_unlocked"];
        for (int i = 0; i < keys.Length; i++) _labels[i].Text = UiStrings.Get(keys[i]);
        foreach (Button button in _seedButtons) button.Content = UiStrings.Get("common.generate");
        int race = _race.SelectedIndex; string? type = (_shipType.SelectedItem as StarshipLogic.ShipTypeItem)?.InternalName;
        _race.ItemsSource = SquadronLogic.PilotRaces.Select(SquadronLogic.GetLocalisedPilotRaceName).ToArray(); _race.SelectedIndex = race;
        _shipType.ItemsSource = SquadronLogic.GetShipTypeItems();
        _shipType.SelectedItem = _shipType.Items.Cast<object?>().OfType<StarshipLogic.ShipTypeItem>().FirstOrDefault(x => x.InternalName == type);
            RefreshList();
        }
        finally { _loading = wasLoading; }
    }

    private void ModifiedAndRefresh() { RaiseModified(); RefreshList(); }
    private void RequestGoTo(string path) => GoToJsonRequested?.Invoke(this, new GoToJsonRequestEventArgs(path));
    private void RaiseModified() { if (!_loading) DataModified?.Invoke(this, EventArgs.Empty); }
    private static int TryInt(JsonObject obj, string key) { try { return obj.GetInt(key); } catch { return 0; } }
    private async Task<bool> ConfirmOperationAsync(string title, string message)
    {
        if (ConfirmAsync != null) return await ConfirmAsync(title, message, CancellationToken.None);
        return TopLevel.GetTopLevel(this) is Window owner && await FleetUi.ConfirmAsync(owner, title, message);
    }
    private async Task ShowErrorAsync(string message)
    {
        _status.Text = message;
        if (TopLevel.GetTopLevel(this) is Window owner) await FleetUi.MessageAsync(owner, UiStrings.Get("common.error"), message);
    }
}
