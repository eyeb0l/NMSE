using System.Collections.ObjectModel;
using Avalonia.Controls;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.UI.Avalonia.Panels.Catalogue;

public sealed partial class CataloguePanel
{
    private void LoadKnownItems(JsonObject playerState, string arrayName,
        ObservableCollection<CatalogueItemRow> rows,
        ObservableCollection<CatalogueItemRow> visible,
        string? filter)
    {
        rows.Clear();
        foreach (string id in CatalogueLogic.LoadKnownItemIds(playerState, arrayName))
            rows.Add(CreateItemRow(id, useSubtitle: false));
        ApplyItemFilter(rows, visible, filter);
    }

    private CatalogueItemRow CreateItemRow(string id, bool useSubtitle)
    {
        string cleanId = CatalogueLogic.StripCaretPrefix(id);
        GameItem? item = _items.GetItem(cleanId) ?? _items.GetItem(id);
        return new CatalogueItemRow
        {
            Icon = _icons?.GetIconForItem(cleanId, _items),
            Name = item?.Name ?? cleanId,
            Category = useSubtitle && !string.IsNullOrWhiteSpace(item?.Subtitle) ? item.Subtitle : item?.ItemType ?? string.Empty,
            Id = cleanId
        };
    }

    private static void ApplyItemFilter(IEnumerable<CatalogueItemRow> rows, ObservableCollection<CatalogueItemRow> visible, string? text)
    {
        string filter = (text ?? string.Empty).Trim();
        visible.Clear();
        foreach (CatalogueItemRow row in rows.Where(row => filter.Length == 0 ||
                     row.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                     row.Category.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                     row.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            visible.Add(row);
    }

    private void RemoveSelected<T>(DataGrid grid, ObservableCollection<T> rows, ObservableCollection<T> visible)
    {
        T[] selected = grid.SelectedItems.OfType<T>().ToArray();
        if (selected.Length == 0) return;
        foreach (T row in selected) { rows.Remove(row); visible.Remove(row); }
        RaiseModified();
    }

    private Task AddTechnologyAsync() => AddItemsAsync(
        UiStrings.Get("discovery.add_technology"),
        _techRows, _visibleTechRows, _techFilter,
        item => CatalogueLogic.TechItemTypes.Contains(item.ItemType) &&
                !item.BrokenSlotTech && !GameItemDatabase.IsPickerExcluded(item.Id) && CatalogueLogic.IsLearnableTechnology(item));

    private (string Array, string Seen, IReadOnlyList<string> Ids, ObservableCollection<CatalogueItemRow> Rows, ObservableCollection<CatalogueItemRow> Visible, TextBox Filter, TextBlock Counter) CompletionList(CatalogueTransferKind kind) => kind switch
    {
        CatalogueTransferKind.Products => ("KnownProducts", "SeenProducts", _catalogue.KnownProductsForCompletion(_items), _productRows, _visibleProductRows, _productFilter, _productCompletion),
        CatalogueTransferKind.Specials => ("KnownSpecials", "SeenProducts", _catalogue.KnownSpecialsForCompletion(_items), _specialRows, _visibleSpecialRows, _specialFilter, _specialCompletion),
        _ => ("KnownTech", "SeenTechnologies", _catalogue.KnownTechForCompletion(_items), _techRows, _visibleTechRows, _techFilter, _techCompletion)
    };

    private void UpdateTechnologyCompletion()
    {
        foreach (var kind in new[] { CatalogueTransferKind.Technologies, CatalogueTransferKind.Products, CatalogueTransferKind.Specials })
        {
            var list = CompletionList(kind);
            if (_playerState == null) { list.Counter.Text = string.Empty; continue; }
            var view = new JsonObject(); view.Set(list.Array, StringArray(list.Rows.Select(row => row.Id)));
            var (have, total) = CatalogueCompletionLogic.GetCombinedCompletion(view, list.Array, _userSettings, list.Seen, list.Ids);
            list.Counter.Text = UiStrings.Format("discovery.completion_counter", have, total, total == 0 ? 0 : (int)Math.Round(100d * have / total));
        }
    }

    private Task CompleteTechnologiesAsync() => CompleteItemsAsync(CatalogueTransferKind.Technologies);

    private async Task CompleteItemsAsync(CatalogueTransferKind kind)
    {
        if (_playerState == null) return;
        var list = CompletionList(kind);
        var view = new JsonObject(); view.Set(list.Array, StringArray(list.Rows.Select(row => row.Id)));
        var (have, total) = CatalogueCompletionLogic.GetCombinedCompletion(view, list.Array, _userSettings, list.Seen, list.Ids);
        int missing = total - have;
        if (kind == CatalogueTransferKind.Products)
        {
            var specials = CompletionList(CatalogueTransferKind.Specials);
            var specialView = new JsonObject(); specialView.Set(specials.Array, StringArray(specials.Rows.Select(row => row.Id)));
            var specialCount = CatalogueCompletionLogic.GetCompletion(specialView, specials.Array, specials.Ids);
            missing += specialCount.Total - specialCount.Have;
            if (_userSettings != null)
            {
                var substances = CatalogueCompletionLogic.GetSeenCompletion(_userSettings, "SeenSubstances", _catalogue.SeenSubstances);
                missing += substances.Total - substances.Have;
            }
        }
        // Technology entries filtered out of KnownTech still belong in the account seen list.
        if (kind == CatalogueTransferKind.Technologies && _userSettings != null)
        {
            var seen = CatalogueCompletionLogic.GetSeenCompletion(_userSettings, list.Seen, _catalogue.KnownTech);
            missing = Math.Max(missing, seen.Total - seen.Have);
        }
        if (missing == 0) return;
        if (ConfirmAsync != null && !await ConfirmAsync(UiStrings.Get("discovery.add_all_missing_title"), UiStrings.Format("discovery.add_all_missing_confirm", missing), default)) return;
        CatalogueLogic.SaveKnownItemIds(_playerState, list.Array, list.Rows.Select(row => row.Id).ToList());
        int changed = CatalogueCompletionLogic.AddMissingIds(_playerState, list.Array, list.Ids);
        if (_userSettings != null)
            changed += CatalogueCompletionLogic.AddMissingSeen(_userSettings, list.Seen, kind == CatalogueTransferKind.Technologies ? _catalogue.KnownTech : list.Ids);
        if (kind == CatalogueTransferKind.Products)
        {
            CatalogueLogic.SaveKnownItemIds(_playerState, "KnownSpecials", _specialRows.Select(row => row.Id).ToList());
            changed += CatalogueCompletionLogic.AddMissingIds(_playerState, "KnownSpecials", _catalogue.KnownSpecialsForCompletion(_items));
            if (_userSettings != null) changed += CatalogueCompletionLogic.AddMissingSeen(_userSettings, "SeenSubstances", _catalogue.SeenSubstances);
            LoadKnownItems(_playerState, "KnownSpecials", _specialRows, _visibleSpecialRows, _specialFilter.Text);
        }
        LoadKnownItems(_playerState, list.Array, list.Rows, list.Visible, list.Filter.Text);
        if (changed > 0) RaiseModified();
        else UpdateTechnologyCompletion();
    }

    private Task AddProductAsync() => AddItemsAsync(
        UiStrings.Get("discovery.add_product"),
        _productRows, _visibleProductRows, _productFilter,
        item => CatalogueLogic.ProductItemTypes.Contains(item.ItemType) &&
                !GameItemDatabase.IsPickerExcluded(item.Id) && CatalogueLogic.IsLearnableProduct(item));

    private Task AddSpecialAsync() => AddItemsAsync(
        UiStrings.Get("discovery.add_special"),
        _specialRows, _visibleSpecialRows, _specialFilter,
        item => string.Equals(item.TradeCategory, "SpecialShop", StringComparison.OrdinalIgnoreCase) &&
                !GameItemDatabase.IsPickerExcluded(item.Id));

    private async Task AddItemsAsync(string title,
        ObservableCollection<CatalogueItemRow> rows,
        ObservableCollection<CatalogueItemRow> visible,
        TextBox filter,
        Func<GameItem, bool> predicate)
    {
        if (Owner is not { } owner) return;
        var known = rows.Select(row => row.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        CataloguePickerItem[] candidates = _items.Items.Values
            .Where(item => predicate(item) && !known.Contains(item.Id))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => new CataloguePickerItem
            {
                Icon = _icons?.GetIconForItem(item.Id, _items),
                Name = item.Name,
                Id = item.Id,
                Category = string.IsNullOrWhiteSpace(item.Subtitle) ? item.ItemType : item.Subtitle
            }).ToArray();
        if (candidates.Length == 0) return;
        var picker = new CataloguePickerDialog(title, candidates);
        string[]? selected = await picker.ShowDialog<string[]>(owner);
        if (selected == null || selected.Length == 0) return;
        foreach (string id in selected)
        {
            if (known.Add(id)) rows.Add(CreateItemRow(id, useSubtitle: true));
        }
        ApplyItemFilter(rows, visible, filter.Text);
        RaiseModified();
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
