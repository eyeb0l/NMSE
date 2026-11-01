using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Contracts;
using NMSE.UI.Avalonia.Controls;
using NMSE.UI.Avalonia.Inventory;
using NMSE.UI.Avalonia.Services;

namespace NMSE.UI.Avalonia.Panels.Fleet;

public sealed class FreighterPanel : UserControl, ISaveEditorPanel, IGoToJsonSource
{
    private readonly TextBlock _title = new() { FontSize = 20, FontWeight = FontWeight.SemiBold };
    private readonly TextBox _name = new();
    private readonly ComboBox _type = new();
    private readonly ComboBox _class = new();
    private readonly TextBox _homeSeed = new() { MinWidth = 210 };
    private readonly ComboBox _crewRace = new();
    private readonly InvariantNumericTextBox _hyperdrive = new();
    private readonly InvariantNumericTextBox _fleetCoordination = new();
    private readonly TextBox _baseItems = new() { IsReadOnly = true };
    private readonly TextBox _modelSeed = new() { MinWidth = 210 };
    private readonly TextBox _crewSeed = new() { MinWidth = 210 };
    private readonly Button _homeGenerate = new();
    private readonly Button _modelGenerate = new();
    private readonly Button _crewGenerate = new();
    private readonly Button _export = new();
    private readonly Button _import = new();
    private readonly TextBlock _status = new() { Foreground = Brushes.Gray };
    private readonly ListBox _rooms = new();
    private readonly TabItem _detailsTab = new();
    private readonly TabItem _roomsTab = new();
    private readonly TabItem _cargoTab = new();
    private readonly TabItem _technologyTab = new();
    private readonly TextBlock[] _labels = new TextBlock[10];
    private readonly InventoryGrid _cargo = new();
    private readonly InventoryGrid _technology = new();

    private JsonObject? _playerState;
    private JsonObject? _freighterBase;
    private Dictionary<string, double>? _rawStats;
    private int _originalClassIndex = -1;
    private bool _loading;

    public event EventHandler? DataModified;
    public event EventHandler<GoToJsonRequestEventArgs>? GoToJsonRequested;
    public Func<FleetTransferRequest, CancellationToken, Task<FleetTransferContent?>>? ImportAsync { get; set; }
    public Func<FleetTransferRequest, string, CancellationToken, Task<bool>>? ExportAsync { get; set; }
    public Func<string, string, CancellationToken, Task<bool>>? ConfirmAsync { get; set; }

    public FreighterPanel(GameItemDatabase database, AvaloniaIconProvider? icons)
    {
        _type.ItemsSource = FreighterLogic.GetFreighterTypeItems();
        _class.ItemsSource = FreighterLogic.FreighterClasses;
        _crewRace.ItemsSource = FreighterLogic.GetCrewRaceItems();
        ConfigureInventory(_cargo, database, icons, false, FleetTransferKind.FreighterCargo);
        ConfigureInventory(_technology, database, icons, true, FleetTransferKind.FreighterTechnology);

        _homeGenerate.Click += (_, _) => { _homeSeed.Text = FleetUi.NewSeed(); RaiseModified(); };
        _modelGenerate.Click += (_, _) => { _modelSeed.Text = FleetUi.NewSeed(); RaiseModified(); };
        _crewGenerate.Click += (_, _) => { _crewSeed.Text = FleetUi.NewSeed(); RaiseModified(); };
        _export.Click += async (_, _) => await ExportFreighterAsync();
        _import.Click += async (_, _) => await ImportFreighterAsync();
        _cargo.DataModified += (_, _) => RaiseModified();
        _technology.DataModified += (_, _) => RaiseModified();

        foreach (TextBox box in new[] { _name, _homeSeed, _modelSeed, _crewSeed })
            box.TextChanged += (_, _) => RaiseModified();
        _type.SelectionChanged += (_, _) => RaiseModified();
        _class.SelectionChanged += (_, _) => RaiseModified();
        _crewRace.SelectionChanged += (_, _) => RaiseModified();
        _hyperdrive.NumericValueChanged += (_, _) => RaiseModified();
        _fleetCoordination.NumericValueChanged += (_, _) => RaiseModified();

        Content = BuildLayout();
        ApplyUiLocalisation();
    }

    private Control BuildLayout()
    {
        var jsonButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                FleetUi.JsonButton("Freighter JSON", () => RequestGoTo("PlayerStateData.CurrentFreighter")),
                FleetUi.JsonButton("Cargo JSON", () => RequestGoTo("PlayerStateData.FreighterInventory")),
                FleetUi.JsonButton("Technology JSON", () => RequestGoTo("PlayerStateData.FreighterInventory_TechOnly"))
            }
        };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(_title);
        Grid.SetColumn(jsonButtons, 1);
        header.Children.Add(jsonButtons);

        var form = FleetUi.FormGrid();
        _labels[0] = FleetUi.AddRow(form, 0, "Name:", _name);
        _labels[1] = FleetUi.AddRow(form, 1, "Type:", _type);
        _labels[2] = FleetUi.AddRow(form, 2, "Class:", _class);
        _labels[3] = FleetUi.AddRow(form, 3, "Home Seed:", FleetUi.SeedEditor(_homeSeed, _homeGenerate));
        _labels[4] = FleetUi.AddRow(form, 4, "Crew Race:", _crewRace);
        _labels[5] = FleetUi.AddRow(form, 5, "Hyperdrive:", _hyperdrive);
        _labels[6] = FleetUi.AddRow(form, 6, "Fleet Coordination:", _fleetCoordination);
        _labels[7] = FleetUi.AddRow(form, 7, "Items:", _baseItems);
        _labels[8] = FleetUi.AddRow(form, 8, "Model Seed:", FleetUi.SeedEditor(_modelSeed, _modelGenerate));
        _labels[9] = FleetUi.AddRow(form, 9, "Crew Seed:", FleetUi.SeedEditor(_crewSeed, _crewGenerate));

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { _export, _import, _status }
        };
        _cargoTab.Content = _cargo;
        _technologyTab.Content = _technology;
        var inventoryTabs = new TabControl
        {
            MinHeight = 420,
            ItemsSource = new[] { _cargoTab, _technologyTab }
        };
        var details = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 8 };
        details.Children.Add(form);
        Grid.SetRow(actions, 1); details.Children.Add(actions);
        Grid.SetRow(inventoryTabs, 2); details.Children.Add(inventoryTabs);
        _detailsTab.Content = new ScrollViewer { Content = details };
        _roomsTab.Content = _rooms;

        var tabs = new TabControl { ItemsSource = new[] { _detailsTab, _roomsTab } };
        var root = new Grid { Margin = new Thickness(10), RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 7 };
        root.Children.Add(header);
        Grid.SetRow(tabs, 1); root.Children.Add(tabs);
        return root;
    }

    private void ConfigureInventory(
        InventoryGrid grid,
        GameItemDatabase database,
        AvaloniaIconProvider? icons,
        bool tech,
        FleetTransferKind kind)
    {
        grid.SetDatabase(database);
        grid.SetIconProvider(icons);
        grid.SetInventoryGroup("Freighter");
        grid.SetInventoryOwnerType("Freighter");
        grid.SetIsTechInventory(tech);
        grid.SetIsCargoInventory(!tech);
        grid.SetSortingEnabled(true);
        grid.SetSuperchargeConstraints(4, tech ? 5 : 11);
        grid.ConfigureTransfer(
            async (inventory, suggested) =>
            {
                if (ExportAsync == null) { await ShowErrorAsync(UiStrings.Get("common.error")); return; }
                string extension = tech ? ExportConfig.Instance.FreighterTechExt : ExportConfig.Instance.FreighterCargoExt;
                await ExportAsync(new FleetTransferRequest(kind, UiStrings.Get("common.export"), suggested, [extension]),
                    inventory.ToDisplayString(), CancellationToken.None);
            },
            async suggested =>
            {
                if (ImportAsync == null) { await ShowErrorAsync(UiStrings.Get("common.error")); return null; }
                string extension = tech ? ExportConfig.Instance.FreighterTechExt : ExportConfig.Instance.FreighterCargoExt;
                FleetTransferContent? content = await ImportAsync(
                    new FleetTransferRequest(kind, UiStrings.Get("common.import"), suggested, [extension]), CancellationToken.None);
                return content == null ? null : JsonObject.Parse(content.JsonText);
            });
    }

    public void LoadData(JsonObject saveData)
    {
        _loading = true;
        try
        {
            _playerState = saveData.GetObject("PlayerStateData");
            if (_playerState == null) return;
            FreighterLogic.FreighterData data = FreighterLogic.LoadFreighterData(_playerState);
            _name.Text = data.Name;
            SelectByInternalName(_type, data.TypeDisplayName);
            _class.SelectedIndex = data.ClassIndex;
            _originalClassIndex = data.ClassIndex;
            _homeSeed.Text = data.HomeSeed;
            _modelSeed.Text = data.ModelSeed;
            _hyperdrive.SetValueWithText(data.Hyperdrive, data.HyperdriveText);
            _fleetCoordination.SetValueWithText(data.FleetCoordination, data.FleetCoordinationText);
            _rawStats = new Dictionary<string, double>
            {
                ["^FREI_HYPERDRIVE"] = data.Hyperdrive,
                ["^FREI_FLEET"] = data.FleetCoordination
            };
            _freighterBase = data.FreighterBase;
            _baseItems.Text = data.FreighterBase == null ? UiStrings.Get("common.na") : data.BaseItemCount.ToString(CultureInfo.CurrentCulture);
            _export.IsEnabled = data.FreighterBase != null;
            _cargo.LoadInventory(data.CargoInventory);
            _technology.LoadInventory(data.TechInventory);
            _rooms.ItemsSource = FreighterLogic.DetectFreighterRooms(data.FreighterBase);
            LoadCrew(_playerState);

            string exportBase = FreighterLogic.BuildExportFileName(data.Name, data.TypeDisplayName, data.ClassIndex);
            _cargo.SetExportFileName(exportBase + "_inv" + ExportConfig.Instance.FreighterCargoExt);
            _technology.SetExportFileName(exportBase + "_tech_inv" + ExportConfig.Instance.FreighterTechExt);
        }
        finally { _loading = false; }
    }

    public void SaveData(JsonObject saveData)
    {
        bool wasLoading = _loading;
        _loading = true;
        try
        {
            _hyperdrive.TryCommit();
            _fleetCoordination.TryCommit();
            JsonObject? player = saveData.GetObject("PlayerStateData");
            if (player == null) return;
            FreighterLogic.SaveFreighterData(player, new FreighterLogic.FreighterSaveValues
            {
            Name = _name.Text ?? string.Empty,
            SelectedTypeName = (_type.SelectedItem as FreighterLogic.FreighterTypeItem)?.InternalName,
            ClassIndex = _class.SelectedIndex,
            OriginalClassIndex = _originalClassIndex,
            HomeSeed = _homeSeed.Text ?? string.Empty,
            ModelSeed = _modelSeed.Text ?? string.Empty,
            Hyperdrive = _hyperdrive.UserModified ? _hyperdrive.NumericValue ?? 0 : _rawStats?.GetValueOrDefault("^FREI_HYPERDRIVE") ?? _hyperdrive.NumericValue ?? 0,
            FleetCoordination = _fleetCoordination.UserModified ? _fleetCoordination.NumericValue ?? 0 : _rawStats?.GetValueOrDefault("^FREI_FLEET") ?? _fleetCoordination.NumericValue ?? 0,
            HyperdriveText = _hyperdrive.UserModified ? _hyperdrive.DisplayText : null,
            FleetCoordinationText = _fleetCoordination.UserModified ? _fleetCoordination.DisplayText : null,
            RawStatValues = _rawStats
            });
            _cargo.SaveInventory(player.GetObject("FreighterInventory"));
            _technology.SaveInventory(player.GetObject("FreighterInventory_TechOnly"));
            SaveCrew(player);
        }
        finally { _loading = wasLoading; }
    }

    private void LoadCrew(JsonObject player)
    {
        JsonObject? npc = player.GetObject("CurrentFreighterNPC");
        string filename = npc?.GetString("Filename") ?? string.Empty;
        SelectByInternalName(_crewRace, FreighterLogic.NpcResourceToRace.GetValueOrDefault(filename));
        try { _crewSeed.Text = npc?.GetArray("Seed")?.GetString(1) ?? string.Empty; }
        catch { _crewSeed.Text = string.Empty; }
    }

    private void SaveCrew(JsonObject player)
    {
        JsonObject? npc = player.GetObject("CurrentFreighterNPC");
        if (npc == null) return;
        string? race = (_crewRace.SelectedItem as FreighterLogic.CrewRaceItem)?.InternalName;
        if (race != null && FreighterLogic.RaceToNpcResource.TryGetValue(race, out string? resource)) npc.Set("Filename", resource);
        string? seed = SeedHelper.NormalizeSeed(_crewSeed.Text);
        JsonArray? array = npc.GetArray("Seed");
        if (seed != null && array is { Length: >= 2 }) array.Set(1, seed);
    }

    private async Task ExportFreighterAsync()
    {
        if (_freighterBase == null) { await ShowErrorAsync(UiStrings.Get("freighter.backup_no_base")); return; }
        if (ExportAsync == null) { await ShowErrorAsync(UiStrings.Get("common.error")); return; }
        string extension = ExportConfig.Instance.FreighterExt;
        var vars = new Dictionary<string, string>
        {
            ["freighter_name"] = _name.Text ?? string.Empty,
            ["type"] = (_type.SelectedItem as FreighterLogic.FreighterTypeItem)?.InternalName ?? string.Empty,
            ["class"] = _class.SelectedItem?.ToString() ?? string.Empty
        };
        string file = ExportConfig.BuildFileName(ExportConfig.Instance.FreighterTemplate, extension, vars);
        if (await ExportAsync(new FleetTransferRequest(FleetTransferKind.Freighter, UiStrings.Get("freighter.backup_title"), file, [extension]),
                _freighterBase.ToDisplayString(), CancellationToken.None))
            _status.Text = UiStrings.Get("freighter.restore_success");
    }

    private async Task ImportFreighterAsync()
    {
        if (_playerState == null || ImportAsync == null) { await ShowErrorAsync(UiStrings.Get("common.error")); return; }
        string extension = ExportConfig.Instance.FreighterExt;
        FleetTransferContent? content = await ImportAsync(new FleetTransferRequest(
            FleetTransferKind.Freighter, UiStrings.Get("freighter.restore_title"), "freighter" + extension, [extension]), CancellationToken.None);
        if (content == null) return;
        try
        {
            JsonObject imported = JsonObject.Parse(content.JsonText);
            JsonObject? target = FreighterLogic.FindFreighterBase(_playerState);
            if (target == null) { await ShowErrorAsync(UiStrings.Get("freighter.restore_no_slot")); return; }
            foreach (string name in imported.Names()) target.Set(name, imported.Get(name));
            _freighterBase = target;
            _baseItems.Text = target.GetArray("Objects")?.Length.ToString(CultureInfo.CurrentCulture) ?? "0";
            _rooms.ItemsSource = FreighterLogic.DetectFreighterRooms(target);
            RaiseModified();
            _status.Text = UiStrings.Get("freighter.restore_success");
        }
        catch (Exception ex) { await ShowErrorAsync(UiStrings.Format("freighter.restore_failed", ex.Message)); }
    }

    public void ApplyUiLocalisation()
    {
        bool wasLoading = _loading;
        _loading = true;
        try
        {
        _title.Text = UiStrings.Get("freighter.title");
        _detailsTab.Header = UiStrings.Get("freighter.tab_freighter");
        _roomsTab.Header = UiStrings.Get("freighter.tab_rooms");
        string[] keys = ["freighter.name", "freighter.type", "freighter.class", "freighter.home_seed", "freighter.crew_race",
            "freighter.hyperdrive", "freighter.fleet_coord", "freighter.items", "freighter.model_seed", "freighter.crew_seed"];
        for (int i = 0; i < keys.Length; i++) _labels[i].Text = UiStrings.Get(keys[i]);
        _homeGenerate.Content = _modelGenerate.Content = _crewGenerate.Content = UiStrings.Get("common.generate");
        _export.Content = UiStrings.Get("freighter.export");
        _import.Content = UiStrings.Get("freighter.import");
        _cargoTab.Header = UiStrings.Get("common.cargo");
        _technologyTab.Header = UiStrings.Get("common.technology");
        _cargo.SetMaxSupportedLabel(UiStrings.Format("common.max_supported", "10x12"));
        _technology.SetMaxSupportedLabel(UiStrings.Format("common.max_supported", "10x6"));
        _cargo.ApplyUiLocalisation();
        _technology.ApplyUiLocalisation();
        string? type = (_type.SelectedItem as FreighterLogic.FreighterTypeItem)?.InternalName;
        string? race = (_crewRace.SelectedItem as FreighterLogic.CrewRaceItem)?.InternalName;
        _type.ItemsSource = FreighterLogic.GetFreighterTypeItems(); SelectByInternalName(_type, type);
        _crewRace.ItemsSource = FreighterLogic.GetCrewRaceItems(); SelectByInternalName(_crewRace, race);
            if (_freighterBase != null) _rooms.ItemsSource = FreighterLogic.DetectFreighterRooms(_freighterBase);
        }
        finally { _loading = wasLoading; }
    }

    private static void SelectByInternalName(ComboBox combo, string? internalName)
    {
        combo.SelectedItem = combo.Items.Cast<object?>().FirstOrDefault(item => item switch
        {
            FreighterLogic.FreighterTypeItem type => type.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase),
            FreighterLogic.CrewRaceItem race => race.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase),
            _ => false
        });
    }

    private void RequestGoTo(string path) => GoToJsonRequested?.Invoke(this, new GoToJsonRequestEventArgs(path));
    private void RaiseModified() { if (!_loading) DataModified?.Invoke(this, EventArgs.Empty); }
    private async Task ShowErrorAsync(string message)
    {
        _status.Text = message;
        if (TopLevel.GetTopLevel(this) is Window owner) await FleetUi.MessageAsync(owner, UiStrings.Get("common.error"), message);
    }
}
