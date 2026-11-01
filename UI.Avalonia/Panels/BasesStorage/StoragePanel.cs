using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Contracts;
using NMSE.UI.Avalonia.Inventory;
using NMSE.UI.Avalonia.Services;

namespace NMSE.UI.Avalonia.Panels.BasesStorage;

public sealed class StoragePanel : UserControl, ISaveEditorPanel, IGoToJsonSource
{
    private sealed class Entry(string key, string exportFile, string localisationKey, InventoryGrid grid, TabItem page)
    {
        public string Key { get; } = key;
        public string ExportFile { get; } = exportFile;
        public string LocalisationKey { get; } = localisationKey;
        public InventoryGrid Grid { get; } = grid;
        public TabItem Page { get; } = page;
        public JsonObject? Pending { get; set; }
        public bool Loaded { get; set; }
    }

    private readonly TabControl _tabs = new();
    private readonly List<Entry> _entries = [];
    private readonly TextBlock _refundWarning = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };

    public event EventHandler? DataModified;
    public event EventHandler<GoToJsonRequestEventArgs>? GoToJsonRequested;
    public Func<BasesStorageTransferRequest, CancellationToken, Task<BasesStorageTransferContent?>>? ImportAsync { get; set; }
    public Func<BasesStorageTransferRequest, string, CancellationToken, Task<bool>>? ExportAsync { get; set; }

    public StoragePanel(GameItemDatabase database, AvaloniaIconProvider? icons)
    {
        string[] keys = ["base.storage_ingredient", "base.storage_corvette_parts", "base.storage_salvage_capsule", "base.storage_rocket",
            "base.storage_fishing_platform", "base.storage_fish_bait", "base.storage_food_unit", "base.storage_freighter_refund"];
        for (int i = 0; i < BaseLogic.StorageInventories.Length; i++)
        {
            var definition = BaseLogic.StorageInventories[i];
            var grid = new InventoryGrid();
            grid.SetDatabase(database); grid.SetIconProvider(icons); grid.SetIsStorageInventory(true); grid.SetIsCargoInventory(true);
            grid.SetSuperchargeDisabled(true); grid.SetSortingEnabled(true); grid.SetInventoryGroup(i == 2 ? "BaseCapsule" : i == 7 ? "Freighter" : "Chest");
            string file = Path.GetFileNameWithoutExtension(definition.ExportFileName) + ExportConfig.Instance.StorageExt;
            grid.SetExportFileName(file); grid.DataModified += (_, _) => DataModified?.Invoke(this, EventArgs.Empty);
            var goTo = BaseUi.JsonButton(() => GoToJsonRequested?.Invoke(this, new GoToJsonRequestEventArgs($"PlayerStateData.{definition.Key}")));
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; Grid.SetColumn(goTo, 1); header.Children.Add(goTo);
            var content = new Grid { RowDefinitions = new RowDefinitions(i == 7 ? "Auto,Auto,*" : "Auto,*"), RowSpacing = 5 };
            content.Children.Add(header);
            if (i == 7) { Grid.SetRow(_refundWarning, 1); content.Children.Add(_refundWarning); Grid.SetRow(grid, 2); }
            else Grid.SetRow(grid, 1);
            content.Children.Add(grid);
            var page = new TabItem { Content = content };
            var entry = new Entry(definition.Key, file, keys[i], grid, page);
            grid.ConfigureTransfer(async (inventory, suggested) => await ExportInventoryAsync(entry, inventory, suggested),
                async _ => await ImportInventoryAsync(entry));
            _entries.Add(entry);
        }
        _tabs.ItemsSource = _entries.Select(entry => entry.Page).ToArray();
        _tabs.SelectionChanged += (_, _) => EnsureLoaded(_tabs.SelectedIndex);
        Content = _tabs; ApplyUiLocalisation();
    }

    public void LoadData(JsonObject saveData)
    {
        JsonObject? player = saveData.GetObject("PlayerStateData");
        foreach (Entry entry in _entries) { entry.Pending = player?.GetObject(entry.Key); entry.Loaded = false; }
        EnsureLoaded(Math.Max(0, _tabs.SelectedIndex));
    }

    public void SaveData(JsonObject saveData)
    {
        JsonObject? player = saveData.GetObject("PlayerStateData"); if (player == null) return;
        foreach (Entry entry in _entries) if (entry.Loaded) entry.Grid.SaveInventory(player.GetObject(entry.Key));
    }

    public void ApplyUiLocalisation()
    {
        foreach (Entry entry in _entries) { entry.Page.Header = UiStrings.Get(entry.LocalisationKey); entry.Grid.ApplyUiLocalisation(); }
        _refundWarning.Text = UiStrings.Get("base.storage_freighter_refund_warning");
    }

    private void EnsureLoaded(int index) { if (index < 0 || index >= _entries.Count || _entries[index].Loaded) return; _entries[index].Loaded = true; _entries[index].Grid.LoadInventory(_entries[index].Pending); }

    private async Task ExportInventoryAsync(Entry entry, JsonObject inventory, string suggested)
    {
        if (ExportAsync == null) { await ShowErrorAsync(); return; }
        await ExportAsync(new BasesStorageTransferRequest(BasesStorageTransferKind.Storage, UiStrings.Get("common.export"), suggested,
            [ExportConfig.Instance.StorageExt], $"PlayerStateData.{entry.Key}"), inventory.ToDisplayString(), CancellationToken.None);
    }

    private async Task<JsonObject?> ImportInventoryAsync(Entry entry)
    {
        if (ImportAsync == null) { await ShowErrorAsync(); return null; }
        BasesStorageTransferContent? content = await ImportAsync(new BasesStorageTransferRequest(BasesStorageTransferKind.Storage,
            UiStrings.Get("common.import"), entry.ExportFile, [ExportConfig.Instance.StorageExt], $"PlayerStateData.{entry.Key}"), CancellationToken.None);
        return content == null ? null : JsonObject.Parse(content.JsonText);
    }

    private async Task ShowErrorAsync() { if (TopLevel.GetTopLevel(this) is Window owner) await BaseUi.MessageAsync(owner, UiStrings.Get("common.error"), UiStrings.Get("common.error")); }
}
