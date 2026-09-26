using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Contracts;
using NMSE.UI.Avalonia.Controls;
using NMSE.UI.Avalonia.Inventory;
using NMSE.UI.Avalonia.Platform;
using NMSE.UI.Avalonia.Services;

namespace NMSE.UI.Avalonia.Panels.Multitools;

public sealed class MultitoolPanel : UserControl, ISaveEditorPanel, IGoToJsonSource
{
    private readonly ComboBox _selector = new();
    private readonly ComboBox _type = new();
    private readonly ComboBox _size = new();
    private readonly ComboBox _class = new();
    private readonly TextBox _name = new();
    private readonly TextBox _seed = new();
    private readonly InvariantNumericTextBox _damage = new();
    private readonly InvariantNumericTextBox _mining = new();
    private readonly InvariantNumericTextBox _scan = new();
    private readonly TextBlock _title = new() { FontSize = 20, FontWeight = global::Avalonia.Media.FontWeight.SemiBold };
    private readonly TextBlock _primaryLabel = new() { Foreground = global::Avalonia.Media.Brushes.Gray };
    private readonly TextBlock _detailsHeading = new() { FontWeight = global::Avalonia.Media.FontWeight.SemiBold };
    private readonly TextBlock _statsHeading = new() { FontWeight = global::Avalonia.Media.FontWeight.SemiBold };
    private readonly TextBlock _selectLabel = new();
    private readonly TextBlock _nameLabel = new();
    private readonly TextBlock _typeLabel = new();
    private readonly TextBlock _sizeLabel = new();
    private readonly TextBlock _classLabel = new();
    private readonly TextBlock _seedLabel = new();
    private readonly TextBlock _damageLabel = new();
    private readonly TextBlock _miningLabel = new();
    private readonly TextBlock _scanLabel = new();
    private readonly Button _generateSeed = new();
    private readonly Button _delete = new();
    private readonly Button _copy = new();
    private readonly Button _swap = new();
    private readonly Button _export = new();
    private readonly Button _import = new();
    private readonly Button _makePrimary = new();
    private readonly Button _moveArchive = new();
    private readonly Button _importArchive = new();
    private readonly Button _goList = JsonNavigationButton();
    private readonly Button _goSelected = JsonNavigationButton();
    private readonly Button _goStore = JsonNavigationButton();
    private readonly IDesktopServices? _desktopServices;
    private JsonArray? _multitools;
    private JsonObject? _playerState;
    private int _activeToolIndex;
    private int _selectedToolDataIndex = -1;
    private int _originalClassIndex = -1;
    private int _originalTypeIndex = -1;
    private Dictionary<string, double>? _rawStats;
    private bool _loading;

    public MultitoolPanel(
        GameItemDatabase? database = null,
        AvaloniaIconProvider? iconProvider = null,
        IDesktopServices? desktopServices = null)
    {
        _desktopServices = desktopServices;
        StoreGrid = new InventoryGrid();
        StoreGrid.SetDatabase(database);
        StoreGrid.SetIconProvider(iconProvider);
        StoreGrid.SetIsTechInventory(true);
        StoreGrid.SetInventoryOwnerType("Weapon");
        StoreGrid.SetInventoryGroup("Personal");
        StoreGrid.SetMaxSupportedLabel(UiStrings.Format("common.max_supported", "10x6"));
        StoreGrid.DataModified += (_, _) => RaiseModified();
        if (desktopServices is not null)
            StoreGrid.ConfigureTransfer(ExportInventoryAsync, ImportInventoryAsync);

        _selector.SelectionChanged += OnSelectionChanged;
        _type.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            int index = GetSelectedTypeIndex();
            if (index >= 0)
            {
                int canonical = MultitoolLogic.GetCanonicalIsLargeIndex(MultitoolLogic.ToolTypes[index].Name);
                if (canonical >= 0) _size.SelectedIndex = canonical;
            }
            RaiseModified();
        };
        _size.SelectionChanged += (_, _) => Changed();
        _class.SelectionChanged += (_, _) => Changed();
        _name.TextChanged += (_, _) => Changed();
        _name.LostFocus += (_, _) => RefreshSelectedLabel();
        _seed.TextChanged += (_, _) => Changed();
        _damage.NumericValueChanged += (_, _) => Changed();
        _mining.NumericValueChanged += (_, _) => Changed();
        _scan.NumericValueChanged += (_, _) => Changed();
        _generateSeed.Click += (_, _) => _seed.Text = $"0x{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}";
        _delete.Click += async (_, _) => await RunUiOperation(async () => { await DeleteSelectedAsync(); }).ConfigureAwait(true);
        _copy.Click += (_, _) => CopySelectedTool();
        _swap.Click += async (_, _) => await RunUiOperation(ChooseSwapTargetAsync).ConfigureAwait(true);
        _export.Click += async (_, _) => await RunUiOperation(async () => { await ExportSelectedAsync(); }).ConfigureAwait(true);
        _import.Click += async (_, _) => await RunUiOperation(async () => { await ImportToolAsync(); }).ConfigureAwait(true);
        _makePrimary.Click += (_, _) => MakeSelectedPrimary();
        _moveArchive.Click += async (_, _) => await RunUiOperation(async () => { await MoveSelectedToArchiveAsync(); }).ConfigureAwait(true);
        _importArchive.Click += async (_, _) => await RunUiOperation(async () => { await ImportFromArchiveAsync(); }).ConfigureAwait(true);
        _goList.Click += (_, _) => GoToJsonRequested?.Invoke(this, new GoToJsonRequestEventArgs("PlayerStateData.Multitools"));
        _goSelected.Click += (_, _) => RaiseGoToSelected(false);
        _goStore.Click += (_, _) => RaiseGoToSelected(true);

        _class.ItemsSource = MultitoolLogic.ToolClasses;
        RefreshTypeOptions();
        RefreshSizeOptions();
        Content = BuildLayout();
        ApplyUiLocalisation();
        SetEditorEnabled(false);
    }

    public InventoryGrid StoreGrid { get; }
    public int SelectedToolDataIndex => _selectedToolDataIndex;

    public Func<JsonObject, string, Task>? ExportToolAsync { get; set; }
    public Func<string[], Task<JsonObject?>>? ImportToolDataAsync { get; set; }
    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }
    public Func<string, string, Task>? ErrorAsync { get; set; }
    public Func<IReadOnlyList<string>, string, Task<int>>? SelectArchiveEntryAsync { get; set; }

    public event EventHandler? DataModified;
    public event EventHandler<GoToJsonRequestEventArgs>? GoToJsonRequested;

    public void SetDatabase(GameItemDatabase? database) => StoreGrid.SetDatabase(database);
    public void SetIconProvider(AvaloniaIconProvider? provider) => StoreGrid.SetIconProvider(provider);

    public void LoadData(JsonObject saveData)
    {
        ArgumentNullException.ThrowIfNull(saveData);
        _loading = true;
        try
        {
            _playerState = ResolvePlayerState(saveData);
            _multitools = _playerState?.GetArray("Multitools");
            _activeToolIndex = TryGetInt(_playerState, "ActiveMultioolIndex");
            RefreshToolList(_activeToolIndex);
            if (_multitools is null)
                LoadLegacyTool();
            UpdatePrimaryLabel();
        }
        finally
        {
            _loading = false;
        }
    }

    public void SaveData(JsonObject saveData)
    {
        JsonObject? playerState = ResolvePlayerState(saveData);
        if (playerState is null) return;
        CommitSelected(playerState);
        RawNumberGuard.SetInt(playerState, "ActiveMultioolIndex", _activeToolIndex);
    }

    public void ApplyUiLocalisation()
    {
        _title.Text = UiStrings.Get("multitool.title");
        _detailsHeading.Text = UiStrings.Get("multitool.details");
        _statsHeading.Text = UiStrings.Get("multitool.base_stats");
        _selectLabel.Text = UiStrings.Get("multitool.select");
        _nameLabel.Text = UiStrings.Get("multitool.name");
        _typeLabel.Text = UiStrings.Get("multitool.type");
        _sizeLabel.Text = UiStrings.Get("multitool.size");
        _classLabel.Text = UiStrings.Get("multitool.class");
        _seedLabel.Text = UiStrings.Get("multitool.seed");
        _damageLabel.Text = UiStrings.Get("multitool.damage");
        _miningLabel.Text = UiStrings.Get("multitool.mining");
        _scanLabel.Text = UiStrings.Get("multitool.scan");
        _generateSeed.Content = UiStrings.Get("common.generate");
        _delete.Content = UiStrings.Get("multitool.delete");
        _copy.Content = UiStrings.Get("common.copy");
        _swap.Content = UiStrings.Get("common.swap");
        _export.Content = UiStrings.Get("multitool.export");
        _import.Content = UiStrings.Get("multitool.import");
        _makePrimary.Content = UiStrings.Get("multitool.make_primary");
        _moveArchive.Content = UiStrings.Get("multitool.archive_move_btn");
        _importArchive.Content = UiStrings.Get("multitool.archive_import_btn");
        ToolTip.SetTip(_goList, UiStrings.Format("goto_json.tooltip_section", _title.Text));
        ToolTip.SetTip(_goSelected, UiStrings.Format("goto_json.tooltip_section", _detailsHeading.Text));
        ToolTip.SetTip(_goStore, UiStrings.Format("goto_json.tooltip_section", UiStrings.Get("goto_json.nav_cargo")));
        RefreshTypeOptions();
        RefreshSizeOptions();
        StoreGrid.ApplyUiLocalisation();
        UpdatePrimaryLabel();
    }

    public bool MakeSelectedPrimary()
    {
        if (_playerState is null || _selectedToolDataIndex < 0) return false;
        CommitSelected(_playerState);
        _activeToolIndex = _selectedToolDataIndex;
        RawNumberGuard.SetInt(_playerState, "ActiveMultioolIndex", _activeToolIndex);
        UpdatePrimaryLabel();
        RaiseModified();
        return true;
    }

    public bool SelectToolByDataIndex(int dataIndex)
    {
        if (_selector.ItemsSource is not IEnumerable<MultitoolLogic.ToolListItem> items) return false;
        MultitoolLogic.ToolListItem? item = items.FirstOrDefault(value => value.DataIndex == dataIndex);
        if (item is null) return false;
        _selector.SelectedItem = item;
        return true;
    }

    public bool CopySelectedTool()
    {
        if (_multitools is null || _selectedToolDataIndex < 0) return false;
        int empty = MultitoolLogic.FindEmptySlot(_multitools);
        if (empty < 0) return false;
        CommitSelected(_playerState);
        JsonObject clone = _multitools.GetObject(_selectedToolDataIndex).DeepClone();
        JsonObject target = _multitools.GetObject(empty);
        foreach (string key in clone.Names()) target.Set(key, clone.Get(key));
        RefreshToolList(empty);
        RaiseModified();
        return true;
    }

    public bool SwapSelectedWith(int otherDataIndex)
    {
        if (_multitools is null || _selectedToolDataIndex < 0 || otherDataIndex < 0 || otherDataIndex >= _multitools.Length || otherDataIndex == _selectedToolDataIndex) return false;
        int current = _selectedToolDataIndex;
        CommitSelected(_playerState);
        JsonObject first = _multitools.GetObject(current).DeepClone();
        JsonObject second = _multitools.GetObject(otherDataIndex).DeepClone();
        _multitools.Set(current, second);
        _multitools.Set(otherDataIndex, first);
        if (_activeToolIndex == current) _activeToolIndex = otherDataIndex;
        else if (_activeToolIndex == otherDataIndex) _activeToolIndex = current;
        if (_playerState is not null) RawNumberGuard.SetInt(_playerState, "ActiveMultioolIndex", _activeToolIndex);
        RefreshToolList(otherDataIndex);
        UpdatePrimaryLabel();
        RaiseModified();
        return true;
    }

    public async Task<bool> DeleteSelectedAsync()
    {
        if (_multitools is null || _playerState is null || _selectedToolDataIndex < 0 || MultitoolLogic.CountValidTools(_multitools) <= 1)
        {
            await ShowErrorAsync(UiStrings.Get("multitool.delete_title"), UiStrings.Get("multitool.cannot_delete_only"));
            return false;
        }
        if (!await ConfirmOperationAsync(UiStrings.Get("multitool.delete_title"), UiStrings.Get("multitool.delete_confirm"))) return false;
        int removed = _selectedToolDataIndex;
        MultitoolLogic.DeleteToolData(_multitools.GetObject(removed));
        if (removed == _activeToolIndex)
            _activeToolIndex = Math.Max(0, MultitoolLogic.FindFirstValidToolIndex(_multitools));
        RawNumberGuard.SetInt(_playerState, "ActiveMultioolIndex", _activeToolIndex);
        RefreshToolList(_activeToolIndex);
        UpdatePrimaryLabel();
        RaiseModified();
        return true;
    }

    public async Task<bool> MoveSelectedToArchiveAsync()
    {
        if (_multitools is null || _playerState is null || _selectedToolDataIndex < 0) return false;
        if (_selectedToolDataIndex == _activeToolIndex)
        {
            await ShowErrorAsync(UiStrings.Get("multitool.archive_move_title"), UiStrings.Get("multitool.archive_primary_blocked"));
            return false;
        }
        if (MultitoolLogic.CountValidTools(_multitools) <= 1) return false;
        JsonArray? archive = _playerState.GetArray("ArchivedMultitools");
        int archiveIndex = archive is null ? -1 : MultitoolLogic.FindEmptyArchivedToolSlot(archive);
        if (archive is null || archiveIndex < 0)
        {
            await ShowErrorAsync(UiStrings.Get("multitool.archive_move_title"), UiStrings.Get("multitool.archive_no_slots"));
            return false;
        }
        if (!await ConfirmOperationAsync(UiStrings.Get("multitool.archive_move_title"), UiStrings.Get("multitool.archive_move_confirm"))) return false;
        CommitSelected(_playerState);
        MultitoolLogic.MoveToolToArchive(_multitools.GetObject(_selectedToolDataIndex), archive.GetObject(archiveIndex));
        RefreshToolList(_activeToolIndex);
        RaiseModified();
        return true;
    }

    public async Task<bool> ImportFromArchiveAsync(int archiveListIndex = -1)
    {
        if (_multitools is null || _playerState?.GetArray("ArchivedMultitools") is not { } archive) return false;
        List<MultitoolLogic.ArchivedToolListItem> items = MultitoolLogic.BuildArchivedToolList(archive);
        int targetIndex = MultitoolLogic.FindEmptySlot(_multitools);
        if (items.Count == 0 || targetIndex < 0) return false;
        if (archiveListIndex < 0)
            archiveListIndex = await SelectArchiveAsync(items.Select(item => item.DisplayName).ToArray(), UiStrings.Get("multitool.archive_import_title"));
        if (archiveListIndex < 0 || archiveListIndex >= items.Count) return false;
        if (!await ConfirmOperationAsync(UiStrings.Get("multitool.archive_import_title"), UiStrings.Get("multitool.archive_import_confirm"))) return false;
        MultitoolLogic.ArchivedToolListItem selected = items[archiveListIndex];
        MultitoolLogic.ImportToolFromArchive(archive.GetObject(selected.ArchiveIndex), _multitools.GetObject(targetIndex));
        RefreshToolList(targetIndex);
        RaiseModified();
        return true;
    }

    public async Task<bool> ExportSelectedAsync()
    {
        if (_multitools is null || _selectedToolDataIndex < 0) return false;
        CommitSelected(_playerState);
        JsonObject tool = _multitools.GetObject(_selectedToolDataIndex);
        string suggestedName = BuildExportFileName();
        if (ExportToolAsync is not null)
            await ExportToolAsync(tool, suggestedName);
        else if (_desktopServices is not null)
        {
            string extension = ExportConfig.Instance.MultitoolExt.TrimStart('.');
            string? path = await _desktopServices.PickSaveFileAsync(UiStrings.Get("multitool.export"), suggestedName, extension,
                new DesktopFileType("Multitool", $"*.{extension}"));
            if (path is null) return false;
            tool.ExportToFile(path);
        }
        else return false;
        return true;
    }

    public async Task<bool> ImportToolAsync()
    {
        if (_multitools is null) return false;
        JsonObject? imported;
        string extension = ExportConfig.Instance.MultitoolExt.TrimStart('.');
        if (ImportToolDataAsync is not null)
            imported = await ImportToolDataAsync([$"*.{extension}", "*.wp0", "*.mlt"]);
        else if (_desktopServices is not null)
        {
            IReadOnlyList<string> paths = await _desktopServices.PickFilesAsync(UiStrings.Get("multitool.import"), false,
                new DesktopFileType("Multitool", $"*.{extension}", "*.wp0", "*.mlt"));
            imported = paths.Count == 0 ? null : JsonObject.ImportFromFile(paths[0]);
        }
        else return false;
        if (imported is null) return false;
        imported = InventoryImportHelper.UnwrapNomNom(imported, "Multitool");
        int empty = MultitoolLogic.FindEmptySlot(_multitools);
        if (empty < 0)
        {
            await ShowErrorAsync(UiStrings.Get("multitool.import_title"), UiStrings.Get("multitool.no_empty_slots"));
            return false;
        }
        JsonObject target = _multitools.GetObject(empty);
        foreach (string property in imported.Names())
            target.Set(property, imported.Get(property) switch { JsonObject value => value.DeepClone(), JsonArray value => value.DeepClone(), object value => value, _ => null });
        RefreshToolList(empty);
        RaiseModified();
        return true;
    }

    private Control BuildLayout()
    {
        var headerButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _goList, _goSelected, _goStore } };
        var titleStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { _title, _primaryLabel } };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { titleStack, headerButtons } };
        Grid.SetColumn(headerButtons, 1);
        var selectorRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 7, Children = { _selectLabel, _selector, _moveArchive, _importArchive } };
        Grid.SetColumn(_selector, 1); Grid.SetColumn(_moveArchive, 2); Grid.SetColumn(_importArchive, 3);
        var details = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"), ColumnSpacing = 8, RowSpacing = 6 };
        details.Children.Add(_detailsHeading); Grid.SetColumnSpan(_detailsHeading, 2);
        details.Children.Add(_statsHeading); Grid.SetColumn(_statsHeading, 2); Grid.SetColumnSpan(_statsHeading, 2);
        AddField(details, _nameLabel, _name, 1, 0);
        var typeSize = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), ColumnSpacing = 6, Children = { _type, _sizeLabel, _size } };
        Grid.SetColumn(_sizeLabel, 1); Grid.SetColumn(_size, 2);
        AddField(details, _typeLabel, typeSize, 2, 0);
        AddField(details, _classLabel, _class, 3, 0);
        var seedArea = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6, Children = { _seed, _generateSeed } };
        Grid.SetColumn(_generateSeed, 1);
        AddField(details, _seedLabel, seedArea, 4, 0);
        AddField(details, _damageLabel, _damage, 1, 2);
        AddField(details, _miningLabel, _mining, 2, 2);
        AddField(details, _scanLabel, _scan, 3, 2);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { _delete, _copy, _swap, _export, _import, _makePrimary } };
        var root = new Grid { Margin = new Thickness(12), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*"), RowSpacing = 9, Children = { header, selectorRow, details, buttons, StoreGrid } };
        Grid.SetRow(selectorRow, 1); Grid.SetRow(details, 2); Grid.SetRow(buttons, 3); Grid.SetRow(StoreGrid, 4);
        return root;
    }

    private static Button JsonNavigationButton() => new()
    {
        Content = "JSON",
        MinWidth = 48,
        Padding = new Thickness(8, 4),
    };

    private static void AddField(Grid grid, TextBlock label, Control field, int row, int column)
    {
        label.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(label); Grid.SetRow(label, row); Grid.SetColumn(label, column);
        grid.Children.Add(field); Grid.SetRow(field, row); Grid.SetColumn(field, column + 1);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || _selector.SelectedItem is not MultitoolLogic.ToolListItem item) return;
        if (_selectedToolDataIndex >= 0) CommitSelected(_playerState);
        LoadTool(item.DataIndex);
    }

    private void LoadTool(int index)
    {
        if (_multitools is null || index < 0 || index >= _multitools.Length) return;
        _loading = true;
        try
        {
            _selectedToolDataIndex = index;
            MultitoolLogic.ToolData data = MultitoolLogic.LoadToolData(_multitools.GetObject(index));
            _name.Text = data.Name;
            SelectType(data.TypeIndex);
            _originalTypeIndex = data.TypeIndex;
            _size.SelectedIndex = data.IsLarge.HasValue ? data.IsLarge.Value ? 0 : 1 : -1;
            _class.SelectedIndex = data.ClassIndex;
            _originalClassIndex = data.ClassIndex;
            _seed.Text = data.Seed;
            _damage.SetValueWithText(data.Damage, data.DamageText);
            _mining.SetValueWithText(data.Mining, data.MiningText);
            _scan.SetValueWithText(data.Scan, data.ScanText);
            _rawStats = new Dictionary<string, double> { ["^WEAPON_DAMAGE"] = data.Damage, ["^WEAPON_MINING"] = data.Mining, ["^WEAPON_SCAN"] = data.Scan };
            StoreGrid.LoadInventory(data.Store);
            StoreGrid.SetExportFileName(data.ExportFileName);
            StoreGrid.SetExportFileFilter(string.Empty, string.Empty, ExportConfig.Instance.MultitoolExt);
            SetEditorEnabled(true);
        }
        finally { _loading = false; }
    }

    private void CommitSelected(JsonObject? playerState)
    {
        if (_multitools is null || _selectedToolDataIndex < 0 || _selectedToolDataIndex >= _multitools.Length)
        {
            StoreGrid.SaveInventory(playerState?.GetObject("WeaponInventory"));
            return;
        }
        _damage.TryCommit(); _mining.TryCommit(); _scan.TryCommit();
        var values = new MultitoolLogic.ToolSaveValues
        {
            Name = _name.Text ?? string.Empty,
            TypeIndex = GetSelectedTypeIndex() != _originalTypeIndex ? GetSelectedTypeIndex() : -1,
            IsLargeIndex = _size.SelectedIndex,
            ClassIndex = _class.SelectedIndex,
            OriginalClassIndex = _originalClassIndex,
            Seed = _seed.Text ?? string.Empty,
            Damage = _damage.UserModified ? _damage.NumericValue ?? 0 : _rawStats?.GetValueOrDefault("^WEAPON_DAMAGE") ?? _damage.NumericValue ?? 0,
            Mining = _mining.UserModified ? _mining.NumericValue ?? 0 : _rawStats?.GetValueOrDefault("^WEAPON_MINING") ?? _mining.NumericValue ?? 0,
            Scan = _scan.UserModified ? _scan.NumericValue ?? 0 : _rawStats?.GetValueOrDefault("^WEAPON_SCAN") ?? _scan.NumericValue ?? 0,
            DamageText = _damage.UserModified ? _damage.DisplayText : null,
            MiningText = _mining.UserModified ? _mining.DisplayText : null,
            ScanText = _scan.UserModified ? _scan.DisplayText : null,
            RawStatValues = _rawStats,
        };
        JsonObject tool = _multitools.GetObject(_selectedToolDataIndex);
        MultitoolLogic.SaveToolData(tool, playerState, values, _selectedToolDataIndex == _activeToolIndex);
        StoreGrid.SaveInventory(tool.GetObject("Store"));
        _originalClassIndex = _class.SelectedIndex;
        _originalTypeIndex = GetSelectedTypeIndex();
        if (_selector.SelectedItem is MultitoolLogic.ToolListItem item) item.DisplayName = string.IsNullOrWhiteSpace(_name.Text) ? UiStrings.Format("multitool.default_name", item.DataIndex + 1) : _name.Text;
    }

    private void RefreshToolList(int selectDataIndex)
    {
        _loading = true;
        try
        {
            List<MultitoolLogic.ToolListItem> items = _multitools is null ? [] : MultitoolLogic.BuildToolList(_multitools);
            _selector.ItemsSource = items;
            MultitoolLogic.ToolListItem? selected = items.FirstOrDefault(item => item.DataIndex == selectDataIndex) ?? items.FirstOrDefault();
            _selector.SelectedItem = selected;
            if (selected is not null) LoadTool(selected.DataIndex); else { _selectedToolDataIndex = -1; StoreGrid.LoadInventory(null); SetEditorEnabled(false); }
        }
        finally { _loading = false; }
    }

    private void LoadLegacyTool()
    {
        if (_playerState?.GetObject("WeaponInventory") is not { } inventory) return;
        _selector.ItemsSource = new[] { _playerState.GetString("PlayerWeaponName") ?? "Primary Weapon" };
        _selector.SelectedIndex = 0;
        _name.Text = _playerState.GetString("PlayerWeaponName") ?? string.Empty;
        _seed.Text = _playerState.GetObject("CurrentWeapon")?.GetArray("GenerationSeed") is { Length: > 1 } seed ? seed.Get(1)?.ToString() ?? string.Empty : string.Empty;
        StoreGrid.LoadInventory(inventory);
        SetEditorEnabled(false);
        StoreGrid.IsEnabled = true;
    }

    private void RefreshTypeOptions()
    {
        int selected = GetSelectedTypeIndex();
        _type.ItemsSource = MultitoolLogic.GetToolTypeItems();
        SelectType(selected);
    }

    private void RefreshSizeOptions()
    {
        int selected = _size.SelectedIndex;
        _size.ItemsSource = MultitoolLogic.GetToolSizeItems();
        _size.SelectedIndex = selected;
    }

    private int GetSelectedTypeIndex() => _type.SelectedItem is MultitoolLogic.ToolTypeItem selected
        ? Array.FindIndex(MultitoolLogic.ToolTypes, item => item.Name.Equals(selected.InternalName, StringComparison.OrdinalIgnoreCase))
        : _type.SelectedIndex;

    private void SelectType(int typeIndex)
    {
        if (_type.ItemsSource is not IEnumerable<MultitoolLogic.ToolTypeItem> source) { _type.SelectedIndex = typeIndex; return; }
        string? name = typeIndex >= 0 && typeIndex < MultitoolLogic.ToolTypes.Length ? MultitoolLogic.ToolTypes[typeIndex].Name : null;
        _type.SelectedItem = source.FirstOrDefault(item => item.InternalName == name);
    }

    private string BuildExportFileName()
    {
        string typeName = _type.SelectedItem is MultitoolLogic.ToolTypeItem type ? type.InternalName : "Unknown";
        string className = _class.SelectedIndex is >= 0 and < 4 ? MultitoolLogic.ToolClasses[_class.SelectedIndex] : "C";
        return ExportConfig.BuildFileName(ExportConfig.Instance.MultitoolTemplate, ExportConfig.Instance.MultitoolExt,
            new Dictionary<string, string> { ["multitool_name"] = _name.Text ?? string.Empty, ["type"] = typeName, ["class"] = className });
    }

    private async Task ExportInventoryAsync(JsonObject inventory, string name)
    {
        if (_desktopServices is null) return;
        string extension = ExportConfig.Instance.MultitoolExt.TrimStart('.');
        string? path = await _desktopServices.PickSaveFileAsync(UiStrings.Get("common.export"), name, extension, new DesktopFileType("Multitool inventory", $"*.{extension}"));
        if (path is not null) await File.WriteAllTextAsync(path, JsonParser.Serialize(inventory, true, skipReverseMapping: true));
    }

    private async Task<JsonObject?> ImportInventoryAsync(string extension)
    {
        if (_desktopServices is null) return null;
        IReadOnlyList<string> paths = await _desktopServices.PickFilesAsync(UiStrings.Get("common.import"), false,
            new DesktopFileType("Multitool inventory", $"*.{extension}", "*.wp0", "*.mlt"));
        return paths.Count == 0 ? null : JsonObject.ImportFromFile(paths[0]);
    }

    private async Task<bool> ConfirmOperationAsync(string title, string message) => ConfirmAsync is not null
        ? await ConfirmAsync(title, message)
        : _desktopServices is not null && await _desktopServices.ConfirmAsync(title, message);

    private async Task<int> SelectArchiveAsync(IReadOnlyList<string> entries, string title) => SelectArchiveEntryAsync is not null
        ? await SelectArchiveEntryAsync(entries, title)
        : TopLevel.GetTopLevel(this) is Window owner ? await ArchiveSelectionDialog.ShowAsync(owner, entries, title) : -1;

    private async Task ChooseSwapTargetAsync()
    {
        if (_selector.ItemsSource is not IEnumerable<MultitoolLogic.ToolListItem> source) return;
        MultitoolLogic.ToolListItem[] items = source.Where(item => item.DataIndex != _selectedToolDataIndex).ToArray();
        if (items.Length == 0) return;
        int selected = await SelectArchiveAsync(items.Select(item => item.DisplayName).ToArray(), UiStrings.Get("common.swap"));
        if (selected >= 0 && selected < items.Length) SwapSelectedWith(items[selected].DataIndex);
    }

    private Task ShowErrorAsync(string title, string message) => ErrorAsync is not null ? ErrorAsync(title, message) : _desktopServices?.ShowErrorAsync(title, message) ?? Task.CompletedTask;
    private async Task RunUiOperation(Func<Task> action) { try { await action(); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException) { await ShowErrorAsync(UiStrings.Get("common.error"), exception.Message); } }
    private void RaiseGoToSelected(bool store) { if (_selectedToolDataIndex >= 0) GoToJsonRequested?.Invoke(this, new GoToJsonRequestEventArgs($"PlayerStateData.Multitools[{_selectedToolDataIndex}]" + (store ? ".Store" : string.Empty))); }
    private void Changed() { if (!_loading) RaiseModified(); }
    private void RefreshSelectedLabel() { if (_selector.SelectedItem is not MultitoolLogic.ToolListItem item) return; item.DisplayName = string.IsNullOrWhiteSpace(_name.Text) ? UiStrings.Format("multitool.default_name", item.DataIndex + 1) : _name.Text; _selector.InvalidateVisual(); }
    private void RaiseModified() => DataModified?.Invoke(this, EventArgs.Empty);
    private void UpdatePrimaryLabel() => _primaryLabel.Text = UiStrings.Format("multitool.primary_label", MultitoolLogic.GetPrimaryToolName(_multitools, _activeToolIndex));
    private void SetEditorEnabled(bool value) { foreach (Control control in new Control[] { _name, _type, _size, _class, _seed, _generateSeed, _damage, _mining, _scan, _delete, _copy, _swap, _export, _makePrimary, _moveArchive }) control.IsEnabled = value; }
    private static int TryGetInt(JsonObject? owner, string key) { try { return owner?.GetInt(key) ?? 0; } catch { return 0; } }
    private static JsonObject? ResolvePlayerState(JsonObject root) => root.GetObject("PlayerStateData") ?? root.GetObject("SaveData")?.GetObject("PlayerStateData") ?? (root.Contains("Multitools") || root.Contains("WeaponInventory") ? root : null);
}
