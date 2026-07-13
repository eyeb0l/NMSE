using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using NMSE.Config;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Contracts;
using NMSE.UI.Avalonia.Inventory;
using NMSE.UI.Avalonia.Platform;
using NMSE.UI.Avalonia.Services;

namespace NMSE.UI.Avalonia.Panels.Exosuit;

/// <summary>Complete cargo and technology editing slice for the player's Exosuit.</summary>
public sealed class ExosuitPanel : UserControl, ISaveEditorPanel, IGoToJsonSource
{
    private readonly TabControl _tabs = new();
    private readonly TabItem _cargoTab = new();
    private readonly TabItem _technologyTab = new();
    private readonly TextBlock _title = new() { FontSize = 20, FontWeight = global::Avalonia.Media.FontWeight.SemiBold };
    private readonly Button _goToJson = new();
    private readonly IDesktopServices? _desktopServices;
    private JsonObject? _playerState;
    private string _saveScopeKey = "unknown";

    public ExosuitPanel(
        GameItemDatabase? database = null,
        AvaloniaIconProvider? iconProvider = null,
        IDesktopServices? desktopServices = null,
        Func<JsonObject, string, Task>? exportInventory = null,
        Func<string, Task<JsonObject?>>? importInventory = null)
    {
        _desktopServices = desktopServices;
        exportInventory ??= desktopServices is null ? null : ExportInventoryAsync;
        importInventory ??= desktopServices is null ? null : ImportInventoryAsync;
        CargoGrid = CreateCargoGrid();
        TechnologyGrid = CreateTechnologyGrid();
        CargoGrid.SetDatabase(database);
        TechnologyGrid.SetDatabase(database);
        CargoGrid.SetIconProvider(iconProvider);
        TechnologyGrid.SetIconProvider(iconProvider);
        CargoGrid.ConfigureTransfer(exportInventory, importInventory);
        TechnologyGrid.ConfigureTransfer(exportInventory, importInventory);

        CargoGrid.DataModified += BubbleModified;
        TechnologyGrid.DataModified += BubbleModified;
        CargoGrid.PinnedSlotsChanged += OnPinnedSlotsChanged;
        CargoGrid.AutoStackToStorageRequested += (_, _) => AutoStackAll(AutoStackDestination.Storage);
        CargoGrid.AutoStackToStarshipRequested += (_, _) => AutoStackAll(AutoStackDestination.Starship);
        CargoGrid.AutoStackToFreighterRequested += (_, _) => AutoStackAll(AutoStackDestination.Freighter);
        CargoGrid.AutoStackSelectedSlotToStorageRequested += (_, args) => AutoStackSelected(AutoStackDestination.Storage, args);
        CargoGrid.AutoStackSelectedSlotToStarshipRequested += (_, args) => AutoStackSelected(AutoStackDestination.Starship, args);
        CargoGrid.AutoStackSelectedSlotToFreighterRequested += (_, args) => AutoStackSelected(AutoStackDestination.Freighter, args);
        CargoGrid.RefreshToolbarActions();
        PinnedAutoStackBlocked += async (_, _) =>
        {
            if (_desktopServices is not null)
                await _desktopServices.ShowErrorAsync(UiStrings.Get("dialog.info"), UiStrings.Get("inventory.auto_stack_pinned_slot_blocked"));
        };

        _cargoTab.Content = CargoGrid;
        _technologyTab.Content = TechnologyGrid;
        _tabs.Items.Add(_cargoTab);
        _tabs.Items.Add(_technologyTab);
        _tabs.SelectedIndex = 0;
        _goToJson.Click += (_, _) => RaiseGoToJson();

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 8),
            Children = { _title, _goToJson },
        };
        Grid.SetColumn(_goToJson, 1);
        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(12),
            Children = { header, _tabs },
        };
        Grid.SetRow(_tabs, 1);
        Content = layout;
        ApplyUiLocalisation();
    }

    public InventoryGrid CargoGrid { get; }
    public InventoryGrid TechnologyGrid { get; }

    public event EventHandler? DataModified;
    public event EventHandler<GoToJsonRequestEventArgs>? GoToJsonRequested;
    public event EventHandler? CrossInventoryTransferCompleted;
    public event EventHandler<AutoStackSlotRequestEventArgs>? PinnedAutoStackBlocked;

    public void SetDatabase(GameItemDatabase? database)
    {
        CargoGrid.SetDatabase(database);
        TechnologyGrid.SetDatabase(database);
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
        CargoGrid.ConfigureTransfer(exportInventory, importInventory);
        TechnologyGrid.ConfigureTransfer(exportInventory, importInventory);
    }

    public void SetSaveScopeKey(string? saveScopeKey)
    {
        _saveScopeKey = string.IsNullOrWhiteSpace(saveScopeKey) ? "unknown" : saveScopeKey;
        LoadPinnedSlots();
    }

    public void LoadData(JsonObject saveData)
    {
        ArgumentNullException.ThrowIfNull(saveData);
        _playerState = ResolvePlayerState(saveData);
        CargoGrid.LoadInventory(_playerState?.GetObject(ExosuitLogic.CargoInventoryKey));
        TechnologyGrid.LoadInventory(_playerState?.GetObject(ExosuitLogic.TechInventoryKey));
        LoadPinnedSlots();
    }

    public void SaveData(JsonObject saveData)
    {
        ArgumentNullException.ThrowIfNull(saveData);
        JsonObject? playerState = ResolvePlayerState(saveData);
        if (playerState is null)
            return;
        CargoGrid.SaveInventory(playerState.GetObject(ExosuitLogic.CargoInventoryKey));
        TechnologyGrid.SaveInventory(playerState.GetObject(ExosuitLogic.TechInventoryKey));
    }

    public void ApplyUiLocalisation()
    {
        _title.Text = UiStrings.Get("exosuit.title");
        _cargoTab.Header = UiStrings.Get("common.cargo");
        _technologyTab.Header = UiStrings.Get("common.technology");
        _goToJson.Content = UiStrings.Get("goto_json.tooltip");
        CargoGrid.SetMaxSupportedLabel(ExosuitLogic.CargoMaxLabel);
        TechnologyGrid.SetMaxSupportedLabel(ExosuitLogic.TechMaxLabel);
        CargoGrid.ApplyUiLocalisation();
        TechnologyGrid.ApplyUiLocalisation();
    }

    public void RefreshAfterExternalTransfer()
    {
        if (_playerState is null)
            return;
        CargoGrid.LoadInventory(_playerState.GetObject(ExosuitLogic.CargoInventoryKey));
        TechnologyGrid.LoadInventory(_playerState.GetObject(ExosuitLogic.TechInventoryKey));
        LoadPinnedSlots();
    }

    private static InventoryGrid CreateCargoGrid()
    {
        var grid = new InventoryGrid();
        grid.SetInventoryGroup("PersonalCargo");
        grid.SetInventoryOwnerType("Suit");
        grid.SetIsCargoInventory(true);
        grid.SetSortingEnabled(true);
        grid.SetPinSlotFeatureEnabled(true);
        grid.SetSuperchargeDisabled(true);
        string extension = ExportConfig.Instance.ExosuitExt;
        grid.SetExportFileName($"exosuit_cargo_inv{extension}");
        grid.SetExportFileFilter(string.Empty, string.Empty, extension);
        return grid;
    }

    private static InventoryGrid CreateTechnologyGrid()
    {
        var grid = new InventoryGrid();
        grid.SetInventoryGroup("Personal");
        grid.SetInventoryOwnerType("Suit");
        grid.SetIsTechInventory(true);
        grid.SetSortingEnabled(false);
        string extension = ExportConfig.Instance.ExosuitExt;
        grid.SetExportFileName($"exosuit_tech_inv{extension}");
        grid.SetExportFileFilter(string.Empty, string.Empty, extension);
        return grid;
    }

    private void AutoStackAll(AutoStackDestination destination)
    {
        if (_playerState is null || CargoGrid.LoadedInventory is not { } cargo)
            return;
        var pinned = new HashSet<(int x, int y)>(CargoGrid.PinnedSlots);
        bool changed = destination switch
        {
            AutoStackDestination.Storage => InventoryBulkActions.AutoStackCargoToChests(cargo, _playerState, out _, out _, pinned),
            AutoStackDestination.Starship => InventoryBulkActions.AutoStackCargoToStarship(cargo, _playerState, out _, out _, pinned),
            AutoStackDestination.Freighter => InventoryBulkActions.AutoStackCargoToFreighter(cargo, _playerState, out _, out _, pinned),
            _ => false,
        };
        CompleteTransfer(changed, cargo);
    }

    private void AutoStackSelected(AutoStackDestination destination, AutoStackSlotRequestEventArgs request)
    {
        if (_playerState is null || CargoGrid.LoadedInventory is not { } cargo)
            return;
        var pinned = new HashSet<(int x, int y)>(CargoGrid.PinnedSlots);
        if (pinned.Contains((request.X, request.Y)))
        {
            PinnedAutoStackBlocked?.Invoke(this, request);
            return;
        }
        bool changed = destination switch
        {
            AutoStackDestination.Storage => InventoryBulkActions.AutoStackCargoToChests(
                cargo, _playerState, out _, out _, pinned, (request.X, request.Y), request.ItemId),
            AutoStackDestination.Starship => InventoryBulkActions.AutoStackCargoToStarship(
                cargo, _playerState, out _, out _, pinned, (request.X, request.Y), request.ItemId),
            AutoStackDestination.Freighter => InventoryBulkActions.AutoStackCargoToFreighter(
                cargo, _playerState, out _, out _, pinned, (request.X, request.Y), request.ItemId),
            _ => false,
        };
        CompleteTransfer(changed, cargo);
    }

    private void CompleteTransfer(bool changed, JsonObject cargo)
    {
        if (!changed)
            return;
        CargoGrid.LoadInventory(cargo);
        LoadPinnedSlots();
        DataModified?.Invoke(this, EventArgs.Empty);
        CrossInventoryTransferCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void LoadPinnedSlots()
    {
        CargoGrid.SetPinnedSlots(AppConfig.Instance.GetPinnedSlots(_saveScopeKey, "ExosuitCargo"));
    }

    private void OnPinnedSlotsChanged(object? sender, EventArgs e)
    {
        AppConfig.Instance.SetPinnedSlots(_saveScopeKey, "ExosuitCargo", CargoGrid.PinnedSlots);
    }

    private void BubbleModified(object? sender, EventArgs e) => DataModified?.Invoke(this, EventArgs.Empty);

    private async Task ExportInventoryAsync(JsonObject inventory, string suggestedName)
    {
        if (_desktopServices is null)
            return;
        string extension = Path.GetExtension(suggestedName).TrimStart('.');
        string? path = await _desktopServices.PickSaveFileAsync(
            UiStrings.Get("inventory.export_title"),
            suggestedName,
            extension,
            new DesktopFileType("No Man's Sky inventory", $"*.{extension}"),
            new DesktopFileType("JSON", "*.json"));
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            string json = JsonParser.Serialize(inventory, true, skipReverseMapping: true);
            await File.WriteAllTextAsync(path, json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await _desktopServices.ShowErrorAsync(UiStrings.Get("inventory.export_error_title"), exception.Message);
        }
    }

    private async Task<JsonObject?> ImportInventoryAsync(string extension)
    {
        if (_desktopServices is null)
            return null;
        IReadOnlyList<string> files = await _desktopServices.PickFilesAsync(
            UiStrings.Get("inventory.import_title"),
            false,
            new DesktopFileType("No Man's Sky inventory", $"*.{extension}"),
            new DesktopFileType("JSON", "*.json"));
        string? path = files.FirstOrDefault();
        if (path is null)
            return null;
        try
        {
            return JsonParser.ParseObject(await File.ReadAllTextAsync(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or JsonException)
        {
            await _desktopServices.ShowErrorAsync(UiStrings.Get("inventory.import_error_title"), exception.Message);
            return null;
        }
    }

    private void RaiseGoToJson()
    {
        string inventoryKey = _tabs.SelectedIndex == 1 ? ExosuitLogic.TechInventoryKey : ExosuitLogic.CargoInventoryKey;
        GoToJsonRequested?.Invoke(this, new GoToJsonRequestEventArgs($"PlayerStateData.{inventoryKey}"));
    }

    private static JsonObject? ResolvePlayerState(JsonObject saveData)
    {
        JsonObject? player = saveData.GetObject("PlayerStateData");
        if (player is not null)
            return player;
        JsonObject? nested = saveData.GetObject("SaveData")?.GetObject("PlayerStateData");
        if (nested is not null)
            return nested;
        return saveData.Contains(ExosuitLogic.CargoInventoryKey) || saveData.Contains(ExosuitLogic.TechInventoryKey) ? saveData : null;
    }

    private enum AutoStackDestination
    {
        Storage,
        Starship,
        Freighter,
    }
}
