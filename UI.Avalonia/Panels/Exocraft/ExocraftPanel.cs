using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NMSE.Config;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Contracts;
using NMSE.UI.Avalonia.Controls;
using NMSE.UI.Avalonia.Inventory;
using NMSE.UI.Avalonia.Services;

namespace NMSE.UI.Avalonia.Panels.Exocraft;

/// <summary>Edits all six current Exocraft types and their summoning-station records.</summary>
public sealed class ExocraftPanel : UserControl, ISaveEditorPanel, IGoToJsonSource
{
    private readonly ExocraftDesktopActions _desktop;
    private GameItemDatabase? _database;
    private readonly ObservableCollection<VehicleChoice> _vehicles = [];
    private readonly ObservableCollection<StationChoice> _individualStations = [];
    private readonly ObservableCollection<StationChoice> _baseStations = [];
    private readonly Dictionary<string, TextBlock> _labels = new(StringComparer.Ordinal);

    private readonly TextBlock _title;
    private readonly TabControl _mainTabs;
    private readonly TabItem _exocraftTab;
    private readonly TabItem _stationsTab;
    private readonly ComboBox _vehicleSelector;
    private readonly TextBox _name;
    private readonly Button _export;
    private readonly Button _import;
    private readonly CheckBox _primary;
    private readonly TextBlock _deployed;
    private readonly Button _undeploy;
    private readonly CheckBox _thirdPerson;
    private readonly CheckBox _minotaurAi;
    private readonly TabControl _inventoryTabs;
    private readonly TabItem _cargoTab;
    private readonly TabItem _technologyTab;
    private readonly TextBlock _technologyNote;
    private readonly Button _goVehicles;
    private readonly Button _goCargo;
    private readonly Button _goBaseObjects;
    private readonly Button _goBases;

    private readonly ListBox _individualList;
    private readonly ListBox _baseList;
    private readonly TextBlock _stationName = new();
    private readonly TextBlock _stationTimestamp = new();
    private readonly TextBlock _stationBaseName = new();
    private readonly TextBlock _stationAddress = new();
    private readonly TextBlock _stationRegionSeed = new();
    private readonly TextBlock _stationPosition = new();
    private readonly TextBlock _stationGalaxy = new();
    private readonly Border _stationGalaxyDot = new() { Width = 12, Height = 12, CornerRadius = new CornerRadius(6) };
    private readonly TextBlock _stationPortalCode = new();
    private readonly TextBlock _stationPortalDecimal = new();
    private readonly TextBlock _stationSignalBooster = new();
    private readonly GlyphRenderer _stationGlyphs = new() { GlyphSize = 24, GlyphSpacing = 2 };
    private readonly TextBlock _stationVoxelX = new();
    private readonly TextBlock _stationVoxelY = new();
    private readonly TextBlock _stationVoxelZ = new();
    private readonly TextBlock _stationSolarSystem = new();
    private readonly TextBlock _stationPlanet = new();
    private readonly Button _deleteStation;

    private JsonObject? _saveData;
    private JsonObject? _playerState;
    private JsonArray? _vehicleOwnership;
    private JsonArray? _baseBuildingObjects;
    private JsonArray? _persistentPlayerBases;
    private StationChoice? _selectedStation;
    private bool _loading;

    public ExocraftPanel(
        GameItemDatabase? database,
        AvaloniaIconProvider? iconProvider,
        ExocraftDesktopActions desktop)
    {
        _database = database;
        _desktop = desktop ?? throw new ArgumentNullException(nameof(desktop));
        _title = new TextBlock
        {
            FontFamily = Services.FontManager.NmsFont,
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _goVehicles = JsonButton("PlayerStateData.VehicleOwnership");
        _goCargo = JsonButton(() => SelectedVehicle is { } selected
            ? $"PlayerStateData.VehicleOwnership[{selected.ArrayIndex}].Inventory" : null);
        _goBaseObjects = JsonButton("PlayerStateData.BaseBuildingObjects");
        _goBases = JsonButton("PlayerStateData.PersistentPlayerBases");

        CargoGrid = CreateCargoGrid();
        TechnologyGrid = CreateTechnologyGrid();
        CargoGrid.SetDatabase(database);
        TechnologyGrid.SetDatabase(database);
        CargoGrid.SetIconProvider(iconProvider);
        TechnologyGrid.SetIconProvider(iconProvider);
        CargoGrid.ConfigureTransfer(ExportInventoryAsync, ImportInventoryAsync);
        TechnologyGrid.ConfigureTransfer(ExportInventoryAsync, ImportInventoryAsync);
        CargoGrid.DataModified += (_, _) => OnInventoryModified(CargoGrid, false);
        TechnologyGrid.DataModified += (_, _) => OnInventoryModified(TechnologyGrid, true);

        _vehicleSelector = new ComboBox { ItemsSource = _vehicles, MinWidth = 180 };
        _vehicleSelector.SelectionChanged += (_, _) => LoadSelectedVehicle();
        _name = new TextBox { MinWidth = 260 };
        _name.LostFocus += (_, _) => CommitName();
        _export = ActionButton(ExportVehicleAsync);
        _import = ActionButton(ImportVehicleAsync);
        _primary = new CheckBox();
        _primary.IsCheckedChanged += (_, _) => WritePrimary();
        _deployed = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        _undeploy = ActionButton(UndeployAsync);
        _thirdPerson = new CheckBox();
        _thirdPerson.IsCheckedChanged += (_, _) => WriteGlobalOptions();
        _minotaurAi = new CheckBox();
        _minotaurAi.IsCheckedChanged += (_, _) => WriteGlobalOptions();
        _technologyNote = new TextBlock
        {
            Foreground = Brushes.DarkOrange,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        _cargoTab = new TabItem { Content = CargoGrid };
        _technologyTab = new TabItem
        {
            Content = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                Children = { _technologyNote, TechnologyGrid },
            },
        };
        Grid.SetRow(TechnologyGrid, 1);
        _inventoryTabs = new TabControl { Items = { _cargoTab, _technologyTab } };

        _individualList = new ListBox { ItemsSource = _individualStations, MinHeight = 150 };
        _baseList = new ListBox { ItemsSource = _baseStations, MinHeight = 150 };
        _individualList.SelectionChanged += (_, _) => SelectStation(_individualList.SelectedItem as StationChoice, _baseList);
        _baseList.SelectionChanged += (_, _) => SelectStation(_baseList.SelectedItem as StationChoice, _individualList);
        _deleteStation = ActionButton(DeleteStationAsync);
        _deleteStation.IsEnabled = false;

        _exocraftTab = new TabItem { Content = BuildExocraftView() };
        _stationsTab = new TabItem { Content = BuildStationsView() };
        _mainTabs = new TabControl { Items = { _exocraftTab, _stationsTab } };
        var headerButtons = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _goVehicles, _goCargo, _goBaseObjects, _goBases },
        };
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { _title, headerButtons },
        };
        Grid.SetColumn(headerButtons, 1);
        Content = new Grid
        {
            Margin = new Thickness(10),
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 8,
            Children = { header, _mainTabs },
        };
        Grid.SetRow(_mainTabs, 1);
        ApplyUiLocalisation();
    }

    public InventoryGrid CargoGrid { get; }
    public InventoryGrid TechnologyGrid { get; }
    public TabControl MainTabs => _mainTabs;
    public ComboBox VehicleSelector => _vehicleSelector;

    public event EventHandler? DataModified;
    public event EventHandler<GoToJsonRequestEventArgs>? GoToJsonRequested;
    public event EventHandler<ExocraftInventoryModifiedEventArgs>? InventoryModified;
    public event EventHandler? CrossInventoryTransferCompleted;

    private static InventoryGrid CreateCargoGrid()
    {
        var grid = new InventoryGrid();
        grid.SetIsCargoInventory(true);
        grid.SetSuperchargeDisabled(true);
        grid.SetInventoryOwnerType("Vehicle");
        grid.SetInventoryGroup("Vehicle");
        grid.SetMaxSupportedLabel(string.Empty);
        return grid;
    }

    private static InventoryGrid CreateTechnologyGrid()
    {
        var grid = new InventoryGrid();
        grid.SetIsTechInventory(true);
        grid.SetSuperchargeDisabled(true);
        grid.SetSlotToggleDisabled(true);
        grid.SetInventoryOwnerType("Vehicle");
        grid.SetInventoryGroup("Vehicle");
        grid.SetMaxSupportedLabel(string.Empty);
        return grid;
    }

    public void SetDatabase(GameItemDatabase? database)
    {
        _database = database;
        CargoGrid.SetDatabase(database);
        TechnologyGrid.SetDatabase(database);
        PopulateStations();
    }

    public void SetIconProvider(AvaloniaIconProvider? provider)
    {
        CargoGrid.SetIconProvider(provider);
        TechnologyGrid.SetIconProvider(provider);
    }

    public void ConfigureTransfer(
        Func<JsonObject, string, Task>? exportInventory,
        Func<string, Task<JsonObject?>>? importInventory)
    {
        CargoGrid.ConfigureTransfer(exportInventory ?? ExportInventoryAsync, importInventory ?? ImportInventoryAsync);
        TechnologyGrid.ConfigureTransfer(exportInventory ?? ExportInventoryAsync, importInventory ?? ImportInventoryAsync);
    }

    public void RefreshAfterExternalTransfer() => LoadSelectedVehicle();

    private Control BuildExocraftView()
    {
        var form = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 8,
            RowSpacing = 5,
        };
        AddRow(form, 0, LocalLabel("vehicle"), _vehicleSelector);
        AddRow(form, 1, LocalLabel("name"), _name);
        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _export, _import, _primary, _deployed, _undeploy },
        };
        AddRow(form, 2, new TextBlock(), actions);
        var options = new WrapPanel { Orientation = Orientation.Horizontal, Children = { _thirdPerson, _minotaurAi } };
        AddRow(form, 3, new TextBlock(), options);
        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 8,
            Children = { form, _inventoryTabs },
        };
        Grid.SetRow(_inventoryTabs, 1);
        return layout;
    }

    private Control BuildStationsView()
    {
        var lists = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,*"),
            RowSpacing = 4,
            Children =
            {
                LocalLabel("individual_stations"), _individualList,
                LocalLabel("base_stations"), _baseList,
            },
        };
        Grid.SetRow(_individualList, 1);
        Grid.SetRow(_labels["base_stations"], 2);
        Grid.SetRow(_baseList, 3);

        var details = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("200,*"),
            ColumnSpacing = 8,
            RowSpacing = 4,
        };
        int row = 0;
        AddDetailRow(details, ref row, "station_name", _stationName);
        AddDetailRow(details, ref row, "station_timestamp", _stationTimestamp);
        AddDetailRow(details, ref row, "station_base_name", _stationBaseName);
        AddDetailRow(details, ref row, "station_address", _stationAddress);
        AddDetailRow(details, ref row, "station_region_seed", _stationRegionSeed);
        AddDetailRow(details, ref row, "station_position", _stationPosition);
        var galaxy = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { _stationGalaxyDot, _stationGalaxy },
        };
        AddDetailRow(details, ref row, "station_galaxy", galaxy);
        AddDetailRow(details, ref row, "station_portal_code", _stationPortalCode);
        AddDetailRow(details, ref row, "station_portal_decimal", _stationPortalDecimal);
        AddDetailRow(details, ref row, "station_signal", _stationSignalBooster);
        details.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        details.Children.Add(_stationGlyphs);
        Grid.SetRow(_stationGlyphs, row++);
        Grid.SetColumn(_stationGlyphs, 1);
        AddDetailRow(details, ref row, "station_voxel_x", _stationVoxelX);
        AddDetailRow(details, ref row, "station_voxel_y", _stationVoxelY);
        AddDetailRow(details, ref row, "station_voxel_z", _stationVoxelZ);
        AddDetailRow(details, ref row, "station_solar_system", _stationSolarSystem);
        AddDetailRow(details, ref row, "station_planet", _stationPlanet);
        details.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        details.Children.Add(_deleteStation);
        Grid.SetRow(_deleteStation, row);
        Grid.SetColumnSpan(_deleteStation, 2);

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("260,*"),
            ColumnSpacing = 14,
            Children =
            {
                lists,
                new ScrollViewer
                {
                    VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    Content = details,
                },
            },
        };
        Grid.SetColumn(content.Children[1], 1);
        return content;
    }

    private static void AddRow(Grid grid, int row, Control label, Control editor)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Children.Add(label);
        grid.Children.Add(editor);
        Grid.SetRow(label, row);
        Grid.SetRow(editor, row);
        Grid.SetColumn(editor, 1);
    }

    private void AddDetailRow(Grid grid, ref int row, string key, Control value)
    {
        AddRow(grid, row++, LocalLabel(key), value);
    }

    private TextBlock LocalLabel(string key)
    {
        var label = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        _labels[key] = label;
        return label;
    }

    private static Button ActionButton(Func<Task> action)
    {
        var button = new Button { Margin = new Thickness(2) };
        button.Click += async (_, _) => await action();
        return button;
    }

    private Button JsonButton(string path) => JsonButton(() => path);
    private Button JsonButton(Func<string?> path)
    {
        var button = new Button { Content = "{}", MinWidth = 34, Margin = new Thickness(2) };
        button.Click += (_, _) =>
        {
            string? selected = path();
            if (!string.IsNullOrEmpty(selected))
                GoToJsonRequested?.Invoke(this, new GoToJsonRequestEventArgs(selected));
        };
        return button;
    }

    public void LoadData(JsonObject saveData)
    {
        ArgumentNullException.ThrowIfNull(saveData);
        _loading = true;
        try
        {
            _saveData = saveData;
            _playerState = saveData.GetObject("PlayerStateData");
            _vehicleOwnership = _playerState?.GetArray("VehicleOwnership");
            _baseBuildingObjects = _playerState?.GetArray("BaseBuildingObjects");
            _persistentPlayerBases = _playerState?.GetArray("PersistentPlayerBases");
            _vehicles.Clear();
            if (_vehicleOwnership is not null)
                foreach ((int index, string name) in ExocraftLogic.VehicleTypes)
                    if (index < _vehicleOwnership.Length)
                        _vehicles.Add(new VehicleChoice(index, name, ExocraftLogic.GetLocalisedVehicleTypeName(name)));
            _thirdPerson.IsChecked = GetBool(saveData.GetObject("CommonStateData"), "UsesThirdPersonVehicleCam");
            _minotaurAi.IsChecked = GetBool(_playerState, "VehicleAIControlEnabled");
            PopulateStations();
            _vehicleSelector.SelectedIndex = _vehicles.Count > 0 ? 0 : -1;
        }
        finally { _loading = false; }
        LoadSelectedVehicle();
    }

    public void SaveData(JsonObject saveData)
    {
        ArgumentNullException.ThrowIfNull(saveData);
        CargoGrid.CommitPendingEdits();
        TechnologyGrid.CommitPendingEdits();
        CommitName();
        JsonObject? player = saveData.GetObject("PlayerStateData");
        saveData.GetObject("CommonStateData")?.Set("UsesThirdPersonVehicleCam", _thirdPerson.IsChecked == true);
        player?.Set("VehicleAIControlEnabled", _minotaurAi.IsChecked == true);
        if (SelectedVehicle is not { } selected || player?.GetArray("VehicleOwnership")?.GetObject(selected.ArrayIndex) is not { } vehicle)
            return;
        CargoGrid.SaveInventory(vehicle.GetObject("Inventory"));
        TechnologyGrid.SaveInventory(vehicle.GetObject("Inventory_TechOnly"));
    }

    public void ApplyUiLocalisation()
    {
        _title.Text = UiStrings.Get("exocraft.title");
        _exocraftTab.Header = UiStrings.Get("exocraft.tab_exocrafts");
        _stationsTab.Header = UiStrings.Get("exocraft.tab_stations");
        Set("vehicle", "exocraft.vehicle");
        Set("name", "exocraft.name");
        _export.Content = UiStrings.Get("exocraft.export");
        _import.Content = UiStrings.Get("exocraft.import");
        _primary.Content = UiStrings.Get("exocraft.primary_vehicle");
        _undeploy.Content = UiStrings.Get("exocraft.undeploy_title");
        _thirdPerson.Content = UiStrings.Get("exocraft.third_person");
        _minotaurAi.Content = UiStrings.Get("exocraft.minotaur_ai");
        _technologyNote.Text = UiStrings.Get("exocraft.supercharge_note");
        _cargoTab.Header = UiStrings.Get("common.cargo");
        _technologyTab.Header = UiStrings.Get("common.technology");
        Set("individual_stations", "exocraft.individual_stations");
        Set("base_stations", "exocraft.part_of_bases");
        Set("station_name", "exocraft.station_name");
        Set("station_timestamp", "exocraft.station_timestamp");
        Set("station_base_name", "exocraft.station_base_name");
        Set("station_address", "exocraft.station_galactic_address");
        Set("station_region_seed", "exocraft.station_region_seed");
        Set("station_position", "exocraft.station_position");
        Set("station_galaxy", "exocraft.station_galaxy");
        Set("station_portal_code", "exocraft.station_portal_code");
        Set("station_portal_decimal", "exocraft.station_portal_code_dec");
        Set("station_signal", "exocraft.station_signal_booster");
        Set("station_voxel_x", "exocraft.station_voxel_x");
        Set("station_voxel_y", "exocraft.station_voxel_y");
        Set("station_voxel_z", "exocraft.station_voxel_z");
        Set("station_solar_system", "exocraft.station_solar_system");
        Set("station_planet", "exocraft.station_planet");
        _deleteStation.Content = UiStrings.Get("exocraft.delete_station");
        CargoGrid.ApplyUiLocalisation();
        TechnologyGrid.ApplyUiLocalisation();
        RefreshVehicleNames();
        PopulateStations();
        if (_selectedStation is not null) DisplayStation(_selectedStation);
    }

    private void Set(string control, string localisationKey)
    {
        if (_labels.TryGetValue(control, out TextBlock? label)) label.Text = UiStrings.Get(localisationKey);
    }

    private static bool GetBool(JsonObject? value, string key)
    {
        try { return value?.GetBool(key) ?? false; }
        catch { return false; }
    }

    private VehicleChoice? SelectedVehicle => _vehicleSelector.SelectedItem as VehicleChoice;

    private void RefreshVehicleNames()
    {
        int selected = _vehicleSelector.SelectedIndex;
        for (int i = 0; i < _vehicles.Count; i++)
            _vehicles[i] = _vehicles[i] with { Label = ExocraftLogic.GetLocalisedVehicleTypeName(_vehicles[i].InternalName) };
        _vehicleSelector.SelectedIndex = selected;
    }

    private void LoadSelectedVehicle()
    {
        if (SelectedVehicle is not { } selected || _vehicleOwnership is null)
        {
            CargoGrid.LoadInventory(null);
            TechnologyGrid.LoadInventory(null);
            _goCargo.IsEnabled = false;
            return;
        }
        bool old = _loading;
        _loading = true;
        try
        {
            JsonObject vehicle = _vehicleOwnership.GetObject(selected.ArrayIndex);
            string owner = ExocraftLogic.GetOwnerTypeForVehicle(selected.InternalName);
            CargoGrid.SetInventoryOwnerType(owner);
            TechnologyGrid.SetInventoryOwnerType(owner);
            CargoGrid.LoadInventory(vehicle.GetObject("Inventory"));
            TechnologyGrid.LoadInventory(vehicle.GetObject("Inventory_TechOnly"));
            _name.Text = vehicle.GetString("Name") ?? string.Empty;
            try { _primary.IsChecked = _playerState?.GetInt("PrimaryVehicle") == selected.ArrayIndex; }
            catch { _primary.IsChecked = false; }
            long location = 0;
            try { location = vehicle.GetLong("Location"); } catch { }
            bool deployed = location != 0;
            _deployed.Text = deployed ? UiStrings.Get("exocraft.status_deployed") : UiStrings.Get("exocraft.status_not_deployed");
            _deployed.Foreground = deployed ? Brushes.Green : Brushes.Gray;
            _undeploy.IsEnabled = deployed;
            _goCargo.IsEnabled = true;
            string safe = selected.InternalName.Replace(' ', '_');
            ExportConfig config = ExportConfig.Instance;
            CargoGrid.SetExportFileName($"{safe}_cargo_inv{config.ExocraftCargoExt}");
            CargoGrid.SetExportFileFilter(string.Empty, string.Empty, config.ExocraftCargoExt);
            TechnologyGrid.SetExportFileName($"{safe}_tech_inv{config.ExocraftTechExt}");
            TechnologyGrid.SetExportFileFilter(string.Empty, string.Empty, config.ExocraftTechExt);
        }
        finally { _loading = old; }
    }

    private void CommitName()
    {
        if (_loading || SelectedVehicle is not { } selected || _vehicleOwnership is null) return;
        JsonObject vehicle = _vehicleOwnership.GetObject(selected.ArrayIndex);
        string value = _name.Text ?? string.Empty;
        if (vehicle.GetString("Name") == value) return;
        vehicle.Set("Name", value);
        Modified();
    }

    private void WritePrimary()
    {
        if (_loading || _playerState is null || SelectedVehicle is not { } selected) return;
        if (_primary.IsChecked == true)
            _playerState.Set("PrimaryVehicle", selected.ArrayIndex);
        else
        {
            try
            {
                if (_playerState.GetInt("PrimaryVehicle") == selected.ArrayIndex)
                    _playerState.Set("PrimaryVehicle", -1);
            }
            catch { }
        }
        Modified();
    }

    private void WriteGlobalOptions()
    {
        if (_loading) return;
        _saveData?.GetObject("CommonStateData")?.Set("UsesThirdPersonVehicleCam", _thirdPerson.IsChecked == true);
        _playerState?.Set("VehicleAIControlEnabled", _minotaurAi.IsChecked == true);
        Modified();
    }

    private void OnInventoryModified(InventoryGrid grid, bool technology)
    {
        if (SelectedVehicle is not { } selected || grid.LoadedInventory is not { } inventory) return;
        DataModified?.Invoke(this, EventArgs.Empty);
        InventoryModified?.Invoke(this,
            new ExocraftInventoryModifiedEventArgs(selected.ArrayIndex, selected.InternalName, inventory, technology));
    }

    private void Modified() => DataModified?.Invoke(this, EventArgs.Empty);

    private async Task ExportVehicleAsync()
    {
        if (SelectedVehicle is not { } selected || _vehicleOwnership is null)
        {
            await _desktop.ShowInfoAsync(UiStrings.Get("common.export"), UiStrings.Get("exocraft.no_vehicle_selected"));
            return;
        }
        try
        {
            CommitName();
            ExportConfig config = ExportConfig.Instance;
            var variables = new Dictionary<string, string>
            {
                ["vehicle_name"] = _name.Text ?? string.Empty,
                ["vehicle_type"] = selected.InternalName,
            };
            string suggested = ExportConfig.BuildFileName(config.ExocraftTemplate, config.ExocraftExt, variables);
            string? path = await _desktop.PickSaveFileAsync(new ExocraftFileRequest(
                UiStrings.Get("exocraft.export"), suggested, config.ExocraftExt));
            if (string.IsNullOrEmpty(path)) return;
            _vehicleOwnership.GetObject(selected.ArrayIndex).ExportToFile(path);
        }
        catch (Exception exception)
        {
            await _desktop.ShowErrorAsync(UiStrings.Get("common.error"), UiStrings.Format("common.export_failed", exception.Message));
        }
    }

    private async Task ImportVehicleAsync()
    {
        if (SelectedVehicle is not { } selected || _vehicleOwnership is null)
        {
            await _desktop.ShowInfoAsync(UiStrings.Get("common.import"), UiStrings.Get("exocraft.no_vehicle_selected"));
            return;
        }
        try
        {
            string? path = await _desktop.PickOpenFileAsync(new ExocraftFileRequest(
                UiStrings.Get("exocraft.import"), string.Empty, ExportConfig.Instance.ExocraftExt, ".exo"));
            if (string.IsNullOrEmpty(path)) return;
            JsonObject imported = InventoryImportHelper.UnwrapNomNom(JsonObject.ImportFromFile(path), "Vehicle");
            JsonObject vehicle = _vehicleOwnership.GetObject(selected.ArrayIndex);
            foreach (string key in imported.Names()) vehicle.Set(key, imported.Get(key));
            LoadSelectedVehicle();
            Modified();
            CrossInventoryTransferCompleted?.Invoke(this, EventArgs.Empty);
            await _desktop.ShowInfoAsync(UiStrings.Get("common.import"), UiStrings.Get("exocraft.import_success"));
        }
        catch (Exception exception)
        {
            await _desktop.ShowErrorAsync(UiStrings.Get("common.error"), UiStrings.Format("common.import_failed", exception.Message));
        }
    }

    private async Task UndeployAsync()
    {
        if (SelectedVehicle is not { } selected || _vehicleOwnership is null) return;
        if (!await _desktop.ConfirmAsync(UiStrings.Get("exocraft.undeploy_title"), UiStrings.Get("exocraft.undeploy_confirm"))) return;
        try
        {
            JsonObject vehicle = _vehicleOwnership.GetObject(selected.ArrayIndex);
            vehicle.Set("Location", 0);
            ResetVector(vehicle.GetArray("Direction"));
            ResetVector(vehicle.GetArray("Position"));
            _deployed.Text = UiStrings.Get("exocraft.status_not_deployed");
            _deployed.Foreground = Brushes.Gray;
            _undeploy.IsEnabled = false;
            Modified();
        }
        catch (Exception exception)
        {
            await _desktop.ShowErrorAsync(UiStrings.Get("common.error"), UiStrings.Format("exocraft.undeploy_failed", exception.Message));
        }
    }

    private static void ResetVector(JsonArray? values)
    {
        if (values is null || values.Length < 4) return;
        values.Set(0, 0.0);
        values.Set(1, 0.0);
        values.Set(2, 0.0);
        values.Set(3, -1.0);
    }

    private async Task ExportInventoryAsync(JsonObject inventory, string suggestedName)
    {
        string extension = Path.GetExtension(suggestedName);
        string? path = await _desktop.PickSaveFileAsync(new ExocraftFileRequest(
            UiStrings.Get("common.export"), suggestedName, extension));
        if (string.IsNullOrEmpty(path)) return;
        inventory.ExportToFile(path);
    }

    private async Task<JsonObject?> ImportInventoryAsync(string extension)
    {
        string? path = await _desktop.PickOpenFileAsync(new ExocraftFileRequest(
            UiStrings.Get("common.import"), string.Empty, extension, ".exo", ".json"));
        if (string.IsNullOrEmpty(path)) return null;
        JsonObject imported = JsonObject.ImportFromFile(path);
        CrossInventoryTransferCompleted?.Invoke(this, EventArgs.Empty);
        return imported;
    }

    private void PopulateStations()
    {
        _individualStations.Clear();
        _baseStations.Clear();
        if (_baseBuildingObjects is not null)
            for (int index = 0; index < _baseBuildingObjects.Length; index++)
            {
                JsonObject entry = _baseBuildingObjects.GetObject(index);
                string id = entry.GetString("ObjectID") ?? string.Empty;
                if (!id.Contains("GARAGE", StringComparison.OrdinalIgnoreCase)) continue;
                _individualStations.Add(new StationChoice(
                    StationKind.Individual, entry, null, index, -1, GetStationName(id)));
            }
        if (_persistentPlayerBases is not null)
            for (int baseIndex = 0; baseIndex < _persistentPlayerBases.Length; baseIndex++)
            {
                JsonObject baseEntry = _persistentPlayerBases.GetObject(baseIndex);
                JsonArray? objects = baseEntry.GetArray("Objects");
                if (objects is null) continue;
                for (int objectIndex = 0; objectIndex < objects.Length; objectIndex++)
                {
                    JsonObject entry = objects.GetObject(objectIndex);
                    string id = entry.GetString("ObjectID") ?? string.Empty;
                    if (!id.Contains("GARAGE", StringComparison.OrdinalIgnoreCase)) continue;
                    _baseStations.Add(new StationChoice(
                        StationKind.Base, entry, baseEntry, objectIndex, baseIndex, GetStationName(id)));
                }
            }
        if (_selectedStation is not null)
        {
            _selectedStation = _selectedStation.Kind == StationKind.Individual
                ? _individualStations.FirstOrDefault(item => ReferenceEquals(item.Entry, _selectedStation.Entry))
                : _baseStations.FirstOrDefault(item => ReferenceEquals(item.Entry, _selectedStation.Entry));
        }
        if (_selectedStation is null)
            ClearStationDetail();
        else
        {
            bool old = _loading;
            _loading = true;
            try
            {
                if (_selectedStation.Kind == StationKind.Individual)
                    _individualList.SelectedItem = _selectedStation;
                else
                    _baseList.SelectedItem = _selectedStation;
            }
            finally { _loading = old; }
        }
    }

    private string GetStationName(string objectId)
    {
        string id = objectId.TrimStart('^');
        return _database?.GetItem(id)?.Name ?? objectId;
    }

    private void SelectStation(StationChoice? station, ListBox otherList)
    {
        if (_loading || station is null) return;
        bool old = _loading;
        _loading = true;
        try { otherList.SelectedIndex = -1; }
        finally { _loading = old; }
        _selectedStation = station;
        DisplayStation(station);
    }

    private void DisplayStation(StationChoice station)
    {
        JsonObject entry = station.Entry;
        JsonObject addressOwner = station.BaseEntry ?? entry;
        string objectId = entry.GetString("ObjectID") ?? string.Empty;
        long timestamp = 0;
        try { timestamp = entry.GetLong("Timestamp"); } catch { }
        string address = CoordinateHelper.NormalizeGalacticAddress(addressOwner.Get("GalacticAddress"));
        string portal = ExtractPortalCode(address);
        var coordinates = ExocraftLogic.ParseGalacticAddressToVoxel(addressOwner.Get("GalacticAddress"));
        int vx = coordinates?.VoxelX ?? 0;
        int vy = coordinates?.VoxelY ?? 0;
        int vz = coordinates?.VoxelZ ?? 0;
        int system = coordinates?.SolarSystemIndex ?? 0;
        int planet = coordinates?.PlanetIndex ?? 0;
        _stationName.Text = GetStationName(objectId);
        _stationTimestamp.Text = FormatTimestamp(timestamp);
        _stationBaseName.Text = station.BaseEntry is null
            ? UiStrings.Get("exocraft.station_not_in_base")
            : GetBaseName(station.BaseEntry, station.BaseIndex);
        _stationAddress.Text = address;
        _stationRegionSeed.Text = station.BaseEntry is null
            ? entry.Get("RegionSeed")?.ToString() ?? UiStrings.Get("common.unknown")
            : UiStrings.Get("common.unknown");
        _labels["station_region_seed"].IsVisible = station.BaseEntry is null;
        _stationRegionSeed.IsVisible = station.BaseEntry is null;
        _stationPosition.Text = FormatPosition(entry.GetArray("Position"));
        int? reality = GetRealityIndex(address);
        if (reality is int index)
        {
            string galaxyType = GalaxyDatabase.GetGalaxyType(index);
            _stationGalaxy.Text = $"{GalaxyDatabase.GetGalaxyDisplayName(index)} ({galaxyType})";
            _stationGalaxyDot.Background = galaxyType switch
            {
                "Lush" => Brushes.LimeGreen,
                "Harsh" => Brushes.IndianRed,
                "Empty" => Brushes.DeepSkyBlue,
                _ => Brushes.Gray,
            };
            _stationGalaxyDot.IsVisible = true;
        }
        else
        {
            _stationGalaxy.Text = UiStrings.Get("exocraft.station_no_galaxy_info");
            _stationGalaxyDot.IsVisible = false;
        }
        _stationPortalCode.Text = portal;
        _stationPortalDecimal.Text = CoordinateHelper.PortalHexToDec(portal);
        _stationSignalBooster.Text = CoordinateHelper.VoxelToSignalBooster(vx, vy, vz, system);
        _stationGlyphs.PortalCode = portal;
        _stationVoxelX.Text = vx.ToString(CultureInfo.CurrentCulture);
        _stationVoxelY.Text = vy.ToString(CultureInfo.CurrentCulture);
        _stationVoxelZ.Text = vz.ToString(CultureInfo.CurrentCulture);
        _stationSolarSystem.Text = system.ToString(CultureInfo.CurrentCulture);
        _stationPlanet.Text = planet.ToString(CultureInfo.CurrentCulture);
        _deleteStation.IsEnabled = true;
    }

    private static string GetBaseName(JsonObject entry, int index)
    {
        string name = entry.GetString("Name") ?? string.Empty;
        return string.IsNullOrEmpty(name) ? UiStrings.Format("base.fallback_base_name", index + 1) : name;
    }

    private static string ExtractPortalCode(string address)
    {
        string raw = address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? address[2..] : address;
        return raw.Length switch
        {
            12 => raw,
            14 => string.Concat(raw.AsSpan(0, 4), raw.AsSpan(6, 8)),
            _ => string.Empty,
        };
    }

    private static int? GetRealityIndex(string address)
    {
        string raw = address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? address[2..] : address;
        return raw.Length == 14 && int.TryParse(raw.AsSpan(4, 2), NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out int result) ? result : null;
    }

    private static string FormatTimestamp(long value)
    {
        if (value <= 0) return UiStrings.Get("common.unknown");
        try { return DateTimeOffset.FromUnixTimeSeconds(value).LocalDateTime.ToString("g", CultureInfo.CurrentCulture); }
        catch { return value.ToString(CultureInfo.CurrentCulture); }
    }

    private static string FormatPosition(JsonArray? position)
    {
        if (position is null || position.Length < 3) return UiStrings.Get("common.unknown");
        try
        {
            return $"X: {position.GetDouble(0).ToString("F2", CultureInfo.CurrentCulture)}, " +
                   $"Y: {position.GetDouble(1).ToString("F2", CultureInfo.CurrentCulture)}, " +
                   $"Z: {position.GetDouble(2).ToString("F2", CultureInfo.CurrentCulture)}";
        }
        catch { return UiStrings.Get("common.unknown"); }
    }

    private void ClearStationDetail()
    {
        foreach (TextBlock value in new[]
                 {
                     _stationName, _stationTimestamp, _stationBaseName, _stationAddress, _stationRegionSeed,
                     _stationPosition, _stationGalaxy, _stationPortalCode, _stationPortalDecimal,
                     _stationSignalBooster, _stationVoxelX, _stationVoxelY, _stationVoxelZ,
                     _stationSolarSystem, _stationPlanet,
                 }) value.Text = string.Empty;
        _stationGlyphs.PortalCode = string.Empty;
        _stationGalaxyDot.IsVisible = false;
        _deleteStation.IsEnabled = false;
    }

    private async Task DeleteStationAsync()
    {
        if (_selectedStation is null)
        {
            await _desktop.ShowErrorAsync(UiStrings.Get("common.error"), UiStrings.Get("exocraft.no_station_selected"));
            return;
        }
        string message = _selectedStation.Kind == StationKind.Base
            ? UiStrings.Get("exocraft.delete_base_station_confirm")
            : UiStrings.Get("exocraft.delete_station_confirm");
        if (!await _desktop.ConfirmAsync(UiStrings.Get("exocraft.delete_station_title"), message)) return;
        if (_selectedStation.Kind == StationKind.Individual && _baseBuildingObjects is not null)
        {
            int index = FindReference(_baseBuildingObjects, _selectedStation.Entry);
            if (index >= 0) _baseBuildingObjects.RemoveAt(index);
        }
        else if (_selectedStation.BaseEntry?.GetArray("Objects") is { } objects)
        {
            int index = FindReference(objects, _selectedStation.Entry);
            if (index >= 0) objects.RemoveAt(index);
        }
        _selectedStation = null;
        _loading = true;
        try
        {
            _individualList.SelectedIndex = -1;
            _baseList.SelectedIndex = -1;
        }
        finally { _loading = false; }
        PopulateStations();
        Modified();
    }

    private static int FindReference(JsonArray array, JsonObject target)
    {
        for (int i = 0; i < array.Length; i++)
            if (ReferenceEquals(array.GetObject(i), target)) return i;
        return -1;
    }

    private enum StationKind { Individual, Base }
    private sealed record VehicleChoice(int ArrayIndex, string InternalName, string Label)
    {
        public override string ToString() => Label;
    }
    private sealed record StationChoice(
        StationKind Kind,
        JsonObject Entry,
        JsonObject? BaseEntry,
        int ObjectIndex,
        int BaseIndex,
        string Label)
    {
        public override string ToString() => Label;
    }
}
