using System.Collections.ObjectModel;
using Avalonia.Controls;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.UI.Avalonia.Panels.Catalogue;

public sealed partial class CataloguePanel
{
    private static (string Title, string Name, string Path) TransferInfo(CatalogueTransferKind kind) => kind switch
    {
        CatalogueTransferKind.Technologies => ("Known Technologies", "Technologies", "PlayerStateData.KnownTech"),
        CatalogueTransferKind.Products => ("Known Products", "Products", "PlayerStateData.KnownProducts"),
        CatalogueTransferKind.Specials => ("Known Specials", "Specials", "PlayerStateData.KnownSpecials"),
        CatalogueTransferKind.Words => ("Known Words", "Words", "PlayerStateData.KnownWordGroups"),
        CatalogueTransferKind.Glyphs => ("Known Glyphs", "Glyphs", "PlayerStateData.KnownPortalRunes"),
        CatalogueTransferKind.Locations => ("Known Locations", "Locations", "PlayerStateData.TeleportEndpoints"),
        CatalogueTransferKind.Fish => ("Known Fish", "Fish", "PlayerStateData.FishingRecord"),
        CatalogueTransferKind.Recipes => ("Known Recipes", "Recipes", "PlayerStateData.KnownRefinerRecipes"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private CatalogueTransferRequest Request(CatalogueTransferKind kind, bool import)
    {
        (string title, string name, string path) = TransferInfo(kind);
        string extension = ExportConfig.Instance.DiscoveryExt;
        string suggested = ExportConfig.BuildFileName(ExportConfig.Instance.DiscoveryTemplate, extension,
            new Dictionary<string, string> { ["name"] = name.Replace(' ', '_') });
        return new CatalogueTransferRequest(kind,
            import ? UiStrings.Get("discovery.import_title") : title,
            suggested, [extension], path);
    }

    private async Task ExportListAsync(CatalogueTransferKind kind)
    {
        if (ExportAsync == null)
        {
            await ShowMessageAsync(UiStrings.Get("common.error"), UiStrings.Get("common.error"));
            return;
        }
        JsonObject root = BuildExport(kind);
        try { await ExportAsync(Request(kind, import: false), root.ToDisplayString(), CancellationToken.None); }
        catch (Exception ex) { await ShowMessageAsync(UiStrings.Get("common.error"), UiStrings.Format("discovery.export_failed", ex.Message)); }
    }

    private JsonObject BuildExport(CatalogueTransferKind kind)
    {
        var root = new JsonObject();
        switch (kind)
        {
            case CatalogueTransferKind.Technologies: root.Set("KnownTechnologies", StringArray(_techRows.Select(row => row.Id))); break;
            case CatalogueTransferKind.Products: root.Set("KnownProducts", StringArray(_productRows.Select(row => row.Id))); break;
            case CatalogueTransferKind.Specials: root.Set("KnownSpecials", StringArray(_specialRows.Select(row => row.Id))); break;
            case CatalogueTransferKind.Words: root.Set("KnownWords", StringArray(_knownWordGroups is null ? [] : CatalogueLogic.CollectKnownWordIds(_knownWordGroups, _words.Words))); break;
            case CatalogueTransferKind.Glyphs:
            {
                var values = new JsonArray();
                foreach (var check in _glyphChecks) values.Add(check.IsChecked == true);
                root.Set("KnownGlyphs", values);
                break;
            }
            case CatalogueTransferKind.Locations:
                root.Set("TeleportEndpoints", _teleportEndpoints?.DeepClone() ?? new JsonArray());
                break;
            case CatalogueTransferKind.Fish:
                root.Set("FishingRecord", _fishingRecord?.DeepClone() ?? new JsonObject());
                break;
            case CatalogueTransferKind.Recipes:
                root.Set("KnownRecipes", StringArray(_recipeRows.Select(row => row.Id)));
                break;
        }
        return root;
    }

    private static JsonArray StringArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (string value in values) array.Add(value);
        return array;
    }

    private async Task ImportListAsync(CatalogueTransferKind kind)
    {
        if (ImportAsync == null)
        {
            await ShowMessageAsync(UiStrings.Get("common.error"), UiStrings.Get("common.error"));
            return;
        }
        CatalogueTransferContent? content = await ImportAsync(Request(kind, import: true), CancellationToken.None);
        if (content == null) return;
        try
        {
            JsonObject imported = JsonObject.Parse(content.JsonText);
            int affected = kind switch
            {
                CatalogueTransferKind.Technologies => ImportItems(imported, _techRows, _visibleTechRows, _techFilter.Text),
                CatalogueTransferKind.Products => ImportItems(imported, _productRows, _visibleProductRows, _productFilter.Text),
                CatalogueTransferKind.Specials => ImportItems(imported, _specialRows, _visibleSpecialRows, _specialFilter.Text),
                CatalogueTransferKind.Words => ImportWords(imported),
                CatalogueTransferKind.Glyphs => ImportGlyphs(imported),
                CatalogueTransferKind.Locations => ImportLocations(imported),
                CatalogueTransferKind.Fish => ImportFish(imported),
                CatalogueTransferKind.Recipes => ImportRecipes(imported),
                _ => 0
            };
            if (affected > 0 || kind is CatalogueTransferKind.Glyphs or CatalogueTransferKind.Fish)
                RaiseModified();
            await ShowMessageAsync(UiStrings.Get("discovery.import_title"),
                kind switch
                {
                    CatalogueTransferKind.Words => UiStrings.Format("discovery.import_words_success", affected),
                    CatalogueTransferKind.Locations => UiStrings.Format("discovery.import_locations_success", affected),
                    CatalogueTransferKind.Fish => UiStrings.Get("discovery.import_fish_success"),
                    _ => UiStrings.Format("discovery.import_success_items", affected)
                });
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(UiStrings.Get("common.error"), UiStrings.Format("discovery.import_failed", ex.Message));
        }
    }

    private static JsonArray? FirstArray(JsonObject imported)
    {
        foreach (string name in imported.Names())
        {
            try { JsonArray? value = imported.GetArray(name); if (value != null) return value; } catch { }
        }
        return null;
    }

    private int ImportItems(JsonObject imported,
        ObservableCollection<CatalogueItemRow> rows,
        ObservableCollection<CatalogueItemRow> visible,
        string? filter)
    {
        JsonArray? array = FirstArray(imported);
        if (array == null) return 0;
        int added = 0;
        var existing = rows.Select(row => row.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < array.Length; i++)
        {
            string id;
            try { id = CatalogueLogic.StripCaretPrefix(array.GetString(i) ?? string.Empty); } catch { continue; }
            if (id.Length == 0 || !existing.Add(id)) continue;
            rows.Add(CreateItemRow(id, useSubtitle: false));
            added++;
        }
        ApplyItemFilter(rows, visible, filter);
        return added;
    }

    private int ImportWords(JsonObject imported)
    {
        if (_knownWordGroups == null) return 0;
        JsonArray? array = imported.GetArray("KnownWords") ?? FirstArray(imported);
        if (array is null) return 0;
        var values = new List<string>();
        for (int i = 0; i < array.Length; i++)
            if (array.GetString(i) is { } value) values.Add(value);
        var result = CatalogueLogic.ApplyKnownWords(_knownWordGroups, _words.Words, values);
        if (_playerState is not null) LoadWords(_playerState);
        return result.Applied;
    }

    private int ImportGlyphs(JsonObject imported)
    {
        JsonArray? array = FirstArray(imported);
        if (array == null) return 0;
        _loading = true;
        int affected = 0;
        try
        {
            for (int i = 0; i < _glyphChecks.Length && i < array.Length; i++)
            {
                try { _glyphChecks[i].IsChecked = array.GetBool(i); affected++; } catch { }
            }
        }
        finally { _loading = false; }
        return affected;
    }

    private int ImportLocations(JsonObject imported)
    {
        if (_teleportEndpoints == null || _playerState == null) return 0;
        JsonArray? array = imported.GetArray("TeleportEndpoints");
        if (array == null) return 0;
        int added = 0;
        for (int i = 0; i < array.Length; i++)
        {
            try { _teleportEndpoints.Add(array.GetObject(i)); added++; } catch { }
        }
        LoadLocations(_playerState);
        return added;
    }

    private int ImportFish(JsonObject imported)
    {
        if (_fishingRecord == null || _playerState == null) return 0;
        JsonObject? record = imported.GetObject("FishingRecord");
        if (record == null) return 0;
        foreach (string name in record.Names()) _fishingRecord.Set(name, record.Get(name));
        LoadFish(_playerState);
        return 1;
    }

    private int ImportRecipes(JsonObject imported)
    {
        JsonArray? array = FirstArray(imported);
        if (array == null) return 0;
        int added = 0;
        var existing = _recipeRows.Select(row => row.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < array.Length; i++)
        {
            string id;
            try { id = CatalogueLogic.StripCaretPrefix(array.GetString(i) ?? string.Empty); } catch { continue; }
            if (id.Length == 0 || !existing.Add(id) || _recipes.GetRecipe(id) == null) continue;
            _recipeRows.Add(CreateRecipeRow(id));
            added++;
        }
        ApplyRecipeFilter();
        return added;
    }

    private void LocaliseButtons()
    {
        SetButton("Technologies.add", "discovery.add_technology");
        SetButton("Products.add", "discovery.add_product");
        SetButton("Specials.add", "discovery.add_special");
        foreach (string prefix in new[] { "Technologies", "Products", "Specials" })
        {
            SetButton($"{prefix}.remove", "discovery.remove_selected");
            SetButton($"{prefix}.export", "common.export");
            SetButton($"{prefix}.import", "common.import");
        }
        SetButton("Words.learnAll", "discovery.learn_all");
        SetButton("Words.unlearnAll", "discovery.unlearn_all");
        SetButton("Words.learnSelected", "discovery.learn_selected");
        SetButton("Words.unlearnSelected", "discovery.unlearn_selected");
        SetButton("Words.export", "common.export"); SetButton("Words.import", "common.import");
        SetButton("Glyphs.learnAll", "discovery.learn_all"); SetButton("Glyphs.unlearnAll", "discovery.unlearn_all");
        SetButton("Glyphs.export", "common.export"); SetButton("Glyphs.import", "common.import");
        SetButton("Locations.delete", "discovery.delete_selected"); SetButton("Locations.travel", "discovery.travel_to_system");
        SetButton("Locations.export", "common.export"); SetButton("Locations.import", "common.import");
        SetButton("Fish.add", "discovery.add_fish_title"); SetButton("Fish.remove", "discovery.remove_selected");
        SetButton("Fish.export", "common.export"); SetButton("Fish.import", "common.import");
        SetButton("Recipes.add", "recipe.add_recipe"); SetButton("Recipes.remove", "recipe.remove_selected");
        SetButton("Recipes.export", "common.export"); SetButton("Recipes.import", "common.import");
    }

    private void SetButton(string name, string key) { if (_buttons.TryGetValue(name, out var button)) button.Content = UiStrings.Get(key); }

    private void LocaliseColumns()
    {
        foreach (DataGrid grid in new[] { _techGrid, _productGrid, _specialGrid })
        {
            grid.Columns[1].Header = UiStrings.Get("discovery.col_name");
            grid.Columns[2].Header = UiStrings.Get("discovery.col_category");
            grid.Columns[3].Header = UiStrings.Get("discovery.col_id");
        }
        _wordGrid.Columns[0].Header = UiStrings.Get("discovery.col_word");
        _wordGrid.Columns[1].Header = UiStrings.Get("discovery.col_word_id");
        for (int i = 0; i < Races.Length; i++) _wordGrid.Columns[i + 2].Header = _raceLabels[i].Text;
        _locationGrid.Columns[0].Header = UiStrings.Get("discovery.col_index");
        _locationGrid.Columns[1].Header = UiStrings.Get("discovery.col_name");
        _locationGrid.Columns[2].Header = UiStrings.Get("discovery.col_type");
        _locationGrid.Columns[3].Header = UiStrings.Get("discovery.col_galaxy");
        _locationGrid.Columns[4].Header = UiStrings.Get("discovery.col_portal_hex");
        _locationGrid.Columns[5].Header = UiStrings.Get("discovery.col_portal_dec");
        _locationGrid.Columns[6].Header = UiStrings.Get("discovery.col_signal_booster");
        _fishGrid.Columns[1].Header = UiStrings.Get("discovery.col_caught_fish");
        _fishGrid.Columns[2].Header = UiStrings.Get("discovery.col_name");
        _fishGrid.Columns[3].Header = UiStrings.Get("discovery.col_count");
        _fishGrid.Columns[4].Header = UiStrings.Get("discovery.col_largest_catch");
        _recipeGrid.Columns[1].Header = UiStrings.Get("recipe.col_name");
        _recipeGrid.Columns[2].Header = UiStrings.Get("recipe.col_type");
        _recipeGrid.Columns[4].Header = UiStrings.Get("recipe.col_result");
        _recipeGrid.Columns[5].Header = UiStrings.Get("recipe.col_ingredients");
        _recipeGrid.Columns[6].Header = UiStrings.Get("recipe.col_id");
    }
}
