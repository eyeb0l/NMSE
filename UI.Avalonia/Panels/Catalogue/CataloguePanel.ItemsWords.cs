using System.Collections.ObjectModel;
using Avalonia.Controls;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.UI.Avalonia.Panels.Catalogue;

public sealed partial class CataloguePanel
{
    private readonly Dictionary<CatalogueTransferKind, CatalogueItemCatalog> _itemCatalogs = [];
    private readonly Dictionary<CatalogueTransferKind, CheckBox> _catalogueOnly = [];
    private readonly Dictionary<CatalogueTransferKind, CheckBox> _showSeen = [];

    private (ObservableCollection<CatalogueItemRow> Rows, ObservableCollection<CatalogueItemRow> Visible, TextBox Filter, TextBlock Counter) ItemList(CatalogueTransferKind kind) => kind switch
    {
        CatalogueTransferKind.Products => (_productRows, _visibleProductRows, _productFilter, _productCompletion),
        CatalogueTransferKind.Specials => (_specialRows, _visibleSpecialRows, _specialFilter, _specialCompletion),
        _ => (_techRows, _visibleTechRows, _techFilter, _techCompletion)
    };

    private static CatalogueItemKind ItemKind(CatalogueTransferKind kind) => kind switch
    {
        CatalogueTransferKind.Products => CatalogueItemKind.Product,
        CatalogueTransferKind.Specials => CatalogueItemKind.Special,
        _ => CatalogueItemKind.Technology
    };

    private void LoadItemCatalogs()
    {
        _itemCatalogs.Clear();
        if (_playerState == null) return;
        foreach (var kind in new[] { CatalogueTransferKind.Technologies, CatalogueTransferKind.Products, CatalogueTransferKind.Specials })
        {
            var catalog = new CatalogueItemCatalog(ItemKind(kind), _playerState, _userSettings, _items, _catalogue);
            _itemCatalogs.Add(kind, catalog);
            var list = ItemList(kind);
            list.Rows.Clear();
            foreach (var entry in catalog.Entries)
                list.Rows.Add(new CatalogueItemRow(catalog, entry,
                    _icons?.GetIconForItem(entry.Id, _items), ItemStateChanged));
            ApplyItemFilter(list.Rows, list.Visible, list.Filter.Text);
        }
        UpdateTechnologyCompletion();
    }

    private void ItemStateChanged()
    {
        // Special buildings share KnownProducts with the products tab; account Seen
        // state also overlaps. Refresh all models without rebuilding the active editor.
        foreach (var catalog in _itemCatalogs.Values) catalog.RefreshKnownSets();
        foreach (var row in _techRows.Concat(_productRows).Concat(_specialRows)) row.Refresh();
        RaiseModified();
    }

    private void ApplyItemFilter(IEnumerable<CatalogueItemRow> rows, ObservableCollection<CatalogueItemRow> visible, string? text)
    {
        string filter = (text ?? string.Empty).Trim();
        var kind = ReferenceEquals(rows, _productRows) ? CatalogueTransferKind.Products :
            ReferenceEquals(rows, _specialRows) ? CatalogueTransferKind.Specials : CatalogueTransferKind.Technologies;
        bool catalogueOnly = _catalogueOnly.TryGetValue(kind, out var check) && check.IsChecked == true;
        bool? catalogueAlias = filter.ToLowerInvariant() switch
        {
            "catalogue" or "counted" or "yes" or "y" => true,
            "not catalogue" or "not counted" or "no" or "n" => false,
            _ => null
        };
        visible.Clear();
        foreach (CatalogueItemRow row in rows.Where(row => (!catalogueOnly || row.IsCatalogue) &&
                     (filter.Length == 0 || (catalogueAlias.HasValue ? row.IsCatalogue == catalogueAlias.Value :
                     row.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                     row.Category.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                     row.Description.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                     row.Expedition.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                     row.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)))))
            visible.Add(row);
    }

    private void RemoveSelected<T>(DataGrid grid, ObservableCollection<T> rows, ObservableCollection<T> visible)
    {
        T[] selected = grid.SelectedItems.OfType<T>().ToArray();
        if (selected.Length == 0) return;
        foreach (T row in selected) { rows.Remove(row); visible.Remove(row); }
        RaiseModified();
    }

    private async Task SetVisibleItemsAsync(CatalogueTransferKind kind, bool known)
    {
        if (!_itemCatalogs.TryGetValue(kind, out var catalog)) return;
        var entries = ItemList(kind).Visible.Where(row => known ? row.CanAddKnown && row.Known != true : row.Known != false)
            .Select(row => row.Entry).ToArray();
        if (entries.Length == 0) return;
        string key = known ? "discovery.add_all_visible_confirm" : "discovery.remove_all_visible_confirm";
        if (ConfirmAsync == null || !await ConfirmAsync(UiStrings.Get("discovery.add_all_title"), UiStrings.Format(key, entries.Length), default)) return;
        if (catalog.SetKnown(entries, known) > 0) ItemStateChanged();
    }

    private void UpdateTechnologyCompletion()
    {
        foreach (var kind in new[] { CatalogueTransferKind.Technologies, CatalogueTransferKind.Products, CatalogueTransferKind.Specials })
        {
            var list = ItemList(kind);
            if (!_itemCatalogs.TryGetValue(kind, out var catalog)) { list.Counter.Text = string.Empty; continue; }
            var (have, total) = catalog.GetCatalogueCompletion();
            int all = catalog.Entries.Count;
            int known = catalog.GetKnownCount();
            list.Counter.Text = UiStrings.Format("discovery.catalogue_counter", have, total, total == 0 ? 0 : (int)Math.Round(100d * have / total)) +
                "   " + UiStrings.Format("discovery.all_counter", known, all, all == 0 ? 0 : (int)Math.Round(100d * known / all));
        }
    }

    private Task CompleteTechnologiesAsync() => CompleteItemsAsync(CatalogueTransferKind.Technologies);

    private async Task CompleteItemsAsync(CatalogueTransferKind kind)
    {
        if (_playerState == null) return;
        int missing = CatalogueCompletionLogic.CountMissingCatalogue(_playerState, _userSettings, _items, _catalogue);
        if (missing == 0) return;
        if (ConfirmAsync == null || !await ConfirmAsync(UiStrings.Get("discovery.complete_catalogue"), UiStrings.Format("discovery.complete_catalogue_confirm", missing), default)) return;
        if (CatalogueCompletionLogic.CompleteCatalogue(_playerState, _userSettings, _items, _catalogue) == 0) return;
        ItemStateChanged();
        foreach (var panel in _completionPanels) panel.LoadData(_saveData);
    }

    private void LoadWords(JsonObject playerState)
    {
        _knownWordGroups = playerState.GetArray("KnownWordGroups");
        if (_knownWordGroups == null)
        {
            _knownWordGroups = new JsonArray();
            playerState.Set("KnownWordGroups", _knownWordGroups);
        }
        _wordRows.Clear();
        foreach (WordEntry word in _words.Words)
        {
            var known = new bool[Races.Length];
            for (int i = 0; i < Races.Length; i++)
            {
                string? group = word.GetGroupForRace(Races[i].Race);
                known[i] = group != null && CatalogueLogic.IsWordKnown(_knownWordGroups, group, Races[i].Race);
            }
            _wordRows.Add(new CatalogueWordRow(word, known, WordChanged));
        }
        ApplyWordFilter(_wordFilter.Text ?? string.Empty);
    }

    private void WordChanged(CatalogueWordRow row, int race, bool value)
    {
        if (_loading || _knownWordGroups == null) return;
        string? group = row.Word.GetGroupForRace(race);
        if (group == null) return;
        CatalogueLogic.SetWordKnown(_knownWordGroups, group, race, value);
        RaiseModified();
    }

    private void ApplyWordFilter(string text)
    {
        string filter = text.Trim();
        _visibleWordRows.Clear();
        foreach (CatalogueWordRow row in _wordRows.Where(row => filter.Length == 0 ||
                     row.Text.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                     row.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            _visibleWordRows.Add(row);
    }

    private void SetAllWords(bool value)
    {
        if (_knownWordGroups == null) return;
        foreach (CatalogueWordRow row in _wordRows)
        {
            foreach ((_, int race) in Races)
            {
                string? group = row.Word.GetGroupForRace(race);
                if (group == null) continue;
                CatalogueLogic.SetWordKnown(_knownWordGroups, group, race, value);
                row.SetKnown(race, value, notifyBackingData: false);
            }
        }
        RaiseModified();
    }

    private void SetRaceWords(int race, bool value)
    {
        if (_knownWordGroups == null) return;
        CatalogueLogic.SetWordFlagsForRace(_knownWordGroups, _words.Words, race, value);
        foreach (CatalogueWordRow row in _wordRows)
            if (row.HasRace(race)) row.SetKnown(race, value, notifyBackingData: false);
        RaiseModified();
    }

    private void SetSelectedWords(bool value)
    {
        if (_knownWordGroups == null) return;
        CatalogueWordRow[] selected = _wordGrid.SelectedItems.OfType<CatalogueWordRow>().ToArray();
        if (selected.Length == 0) return;
        CatalogueLogic.SetWordFlagsForEntries(_knownWordGroups, selected.Select(row => row.Word).ToArray(),
            Races.Select(race => (race.Name, race.Race)).ToArray(), value);
        foreach (CatalogueWordRow row in selected)
            foreach ((_, int race) in Races)
                if (row.HasRace(race)) row.SetKnown(race, value, notifyBackingData: false);
        RaiseModified();
    }

    private void SetAllGlyphs(bool value)
    {
        _loading = true;
        try { foreach (CheckBox check in _glyphChecks) check.IsChecked = value; }
        finally { _loading = false; }
        RaiseModified();
    }

    private void LoadGlyphs(JsonObject playerState)
    {
        int value = CatalogueLogic.LoadGlyphBitfield(playerState);
        for (int i = 0; i < _glyphChecks.Length; i++) _glyphChecks[i].IsChecked = (value & (1 << i)) != 0;
    }

    private void SaveGlyphs(JsonObject playerState)
    {
        int value = 0;
        for (int i = 0; i < _glyphChecks.Length; i++) if (_glyphChecks[i].IsChecked == true) value |= 1 << i;
        CatalogueLogic.SaveGlyphBitfield(playerState, value);
    }
}
