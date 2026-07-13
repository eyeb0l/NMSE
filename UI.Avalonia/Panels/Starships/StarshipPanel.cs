using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using NMSE.Config;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Contracts;
using NMSE.UI.Avalonia.Controls;
using NMSE.UI.Avalonia.Inventory;
using NMSE.UI.Avalonia.Platform;
using NMSE.UI.Avalonia.Services;

namespace NMSE.UI.Avalonia.Panels.Starships;

public sealed partial class StarshipPanel : UserControl, ISaveEditorPanel, IGoToJsonSource
{
    private readonly ComboBox _selector = new();
    private readonly ComboBox _type = new();
    private readonly ComboBox _class = new();
    private readonly TextBox _name = new();
    private readonly TextBox _seed = new();
    private readonly CheckBox _legacyColours = new();
    private readonly InvariantNumericTextBox _damage = new();
    private readonly InvariantNumericTextBox _shield = new();
    private readonly InvariantNumericTextBox _hyperdrive = new();
    private readonly InvariantNumericTextBox _maneuver = new();
    private readonly TextBlock _title = new() { FontSize = 20, FontWeight = global::Avalonia.Media.FontWeight.SemiBold };
    private readonly TextBlock _primaryLabel = new() { Foreground = global::Avalonia.Media.Brushes.Gray };
    private readonly TextBlock _detailsHeading = new() { FontWeight = global::Avalonia.Media.FontWeight.SemiBold };
    private readonly TextBlock _statsHeading = new() { FontWeight = global::Avalonia.Media.FontWeight.SemiBold };
    private readonly TextBlock _selectLabel = new();
    private readonly TextBlock _nameLabel = new();
    private readonly TextBlock _typeLabel = new();
    private readonly TextBlock _classLabel = new();
    private readonly TextBlock _seedLabel = new();
    private readonly TextBlock _damageLabel = new();
    private readonly TextBlock _shieldLabel = new();
    private readonly TextBlock _hyperdriveLabel = new();
    private readonly TextBlock _maneuverLabel = new();
    private readonly TextBlock _corvetteWarning = new() { Foreground = global::Avalonia.Media.Brushes.OrangeRed, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock _optimiseIndicator = new() { FontWeight = global::Avalonia.Media.FontWeight.Bold };
    private readonly Button _generateSeed = new();
    private readonly Button _delete = new();
    private readonly Button _copy = new();
    private readonly Button _swap = new();
    private readonly Button _export = new();
    private readonly Button _import = new();
    private readonly Button _makePrimary = new();
    private readonly Button _moveArchive = new();
    private readonly Button _importArchive = new();
    private readonly Button _snapshot = new();
    private readonly Button _importSnapshot = new();
    private readonly Button _optimise = new();
    private readonly Button _goList = JsonNavigationButton();
    private readonly Button _goSelected = JsonNavigationButton();
    private readonly Button _goCargo = JsonNavigationButton();
    private readonly Button _goCustomisation = JsonNavigationButton();
    private readonly TabControl _outerTabs = new();
    private readonly TabControl _inventoryTabs = new();
    private readonly TabItem _detailsTab = new();
    private readonly TabItem _customisationTab = new();
    private readonly TabItem _cargoTab = new();
    private readonly TabItem _techTab = new();
    private readonly StackPanel _corvetteActions = new() { Orientation = Orientation.Horizontal, Spacing = 7 };
    private readonly IDesktopServices? _desktopServices;
    private JsonObject? _saveRoot;
    private JsonObject? _playerState;
    private JsonArray? _ships;
    private int _primaryShipIndex;
    private int _selectedShipIndex = -1;
    private int _originalClassIndex = -1;
    private string _saveScopeKey = "unknown";
    private Dictionary<string, double>? _rawStats;
    private bool _loading;

    public StarshipPanel(
        GameItemDatabase? database = null,
        AvaloniaIconProvider? iconProvider = null,
        IDesktopServices? desktopServices = null)
    {
        _desktopServices = desktopServices;
        CargoGrid = new InventoryGrid();
        CargoGrid.SetDatabase(database); CargoGrid.SetIconProvider(iconProvider);
        CargoGrid.SetIsCargoInventory(true); CargoGrid.SetSortingEnabled(true); CargoGrid.SetPinSlotFeatureEnabled(true);
        CargoGrid.SetInventoryGroup("ShipCargo"); CargoGrid.SetInventoryOwnerType("Ship"); CargoGrid.SetSuperchargeDisabled(true);
        TechnologyGrid = new InventoryGrid();
        TechnologyGrid.SetDatabase(database); TechnologyGrid.SetIconProvider(iconProvider);
        TechnologyGrid.SetIsTechInventory(true); TechnologyGrid.SetInventoryGroup("Ship"); TechnologyGrid.SetInventoryOwnerType("Ship");
        if (desktopServices is not null)
        {
            CargoGrid.ConfigureTransfer(ExportInventoryAsync, ImportInventoryAsync);
            TechnologyGrid.ConfigureTransfer(ExportInventoryAsync, ImportInventoryAsync);
        }
        CargoGrid.DataModified += (_, _) => RaiseModified();
        TechnologyGrid.DataModified += (_, _) => RaiseModified();
        CargoGrid.PinnedSlotsChanged += (_, _) => SavePins();
        CargoGrid.AutoStackToStorageRequested += (_, _) => AutoStack(AutoStackDestination.Storage, null);
        CargoGrid.AutoStackToFreighterRequested += (_, _) => AutoStack(AutoStackDestination.Freighter, null);
        CargoGrid.AutoStackSelectedSlotToStorageRequested += (_, args) => AutoStack(AutoStackDestination.Storage, args);
        CargoGrid.AutoStackSelectedSlotToFreighterRequested += (_, args) => AutoStack(AutoStackDestination.Freighter, args);
        CargoGrid.RefreshToolbarActions();

        _selector.SelectionChanged += OnShipSelectionChanged;
        _type.SelectionChanged += (_, _) => OnTypeChanged();
        _class.SelectionChanged += (_, _) => Changed();
        _name.TextChanged += (_, _) => Changed();
        _name.LostFocus += (_, _) => RefreshSelectedLabel();
        _seed.TextChanged += (_, _) => Changed();
        _legacyColours.IsCheckedChanged += (_, _) => Changed();
        _damage.NumericValueChanged += (_, _) => Changed();
        _shield.NumericValueChanged += (_, _) => Changed();
        _hyperdrive.NumericValueChanged += (_, _) => Changed();
        _maneuver.NumericValueChanged += (_, _) => Changed();
        _generateSeed.Click += (_, _) => _seed.Text = $"0x{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}";
        _delete.Click += async (_, _) => await RunUiOperation(async () => { await DeleteSelectedAsync(); }).ConfigureAwait(true);
        _copy.Click += async (_, _) => await RunUiOperation(async () => { await CopySelectedShipAsync(); }).ConfigureAwait(true);
        _swap.Click += async (_, _) => await RunUiOperation(ChooseSwapTargetAsync).ConfigureAwait(true);
        _export.Click += async (_, _) => await RunUiOperation(async () => { await ExportSelectedAsync(); }).ConfigureAwait(true);
        _import.Click += async (_, _) => await RunUiOperation(async () => { await ImportShipAsync(); }).ConfigureAwait(true);
        _makePrimary.Click += async (_, _) => await RunUiOperation(async () => { await MakeSelectedPrimaryAsync(); }).ConfigureAwait(true);
        _moveArchive.Click += async (_, _) => await RunUiOperation(async () => { await MoveSelectedToArchiveAsync(); }).ConfigureAwait(true);
        _importArchive.Click += async (_, _) => await RunUiOperation(async () => { await ImportFromArchiveAsync(); }).ConfigureAwait(true);
        _snapshot.Click += async (_, _) => await RunUiOperation(async () => { await ExportSnapshotAsync(); }).ConfigureAwait(true);
        _importSnapshot.Click += async (_, _) => await RunUiOperation(async () => { await ImportSnapshotAsync(); }).ConfigureAwait(true);
        _optimise.Click += async (_, _) => await RunUiOperation(() => { OptimiseSelectedCorvette(); return Task.CompletedTask; }).ConfigureAwait(true);
        _goList.Click += (_, _) => GoToJsonRequested?.Invoke(this, new GoToJsonRequestEventArgs("PlayerStateData.ShipOwnership"));
        _goSelected.Click += (_, _) => RaiseGoToSelected(string.Empty);
        _goCargo.Click += (_, _) => RaiseGoToSelected(".Inventory");
        _goCustomisation.Click += (_, _) => { if (_selectedShipIndex >= 0) GoToJsonRequested?.Invoke(this, new GoToJsonRequestEventArgs($"PlayerStateData.CharacterCustomisationData[{StarshipLogic.ShipIndexToCcdIndex(_selectedShipIndex)}]")); };

        _class.ItemsSource = StarshipLogic.ShipClasses;
        RefreshTypeOptions();
        _corvetteActions.Children.Add(_snapshot); _corvetteActions.Children.Add(_importSnapshot); _corvetteActions.Children.Add(_optimise); _corvetteActions.Children.Add(_optimiseIndicator);
        Content = BuildLayout();
        ApplyUiLocalisation();
        SetEditorEnabled(false);
    }

    public InventoryGrid CargoGrid { get; }
    public InventoryGrid TechnologyGrid { get; }
    public int SelectedShipDataIndex => _selectedShipIndex;

    public Func<JsonObject, string, Task>? ExportShipAsync { get; set; }
    public Func<string[], Task<StarshipImportPayload?>>? ImportShipDataAsync { get; set; }
    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }
    public Func<string, string, Task>? ErrorAsync { get; set; }
    public Func<IReadOnlyList<string>, string, Task<int>>? SelectArchiveEntryAsync { get; set; }

    public event EventHandler? DataModified;
    public event EventHandler<GoToJsonRequestEventArgs>? GoToJsonRequested;
    public event EventHandler? CrossInventoryTransferCompleted;

    public void SetDatabase(GameItemDatabase? database) { CargoGrid.SetDatabase(database); TechnologyGrid.SetDatabase(database); }
    public void SetIconProvider(AvaloniaIconProvider? provider) { CargoGrid.SetIconProvider(provider); TechnologyGrid.SetIconProvider(provider); }
    public void SetSaveScopeKey(string? key) { _saveScopeKey = string.IsNullOrWhiteSpace(key) ? "unknown" : key; LoadPins(); }

    public void LoadData(JsonObject saveData)
    {
        _loading = true;
        try
        {
            _saveRoot = saveData;
            _playerState = ResolvePlayerState(saveData);
            _ships = _playerState?.GetArray("ShipOwnership");
            _primaryShipIndex = TryGetInt(_playerState, "PrimaryShip");
            RefreshShipList(_primaryShipIndex);
            UpdatePrimaryLabel();
        }
        finally { _loading = false; }
    }

    public void SaveData(JsonObject saveData)
    {
        JsonObject? player = ResolvePlayerState(saveData);
        if (player is null) return;
        CommitSelected(player);
        RawNumberGuard.SetInt(player, "PrimaryShip", _primaryShipIndex);
    }

    public void ApplyUiLocalisation()
    {
        _title.Text = UiStrings.Get("starship.title"); _detailsHeading.Text = UiStrings.Get("starship.details"); _statsHeading.Text = UiStrings.Get("starship.base_stats");
        _selectLabel.Text = UiStrings.Get("starship.select"); _nameLabel.Text = UiStrings.Get("starship.name"); _typeLabel.Text = UiStrings.Get("starship.type");
        _classLabel.Text = UiStrings.Get("starship.class"); _seedLabel.Text = UiStrings.Get("starship.seed"); _damageLabel.Text = UiStrings.Get("starship.damage");
        _shieldLabel.Text = UiStrings.Get("starship.shield"); _hyperdriveLabel.Text = UiStrings.Get("starship.hyperdrive"); _maneuverLabel.Text = UiStrings.Get("starship.maneuverability");
        _legacyColours.Content = UiStrings.Get("starship.use_old_colour"); _generateSeed.Content = UiStrings.Get("common.generate");
        _delete.Content = UiStrings.Get("starship.delete"); _copy.Content = UiStrings.Get("common.copy"); _export.Content = UiStrings.Get("starship.export");
        _swap.Content = UiStrings.Get("common.swap");
        _import.Content = UiStrings.Get("starship.import"); _makePrimary.Content = UiStrings.Get("starship.make_primary");
        _moveArchive.Content = UiStrings.Get("starship.archive_move_btn"); _importArchive.Content = UiStrings.Get("starship.archive_import_btn");
        _snapshot.Content = UiStrings.Get("starship.export_snapshot"); _importSnapshot.Content = UiStrings.Get("starship.import_snapshot"); _optimise.Content = UiStrings.Get("starship.optimise");
        _corvetteWarning.Text = UiStrings.Get("starship.corvette_warning");
        _cargoTab.Header = UiStrings.Get("starship.tab_cargo"); _techTab.Header = UiStrings.Get("starship.tab_tech");
        _detailsTab.Header = UiStrings.Get("starship.tab_ship_details"); _customisationTab.Header = UiStrings.Get("starship.tab_customisation");
        ToolTip.SetTip(_goList, UiStrings.Format("goto_json.tooltip_section", _title.Text));
        ToolTip.SetTip(_goSelected, UiStrings.Format("goto_json.tooltip_section", _detailsHeading.Text));
        ToolTip.SetTip(_goCargo, UiStrings.Format("goto_json.tooltip_section", _cargoTab.Header));
        ToolTip.SetTip(_goCustomisation, UiStrings.Format("goto_json.tooltip_section", _customisationTab.Header));
        RefreshTypeOptions(); CargoGrid.ApplyUiLocalisation(); TechnologyGrid.ApplyUiLocalisation(); ApplyCustomisationLocalisation(); UpdatePrimaryLabel();
    }

    public async Task<bool> MakeSelectedPrimaryAsync()
    {
        if (_ships is null || _playerState is null || _selectedShipIndex < 0) return false;
        JsonObject ship = _ships.GetObject(_selectedShipIndex);
        if (IsCorvette(ship) && !await ConfirmOperationAsync(UiStrings.Get("starship.corvette_warning_title"), UiStrings.Get("starship.corvette_primary_warning"))) return false;
        CommitSelected(_playerState); _primaryShipIndex = _selectedShipIndex; RawNumberGuard.SetInt(_playerState, "PrimaryShip", _primaryShipIndex);
        UpdatePrimaryLabel(); RaiseModified(); return true;
    }

    public async Task<bool> DeleteSelectedAsync()
    {
        if (_ships is null || _playerState is null || _selectedShipIndex < 0 || StarshipLogic.CountValidShips(_ships) <= 1) return false;
        if (!await ConfirmOperationAsync(UiStrings.Get("starship.delete_title"), UiStrings.Get("starship.delete_confirm"))) return false;
        int removed = _selectedShipIndex; JsonObject ship = _ships.GetObject(removed);
        if (IsCorvette(ship)) StarshipLogic.InvalidateCorvetteBase(_playerState.GetArray("PersistentPlayerBases"), removed);
        StarshipLogic.DeleteShipData(ship); StarshipLogic.ResetShipCustomisation(_playerState.GetArray("CharacterCustomisationData"), removed);
        if (_primaryShipIndex == removed) _primaryShipIndex = Math.Max(0, StarshipLogic.FindFirstValidShipIndex(_ships));
        RawNumberGuard.SetInt(_playerState, "PrimaryShip", _primaryShipIndex); RefreshShipList(_primaryShipIndex); RaiseModified(); return true;
    }

    public Task<bool> CopySelectedShipAsync()
    {
        if (_ships is null || _playerState is null || _selectedShipIndex < 0) return Task.FromResult(false);
        int empty = StarshipLogic.FindEmptySlot(_ships); if (empty < 0) return Task.FromResult(false);
        CommitSelected(_playerState); JsonObject clone = _ships.GetObject(_selectedShipIndex).DeepClone();
        JsonObject target = _ships.GetObject(empty); foreach (string key in clone.Names()) target.Set(key, clone.Get(key));
        JsonArray? legacy = _playerState.GetArray("ShipUsesLegacyColours"); if (legacy is not null && _selectedShipIndex < legacy.Length && empty < legacy.Length) legacy.Set(empty, legacy.Get(_selectedShipIndex));
        JsonObject? ccd = StarshipLogic.GetShipCustomisation(_playerState.GetArray("CharacterCustomisationData"), _selectedShipIndex);
        StarshipLogic.SetShipCustomisation(_playerState.GetArray("CharacterCustomisationData"), empty, ccd?.DeepClone());
        if (IsCorvette(_ships.GetObject(_selectedShipIndex)) && _playerState.GetArray("PersistentPlayerBases") is { } bases) { int baseIndex = StarshipLogic.FindCorvetteBaseIndex(bases, _selectedShipIndex); if (baseIndex >= 0) { JsonObject baseClone = bases.GetObject(baseIndex).DeepClone(); baseClone.Set("UserData", empty); bases.Add(baseClone); } }
        RefreshShipList(empty); RaiseModified(); return Task.FromResult(true);
    }

    public bool SelectShipByDataIndex(int dataIndex)
    {
        if (_selector.ItemsSource is not IEnumerable<StarshipLogic.ShipListItem> items) return false;
        StarshipLogic.ShipListItem? item = items.FirstOrDefault(value => value.DataIndex == dataIndex);
        if (item is null) return false;
        _selector.SelectedItem = item;
        return true;
    }

    public bool SwapSelectedWith(int otherIndex)
    {
        if (_ships is null || _playerState is null || _selectedShipIndex < 0 || otherIndex < 0 || otherIndex >= _ships.Length || otherIndex == _selectedShipIndex) return false;
        int first = _selectedShipIndex; CommitSelected(_playerState);
        JsonArray? bases = _playerState.GetArray("PersistentPlayerBases"); int firstBase = StarshipLogic.FindCorvetteBaseIndex(bases, first); int otherBase = StarshipLogic.FindCorvetteBaseIndex(bases, otherIndex);
        JsonObject a = _ships.GetObject(first).DeepClone(); JsonObject b = _ships.GetObject(otherIndex).DeepClone(); _ships.Set(first, b); _ships.Set(otherIndex, a);
        JsonArray? legacy = _playerState.GetArray("ShipUsesLegacyColours"); if (legacy is not null && first < legacy.Length && otherIndex < legacy.Length) { object? av = legacy.Get(first); object? bv = legacy.Get(otherIndex); legacy.Set(first, bv); legacy.Set(otherIndex, av); }
        JsonArray? ccdArray = _playerState.GetArray("CharacterCustomisationData"); JsonObject? ac = StarshipLogic.GetShipCustomisation(ccdArray, first)?.DeepClone(); JsonObject? bc = StarshipLogic.GetShipCustomisation(ccdArray, otherIndex)?.DeepClone(); StarshipLogic.SetShipCustomisation(ccdArray, first, bc); StarshipLogic.SetShipCustomisation(ccdArray, otherIndex, ac);
        if (bases is not null) { if (firstBase >= 0) bases.GetObject(firstBase).Set("UserData", otherIndex); if (otherBase >= 0) bases.GetObject(otherBase).Set("UserData", first); }
        if (_primaryShipIndex == first) _primaryShipIndex = otherIndex; else if (_primaryShipIndex == otherIndex) _primaryShipIndex = first;
        RefreshShipList(otherIndex); RaiseModified(); return true;
    }

    public async Task<bool> MoveSelectedToArchiveAsync()
    {
        if (_ships is null || _playerState is null || _selectedShipIndex < 0) return false;
        JsonObject ship = _ships.GetObject(_selectedShipIndex); if (IsCorvette(ship) || _selectedShipIndex == _primaryShipIndex || StarshipLogic.CountValidShips(_ships) <= 1) return false;
        JsonArray? archive = _playerState.GetArray("ArchivedShipOwnership"); int empty = archive is null ? -1 : StarshipLogic.FindEmptyArchivedShipSlot(archive); if (archive is null || empty < 0) return false;
        if (!await ConfirmOperationAsync(UiStrings.Get("starship.archive_move_title"), UiStrings.Get("starship.archive_move_confirm"))) return false;
        bool legacy = _playerState.GetArray("ShipUsesLegacyColours") is { } legacyArray && _selectedShipIndex < legacyArray.Length && legacyArray.Get(_selectedShipIndex) is true;
        CommitSelected(_playerState); StarshipLogic.MoveShipToArchive(ship, _selectedShipIndex, archive.GetObject(empty), _playerState.GetArray("CharacterCustomisationData"), legacy);
        RefreshShipList(_primaryShipIndex); RaiseModified(); return true;
    }

    public async Task<bool> ImportFromArchiveAsync(int listIndex = -1)
    {
        if (_ships is null || _playerState?.GetArray("ArchivedShipOwnership") is not { } archive) return false;
        List<StarshipLogic.ArchivedShipListItem> entries = StarshipLogic.BuildArchivedShipList(archive); int empty = StarshipLogic.FindEmptySlot(_ships);
        if (entries.Count == 0 || empty < 0) return false;
        if (listIndex < 0) listIndex = await SelectArchiveAsync(entries.Select(value => value.DisplayName).ToArray(), UiStrings.Get("starship.archive_import_title"));
        if (listIndex < 0 || listIndex >= entries.Count || !await ConfirmOperationAsync(UiStrings.Get("starship.archive_import_title"), UiStrings.Get("starship.archive_import_confirm"))) return false;
        StarshipLogic.ArchivedShipListItem selected = entries[listIndex]; StarshipLogic.ImportShipFromArchive(archive.GetObject(selected.ArchiveIndex), _ships.GetObject(empty), empty, _playerState.GetArray("CharacterCustomisationData"));
        RefreshShipList(empty); RaiseModified(); return true;
    }

    public async Task<bool> ExportSelectedAsync()
    {
        if (_ships is null || _playerState is null || _selectedShipIndex < 0) return false;
        CommitSelected(_playerState); JsonObject ship = _ships.GetObject(_selectedShipIndex); bool corvette = IsCorvette(ship);
        if (corvette && !CheckCorvetteSafety()) return false;
        JsonObject export = new(); export.Set("Ship", ship.DeepClone());
        if (corvette)
        {
            int baseIndex = StarshipLogic.FindCorvetteBaseIndex(_playerState.GetArray("PersistentPlayerBases"), _selectedShipIndex); if (baseIndex < 0) return false;
            export.Set("Base", _playerState.GetArray("PersistentPlayerBases")!.GetObject(baseIndex).DeepClone());
        }
        JsonObject? ccd = StarshipLogic.GetShipCustomisation(_playerState.GetArray("CharacterCustomisationData"), _selectedShipIndex); if (ccd is not null) export.Set("CharacterCustomisationData", ccd.DeepClone());
        string suggested = BuildExportFileName(corvette, snapshot: false);
        if (ExportShipAsync is not null) await ExportShipAsync(export, suggested);
        else if (_desktopServices is not null) { string ext = (corvette ? ExportConfig.Instance.CorvetteExt : ExportConfig.Instance.StarshipExt).TrimStart('.'); string? path = await _desktopServices.PickSaveFileAsync(UiStrings.Get("starship.export"), suggested, ext, new DesktopFileType("Starship", $"*.{ext}")); if (path is null) return false; export.ExportToFile(path); }
        else return false;
        return true;
    }

    public async Task<bool> ImportShipAsync()
    {
        if (_ships is null || _playerState is null) return false;
        StarshipImportPayload? payload = ImportShipDataAsync is not null
            ? await ImportShipDataAsync([$"*{ExportConfig.Instance.StarshipExt}", $"*{ExportConfig.Instance.CorvetteExt}", "*.nmsship"])
            : await PickAndReadShipAsync();
        if (payload is null) return false;
        int empty = StarshipLogic.FindEmptySlot(_ships); int targetIndex = empty >= 0 ? empty : _selectedShipIndex; if (targetIndex < 0) return false;
        bool overwrite = empty < 0; if (overwrite && IsCorvette(payload.Ship) && targetIndex == _primaryShipIndex) return false; if (overwrite && !await ConfirmOperationAsync(UiStrings.Get("starship.import_overwrite_title"), UiStrings.Get("starship.import_overwrite_confirm"))) return false;
        JsonObject target = _ships.GetObject(targetIndex); if (overwrite && IsCorvette(target)) StarshipLogic.InvalidateCorvetteBase(_playerState.GetArray("PersistentPlayerBases"), targetIndex);
        JsonObject imported = payload.Ship.DeepClone(); JsonObject? legacyCcd = imported.GetObject("__ShipCustomisation");
        foreach (string key in imported.Names()) if (key != "__ShipCustomisation") target.Set(key, imported.Get(key)); target.Remove("__ShipCustomisation");
        JsonObject? ccd = payload.Customisation is not null && !StarshipLogic.IsCcdDefault(payload.Customisation) ? payload.Customisation : legacyCcd;
        StarshipLogic.SetShipCustomisation(_playerState.GetArray("CharacterCustomisationData"), targetIndex, ccd?.DeepClone());
        if (payload.Base is not null && IsCorvette(target)) ImportCorvetteBase(payload.Base, targetIndex);
        RefreshShipList(targetIndex); RaiseModified(); return true;
    }

    public async Task<bool> ExportSnapshotAsync()
    {
        if (_ships is null || _playerState is null || _selectedShipIndex < 0 || !IsCorvette(_ships.GetObject(_selectedShipIndex)) || !CheckCorvetteSafety()) return false;
        CommitSelected(_playerState); JsonObject source = _ships.GetObject(_selectedShipIndex); JsonObject snapshot = new(); foreach (string key in source.Names()) if (key != "Inventory") snapshot.Set(key, source.Get(key) switch { JsonObject v => v.DeepClone(), JsonArray v => v.DeepClone(), object v => v, _ => null });
        JsonObject wrapper = new(); wrapper.Set("Ship", snapshot); int baseIndex = StarshipLogic.FindCorvetteBaseIndex(_playerState.GetArray("PersistentPlayerBases"), _selectedShipIndex); if (baseIndex >= 0) wrapper.Set("Base", _playerState.GetArray("PersistentPlayerBases")!.GetObject(baseIndex).DeepClone());
        string suggested = BuildExportFileName(true, snapshot: true); if (ExportShipAsync is not null) await ExportShipAsync(wrapper, suggested); else if (_desktopServices is not null) { string ext = ExportConfig.Instance.CorvetteSnapshotExt.TrimStart('.'); string? path = await _desktopServices.PickSaveFileAsync(UiStrings.Get("starship.export_snapshot"), suggested, ext, new DesktopFileType("Corvette snapshot", $"*.{ext}")); if (path is null) return false; wrapper.ExportToFile(path); } else return false; return true;
    }

    public async Task<bool> ImportSnapshotAsync()
    {
        if (_ships is null || _selectedShipIndex < 0 || !IsCorvette(_ships.GetObject(_selectedShipIndex)) || !CheckCorvetteSafety()) return false;
        StarshipImportPayload? payload = ImportShipDataAsync is not null ? await ImportShipDataAsync([$"*{ExportConfig.Instance.CorvetteSnapshotExt}"]) : await PickAndReadSnapshotAsync(); if (payload is null) return false;
        JsonObject target = _ships.GetObject(_selectedShipIndex); JsonObject cargo = target.GetObject("Inventory")!; foreach (string key in payload.Ship.Names()) if (key != "Inventory") target.Set(key, payload.Ship.Get(key) switch { JsonObject v => v.DeepClone(), JsonArray v => v.DeepClone(), object v => v, _ => null }); target.Set("Inventory", cargo);
        if (payload.Base is not null) ImportCorvetteBase(payload.Base, _selectedShipIndex); RefreshShipList(_selectedShipIndex); RaiseModified(); return true;
    }

    public int OptimiseSelectedCorvette()
    {
        if (_playerState is null || _selectedShipIndex < 0) return -1; int result = StarshipLogic.OptimiseCorvetteBase(_playerState.GetArray("PersistentPlayerBases"), _selectedShipIndex); UpdateOptimiseIndicator(); if (result >= 0) RaiseModified(); return result;
    }

    private Control BuildLayout()
    {
        var go = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _goList, _goSelected, _goCargo, _goCustomisation } };
        var titleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { _title, _primaryLabel } }, go } }; Grid.SetColumn(go, 1);
        var selector = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 7, Children = { _selectLabel, _selector, _moveArchive, _importArchive } }; Grid.SetColumn(_selector, 1); Grid.SetColumn(_moveArchive, 2); Grid.SetColumn(_importArchive, 3);
        var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto"), ColumnSpacing = 8, RowSpacing = 6 };
        fields.Children.Add(_detailsHeading); Grid.SetColumnSpan(_detailsHeading, 2); fields.Children.Add(_statsHeading); Grid.SetColumn(_statsHeading, 2); Grid.SetColumnSpan(_statsHeading, 2);
        AddField(fields, _nameLabel, _name, 1, 0); AddField(fields, _typeLabel, _type, 2, 0); AddField(fields, _classLabel, _class, 3, 0);
        var seedArea = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 5, Children = { _seed, _generateSeed } }; Grid.SetColumn(_generateSeed, 1); AddField(fields, _seedLabel, seedArea, 4, 0);
        AddField(fields, _damageLabel, _damage, 1, 2); AddField(fields, _shieldLabel, _shield, 2, 2); AddField(fields, _hyperdriveLabel, _hyperdrive, 3, 2); AddField(fields, _maneuverLabel, _maneuver, 4, 2); fields.Children.Add(_legacyColours); Grid.SetRow(_legacyColours, 5); Grid.SetColumn(_legacyColours, 3);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { _delete, _copy, _swap, _export, _import, _makePrimary, _corvetteWarning } };
        var upper = new StackPanel { Spacing = 8, Children = { fields, buttons, _corvetteActions } };
        _cargoTab.Content = CargoGrid; _techTab.Content = TechnologyGrid; _inventoryTabs.Items.Add(_cargoTab); _inventoryTabs.Items.Add(_techTab);
        var detailsGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { upper, _inventoryTabs } }; Grid.SetRow(_inventoryTabs, 1); _detailsTab.Content = detailsGrid;
        _customisationTab.Content = BuildCustomisationLayout(); _outerTabs.Items.Add(_detailsTab); _outerTabs.Items.Add(_customisationTab);
        var root = new Grid { Margin = new Thickness(12), RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 9, Children = { titleRow, selector, _outerTabs } }; Grid.SetRow(selector, 1); Grid.SetRow(_outerTabs, 2); return root;
    }

    private static Button JsonNavigationButton() => new()
    {
        Content = "JSON",
        MinWidth = 48,
        Padding = new Thickness(8, 4),
    };

    private static void AddField(Grid grid, TextBlock label, Control field, int row, int column) { label.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(label); Grid.SetRow(label, row); Grid.SetColumn(label, column); grid.Children.Add(field); Grid.SetRow(field, row); Grid.SetColumn(field, column + 1); }
    private void OnShipSelectionChanged(object? sender, SelectionChangedEventArgs e) { if (_loading || _selector.SelectedItem is not StarshipLogic.ShipListItem item) return; if (_selectedShipIndex >= 0) CommitSelected(_playerState); LoadShip(item.DataIndex); }
    private void LoadShip(int index)
    {
        if (_ships is null || index < 0 || index >= _ships.Length) return; _loading = true;
        try
        {
            _selectedShipIndex = index; JsonObject ship = _ships.GetObject(index); StarshipLogic.ShipData data = StarshipLogic.LoadShipData(ship, _playerState, index);
            _name.Text = data.Name; SelectType(data.ShipTypeName, data.IsResourceModified ? data.Filename : null); _class.SelectedIndex = data.ClassIndex; _originalClassIndex = data.ClassIndex; _seed.Text = data.Seed; _legacyColours.IsChecked = data.UseOldColours;
            _damage.SetValueWithText(data.Damage, data.DamageText); _shield.SetValueWithText(data.Shield, data.ShieldText); _hyperdrive.SetValueWithText(data.Hyperdrive, data.HyperdriveText); _maneuver.SetValueWithText(data.Maneuver, data.ManeuverText);
            _rawStats = new Dictionary<string, double> { ["^SHIP_DAMAGE"] = data.Damage, ["^SHIP_SHIELD"] = data.Shield, ["^SHIP_HYPERDRIVE"] = data.Hyperdrive, ["^SHIP_AGILE"] = data.Maneuver };
            string owner = StarshipLogic.GetOwnerTypeForShip(data.ShipTypeName); CargoGrid.SetInventoryOwnerType(owner); TechnologyGrid.SetInventoryOwnerType(owner);
            CargoGrid.LoadInventory(data.Inventory); TechnologyGrid.SetCorvetteContext(StarshipLogic.IsCorvette(data.Filename) ? _saveRoot : null, index); TechnologyGrid.LoadInventory(data.TechInventory);
            CargoGrid.SetMaxSupportedLabel(data.CargoMaxLabel); TechnologyGrid.SetMaxSupportedLabel(data.TechMaxLabel); CargoGrid.SetExportFileName(data.InvExportFileName); TechnologyGrid.SetExportFileName(data.TechExportFileName); LoadPins();
            CargoGrid.SetExportFileFilter(string.Empty, string.Empty, ExportConfig.Instance.StarshipCargoExt); TechnologyGrid.SetExportFileFilter(string.Empty, string.Empty, ExportConfig.Instance.StarshipTechExt);
            bool corvette = StarshipLogic.IsCorvette(data.Filename); _corvetteActions.IsVisible = corvette; _corvetteWarning.IsVisible = corvette; UpdateOptimiseIndicator(); LoadCustomisation(data.Filename, index, corvette); SetEditorEnabled(true);
        }
        finally { _loading = false; }
    }

    private void CommitSelected(JsonObject? player)
    {
        if (_ships is null || player is null || _selectedShipIndex < 0 || _selectedShipIndex >= _ships.Length) return; _damage.TryCommit(); _shield.TryCommit(); _hyperdrive.TryCommit(); _maneuver.TryCommit();
        StarshipLogic.ShipTypeItem? type = _type.SelectedItem as StarshipLogic.ShipTypeItem;
        var values = new StarshipLogic.ShipSaveValues { Name = _name.Text ?? string.Empty, SelectedTypeName = type?.InternalName, CustomFilename = type?.CustomFilename, ClassIndex = _class.SelectedIndex, OriginalClassIndex = _originalClassIndex, Seed = _seed.Text ?? string.Empty,
            Damage = CurrentStat(_damage, "^SHIP_DAMAGE"), Shield = CurrentStat(_shield, "^SHIP_SHIELD"), Hyperdrive = CurrentStat(_hyperdrive, "^SHIP_HYPERDRIVE"), Maneuver = CurrentStat(_maneuver, "^SHIP_AGILE"),
            DamageText = _damage.UserModified ? _damage.DisplayText : null, ShieldText = _shield.UserModified ? _shield.DisplayText : null, HyperdriveText = _hyperdrive.UserModified ? _hyperdrive.DisplayText : null, ManeuverText = _maneuver.UserModified ? _maneuver.DisplayText : null,
            UseOldColours = _legacyColours.IsChecked == true, ShipIndex = _selectedShipIndex, PrimaryShipIndex = _primaryShipIndex, RawStatValues = _rawStats };
        JsonObject ship = _ships.GetObject(_selectedShipIndex); StarshipLogic.SaveShipData(ship, player, values); CargoGrid.SaveInventory(ship.GetObject("Inventory")); TechnologyGrid.SaveInventory(ship.GetObject("Inventory_TechOnly")); SaveCustomisation(player, _selectedShipIndex); _originalClassIndex = _class.SelectedIndex;
    }

    private double CurrentStat(InvariantNumericTextBox field, string key) => field.UserModified ? field.NumericValue ?? 0 : _rawStats?.GetValueOrDefault(key) ?? field.NumericValue ?? 0;
    private void RefreshShipList(int selectIndex) { _loading = true; try { List<StarshipLogic.ShipListItem> list = _ships is null ? [] : StarshipLogic.BuildShipList(_ships); _selector.ItemsSource = list; StarshipLogic.ShipListItem? selected = list.FirstOrDefault(item => item.DataIndex == selectIndex) ?? list.FirstOrDefault(); _selector.SelectedItem = selected; if (selected is not null) LoadShip(selected.DataIndex); else { _selectedShipIndex = -1; SetEditorEnabled(false); CargoGrid.LoadInventory(null); TechnologyGrid.LoadInventory(null); } } finally { _loading = false; } }
    private void RefreshTypeOptions() { StarshipLogic.ShipTypeItem? old = _type.SelectedItem as StarshipLogic.ShipTypeItem; var list = StarshipLogic.GetShipTypeItems().ToList(); if (old?.CustomFilename is not null) list.Add(new StarshipLogic.ShipTypeItem(old.InternalName, UiStrings.Format("starship.type_modified", StarshipLogic.GetLocalisedShipTypeName(old.InternalName)), old.CustomFilename)); _type.ItemsSource = list; if (old is not null) SelectType(old.InternalName, old.CustomFilename); }
    private void SelectType(string name, string? customFilename) { if (_type.ItemsSource is not IEnumerable<StarshipLogic.ShipTypeItem> source) return; if (customFilename is not null && !source.Any(item => item.CustomFilename == customFilename)) { List<StarshipLogic.ShipTypeItem> list = source.ToList(); list.Add(new StarshipLogic.ShipTypeItem(name, UiStrings.Format("starship.type_modified", StarshipLogic.GetLocalisedShipTypeName(name)), customFilename)); _type.ItemsSource = list; source = list; } _type.SelectedItem = source.FirstOrDefault(item => item.InternalName == name && item.CustomFilename == customFilename) ?? source.FirstOrDefault(item => item.InternalName == name); }
    private void OnTypeChanged() { if (_loading || _type.SelectedItem is not StarshipLogic.ShipTypeItem selected) return; string filename = selected.CustomFilename ?? StarshipLogic.LookupFilenameForType(selected.InternalName); CargoGrid.SetInventoryOwnerType(StarshipLogic.GetOwnerTypeForShip(selected.InternalName)); TechnologyGrid.SetInventoryOwnerType(StarshipLogic.GetOwnerTypeForShip(selected.InternalName)); if (_ships is not null && _selectedShipIndex >= 0) _ships.GetObject(_selectedShipIndex).GetObject("Resource")?.Set("Filename", filename); LoadCustomisation(filename, _selectedShipIndex, StarshipLogic.IsCorvette(filename)); RaiseModified(); }
    private void AutoStack(AutoStackDestination destination, AutoStackSlotRequestEventArgs? request) { if (_playerState is null || CargoGrid.LoadedInventory is not { } cargo) return; var pins = new HashSet<(int x, int y)>(CargoGrid.PinnedSlots); if (request is not null && pins.Contains((request.X, request.Y))) return; bool changed = destination == AutoStackDestination.Storage ? InventoryBulkActions.AutoStackCargoToChests(cargo, _playerState, out _, out _, pins, request is null ? null : (request.X, request.Y), request?.ItemId) : _playerState.GetObject("FreighterInventory") is { } freighter && InventoryBulkActions.AutoStackFromInventoryToInventory(cargo, freighter, out _, out _, pins, request is null ? null : (request.X, request.Y), request?.ItemId); if (changed) { CargoGrid.LoadInventory(cargo); LoadPins(); RaiseModified(); CrossInventoryTransferCompleted?.Invoke(this, EventArgs.Empty); } }
    private void LoadPins() => CargoGrid.SetPinnedSlots(AppConfig.Instance.GetPinnedSlots(_saveScopeKey, $"StarshipCargo:{_selectedShipIndex}"));
    private void SavePins() { if (_selectedShipIndex >= 0) AppConfig.Instance.SetPinnedSlots(_saveScopeKey, $"StarshipCargo:{_selectedShipIndex}", CargoGrid.PinnedSlots); }
    private void UpdatePrimaryLabel() => _primaryLabel.Text = UiStrings.Format("starship.primary_label", StarshipLogic.GetPrimaryShipName(_ships, _primaryShipIndex));
    private bool CheckCorvetteSafety() => _selectedShipIndex != _primaryShipIndex;
    private static bool IsCorvette(JsonObject ship) => StarshipLogic.IsCorvette(ship.GetObject("Resource")?.GetString("Filename") ?? string.Empty);
    private void UpdateOptimiseIndicator() { bool ok = _playerState is null || _selectedShipIndex < 0 || StarshipLogic.IsCorvetteOptimised(_playerState.GetArray("PersistentPlayerBases"), _selectedShipIndex); _optimiseIndicator.Text = ok ? "✔" : "✘"; _optimiseIndicator.Foreground = ok ? global::Avalonia.Media.Brushes.Green : global::Avalonia.Media.Brushes.OrangeRed; }
    private void ImportCorvetteBase(JsonObject importedBase, int targetIndex) { if (_playerState?.GetArray("PersistentPlayerBases") is not { } bases) return; int existing = StarshipLogic.FindCorvetteBaseIndex(bases, targetIndex); JsonObject clone = importedBase.DeepClone(); clone.Set("UserData", targetIndex); if (existing >= 0) { JsonObject target = bases.GetObject(existing); foreach (string key in clone.Names()) target.Set(key, clone.Get(key)); } else bases.Add(clone.GetObject("BaseType") is null ? StarshipLogic.CreatePlayerShipBase(targetIndex, clone.GetArray("Objects") ?? new JsonArray()) : clone); }
    private string BuildExportFileName(bool corvette, bool snapshot) { string type = (_type.SelectedItem as StarshipLogic.ShipTypeItem)?.InternalName ?? string.Empty; string cls = _class.SelectedIndex is >= 0 and < 4 ? StarshipLogic.ShipClasses[_class.SelectedIndex] : "C"; ExportConfig cfg = ExportConfig.Instance; string template = snapshot ? cfg.CorvetteSnapshotTemplate : corvette ? cfg.CorvetteTemplate : cfg.StarshipTemplate; string ext = snapshot ? cfg.CorvetteSnapshotExt : corvette ? cfg.CorvetteExt : cfg.StarshipExt; return ExportConfig.BuildFileName(template, ext, new Dictionary<string, string> { ["ship_name"] = _name.Text ?? string.Empty, ["type"] = type, ["class"] = cls }); }
    private async Task<StarshipImportPayload?> PickAndReadShipAsync() { if (_desktopServices is null) return null; IReadOnlyList<string> paths = await _desktopServices.PickFilesAsync(UiStrings.Get("starship.import"), false, new DesktopFileType("Starship", $"*{ExportConfig.Instance.StarshipExt}", $"*{ExportConfig.Instance.CorvetteExt}", "*.nmsship")); return paths.Count == 0 ? null : ReadShipPayload(paths[0]); }
    private async Task<StarshipImportPayload?> PickAndReadSnapshotAsync() { if (_desktopServices is null) return null; IReadOnlyList<string> paths = await _desktopServices.PickFilesAsync(UiStrings.Get("starship.import_snapshot"), false, new DesktopFileType("Corvette snapshot", $"*{ExportConfig.Instance.CorvetteSnapshotExt}")); return paths.Count == 0 ? null : ReadShipPayload(paths[0]); }
    private static StarshipImportPayload? ReadShipPayload(string path) { var zip = StarshipLogic.TryReadNmsshipZip(path); if (zip is not null) { JsonObject? baseData = zip.Value.objects is null ? null : new JsonObject(); baseData?.Set("Objects", zip.Value.objects); return new StarshipImportPayload(zip.Value.ship, baseData, zip.Value.ccd); } JsonObject root = JsonObject.ImportFromFile(path); JsonObject? ship = root.GetObject("Ship"); if (ship is not null) return new StarshipImportPayload(ship, root.GetObject("Base"), root.GetObject("CharacterCustomisationData")); if (InventoryImportHelper.IsNomNomWrapper(root)) ship = root.GetObject("Data")?.GetObject("Starship") ?? root.GetObject("Data")?.GetObject("Ship"); return new StarshipImportPayload(ship ?? root); }
    private async Task ExportInventoryAsync(JsonObject inventory, string name) { if (_desktopServices is null) return; string ext = Path.GetExtension(name).TrimStart('.'); string? path = await _desktopServices.PickSaveFileAsync(UiStrings.Get("common.export"), name, ext, new DesktopFileType("Starship inventory", $"*.{ext}")); if (path is not null) await File.WriteAllTextAsync(path, JsonParser.Serialize(inventory, true, skipReverseMapping: true)); }
    private async Task<JsonObject?> ImportInventoryAsync(string ext) { if (_desktopServices is null) return null; IReadOnlyList<string> paths = await _desktopServices.PickFilesAsync(UiStrings.Get("common.import"), false, new DesktopFileType("Starship inventory", $"*.{ext}")); return paths.Count == 0 ? null : JsonObject.ImportFromFile(paths[0]); }
    private async Task<bool> ConfirmOperationAsync(string title, string message) => ConfirmAsync is not null ? await ConfirmAsync(title, message) : _desktopServices is not null && await _desktopServices.ConfirmAsync(title, message);
    private Task ShowErrorAsync(string title, string message) => ErrorAsync is not null ? ErrorAsync(title, message) : _desktopServices?.ShowErrorAsync(title, message) ?? Task.CompletedTask;
    private async Task RunUiOperation(Func<Task> action) { try { await action(); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException) { await ShowErrorAsync(UiStrings.Get("common.error"), exception.Message); } }
    private async Task<int> SelectArchiveAsync(IReadOnlyList<string> entries, string title) => SelectArchiveEntryAsync is not null ? await SelectArchiveEntryAsync(entries, title) : TopLevel.GetTopLevel(this) is Window owner ? await StarshipSelectionDialog.ShowAsync(owner, entries, title) : -1;
    private async Task ChooseSwapTargetAsync() { if (_selector.ItemsSource is not IEnumerable<StarshipLogic.ShipListItem> source) return; StarshipLogic.ShipListItem[] items = source.Where(item => item.DataIndex != _selectedShipIndex).ToArray(); if (items.Length == 0) return; int selected = await SelectArchiveAsync(items.Select(item => item.DisplayName).ToArray(), UiStrings.Get("common.swap")); if (selected >= 0 && selected < items.Length) SwapSelectedWith(items[selected].DataIndex); }
    private void RaiseGoToSelected(string suffix) { if (_selectedShipIndex >= 0) GoToJsonRequested?.Invoke(this, new GoToJsonRequestEventArgs($"PlayerStateData.ShipOwnership[{_selectedShipIndex}]{suffix}")); }
    private void RefreshSelectedLabel() { if (_selector.SelectedItem is not StarshipLogic.ShipListItem item) return; string cls = _class.SelectedIndex is >= 0 and < 4 ? StarshipLogic.ShipClasses[_class.SelectedIndex] : "?"; string type = (_type.SelectedItem as StarshipLogic.ShipTypeItem)?.InternalName ?? "Ship"; item.DisplayName = string.IsNullOrWhiteSpace(_name.Text) ? $"[{item.DataIndex + 1}] {type} - {cls}" : $"[{item.DataIndex + 1}] {_name.Text} - {cls}"; _selector.InvalidateVisual(); }
    private void Changed() { if (!_loading) RaiseModified(); }
    private void RaiseModified() => DataModified?.Invoke(this, EventArgs.Empty);
    private void SetEditorEnabled(bool enabled) { foreach (Control control in new Control[] { _name, _type, _class, _seed, _legacyColours, _damage, _shield, _hyperdrive, _maneuver, _generateSeed, _delete, _copy, _swap, _export, _makePrimary, _moveArchive }) control.IsEnabled = enabled; }
    private static int TryGetInt(JsonObject? obj, string key) { try { return obj?.GetInt(key) ?? 0; } catch { return 0; } }
    private static JsonObject? ResolvePlayerState(JsonObject root) => root.GetObject("PlayerStateData") ?? root.GetObject("SaveData")?.GetObject("PlayerStateData") ?? (root.Contains("ShipOwnership") ? root : null);
    private enum AutoStackDestination { Storage, Freighter }
}
