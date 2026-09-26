using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Contracts;

namespace NMSE.UI.Avalonia.Panels.BasesStorage;

public sealed class SpaceStationPanel : UserControl, ISaveEditorPanel, IGoToJsonSource
{
    private readonly ListBox _systems = new() { Width = 260 };
    private readonly StackPanel _form = new() { Spacing = 8 };
    private readonly Dictionary<string, NumericUpDown> _stats = [];
    private readonly Dictionary<string, ComboBox> _missions = [];
    private readonly TextBox[] _packed = [new(), new()];
    private readonly ComboBox[] _slots = new ComboBox[32];
    private readonly TextBlock[] _slotLabels = new TextBlock[32];
    private readonly TextBlock _address = new();
    private readonly Button _claimable = new();
    private readonly Button _base = new();
    private JsonObject? _save;
    private JsonObject? _player;
    private JsonObject? _poi;
    private bool _loading;
    public event EventHandler? DataModified;
    public event EventHandler<GoToJsonRequestEventArgs>? GoToJsonRequested;
    public event EventHandler<int>? GoToBaseRequested;
    public Func<string, string, CancellationToken, Task<bool>>? ConfirmAsync { get; set; }

    public SpaceStationPanel()
    {
        _systems.SelectionChanged += (_, _) => LoadSelected();
        _form.Children.Add(_address);
        foreach (string id in SpaceStationLogic.CosmosMissionIds)
        {
            var combo = new ComboBox();
            _missions[id] = combo;
            AddRow(UiStrings.Get(SpaceStationLogic.GetCosmosMissionLabelKey(id)!), combo);
            combo.SelectionChanged += (_, _) =>
            {
                if (_loading || _player == null || combo.SelectedIndex < 0) return;
                SpaceStationLogic.ApplyMissionState(_player, id, (SpaceStationLogic.MissionState)combo.SelectedIndex);
                DataModified?.Invoke(this, EventArgs.Empty);
            };
        }
        foreach (string id in SpaceStationLogic.DisplayStatOrder)
        {
            var field = new NumericUpDown { Minimum = int.MinValue, Maximum = int.MaxValue, FormatString = "0" };
            _stats[id] = field; AddRow(UiStrings.Get("base.station.stat_" + id.TrimStart('^').ToLowerInvariant()), field);
            field.ValueChanged += (_, _) =>
            {
                if (_loading || _systems.SelectedItem is not SpaceStationLogic.StationSystemEntry system) return;
                SpaceStationLogic.SetStatValue(system.Entry, id, (int)(field.Value ?? 0));
                DataModified?.Invoke(this, EventArgs.Empty);
            };
        }
        _claimable.Click += async (_, _) =>
        {
            if (_systems.SelectedItem is not SpaceStationLogic.StationSystemEntry system) return;
            if (ConfirmAsync == null || !await ConfirmAsync(UiStrings.Get("base.station.set_claimable_title"), UiStrings.Get("base.station.set_claimable_confirm"), default)) return;
            if (SpaceStationLogic.ApplyClaimableMinimums(system.Entry)) DataModified?.Invoke(this, EventArgs.Empty);
            LoadSelected();
        };
        _base.Click += (_, _) =>
        {
            if (_systems.SelectedItem is SpaceStationLogic.StationSystemEntry system &&
                SpaceStationLogic.FindStationBase(_player?.GetArray("PersistentPlayerBases"), system.Address) is { } value)
                GoToBaseRequested?.Invoke(this, value.DataIndex);
        };
        _form.Children.Add(new WrapPanel { Children = { _claimable, _base, BaseUi.JsonButton(() =>
        {
            if (_systems.SelectedItem is SpaceStationLogic.StationSystemEntry system)
                GoToJsonRequested?.Invoke(this, new GoToJsonRequestEventArgs($"PlayerStateData.Stats[{system.StatsIndex}]"));
        }) } });
        for (int i = 0; i < 2; i++)
        {
            int index = i; AddRow(UiStrings.Get("base.station.packed_data" + i), _packed[i]);
            _packed[i].LostFocus += (_, _) => CommitPacked(index);
        }
        for (int i = 0; i < 32; i++)
        {
            int slot = i;
            _slots[i] = new ComboBox();
            _slotLabels[i] = AddRow((i + 1).ToString(CultureInfo.InvariantCulture), _slots[i]);
            _slots[i].SelectionChanged += (_, _) =>
            {
                if (_loading || _poi == null || _slots[slot].SelectedIndex < 0) return;
                string key = "PackedData" + slot / 16;
                if (!SpaceStationLogic.TryGetPackedData(_poi, key, out ulong value, out bool hex)) return;
                ulong updated = SpaceStationLogic.SetPackedDataSlot(value, slot % 16, (SpaceStationLogic.SpacePoiDiscoveryLevel)_slots[slot].SelectedIndex);
                if (SpaceStationLogic.SetPackedData(_poi, key, updated, hex)) DataModified?.Invoke(this, EventArgs.Empty);
                LoadSelected();
            };
        }
        var root = new Grid { Margin = new Thickness(10), ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        root.Children.Add(_systems);
        var scroll = new ScrollViewer { Content = _form }; root.Children.Add(scroll); Grid.SetColumn(scroll, 1);
        Content = root; ApplyUiLocalisation();
    }

    private TextBlock AddRow(string text, Control control)
    {
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("240,*"), ColumnSpacing = 8 };
        row.Children.Add(label); row.Children.Add(control); Grid.SetColumn(control, 1); _form.Children.Add(row);
        return label;
    }

    public void LoadData(JsonObject saveData)
    {
        _save = saveData; _player = saveData.GetObject("PlayerStateData");
        var systems = _player == null ? new List<SpaceStationLogic.StationSystemEntry>() : SpaceStationLogic.FindStationSystems(_player);
        foreach (var system in systems)
            system.DisplayName = SpaceStationLogic.ResolveStationBaseName(_player!, system.Address)
                ?? SpaceStationLogic.ResolveSystemName(saveData, system.Address) ?? SpaceStationLogic.AddressToPortalCode(system.Address);
        _systems.ItemsSource = systems; _systems.SelectedIndex = systems.Count > 0 ? 0 : -1;
        LoadSelected();
    }

    public void SaveData(JsonObject saveData) { CommitPacked(0); CommitPacked(1); SpacePoiLayoutCache.SaveIfDirty(); }

    public void ApplyUiLocalisation()
    {
        _loading = true;
        foreach (var combo in _missions.Values) combo.ItemsSource = new[] { "not_started", "active", "completed" }.Select(x => UiStrings.Get("base.station.mission_" + x)).ToArray();
        foreach (var combo in _slots) combo.ItemsSource = new[] { "hidden", "undiscovered", "discovered", "completed" }.Select(x => UiStrings.Get("base.station.packed_data_level_" + x)).ToArray();
        _claimable.Content = UiStrings.Get("base.station.set_claimable_values"); _base.Content = UiStrings.Get("base.station.go_to_base");
        _loading = false; LoadSelected();
    }

    private void LoadSelected()
    {
        _loading = true;
        try
        {
            var system = _systems.SelectedItem as SpaceStationLogic.StationSystemEntry;
            foreach (var (id, combo) in _missions) { combo.IsEnabled = _player != null; combo.SelectedIndex = _player == null ? -1 : (int)SpaceStationLogic.GetMissionState(_player, id); }
            _address.Text = system == null ? string.Empty : SpaceStationLogic.AddressToPortalCode(system.Address);
            _claimable.IsEnabled = system != null;
            _base.IsEnabled = system != null && SpaceStationLogic.FindStationBase(_player?.GetArray("PersistentPlayerBases"), system.Address) != null;
            foreach (var (id, field) in _stats) { field.IsEnabled = system != null; field.Value = system == null ? 0 : SpaceStationLogic.GetStatValue(system.Entry, id); }
            _poi = system == null ? null : SpaceStationLogic.FindSpacePoiEntry(_player?.GetArray("SpacePoiDiscoveries"), system.Address)?.Data;
            ulong[] values = new ulong[2];
            bool[] present = new bool[2];
            for (int i = 0; i < 2; i++)
            {
                bool hex = false;
                present[i] = _poi != null && SpaceStationLogic.TryGetPackedData(_poi, "PackedData" + i, out values[i], out hex);
                _packed[i].IsEnabled = present[i];
                _packed[i].Text = present[i] ? SpaceStationLogic.FormatPackedData(values[i], hex) : string.Empty;
            }
            string[] tokens = Enumerable.Repeat(SpacePoiSlotTokens.Unknown, 32).ToArray();
            if (present.All(x => x) && SpacePoiTableDatabase.Types.Count > 0)
            {
                bool abandoned = _player?.GetObject("DifficultyState")?.GetObject("Settings")?.GetObject("NPCPopulation")?.GetString("NPCPopulationDifficulty") == "Abandoned";
                var inferred = SpacePoiSlotInference.Infer(values[0], values[1], SpacePoiTableDatabase.Types, abandoned)
                    ?? SpacePoiSlotInference.Infer(values[0], values[1], SpacePoiTableDatabase.Types, !abandoned);
                if (inferred != null)
                    tokens = inferred.Slots.Select(x => x.TypeIsUnique ? SpacePoiTableDatabase.Types[x.TypeIndex].Type : x.CoveredByLayouts ? SpacePoiSlotTokens.Unknown : SpacePoiSlotTokens.Unallocated).ToArray();
                if (system?.Address is long address)
                {
                    ulong ua = unchecked((ulong)address);
                    SpacePoiLayoutCache.Remember(ua, tokens); tokens = SpacePoiLayoutCache.Get(ua) ?? tokens;
                }
            }
            for (int i = 0; i < 32; i++)
            {
                _slots[i].IsEnabled = present[i / 16]; _slots[i].SelectedIndex = present[i / 16] ? (int)SpaceStationLogic.GetPackedDataSlot(values[i / 16], i % 16) : -1;
                string type = SpacePoiTableDatabase.Types.FirstOrDefault(x => x.Type == tokens[i])?.DisplayName
                    ?? UiStrings.Get(tokens[i] == "-" ? "base.station.packed_data_unallocated" : "base.station.packed_data_unidentified");
                _slotLabels[i].Text = $"{i + 1}: {type}";
            }
        }
        finally { _loading = false; }
    }

    private void CommitPacked(int index)
    {
        if (_loading || _poi == null || !_packed[index].IsEnabled) return;
        if (!SpaceStationLogic.TryParsePackedData(_packed[index].Text, out ulong value)) { LoadSelected(); return; }
        SpaceStationLogic.TryGetPackedData(_poi, "PackedData" + index, out _, out bool hex);
        if (SpaceStationLogic.SetPackedData(_poi, "PackedData" + index, value, hex)) DataModified?.Invoke(this, EventArgs.Empty);
        LoadSelected();
    }
}
