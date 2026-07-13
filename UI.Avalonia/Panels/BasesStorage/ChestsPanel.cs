using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Contracts;
using NMSE.UI.Avalonia.Inventory;
using NMSE.UI.Avalonia.Services;

namespace NMSE.UI.Avalonia.Panels.BasesStorage;

public sealed class ChestsPanel : UserControl, ISaveEditorPanel, IGoToJsonSource
{
    private readonly TabControl _tabs = new();
    private readonly TabItem[] _pages = new TabItem[10];
    private readonly InventoryGrid[] _grids = new InventoryGrid[10];
    private readonly TextBox[] _nameFields = new TextBox[10];
    private readonly TextBlock[] _nameLabels = new TextBlock[10];
    private readonly TextBlock[] _warnings = new TextBlock[10];
    private readonly Button[] _rename = new Button[10];
    private readonly Button[] _clear = new Button[10];
    private readonly JsonObject?[] _pending = new JsonObject?[10];
    private readonly bool[] _loaded = new bool[10];
    private readonly string[] _names = new string[10];

    public event EventHandler? DataModified;
    public event EventHandler<GoToJsonRequestEventArgs>? GoToJsonRequested;
    public Func<BasesStorageTransferRequest, CancellationToken, Task<BasesStorageTransferContent?>>? ImportAsync { get; set; }
    public Func<BasesStorageTransferRequest, string, CancellationToken, Task<bool>>? ExportAsync { get; set; }

    public ChestsPanel(GameItemDatabase database, AvaloniaIconProvider? icons)
    {
        for (int i = 0; i < 10; i++)
        {
            int index = i;
            var grid = _grids[i] = new InventoryGrid();
            grid.SetDatabase(database); grid.SetIconProvider(icons); grid.SetInventoryGroup("Chest");
            grid.SetIsStorageInventory(true); grid.SetIsChestInventory(true); grid.SetIsCargoInventory(true);
            grid.SetSortingEnabled(true); grid.SetSuperchargeDisabled(true); grid.SetExportFileName($"Chest_{i}{ExportConfig.Instance.ChestExt}");
            grid.DataModified += (_, _) => DataModified?.Invoke(this, EventArgs.Empty);
            grid.ConfigureTransfer(
                async (inventory, suggested) => await ExportInventoryAsync(index, inventory, suggested),
                async _ => await ImportInventoryAsync(index));

            _nameFields[i] = new TextBox { Width = 220 };
            _nameLabels[i] = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            _rename[i] = new Button(); _clear[i] = new Button();
            _rename[i].Click += (_, _) => Rename(index);
            _clear[i].Click += (_, _) => Clear(index);
            var goTo = BaseUi.JsonButton(() => GoToJsonRequested?.Invoke(this,
                new GoToJsonRequestEventArgs($"PlayerStateData.Chest{index + 1}Inventory")));
            var nameRow = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children = { _nameLabels[i], _nameFields[i], _rename[i], _clear[i], goTo }
            };
            _warnings[i] = new TextBlock { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
            var content = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 5 };
            content.Children.Add(nameRow); Grid.SetRow(_warnings[i], 1); content.Children.Add(_warnings[i]); Grid.SetRow(grid, 2); content.Children.Add(grid);
            _pages[i] = new TabItem { Content = content };
        }
        _tabs.ItemsSource = _pages;
        _tabs.SelectionChanged += (_, _) => EnsureLoaded(_tabs.SelectedIndex);
        Content = _tabs;
        ApplyUiLocalisation();
    }

    public void LoadData(JsonObject saveData)
    {
        JsonObject? player = saveData.GetObject("PlayerStateData");
        for (int i = 0; i < 10; i++)
        {
            _pending[i] = player?.GetObject(BaseLogic.ChestInventoryKeys[i]);
            _loaded[i] = false;
            _names[i] = BaseLogic.GetChestName(_pending[i]);
            _nameFields[i].Text = _names[i];
            UpdateTitle(i);
        }
        EnsureLoaded(Math.Max(0, _tabs.SelectedIndex));
    }

    public void SaveData(JsonObject saveData)
    {
        JsonObject? player = saveData.GetObject("PlayerStateData"); if (player == null) return;
        for (int i = 0; i < 10; i++) if (_loaded[i]) _grids[i].SaveInventory(player.GetObject(BaseLogic.ChestInventoryKeys[i]));
    }

    public void ApplyUiLocalisation()
    {
        for (int i = 0; i < 10; i++)
        {
            _nameLabels[i].Text = UiStrings.Get("base.chest_name_label"); _nameFields[i].PlaceholderText = UiStrings.Get("base.chest_name_placeholder");
            _rename[i].Content = UiStrings.Get("base.chest_rename"); _clear[i].Content = UiStrings.Get("base.chest_clear_name"); _warnings[i].Text = UiStrings.Get("base.chest_warning");
            _grids[i].ApplyUiLocalisation(); UpdateTitle(i);
        }
    }

    private void Rename(int index)
    {
        _names[index] = _nameFields[index].Text?.Trim() ?? string.Empty; BaseLogic.SetChestName(_pending[index], _names[index]); UpdateTitle(index); DataModified?.Invoke(this, EventArgs.Empty);
    }

    private void Clear(int index)
    {
        _nameFields[index].Text = string.Empty; _names[index] = string.Empty; BaseLogic.SetChestName(_pending[index], null); UpdateTitle(index); DataModified?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateTitle(int index) => _pages[index].Header = BaseLogic.FormatChestTabTitle(UiStrings.Format("base.chest_tab", index), _names[index]);
    private void EnsureLoaded(int index) { if (index < 0 || index >= 10 || _loaded[index]) return; _loaded[index] = true; _grids[index].LoadInventory(_pending[index]); }

    private async Task ExportInventoryAsync(int index, JsonObject inventory, string suggested)
    {
        if (ExportAsync == null) { await ShowErrorAsync(); return; }
        string key = BaseLogic.ChestInventoryKeys[index];
        await ExportAsync(new BasesStorageTransferRequest(BasesStorageTransferKind.Chest, UiStrings.Get("common.export"), suggested,
            [ExportConfig.Instance.ChestExt], $"PlayerStateData.{key}"), inventory.ToDisplayString(), CancellationToken.None);
    }

    private async Task<JsonObject?> ImportInventoryAsync(int index)
    {
        if (ImportAsync == null) { await ShowErrorAsync(); return null; }
        string key = BaseLogic.ChestInventoryKeys[index];
        BasesStorageTransferContent? content = await ImportAsync(new BasesStorageTransferRequest(BasesStorageTransferKind.Chest,
            UiStrings.Get("common.import"), $"Chest_{index}{ExportConfig.Instance.ChestExt}", [ExportConfig.Instance.ChestExt], $"PlayerStateData.{key}"), CancellationToken.None);
        return content == null ? null : JsonObject.Parse(content.JsonText);
    }

    private async Task ShowErrorAsync()
    {
        if (TopLevel.GetTopLevel(this) is Window owner) await BaseUi.MessageAsync(owner, UiStrings.Get("common.error"), UiStrings.Get("common.error"));
    }
}
