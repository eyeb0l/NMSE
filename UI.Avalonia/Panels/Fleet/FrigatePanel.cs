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

namespace NMSE.UI.Avalonia.Panels.Fleet;

public sealed class FrigatePanel : UserControl, ISaveEditorPanel, IGoToJsonSource
{
    private const int MaxFrigates = 30;
    private readonly ObservableCollection<string> _names = [];
    private readonly ListBox _list = new() { Width = 290 };
    private readonly TextBlock _title = new() { FontSize = 20, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _count = new() { Foreground = Brushes.Gray };
    private readonly TextBox _name = new();
    private readonly ComboBox _type = new();
    private readonly ComboBox _class = new();
    private readonly ComboBox _race = new();
    private readonly TextBox _homeSeed = new() { MinWidth = 210 };
    private readonly TextBox _modelSeed = new() { MinWidth = 210 };
    private readonly ComboBox[] _traits = [new(), new(), new(), new(), new()];
    private readonly InvariantNumericTextBox[] _stats = Enumerable.Range(0, 11).Select(_ => new InvariantNumericTextBox { Minimum = 0, Maximum = 999 }).ToArray();
    private readonly InvariantNumericTextBox[] _totals = Enumerable.Range(0, 4).Select(_ => new InvariantNumericTextBox { Minimum = 0, Maximum = 999999 }).ToArray();
    private readonly TextBox _state = new() { IsReadOnly = true };
    private readonly TextBox _levelUp = new() { IsReadOnly = true };
    private readonly TextBox _levelsLeft = new() { IsReadOnly = true };
    private readonly TextBox _mission = new() { IsReadOnly = true };
    private readonly TextBox _expeditionStart = new();
    private readonly TextBlock _damage = new();
    private readonly Button _repair = new();
    private readonly Button _delete = new();
    private readonly Button _copy = new();
    private readonly Button _export = new();
    private readonly Button _import = new();
    private readonly Button _fastForward = new();
    private readonly Button _finish = new();
    private readonly Button[] _seedButtons = [new(), new()];
    private readonly TextBlock _infoHeader;
    private readonly TextBlock _traitsHeader;
    private readonly TextBlock _statsHeader;
    private readonly TextBlock _totalsHeader;
    private readonly TextBlock _progressHeader;
    private readonly TextBlock[] _infoLabels = new TextBlock[6];
    private readonly TextBlock[] _traitLabels = new TextBlock[5];
    private readonly TextBlock[] _statLabels = new TextBlock[11];
    private readonly TextBlock[] _totalLabels = new TextBlock[4];
    private readonly TextBlock[] _progressLabels = new TextBlock[5];
    private readonly Border _details = new() { Padding = new Thickness(8) };
    private readonly TextBlock _status = new() { Foreground = Brushes.Gray };
    private readonly Dictionary<int, int> _rawStats = [];
    private JsonArray? _frigates;
    private JsonArray? _expeditions;
    private bool _loading;

    public event EventHandler? DataModified;
    public event EventHandler<GoToJsonRequestEventArgs>? GoToJsonRequested;
    public Func<FleetTransferRequest, CancellationToken, Task<FleetTransferContent?>>? ImportAsync { get; set; }
    public Func<FleetTransferRequest, string, CancellationToken, Task<bool>>? ExportAsync { get; set; }
    public Func<string, string, CancellationToken, Task<bool>>? ConfirmAsync { get; set; }

    public FrigatePanel()
    {
        _list.ItemsSource = _names;
        _type.ItemsSource = FrigateLogic.FrigateTypes.Select(FrigateLogic.GetLocalisedFrigateTypeName).ToArray();
        _class.ItemsSource = FrigateLogic.FrigateGrades;
        _race.ItemsSource = FrigateLogic.FrigateRaces.Select(FrigateLogic.GetLocalisedFrigateRaceName).ToArray();
        RefreshTraitCombos();

        _list.SelectionChanged += (_, _) => LoadSelected();
        _name.LostFocus += (_, _) => CommitIdentity();
        _homeSeed.LostFocus += (_, _) => CommitIdentity();
        _modelSeed.LostFocus += (_, _) => CommitIdentity();
        _type.SelectionChanged += (_, _) => ChangeType();
        _class.SelectionChanged += (_, _) => ChangeClass();
        _race.SelectionChanged += (_, _) => ChangeRace();
        for (int i = 0; i < _traits.Length; i++) { int index = i; _traits[i].SelectionChanged += (_, _) => ChangeTrait(index); }
        for (int i = 0; i < _stats.Length; i++) { int index = i; _stats[i].NumericValueChanged += (_, _) => ChangeStat(index); }
        string[] totalKeys = ["TotalNumberOfExpeditions", "TotalNumberOfSuccessfulEvents", "TotalNumberOfFailedEvents", "NumberOfTimesDamaged"];
        for (int i = 0; i < _totals.Length; i++) { string key = totalKeys[i]; _totals[i].NumericValueChanged += (_, _) => ChangeTotal(key, _totals[Array.IndexOf(totalKeys, key)]); }
        _expeditionStart.LostFocus += (_, _) => ChangeStartTime();
        _seedButtons[0].Click += (_, _) => { _homeSeed.Text = FleetUi.NewSeed(); CommitIdentity(); };
        _seedButtons[1].Click += (_, _) => { _modelSeed.Text = FleetUi.NewSeed(); CommitIdentity(); };
        _repair.Click += (_, _) => Repair();
        _delete.Click += async (_, _) => await DeleteAsync();
        _copy.Click += async (_, _) => await CopyAsync();
        _export.Click += async (_, _) => await ExportFrigateAsync();
        _import.Click += async (_, _) => await ImportFrigateAsync();
        _fastForward.Click += (_, _) => FastForward();
        _finish.Click += (_, _) => FinishExpedition();

        var form = FleetUi.FormGrid();
        int row = 0;
        _infoHeader = FleetUi.AddHeader(form, row++, "Frigate Info");
        _infoLabels[0] = FleetUi.AddRow(form, row++, "Name:", _name);
        _infoLabels[1] = FleetUi.AddRow(form, row++, "Type:", _type);
        _infoLabels[2] = FleetUi.AddRow(form, row++, "Class:", _class);
        _infoLabels[3] = FleetUi.AddRow(form, row++, "Race:", _race);
        _infoLabels[4] = FleetUi.AddRow(form, row++, "Home Seed:", FleetUi.SeedEditor(_homeSeed, _seedButtons[0]));
        _infoLabels[5] = FleetUi.AddRow(form, row++, "Model Seed:", FleetUi.SeedEditor(_modelSeed, _seedButtons[1]));
        _traitsHeader = FleetUi.AddHeader(form, row++, "Traits");
        for (int i = 0; i < _traits.Length; i++) _traitLabels[i] = FleetUi.AddRow(form, row++, $"Trait {i + 1}:", _traits[i]);
        var damageRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _damage, _repair } };
        FleetUi.AddRow(form, row++, string.Empty, damageRow);

        _statsHeader = FleetUi.AddHeader(form, row++, "Stats");
        for (int i = 0; i < _stats.Length; i++) _statLabels[i] = FleetUi.AddRow(form, row++, FrigateLogic.StatNames[i] + ":", _stats[i]);
        _totalsHeader = FleetUi.AddHeader(form, row++, "Totals");
        string[] totalText = ["Expeditions:", "Successful:", "Failed:", "Damaged:"];
        for (int i = 0; i < _totals.Length; i++) _totalLabels[i] = FleetUi.AddRow(form, row++, totalText[i], _totals[i]);
        _progressHeader = FleetUi.AddHeader(form, row++, "Progress / Mission");
        _progressLabels[0] = FleetUi.AddRow(form, row++, "State:", _state);
        _progressLabels[1] = FleetUi.AddRow(form, row++, "Level Up In:", _levelUp);
        _progressLabels[2] = FleetUi.AddRow(form, row++, "Levels Left:", _levelsLeft);
        FleetUi.AddRow(form, row++, string.Empty, _fastForward);
        _progressLabels[3] = FleetUi.AddRow(form, row++, "Mission Type:", _mission);
        _progressLabels[4] = FleetUi.AddRow(form, row++, "Exp. Start:", _expeditionStart);
        FleetUi.AddRow(form, row++, string.Empty, _finish);
        _details.Child = new ScrollViewer { Content = form };
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
                FleetUi.JsonButton("Fleet JSON", () => RequestGoTo("PlayerStateData.FleetFrigates")),
                FleetUi.JsonButton("Selected frigate JSON", () => { if (_list.SelectedIndex >= 0) RequestGoTo($"PlayerStateData.FleetFrigates[{_list.SelectedIndex}]"); }),
                FleetUi.JsonButton("Expeditions JSON", () => RequestGoTo("PlayerStateData.FleetExpeditions"))
            }
        };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(_title); Grid.SetColumn(nav, 1); header.Children.Add(nav);
        var actions = new WrapPanel { Children = { _delete, _copy, _export, _import, _count } };
        var left = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        left.Children.Add(_list); Grid.SetRow(actions, 1); left.Children.Add(actions);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        body.Children.Add(left); Grid.SetColumn(_details, 1); body.Children.Add(_details);
        var root = new Grid { Margin = new Thickness(10), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 8 };
        root.Children.Add(header); Grid.SetRow(body, 1); root.Children.Add(body); Grid.SetRow(_status, 2); root.Children.Add(_status);
        return root;
    }

    public void RefreshTraitCombos()
    {
        foreach (ComboBox combo in _traits)
        {
            string? selectedId = (combo.SelectedItem as FrigateTrait)?.Id;
            combo.ItemsSource = new[] { FrigateTraitDatabase.None }.Concat(FrigateTraitDatabase.Traits).ToArray();
            combo.SelectedItem = combo.Items.Cast<object?>().OfType<FrigateTrait>().FirstOrDefault(t => t.Id == selectedId) ?? FrigateTraitDatabase.None;
        }
    }

    public void LoadData(JsonObject saveData)
    {
        _loading = true;
        try
        {
            JsonObject? player = saveData.GetObject("PlayerStateData");
            _frigates = player?.GetArray("FleetFrigates");
            _expeditions = player?.GetArray("FleetExpeditions");
            RefreshList();
            _list.SelectedIndex = _frigates is { Length: > 0 } ? 0 : -1;
            LoadSelected();
        }
        finally { _loading = false; }
    }

    public void SaveData(JsonObject saveData)
    {
        bool wasLoading = _loading;
        _loading = true;
        try
        {
            CommitIdentity(force: true);
            foreach (InvariantNumericTextBox field in _stats.Concat(_totals)) field.TryCommit();
            for (int i = 0; i < _stats.Length; i++) ChangeStat(i, force: true);
            string[] totalKeys = ["TotalNumberOfExpeditions", "TotalNumberOfSuccessfulEvents", "TotalNumberOfFailedEvents", "NumberOfTimesDamaged"];
            for (int i = 0; i < _totals.Length; i++) ChangeTotal(totalKeys[i], _totals[i], force: true);
            ChangeStartTime(force: true);
        }
        finally { _loading = wasLoading; }
    }

    private JsonObject? SelectedFrigate()
    {
        int index = _list.SelectedIndex;
        return _frigates != null && index >= 0 && index < _frigates.Length ? _frigates.GetObject(index) : null;
    }

    private void RefreshList()
    {
        int selected = _list.SelectedIndex;
        _names.Clear();
        if (_frigates != null)
            for (int i = 0; i < _frigates.Length; i++)
            {
                JsonObject frigate = _frigates.GetObject(i);
                _names.Add($"{FrigateLogic.GetFrigateName(frigate, i)}  [{FrigateLogic.GetFrigateType(frigate)}] ({FrigateLogic.ComputeClassFromTraits(frigate)})");
            }
        _count.Text = _frigates == null || _frigates.Length == 0 ? UiStrings.Get("frigate.no_frigates_found") : UiStrings.Format("frigate.total_frigates", _frigates.Length);
        if (selected >= 0 && selected < _names.Count) _list.SelectedIndex = selected;
    }

    private void LoadSelected()
    {
        JsonObject? frigate = SelectedFrigate(); _details.IsVisible = frigate != null; if (frigate == null) return;
        _loading = true;
        try
        {
            _name.Text = frigate.GetString("CustomName") ?? string.Empty;
            _type.SelectedIndex = Array.IndexOf(FrigateLogic.FrigateTypes, FrigateLogic.GetFrigateType(frigate));
            _class.SelectedIndex = Array.IndexOf(FrigateLogic.FrigateGrades, FrigateLogic.ComputeClassFromTraits(frigate));
            _race.SelectedIndex = Array.IndexOf(FrigateLogic.FrigateRaces, frigate.GetObject("Race")?.GetString("AlienRace") ?? string.Empty);
            _homeSeed.Text = ReadSeed(frigate, "HomeSystemSeed"); _modelSeed.Text = ReadSeed(frigate, "ResourceSeed");
            JsonArray? traits = frigate.GetArray("TraitIDs");
            for (int i = 0; i < 5; i++) SelectTrait(_traits[i], traits != null && i < traits.Length ? traits.GetString(i) : "^");
            int damage = TryInt(frigate, "DamageTaken"); _damage.Text = damage > 0 ? UiStrings.Format("frigate.damage_format", damage) : UiStrings.Get("frigate.no_damage");
            _rawStats.Clear(); JsonArray? stats = frigate.GetArray("Stats");
            for (int i = 0; i < 11; i++) { int value = stats != null && i < stats.Length ? stats.GetInt(i) : 0; _rawStats[i] = value; _stats[i].NumericValue = Math.Clamp(value, 0, 999); }
            string[] totalKeys = ["TotalNumberOfExpeditions", "TotalNumberOfSuccessfulEvents", "TotalNumberOfFailedEvents", "NumberOfTimesDamaged"];
            for (int i = 0; i < 4; i++) _totals[i].NumericValue = TryInt(frigate, totalKeys[i]);
            int expeditions = TryInt(frigate, "TotalNumberOfExpeditions");
            int until = FrigateLogic.GetLevelUpIn(expeditions); _levelUp.Text = until < 0 ? UiStrings.Get("frigate.level_max") : until.ToString(CultureInfo.CurrentCulture);
            _levelsLeft.Text = FrigateLogic.GetLevelUpsRemaining(expeditions).ToString(CultureInfo.CurrentCulture);
            int state = FrigateLogic.GetFrigateState(frigate, _list.SelectedIndex, _expeditions);
            _state.Text = state >= 0 && state < FrigateLogic.FrigateStateKeys.Length ? UiStrings.Get(FrigateLogic.FrigateStateKeys[state]) : UiStrings.Get("common.unknown");
            int missionIndex = _expeditions == null ? -1 : FrigateLogic.FindExpeditionIndex(_list.SelectedIndex, _expeditions);
            _mission.Text = missionIndex >= 0 && _expeditions != null ? FrigateLogic.GetExpeditionCategory(_expeditions, missionIndex) : string.Empty;
            _finish.IsEnabled = state is 1 or 3; _expeditionStart.IsEnabled = missionIndex >= 0;
            _expeditionStart.Text = missionIndex >= 0 && _expeditions != null
                ? DateTimeOffset.FromUnixTimeSeconds(_expeditions.GetObject(missionIndex).GetLong("StartTime")).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)
                : string.Empty;
        }
        catch { }
        finally { _loading = false; }
    }

    private void CommitIdentity(bool force = false)
    {
        if ((_loading && !force) || SelectedFrigate() is not { } frigate) return;
        frigate.Set("CustomName", _name.Text ?? string.Empty);
        WriteSeed(frigate, "HomeSystemSeed", _homeSeed.Text); WriteSeed(frigate, "ResourceSeed", _modelSeed.Text);
        if (!force) ModifiedAndRefresh();
    }

    private void ChangeType()
    {
        if (_loading || SelectedFrigate() is not { } frigate || _type.SelectedIndex < 0) return;
        string old = FrigateLogic.GetFrigateType(frigate); string next = FrigateLogic.FrigateTypes[_type.SelectedIndex];
        frigate.GetObject("FrigateClass")?.Set("FrigateClass", next); FrigateLogic.AutoAdjustTraitsForTypeChange(frigate, old, next);
        SynchronizeTraitsAndClass(frigate); ModifiedAndRefresh();
    }

    private void ChangeClass()
    {
        if (_loading || SelectedFrigate() is not { } frigate || _class.SelectedIndex < 0) return;
        FrigateLogic.AdjustTraitsForTargetGrade(frigate, FrigateLogic.FrigateGrades[_class.SelectedIndex]);
        SynchronizeTraitsAndClass(frigate); ModifiedAndRefresh();
    }

    private void ChangeRace()
    {
        if (_loading || SelectedFrigate() is not { } frigate || _race.SelectedIndex < 0) return;
        frigate.GetObject("Race")?.Set("AlienRace", FrigateLogic.FrigateRaces[_race.SelectedIndex]); RaiseModified();
    }

    private void ChangeTrait(int index)
    {
        if (_loading || SelectedFrigate() is not { } frigate || frigate.GetArray("TraitIDs") is not { } traits) return;
        string id = _traits[index].SelectedItem is FrigateTrait trait && trait != FrigateTraitDatabase.None ? trait.Id : "^";
        if (index < traits.Length) traits.Set(index, id); SynchronizeTraitsAndClass(frigate); ModifiedAndRefresh();
    }

    private void SynchronizeTraitsAndClass(JsonObject frigate)
    {
        _loading = true;
        try
        {
            JsonArray? values = frigate.GetArray("TraitIDs");
            for (int i = 0; i < 5; i++) SelectTrait(_traits[i], values != null && i < values.Length ? values.GetString(i) : "^");
            string grade = FrigateLogic.ComputeClassFromTraits(frigate); _class.SelectedIndex = Array.IndexOf(FrigateLogic.FrigateGrades, grade);
            frigate.GetObject("InventoryClass")?.Set("InventoryClass", grade);
        }
        finally { _loading = false; }
    }

    private void ChangeStat(int index, bool force = false)
    {
        if ((_loading && !force) || SelectedFrigate() is not { } frigate) return;
        int value = (int)(_stats[index].NumericValue ?? 0);
        if (_rawStats.TryGetValue(index, out int raw) && value == Math.Clamp(raw, 0, 999)) return;
        JsonArray? stats = frigate.GetArray("Stats"); if (stats != null && index < stats.Length) RawNumberGuard.SetInt(stats, index, value); RaiseModified();
    }

    private void ChangeTotal(string key, InvariantNumericTextBox field, bool force = false)
    {
        if ((_loading && !force) || SelectedFrigate() is not { } frigate) return;
        int value = (int)(field.NumericValue ?? 0); if (!RawNumberGuard.IsClampedIntValueUnchanged(frigate.Get(key), value, 0, int.MaxValue)) RawNumberGuard.SetInt(frigate, key, value); RaiseModified();
    }

    private void ChangeStartTime(bool force = false)
    {
        if ((_loading && !force) || _expeditions == null || !DateTime.TryParse(_expeditionStart.Text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out DateTime value)) return;
        int index = FrigateLogic.FindExpeditionIndex(_list.SelectedIndex, _expeditions); if (index < 0) return;
        RawNumberGuard.SetLong(_expeditions.GetObject(index), "StartTime", new DateTimeOffset(value).ToUnixTimeSeconds()); if (!force) RaiseModified();
    }

    private void Repair() { if (SelectedFrigate() is not { } frigate) return; frigate.Set("DamageTaken", 0); frigate.Set("RepairsMade", 0); _damage.Text = UiStrings.Get("frigate.no_damage"); RaiseModified(); }

    private async Task DeleteAsync()
    {
        if (_frigates == null || _list.SelectedIndex < 0) return; int index = _list.SelectedIndex;
        if (_expeditions != null && FrigateLogic.FindExpeditionIndex(index, _expeditions) >= 0) { await ShowErrorAsync(UiStrings.Get("frigate.delete_on_mission")); return; }
        if (!await ConfirmOperationAsync(UiStrings.Get("frigate.delete_title"), UiStrings.Get("frigate.delete_confirm"))) return;
        _frigates.RemoveAt(index); if (_expeditions != null) FrigateLogic.AdjustExpeditionIndicesAfterRemoval(index, _expeditions);
        RefreshList(); _list.SelectedIndex = _names.Count == 0 ? -1 : Math.Min(index, _names.Count - 1); LoadSelected(); RaiseModified();
    }

    private async Task CopyAsync()
    {
        if (_frigates == null || SelectedFrigate() is not { } frigate) return;
        if (_frigates.Length >= MaxFrigates) { await ShowErrorAsync(UiStrings.Get("frigate.max_reached")); return; }
        _frigates.Add(frigate.DeepClone()); RefreshList(); _list.SelectedIndex = _names.Count - 1; LoadSelected(); RaiseModified();
    }

    private void FastForward()
    {
        if (SelectedFrigate() is not { } frigate) return; int count = TryInt(frigate, "TotalNumberOfExpeditions");
        int threshold = FrigateLogic.LevelVictoriesRequired.FirstOrDefault(value => count < value, -1); if (threshold < 0) return;
        frigate.Set("TotalNumberOfExpeditions", threshold - 1); LoadSelected(); RaiseModified();
    }

    private void FinishExpedition()
    {
        if (_expeditions == null || _frigates == null) return; int expeditionIndex = FrigateLogic.FindExpeditionIndex(_list.SelectedIndex, _expeditions); if (expeditionIndex < 0) return;
        JsonObject expedition = _expeditions.GetObject(expeditionIndex); JsonArray? all = expedition.GetArray("AllFrigateIndices");
        foreach (string key in new[] { "DamagedFrigateIndices", "DestroyedFrigateIndices", "ActiveFrigateIndices" }) expedition.GetArray(key)?.Clear();
        JsonArray? active = expedition.GetArray("ActiveFrigateIndices"); if (all != null && active != null) for (int i = 0; i < all.Length; i++) active.Add(all.GetInt(i));
        JsonArray? events = expedition.GetArray("Events"); int total = events?.Length ?? 0; expedition.Set("NextEventToTrigger", total); expedition.Set("NumberOfSuccessfulEventsThisExpedition", total); expedition.Set("NumberOfFailedEventsThisExpedition", 0); expedition.Set("PauseTime", 0);
        if (events != null) for (int i = 0; i < events.Length; i++) events.GetObject(i).Set("Success", true);
        if (all != null) for (int i = 0; i < all.Length; i++) { int f = all.GetInt(i); if (f >= 0 && f < _frigates.Length) { _frigates.GetObject(f).Set("DamageTaken", 0); _frigates.GetObject(f).Set("RepairsMade", 0); } }
        LoadSelected(); RaiseModified();
    }

    private async Task ExportFrigateAsync()
    {
        if (SelectedFrigate() is not { } frigate || ExportAsync == null) { await ShowErrorAsync(UiStrings.Get("common.error")); return; }
        string extension = ExportConfig.Instance.FrigateExt;
        var vars = new Dictionary<string, string> { ["frigate_name"] = _name.Text ?? string.Empty, ["type"] = FrigateLogic.GetFrigateType(frigate), ["class"] = FrigateLogic.ComputeClassFromTraits(frigate) };
        string file = ExportConfig.BuildFileName(ExportConfig.Instance.FrigateTemplate, extension, vars);
        await ExportAsync(new FleetTransferRequest(FleetTransferKind.Frigate, UiStrings.Get("common.export"), file, [extension, ".flt"]), frigate.ToDisplayString(), CancellationToken.None);
    }

    private async Task ImportFrigateAsync()
    {
        if (_frigates == null || ImportAsync == null) { await ShowErrorAsync(UiStrings.Get("common.error")); return; }
        if (_frigates.Length >= MaxFrigates) { await ShowErrorAsync(UiStrings.Get("frigate.max_reached")); return; }
        string extension = ExportConfig.Instance.FrigateExt;
        FleetTransferContent? content = await ImportAsync(new FleetTransferRequest(FleetTransferKind.Frigate, UiStrings.Get("frigate.import_title"), "frigate" + extension, [extension, ".flt"]), CancellationToken.None); if (content == null) return;
        try { _frigates.Add(InventoryImportHelper.UnwrapNomNomFrigate(JsonObject.Parse(content.JsonText))); RefreshList(); _list.SelectedIndex = _names.Count - 1; LoadSelected(); RaiseModified(); }
        catch (Exception ex) { await ShowErrorAsync(UiStrings.Format("common.import_failed", ex.Message)); }
    }

    public void ApplyUiLocalisation()
    {
        bool wasLoading = _loading;
        _loading = true;
        try
        {
        _title.Text = UiStrings.Get("frigate.title"); _delete.Content = UiStrings.Get("common.delete"); _copy.Content = UiStrings.Get("common.copy"); _export.Content = UiStrings.Get("common.export"); _import.Content = UiStrings.Get("common.import");
        _repair.Content = UiStrings.Get("frigate.repair"); _fastForward.Content = UiStrings.Get("frigate.fast_forward"); _finish.Content = UiStrings.Get("frigate.finish_expedition");
        _infoHeader.Text = UiStrings.Get("frigate.info_header"); _traitsHeader.Text = UiStrings.Get("frigate.traits_header"); _statsHeader.Text = UiStrings.Get("frigate.stats_header"); _totalsHeader.Text = UiStrings.Get("frigate.totals_header"); _progressHeader.Text = UiStrings.Get("frigate.progress_header");
        string[] info = ["frigate.name", "frigate.type", "frigate.class", "frigate.npc_race", "frigate.home_seed", "frigate.model_seed"]; for (int i = 0; i < info.Length; i++) _infoLabels[i].Text = UiStrings.Get(info[i]);
        for (int i = 0; i < 5; i++) _traitLabels[i].Text = UiStrings.Get($"frigate.trait_{i + 1}");
        string[] stats = ["frigate.stat_combat", "frigate.stat_exploration", "frigate.stat_industry", "frigate.stat_trading", "frigate.stat_cost_per_warp", "frigate.stat_fuel_cost", "frigate.stat_duration", "frigate.stat_loot", "frigate.stat_repair", "frigate.stat_damage_reduction", "frigate.stat_stealth"]; for (int i = 0; i < stats.Length; i++) _statLabels[i].Text = UiStrings.Get(stats[i]);
        string[] totals = ["frigate.expeditions", "frigate.successful", "frigate.failed", "frigate.times_damaged"]; for (int i = 0; i < totals.Length; i++) _totalLabels[i].Text = UiStrings.Get(totals[i]);
        string[] progress = ["frigate.state", "frigate.level_up_in", "frigate.levels_left", "frigate.mission_type", "frigate.exp_start"]; for (int i = 0; i < progress.Length; i++) _progressLabels[i].Text = UiStrings.Get(progress[i]);
        foreach (Button button in _seedButtons) button.Content = UiStrings.Get("common.generate");
            int type = _type.SelectedIndex, race = _race.SelectedIndex; _type.ItemsSource = FrigateLogic.FrigateTypes.Select(FrigateLogic.GetLocalisedFrigateTypeName).ToArray(); _race.ItemsSource = FrigateLogic.FrigateRaces.Select(FrigateLogic.GetLocalisedFrigateRaceName).ToArray(); _type.SelectedIndex = type; _race.SelectedIndex = race; RefreshTraitCombos(); RefreshList();
        }
        finally { _loading = wasLoading; }
    }

    private static void SelectTrait(ComboBox combo, string id) => combo.SelectedItem = combo.Items.Cast<object?>().OfType<FrigateTrait>().FirstOrDefault(t => t.Id == id) ?? FrigateTraitDatabase.None;
    private static string ReadSeed(JsonObject frigate, string key) { try { return frigate.GetArray(key)?.GetString(1) ?? string.Empty; } catch { return string.Empty; } }
    private static void WriteSeed(JsonObject frigate, string key, string? value) { string? normalized = SeedHelper.NormalizeSeed(value); JsonArray? array = frigate.GetArray(key); if (normalized != null && array is { Length: >= 2 }) array.Set(1, normalized); }
    private static int TryInt(JsonObject obj, string key) { try { return obj.GetInt(key); } catch { return 0; } }
    private void ModifiedAndRefresh() { RaiseModified(); RefreshList(); }
    private void RequestGoTo(string path) => GoToJsonRequested?.Invoke(this, new GoToJsonRequestEventArgs(path));
    private void RaiseModified() { if (!_loading) DataModified?.Invoke(this, EventArgs.Empty); }
    private async Task<bool> ConfirmOperationAsync(string title, string message) { if (ConfirmAsync != null) return await ConfirmAsync(title, message, CancellationToken.None); return TopLevel.GetTopLevel(this) is Window owner && await FleetUi.ConfirmAsync(owner, title, message); }
    private async Task ShowErrorAsync(string message) { _status.Text = message; if (TopLevel.GetTopLevel(this) is Window owner) await FleetUi.MessageAsync(owner, UiStrings.Get("common.error"), message); }
}
