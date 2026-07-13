using System.Collections.ObjectModel;
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

namespace NMSE.UI.Avalonia.Panels.BasesStorage;

public sealed class BasesEditorPanel : UserControl, ISaveEditorPanel, IGoToJsonSource
{
    private readonly ObservableCollection<BaseListItem> _baseItems = [];
    private readonly ObservableCollection<NpcWorkerItem> _workers = [];
    private readonly ObservableCollection<BaseObjectItem> _objects = [];
    private readonly ObservableCollection<string> _partCounts = [];
    private readonly ListBox _baseList = new() { Width = 320 };
    private readonly ListBox _objectList = new() { MinHeight = 100 };
    private readonly ListBox _parts = new() { MinHeight = 110 };
    private readonly ComboBox _worker = new();
    private readonly ComboBox _race = new();
    private readonly TextBox _npcSeed = new() { MinWidth = 210 };
    private readonly TextBox _name = new();
    private readonly TextBox _itemCount = new() { IsReadOnly = true };
    private readonly InvariantNumericTextBox[] _position = Enumerable.Range(0, 3).Select(_ => new InvariantNumericTextBox()).ToArray();
    private readonly InvariantNumericTextBox[] _forward = Enumerable.Range(0, 3).Select(_ => new InvariantNumericTextBox()).ToArray();
    private readonly TextBox _address = new();
    private readonly TextBox _galaxy = new();
    private readonly TextBox _timestamp = new();
    private readonly TextBox _ownerTimestamp = new();
    private readonly InvariantNumericTextBox _version = new() { Minimum = 0, Maximum = int.MaxValue };
    private readonly TextBlock _status = new() { Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap };
    private readonly Border _details = new() { Padding = new Thickness(8) };
    private readonly Button _generateNpcSeed = new();
    private readonly Button _summon = new();
    private readonly Button _export = new();
    private readonly Button _import = new();
    private readonly Button _overwrite = new();
    private readonly Button _moveComputer = new();
    private readonly Button _delete = new();
    private readonly Button _top = new();
    private readonly Button _up = new();
    private readonly Button _down = new();
    private readonly Button _bottom = new();
    private readonly Button _sortAsc = new();
    private readonly Button _sortDesc = new();
    private readonly Button _clearTerrain = new();
    private readonly Button _clearAllTerrain = new();
    private readonly Button _clearOutsideBases = new();
    private readonly TextBlock _baseListTitle;
    private readonly TextBlock _npcHeader;
    private readonly TextBlock _baseHeader;
    private readonly TextBlock[] _labels = new TextBlock[16];
    private JsonObject? _playerState;
    private bool _loading;

    public event EventHandler? DataModified;
    public event EventHandler<GoToJsonRequestEventArgs>? GoToJsonRequested;
    public Func<BasesStorageTransferRequest, CancellationToken, Task<BasesStorageTransferContent?>>? ImportAsync { get; set; }
    public Func<BasesStorageTransferRequest, string, CancellationToken, Task<bool>>? ExportAsync { get; set; }
    public Func<string, string, CancellationToken, Task<bool>>? ConfirmAsync { get; set; }

    public BasesEditorPanel()
    {
        _baseList.ItemsSource = _baseItems; _worker.ItemsSource = _workers; _objectList.ItemsSource = _objects; _parts.ItemsSource = _partCounts;
        _race.ItemsSource = BaseNpcRaces.Items();
        _baseList.SelectionChanged += (_, _) => LoadSelectedBase();
        _worker.SelectionChanged += (_, _) => LoadSelectedWorker();
        _race.SelectionChanged += (_, _) => ChangeWorkerRace();
        _npcSeed.LostFocus += (_, _) => CommitWorkerSeed();
        _generateNpcSeed.Click += (_, _) => { _npcSeed.Text = NewSeed(); CommitWorkerSeed(); };
        _summon.Click += async (_, _) => await SummonWorkerAsync();
        _name.LostFocus += (_, _) => CommitSelectedBase();
        foreach (Control field in _position.Cast<Control>().Concat(_forward).Append(_version)) field.LostFocus += (_, _) => CommitSelectedBase();
        foreach (TextBox field in new[] { _address, _galaxy, _timestamp, _ownerTimestamp }) field.LostFocus += (_, _) => CommitSelectedBase();
        _export.Click += async (_, _) => await ExportBaseAsync(); _import.Click += async (_, _) => await ImportBaseAsync(false); _overwrite.Click += async (_, _) => await ImportBaseAsync(true);
        _moveComputer.Click += async (_, _) => await MoveBaseComputerAsync(); _delete.Click += async (_, _) => await DeleteBaseAsync();
        _top.Click += (_, _) => MoveSelected(int.MinValue); _up.Click += (_, _) => MoveSelected(-1); _down.Click += (_, _) => MoveSelected(1); _bottom.Click += (_, _) => MoveSelected(int.MaxValue);
        _sortAsc.Click += (_, _) => SortBases(true); _sortDesc.Click += (_, _) => SortBases(false);
        _clearTerrain.Click += async (_, _) => await ClearTerrainAsync(0); _clearAllTerrain.Click += async (_, _) => await ClearTerrainAsync(1); _clearOutsideBases.Click += async (_, _) => await ClearTerrainAsync(2);

        var form = BaseUi.FormGrid(); int row = 0;
        _npcHeader = BaseUi.AddHeader(form, row++, "NPC");
        _labels[0] = BaseUi.AddRow(form, row++, "Worker:", _worker); _labels[1] = BaseUi.AddRow(form, row++, "Race:", _race);
        var seedRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _npcSeed, _generateNpcSeed } };
        _labels[2] = BaseUi.AddRow(form, row++, "Seed:", seedRow); BaseUi.AddRow(form, row++, string.Empty, _summon);
        _baseHeader = BaseUi.AddHeader(form, row++, "Base");
        _labels[3] = BaseUi.AddRow(form, row++, "Name:", _name); _labels[4] = BaseUi.AddRow(form, row++, "Items:", _itemCount);
        _labels[5] = BaseUi.AddRow(form, row++, "Position:", VectorEditor(_position)); _labels[6] = BaseUi.AddRow(form, row++, "Forward:", VectorEditor(_forward));
        _labels[7] = BaseUi.AddRow(form, row++, "Galactic Address:", _address); _labels[8] = BaseUi.AddRow(form, row++, "Galaxy / Reality:", _galaxy);
        _labels[9] = BaseUi.AddRow(form, row++, "Timestamp:", _timestamp); _labels[10] = BaseUi.AddRow(form, row++, "Owner Timestamp:", _ownerTimestamp);
        _labels[11] = BaseUi.AddRow(form, row++, "Base Version:", _version); _labels[12] = BaseUi.AddRow(form, row++, "Base Objects:", _objectList);
        _labels[13] = BaseUi.AddRow(form, row++, "Part Counts:", _parts);
        var baseActions = new WrapPanel { Children = { _export, _import, _overwrite, _moveComputer, _delete } }; BaseUi.AddRow(form, row++, string.Empty, baseActions);
        var terrainActions = new WrapPanel { Children = { _clearTerrain, _clearAllTerrain, _clearOutsideBases } }; BaseUi.AddRow(form, row++, string.Empty, terrainActions);
        _details.Child = new ScrollViewer { Content = form };

        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        _baseListTitle = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold }; left.Children.Add(_baseListTitle); Grid.SetRow(_baseList, 1); left.Children.Add(_baseList);
        var reorder = new WrapPanel { Children = { _top, _up, _down, _bottom, _sortAsc, _sortDesc } }; Grid.SetRow(reorder, 2); left.Children.Add(reorder);
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children =
        {
            BaseUi.JsonButton(() => { if (SelectedBase is { } item) RequestGoTo($"PlayerStateData.PersistentPlayerBases[{item.DataIndex}]"); }),
            BaseUi.JsonButton(() => RequestGoTo("PlayerStateData.NPCWorkers"))
        }};
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 }; body.Children.Add(left); Grid.SetColumn(_details, 1); body.Children.Add(_details);
        var root = new Grid { Margin = new Thickness(10), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 7 }; root.Children.Add(nav); Grid.SetRow(body, 1); root.Children.Add(body); Grid.SetRow(_status, 2); root.Children.Add(_status);
        Content = root; ApplyUiLocalisation(); UpdateEnabled();
    }

    private static Control VectorEditor(InvariantNumericTextBox[] fields)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        foreach (InvariantNumericTextBox field in fields) { field.Width = 115; panel.Children.Add(field); }
        return panel;
    }

    private BaseListItem? SelectedBase => _baseList.SelectedItem as BaseListItem;

    public void LoadData(JsonObject saveData)
    {
        _loading = true;
        try
        {
            _playerState = saveData.GetObject("PlayerStateData"); _baseItems.Clear(); _workers.Clear();
            JsonArray? workers = _playerState?.GetArray("NPCWorkers");
            if (workers != null) for (int i = 0; i < workers.Length && i < 5; i++) if (TryBool(workers.GetObject(i), "HiredWorker")) _workers.Add(new NpcWorkerItem(workers.GetObject(i), i));
            JsonArray? bases = _playerState?.GetArray("PersistentPlayerBases");
            if (bases != null) for (int i = 0; i < bases.Length; i++)
            {
                JsonObject value = bases.GetObject(i); string type = value.GetObject("BaseType")?.GetString("PersistentBaseTypes") ?? value.GetString("BaseType") ?? string.Empty;
                if (type.Equals("HomePlanetBase", StringComparison.OrdinalIgnoreCase) && TryInt(value, "BaseVersion") >= 3)
                    _baseItems.Add(new BaseListItem(value.GetString("Name") ?? UiStrings.Format("base.fallback_base_name", i + 1), value, i));
            }
            _worker.SelectedIndex = _workers.Count > 0 ? 0 : -1; _baseList.SelectedIndex = _baseItems.Count > 0 ? 0 : -1;
            LoadSelectedWorker(); LoadSelectedBase();
        }
        finally { _loading = false; UpdateEnabled(); }
    }

    public void SaveData(JsonObject saveData) { CommitWorkerSeed(force: true); CommitSelectedBase(force: true); }

    private void LoadSelectedWorker()
    {
        if (_worker.SelectedItem is not NpcWorkerItem worker) { _npcSeed.Text = string.Empty; _race.SelectedIndex = -1; UpdateEnabled(); return; }
        _loading = true;
        try
        {
            JsonObject? resource = worker.Data.GetObject("ResourceElement"); string filename = resource?.GetString("Filename") ?? worker.Data.GetString("ResourceElement.Filename") ?? string.Empty;
            string race = BaseNpcRaces.Lookup(filename); _race.SelectedItem = _race.Items.Cast<object?>().OfType<NpcRaceItem>().FirstOrDefault(item => item.InternalName == race);
            _npcSeed.Text = resource?.GetArray("Seed")?.GetString(1) ?? worker.Data.GetArray("ResourceElement.Seed")?.GetString(1) ?? string.Empty;
        }
        catch { }
        finally { _loading = false; UpdateEnabled(); }
    }

    private void ChangeWorkerRace()
    {
        if (_loading || _worker.SelectedItem is not NpcWorkerItem worker || _race.SelectedItem is not NpcRaceItem race) return;
        JsonObject? resource = worker.Data.GetObject("ResourceElement"); if (resource != null) resource.Set("Filename", race.Filename); else worker.Data.Set("ResourceElement.Filename", race.Filename); RaiseModified();
    }

    private void CommitWorkerSeed(bool force = false)
    {
        if ((_loading && !force) || _worker.SelectedItem is not NpcWorkerItem worker) return; string? seed = SeedHelper.NormalizeSeed(_npcSeed.Text); if (seed == null) return;
        JsonArray? array = worker.Data.GetObject("ResourceElement")?.GetArray("Seed") ?? worker.Data.GetArray("ResourceElement.Seed"); if (array is { Length: >= 2 }) array.Set(1, seed); if (!force) RaiseModified();
    }

    private void LoadSelectedBase()
    {
        BaseListItem? item = SelectedBase; _details.IsVisible = item != null; _objects.Clear(); _partCounts.Clear();
        if (item == null) { UpdateEnabled(); return; }
        _loading = true;
        try
        {
            JsonObject value = item.Data; _name.Text = value.GetString("Name") ?? string.Empty; JsonArray? objects = value.GetArray("Objects"); _itemCount.Text = (objects?.Length ?? 0).ToString(CultureInfo.CurrentCulture);
            ReadVector(value.GetArray("Position"), _position); ReadVector(value.GetArray("Forward"), _forward); _address.Text = Scalar(value.Get("GalacticAddress"));
            _galaxy.Text = Scalar(value.Get("Galaxy") ?? value.Get("RealityIndex")); _timestamp.Text = Scalar(value.Get("Timestamp")); _ownerTimestamp.Text = Scalar(value.GetObject("Owner")?.Get("TS")); _version.NumericValue = TryInt(value, "BaseVersion");
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (objects != null) for (int i = 0; i < objects.Length; i++)
            {
                JsonObject obj = objects.GetObject(i); string id = obj.GetString("ObjectID") ?? UiStrings.Get("common.unknown");
                if (id != "^BASE_FLAG") _objects.Add(new BaseObjectItem(id, obj, i));
                counts[id] = counts.GetValueOrDefault(id) + 1;
            }
            foreach ((string id, int count) in counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key)) _partCounts.Add($"{count,5} × {id}");
            _objectList.SelectedIndex = _objects.Count > 0 ? 0 : -1;
        }
        finally { _loading = false; UpdateEnabled(); }
    }

    private void CommitSelectedBase(bool force = false)
    {
        if ((_loading && !force) || SelectedBase is not { } item) return; JsonObject value = item.Data;
        string newName = _name.Text?.Trim() ?? string.Empty; if (newName.Length > 0) { value.Set("Name", newName); item.DisplayName = newName; }
        WriteVector(value.GetArray("Position"), _position); WriteVector(value.GetArray("Forward"), _forward); SetScalar(value, "GalacticAddress", _address.Text);
        string galaxyKey = value.Contains("Galaxy") ? "Galaxy" : "RealityIndex"; SetScalar(value, galaxyKey, _galaxy.Text); SetScalar(value, "Timestamp", _timestamp.Text);
        if (value.GetObject("Owner") is { } owner) SetScalar(owner, "TS", _ownerTimestamp.Text); _version.TryCommit(); RawNumberGuard.SetInt(value, "BaseVersion", (int)(_version.NumericValue ?? 0));
        RefreshBaseList(item); if (!force) RaiseModified();
    }

    private async Task SummonWorkerAsync()
    {
        if (_worker.SelectedItem is not NpcWorkerItem worker || SelectedBase is not { } baseItem || _playerState == null) return;
        if (!await ConfirmOperationAsync(UiStrings.Get("base.summon_worker_title"), UiStrings.Format("base.summon_worker_confirm", worker, baseItem.DisplayName))) return;
        JsonObject target = _playerState.GetArray("NPCWorkers")?.GetObject(worker.Index) ?? worker.Data; target.Set("BaseUA", baseItem.Data.Get("GalacticAddress"));
        if (baseItem.Data.GetArray("Position") is { Length: >= 3 } position)
        {
            var offset = new JsonArray(); for (int i = 0; i < 3; i++) offset.Add(position.Get(i)); offset.Add(1.0); target.Set("BaseOffset", offset);
        }
        string type = baseItem.Data.GetObject("BaseType")?.GetString("PersistentBaseTypes") ?? string.Empty; target.Set("FreighterBase", type == "FreighterBase"); RaiseModified();
        await ShowMessageAsync(UiStrings.Get("base.summon_complete_title"), UiStrings.Format("base.summon_complete_msg", worker, baseItem.DisplayName));
    }

    private void MoveSelected(int delta)
    {
        if (SelectedBase is not { } selected || _playerState?.GetArray("PersistentPlayerBases") is not { } bases) return;
        List<BaseListItem> ordered = _baseItems.OrderBy(item => item.DataIndex).ToList(); int position = ordered.IndexOf(selected); int destination = delta == int.MinValue ? 0 : delta == int.MaxValue ? ordered.Count - 1 : position + delta;
        if (destination < 0 || destination >= ordered.Count || destination == position) return;
        int step = destination > position ? 1 : -1;
        while (position != destination)
        {
            BaseListItem neighbor = ordered[position + step]; BaseLogic.SwapPlayerBases(bases, selected.DataIndex, neighbor.DataIndex); (selected.DataIndex, neighbor.DataIndex) = (neighbor.DataIndex, selected.DataIndex);
            ordered[position] = neighbor; ordered[position + step] = selected; position += step;
        }
        RefreshBaseList(selected); RaiseModified();
    }

    private void SortBases(bool ascending)
    {
        if (_playerState?.GetArray("PersistentPlayerBases") is not { } bases || _baseItems.Count < 2) return; BaseListItem? selected = SelectedBase;
        List<int> slots = _baseItems.Select(item => item.DataIndex).Order().ToList(); List<BaseListItem> desired = ascending
            ? _baseItems.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToList()
            : _baseItems.OrderByDescending(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        for (int position = 0; position < desired.Count; position++)
        {
            BaseListItem item = desired[position]; int target = slots[position]; if (item.DataIndex == target) continue;
            BaseListItem other = _baseItems.First(value => value.DataIndex == target); BaseLogic.SwapPlayerBases(bases, item.DataIndex, target); (item.DataIndex, other.DataIndex) = (other.DataIndex, item.DataIndex);
        }
        RefreshBaseList(selected); RaiseModified();
    }

    private async Task DeleteBaseAsync()
    {
        if (SelectedBase is not { } selected || _playerState?.GetArray("PersistentPlayerBases") is not { } bases) return;
        if (!await ConfirmOperationAsync(UiStrings.Get("base.delete_base_title"), UiStrings.Format("base.delete_base_confirm", selected.DisplayName))) return;
        bases.RemoveAt(selected.DataIndex); _baseItems.Remove(selected); foreach (BaseListItem item in _baseItems) if (item.DataIndex > selected.DataIndex) item.DataIndex--;
        _baseList.SelectedIndex = _baseItems.Count > 0 ? 0 : -1; LoadSelectedBase(); RaiseModified();
    }

    private async Task MoveBaseComputerAsync()
    {
        if (SelectedBase is not { } item) return;
        if (_objectList.SelectedItem is not BaseObjectItem target) { await ShowMessageAsync(UiStrings.Get("base.move_basecomp_title"), UiStrings.Get("base.move_basecomp_no_objects")); return; }
        JsonObject? flag = _objects.FirstOrDefault(value => value.ObjectId == "^BASE_FLAG")?.Data; if (flag == null) { await ShowMessageAsync(UiStrings.Get("common.error"), UiStrings.Get("base.move_basecomp_not_found")); return; }
        if (ReferenceEquals(flag, target.Data)) return;
        if (!await ConfirmOperationAsync(UiStrings.Get("base.move_basecomp_title"), UiStrings.Get("base.move_basecomp_warning"))) return;
        try { BaseLogic.MoveBaseComputer(item.Data, flag, target.Data); LoadSelectedBase(); RaiseModified(); await ShowMessageAsync(UiStrings.Get("base.move_basecomp_success_title"), UiStrings.Get("base.move_basecomp_success")); }
        catch (Exception ex) { await ShowMessageAsync(UiStrings.Get("common.error"), UiStrings.Format("base.move_basecomp_failed", ex.Message)); }
    }

    private async Task ExportBaseAsync()
    {
        if (SelectedBase is not { } item || ExportAsync == null) { await ShowMessageAsync(UiStrings.Get("common.error"), UiStrings.Get("common.error")); return; }
        string extension = ExportConfig.Instance.BaseExt; string file = ExportConfig.BuildFileName(ExportConfig.Instance.BaseTemplate, extension, new Dictionary<string, string> { ["base_name"] = item.DisplayName });
        await ExportAsync(new BasesStorageTransferRequest(BasesStorageTransferKind.Base, UiStrings.Get("base.export_title"), file, [extension], $"PlayerStateData.PersistentPlayerBases[{item.DataIndex}]"), item.Data.ToDisplayString(), CancellationToken.None);
    }

    private async Task ImportBaseAsync(bool overwrite)
    {
        if (SelectedBase is not { } item || ImportAsync == null) { await ShowMessageAsync(UiStrings.Get("common.error"), UiStrings.Get("common.error")); return; }
        string extension = ExportConfig.Instance.BaseExt; BasesStorageTransferContent? content = await ImportAsync(new BasesStorageTransferRequest(BasesStorageTransferKind.Base, UiStrings.Get("base.import_title"), "base" + extension, [extension], $"PlayerStateData.PersistentPlayerBases[{item.DataIndex}]"), CancellationToken.None); if (content == null) return;
        if (!await ConfirmOperationAsync(UiStrings.Get("base.confirm_import_title"), UiStrings.Get("base.import_confirm"))) return;
        try
        {
            JsonObject imported = JsonObject.Parse(content.JsonText); string[] keys = overwrite ? imported.Names().ToArray() : ["Objects", "BaseVersion", "UserData"];
            foreach (string key in keys) if (imported.Contains(key)) item.Data.Set(key, imported.Get(key) is JsonObject obj ? obj.DeepClone() : imported.Get(key) is JsonArray array ? array.DeepClone() : imported.Get(key));
            LoadSelectedBase(); RaiseModified(); await ShowMessageAsync(UiStrings.Get("base.import_title"), UiStrings.Get("base.import_success"));
        }
        catch (Exception ex) { await ShowMessageAsync(UiStrings.Get("common.error"), UiStrings.Format("base.import_failed", ex.Message)); }
    }

    private async Task ClearTerrainAsync(int scope)
    {
        if (_playerState == null || scope == 0 && SelectedBase == null) return; string title = UiStrings.Get(scope switch { 0 => "base.clear_terrain_title", 1 => "base.clear_all_terrain_title", _ => "base.clear_all_terrain_except_bases_title" });
        string confirm = scope switch { 0 => UiStrings.Format("base.clear_terrain_confirm", SelectedBase!.DisplayName), 1 => UiStrings.Get("base.clear_all_terrain_confirm"), _ => UiStrings.Get("base.clear_all_terrain_except_bases_confirm") };
        if (!await ConfirmOperationAsync(title, confirm)) return; int removed = scope switch { 0 => BaseLogic.ClearTerrainEdits(_playerState, SelectedBase!.Data), 1 => BaseLogic.ClearAllTerrainEdits(_playerState), _ => BaseLogic.ClearAllTerrainEditsExceptBases(_playerState) };
        if (removed > 0) RaiseModified();
        _status.Text = removed > 0
            ? UiStrings.Format(scope switch { 0 => "base.clear_terrain_success", 1 => "base.clear_all_terrain_success", _ => "base.clear_all_terrain_except_bases_success" }, removed)
            : UiStrings.Get(scope switch { 0 => "base.clear_terrain_none", 1 => "base.clear_all_terrain_none", _ => "base.clear_all_terrain_except_bases_none" });
    }

    private void RefreshBaseList(BaseListItem? selected)
    {
        List<BaseListItem> ordered = _baseItems.OrderBy(item => item.DataIndex).ToList(); _baseItems.Clear(); foreach (BaseListItem item in ordered) _baseItems.Add(item); if (selected != null) _baseList.SelectedItem = selected; UpdateEnabled();
    }

    private void UpdateEnabled()
    {
        bool hasBase = SelectedBase != null; bool hasWorker = _worker.SelectedItem is NpcWorkerItem; _summon.IsEnabled = hasBase && hasWorker;
        foreach (Button button in new[] { _export, _import, _overwrite, _delete, _clearTerrain }) button.IsEnabled = hasBase;
        _moveComputer.IsEnabled = hasBase && _objects.Count > 0;
        List<BaseListItem> ordered = _baseItems.OrderBy(item => item.DataIndex).ToList(); int index = SelectedBase == null ? -1 : ordered.IndexOf(SelectedBase); _top.IsEnabled = _up.IsEnabled = index > 0; _down.IsEnabled = _bottom.IsEnabled = index >= 0 && index < ordered.Count - 1; _sortAsc.IsEnabled = _sortDesc.IsEnabled = ordered.Count > 1;
        _clearAllTerrain.IsEnabled = _clearOutsideBases.IsEnabled = _playerState != null;
    }

    public void ApplyUiLocalisation()
    {
        bool old = _loading; _loading = true;
        try
        {
            _baseListTitle.Text = UiStrings.Get("base.base_list_title"); _npcHeader.Text = UiStrings.Get("base.npc_header"); _baseHeader.Text = UiStrings.Get("base.base_header");
            _labels[0].Text = UiStrings.Get("base.npc_label"); _labels[1].Text = UiStrings.Get("base.npc_race_label"); _labels[2].Text = UiStrings.Get("base.npc_seed_label"); _labels[3].Text = UiStrings.Get("base.name_label"); _labels[4].Text = UiStrings.Get("base.items_label");
            _generateNpcSeed.Content = UiStrings.Get("common.generate"); _summon.Content = UiStrings.Get("base.summon_npc"); _export.Content = UiStrings.Get("base.export"); _import.Content = UiStrings.Get("base.import"); _overwrite.Content = UiStrings.Get("base.confirm_import_title");
            _moveComputer.Content = UiStrings.Get("base.move_basecomp"); _delete.Content = UiStrings.Get("base.delete_base"); _top.Content = UiStrings.Get("base.move_base_to_top"); _up.Content = UiStrings.Get("base.move_base_up"); _down.Content = UiStrings.Get("base.move_base_down"); _bottom.Content = UiStrings.Get("base.move_base_to_bottom");
            _sortAsc.Content = UiStrings.Get("base.sort_az"); _sortDesc.Content = UiStrings.Get("base.sort_za"); _clearTerrain.Content = UiStrings.Get("base.clear_terrain"); _clearAllTerrain.Content = UiStrings.Get("base.clear_all_terrain"); _clearOutsideBases.Content = UiStrings.Get("base.clear_all_terrain_except_bases");
            string? race = (_race.SelectedItem as NpcRaceItem)?.InternalName; _race.ItemsSource = BaseNpcRaces.Items(); _race.SelectedItem = _race.Items.Cast<object?>().OfType<NpcRaceItem>().FirstOrDefault(value => value.InternalName == race);
            NpcWorkerItem? selectedWorker = _worker.SelectedItem as NpcWorkerItem; NpcWorkerItem[] workers = _workers.ToArray(); _workers.Clear(); foreach (NpcWorkerItem worker in workers) _workers.Add(worker); _worker.SelectedItem = selectedWorker;
            RefreshBaseList(SelectedBase);
        }
        finally { _loading = old; }
    }

    private static string NewSeed() { Span<byte> bytes = stackalloc byte[8]; System.Security.Cryptography.RandomNumberGenerator.Fill(bytes); return "0x" + Convert.ToHexString(bytes); }
    private static void ReadVector(JsonArray? array, InvariantNumericTextBox[] fields) { for (int i = 0; i < fields.Length; i++) fields[i].NumericValue = array != null && i < array.Length ? array.GetDouble(i) : 0; }
    private static void WriteVector(JsonArray? array, InvariantNumericTextBox[] fields) { if (array == null) return; for (int i = 0; i < fields.Length && i < array.Length; i++) { fields[i].TryCommit(); array.Set(i, fields[i].NumericValue ?? 0); } }
    private static string Scalar(object? value) => value switch { null => string.Empty, IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty, _ => value.ToString() ?? string.Empty };
    private static void SetScalar(JsonObject obj, string key, string? text) { if (string.IsNullOrWhiteSpace(text)) return; string value = text.Trim(); if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer)) obj.Set(key, integer); else if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) obj.Set(key, number); else obj.Set(key, value); }
    private static int TryInt(JsonObject obj, string key) { try { return obj.GetInt(key); } catch { return 0; } }
    private static bool TryBool(JsonObject obj, string key) { try { return obj.GetBool(key); } catch { return false; } }
    private void RequestGoTo(string path) => GoToJsonRequested?.Invoke(this, new GoToJsonRequestEventArgs(path));
    private void RaiseModified() { if (!_loading) DataModified?.Invoke(this, EventArgs.Empty); }
    private async Task<bool> ConfirmOperationAsync(string title, string message) { if (ConfirmAsync != null) return await ConfirmAsync(title, message, CancellationToken.None); return TopLevel.GetTopLevel(this) is Window owner && await BaseUi.ConfirmAsync(owner, title, message); }
    private async Task ShowMessageAsync(string title, string message) { _status.Text = message; if (TopLevel.GetTopLevel(this) is Window owner) await BaseUi.MessageAsync(owner, title, message); }
}
