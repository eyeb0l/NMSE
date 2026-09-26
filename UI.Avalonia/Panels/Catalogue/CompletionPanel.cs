using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.UI.Avalonia.Panels.Catalogue;

internal sealed class CompletionPanel : UserControl
{
    private readonly string _kind;
    private readonly GameItemDatabase _items;
    private readonly CatalogueDatabase _catalogue = new(Path.Combine(ApplicationPaths.Current.ResourcesDirectory, "json"));
    private readonly TextBox _filter = new();
    private readonly DataGrid _grid = new() { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Extended };
    private readonly TextBlock _count = new();
    private readonly Button _complete = new();
    private readonly Button _clear = new();
    private readonly Button _all = new();
    private readonly Button _clearAll = new();
    private readonly NumericUpDown _progress = new() { Minimum = 0, Maximum = int.MaxValue, Width = 130, FormatString = "0" };
    private readonly Button _applyProgress = new();
    private List<Entry> _entries = [];
    private JsonObject? _save;
    private JsonObject? _account;
    public event EventHandler? DataModified;
    public Func<string, string, CancellationToken, Task<bool>>? ConfirmAsync { get; set; }

    internal sealed record Entry(string Name, string Id, bool Complete, int? Progress, int? Target,
        Func<bool, bool> Toggle, Func<int, bool>? SetProgress = null);

    public CompletionPanel(string kind, GameItemDatabase items)
    {
        _kind = kind; _items = items;
        foreach (string field in new[] { "Complete", "Name", "Id", "Progress", "Target" })
            _grid.Columns.Add(new DataGridTextColumn { Header = field, Binding = new Binding(field), Width = new DataGridLength(field == "Name" ? 3 : 1, DataGridLengthUnitType.Star) });
        _filter.TextChanged += (_, _) => Filter();
        _complete.Click += async (_, _) => await ChangeAsync(true, false);
        _clear.Click += async (_, _) => await ChangeAsync(false, false);
        _all.Click += async (_, _) => await ChangeAsync(true, true);
        _clearAll.Click += async (_, _) => await ChangeAsync(false, true);
        _grid.SelectionChanged += (_, _) =>
        {
            var entry = _grid.SelectedItem as Entry;
            _progress.IsEnabled = _applyProgress.IsEnabled = entry?.SetProgress != null;
            _progress.Value = entry?.Progress ?? 0;
        };
        _applyProgress.Click += (_, _) =>
        {
            if (_grid.SelectedItem is Entry entry && entry.SetProgress?.Invoke((int)(_progress.Value ?? 0)) == true)
            { DataModified?.Invoke(this, EventArgs.Empty); Reload(); }
        };
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, Children = { _complete, _clear, _all, _clearAll, _progress, _applyProgress } };
        var root = new Grid { Margin = new Thickness(10), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 8 };
        root.Children.Add(_filter); root.Children.Add(_grid); Grid.SetRow(_grid, 1);
        root.Children.Add(actions); Grid.SetRow(actions, 2); root.Children.Add(_count); Grid.SetRow(_count, 3);
        Content = root; ApplyUiLocalisation();
    }

    public void LoadData(JsonObject? save) { _save = save; Reload(); }
    public void SetAccountData(JsonObject? account) { _account = account; Reload(); }

    public void ApplyUiLocalisation()
    {
        _filter.PlaceholderText = UiStrings.Get("discovery.filter_placeholder");
        _complete.Content = UiStrings.Get("discovery.status_complete");
        _clear.Content = UiStrings.Get("common.remove_selected");
        _all.Content = UiStrings.Get("discovery.complete_all");
        _clearAll.Content = UiStrings.Get("discovery.clear_all");
        _applyProgress.Content = UiStrings.Get("common.apply");
        string[] keys = ["discovery.col_status", "discovery.col_name", "discovery.col_id", "discovery.col_progress", "discovery.col_target"];
        for (int i = 0; i < keys.Length; i++) _grid.Columns[i].Header = UiStrings.Get(keys[i]);
        Reload();
    }

    private async Task ChangeAsync(bool complete, bool all)
    {
        Entry[] entries = (all ? _entries : _grid.SelectedItems.Cast<Entry>()).Where(x => x.Complete != complete).ToArray();
        if (entries.Length == 0) return;
        if (ConfirmAsync != null && !await ConfirmAsync(UiStrings.Get(complete ? "discovery.complete_all" : "discovery.clear_all"),
            UiStrings.Format(complete ? "discovery.complete_all_confirm" : "discovery.clear_all_confirm", entries.Length), default)) return;
        bool changed = false;
        foreach (var entry in entries) changed |= entry.Toggle(complete);
        if (changed) DataModified?.Invoke(this, EventArgs.Empty);
        Reload();
    }

    private void Reload()
    {
        _entries = [];
        var player = _save?.GetObject("PlayerStateData");
        var settings = _account?.GetObject("UserSettingsData") ?? _account;
        bool accountKind = _kind is "fossils" or "materials";
        if (accountKind && settings != null)
        {
            string array = _kind == "fossils" ? "SeenProducts" : "SeenSubstances";
            var ids = _kind == "fossils" ? _catalogue.Fossils : _catalogue.SeenSubstances;
            var current = CatalogueCompletionLogic.GetSeenIdSet(settings, array);
            foreach (string id in ids)
                _entries.Add(new(_items.GetItem(id)?.Name ?? id, id, current.Contains(id), null, null,
                    value => value ? CatalogueCompletionLogic.AddMissingSeen(settings, array, [id]) > 0 : CatalogueCompletionLogic.RemoveSeen(settings, array, [id]) > 0));
        }
        else if (player != null && _kind == "stats")
        {
            var stats = CatalogueCompletionLogic.GetGlobalStatsMap(player);
            foreach (var (id, target) in _catalogue.DiscoveryStats)
            {
                int? value = stats.TryGetValue(id, out var stat) ? CatalogueCompletionLogic.GetGlobalInt(stat) : null;
                _entries.Add(new(id, id, CatalogueCompletionLogic.IsGlobalStatComplete(value, target), value, target,
                    done => done ? CatalogueCompletionLogic.SetGlobalStatAtLeast(player, id, target) : CatalogueCompletionLogic.ClearGlobalStat(player, id),
                    progress => CatalogueCompletionLogic.SetGlobalStatValue(player, id, progress)));
            }
        }
        else if (player != null && _kind == "knowledge")
        {
            foreach (var status in CatalogueCompletionLogic.GetKnowledgeStatuses(player))
            {
                var page = status.Page;
                if (page.Recipe == KnowledgeRecipe.Words) continue;
                _entries.Add(new(page.Name, page.Id, status.Status == "OK", status.LastSeen, page.TableMax,
                    done => done ? CatalogueCompletionLogic.ApplyKnowledgePage(player, page, _catalogue.StoryCompleters) : CatalogueCompletionLogic.ClearKnowledgePage(player, page),
                    value => CatalogueCompletionLogic.ApplyKnowledgePageProgress(player, page, value)));
            }
            foreach (var status in CatalogueCompletionLogic.GetKnowledgeCompleterStatuses(player, _catalogue.StoryCompleters))
                _entries.Add(new(status.Name, status.Id, status.Complete, status.Current, status.Target,
                    done => done ? CatalogueCompletionLogic.ApplyKnowledgeCompleter(player, status, _catalogue.StoryCompleters) : CatalogueCompletionLogic.ClearKnowledgeCompleter(player, status),
                    value => CatalogueCompletionLogic.ApplyKnowledgeCompleterProgress(player, status, value)));
        }
        else if (player != null && _save != null && _kind == "wonders")
        {
            foreach (string key in CatalogueCompletionLogic.WonderKeys)
            {
                IReadOnlyList<JsonObject>? records = key == "WonderTreasureRecords" ? _catalogue.WonderTreasureRecords
                    : key == "WonderWeirdBasePartRecords" ? _catalogue.WonderWeirdBasePartRecords : null;
                var (_, total) = CatalogueCompletionLogic.GetWonderCompletion(player, key, records, _catalogue.WonderFallbackSlots);
                var slots = player.GetArray(key);
                for (int i = 0; i < total; i++)
                {
                    int index = i;
                    bool filled = slots != null && i < slots.Length && CatalogueCompletionLogic.IsWonderSlotFilled(slots.GetObject(i));
                    _entries.Add(new(key, UiStrings.Format("discovery.wonders_slot", i + 1), filled, null, null,
                        done => done ? CatalogueCompletionLogic.FillWonderSlot(_save, player, key, index, records, _catalogue.WonderFallbackSlots)
                            : CatalogueCompletionLogic.ClearWonderSlot(player, key, index)));
                }
            }
        }
        _all.IsEnabled = _clearAll.IsEnabled = _entries.Count > 0;
        _count.Text = accountKind && settings == null ? UiStrings.Get("discovery.account_unavailable")
            : $"{_entries.Count(x => x.Complete)} / {_entries.Count}";
        Filter();
    }

    private void Filter()
    {
        string query = _filter.Text ?? string.Empty;
        _grid.ItemsSource = _entries.Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || x.Id.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
}
