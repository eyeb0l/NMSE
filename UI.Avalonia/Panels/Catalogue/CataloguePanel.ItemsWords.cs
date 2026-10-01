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

    private void UpdateTechnologyCompletion()
    {
        if (_playerState == null) { _techCompletion.Text = string.Empty; return; }
        var view = new JsonObject(); view.Set("KnownTech", StringArray(_techRows.Select(row => row.Id)));
        var (have, total) = CatalogueCompletionLogic.GetCompletion(view, "KnownTech", _catalogue.KnownTechForCompletion(_items));
        _techCompletion.Text = UiStrings.Format("discovery.completion_counter", have, total, total == 0 ? 0 : (int)Math.Round(100d * have / total));
    }

    private async Task CompleteTechnologiesAsync()
    {
        if (_playerState == null) return;
        var ids = _catalogue.KnownTechForCompletion(_items);
        var view = new JsonObject(); view.Set("KnownTech", StringArray(_techRows.Select(row => row.Id)));
        var (have, total) = CatalogueCompletionLogic.GetCompletion(view, "KnownTech", ids);
        if (have == total) return;
        if (ConfirmAsync != null && !await ConfirmAsync(UiStrings.Get("discovery.add_all_missing_title"),
            UiStrings.Format("discovery.add_all_missing_confirm", total - have), default)) return;
        CatalogueLogic.SaveKnownItemIds(_playerState, "KnownTech", _techRows.Select(row => row.Id).ToList());
        if (CatalogueCompletionLogic.AddMissingIds(_playerState, "KnownTech", ids) > 0)
        {
            LoadKnownItems(_playerState, "KnownTech", _techRows, _visibleTechRows, _techFilter.Text);
            RaiseModified();
        }
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
