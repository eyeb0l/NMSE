using System.Globalization;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.UI.Avalonia.Panels.Catalogue;

public sealed partial class CataloguePanel
{
    private static readonly Dictionary<string, string> LocationTypeKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Base"] = "location_type.base",
        ["Spacestation"] = "location_type.spacestation",
        ["Atlas"] = "location_type.atlas",
        ["PlanetAwayFromShip"] = "location_type.planet_away_from_ship",
        ["ExternalBase"] = "location_type.external_base",
        ["EmergencyGalaxyFix"] = "location_type.emergency_galaxy_fix",
        ["OnNexus"] = "location_type.on_nexus",
        ["SpacestationFixPosition"] = "location_type.spacestation_fix_position",
        ["Settlement"] = "location_type.settlement",
        ["Freighter"] = "location_type.freighter",
        ["Frigate"] = "location_type.frigate",
        ["BaseBuildingObject"] = "location_type.base_building_object"
    };

    private void LoadLocations(JsonObject playerState)
    {
        _teleportEndpoints = playerState.GetArray("TeleportEndpoints");
        _locationRows.Clear();
        if (_teleportEndpoints != null)
        {
            for (int i = 0; i < _teleportEndpoints.Length; i++)
            {
                try
                {
                    JsonObject endpoint = _teleportEndpoints.GetObject(i);
                    string name = endpoint.GetString("Name") ?? string.Empty;
                    string type = TryString(endpoint, "TeleporterType");
                    JsonObject? address = endpoint.GetObject("UniverseAddress");
                    int reality = address == null ? 0 : TryInt(address, "RealityIndex");
                    string galaxy = address == null ? string.Empty : $"{GalaxyDatabase.GetGalaxyDisplayName(reality)} ({GalaxyDatabase.GetGalaxyType(reality)})";
                    string portal = string.Empty;
                    string signal = string.Empty;
                    JsonObject? galactic = address?.GetObject("GalacticAddress");
                    if (galactic != null)
                    {
                        int vx = TryInt(galactic, "VoxelX"), vy = TryInt(galactic, "VoxelY"), vz = TryInt(galactic, "VoxelZ");
                        int system = TryInt(galactic, "SolarSystemIndex"), planet = TryInt(galactic, "PlanetIndex");
                        portal = CoordinateHelper.VoxelToPortalCode(vx, vy, vz, system, planet);
                        signal = CoordinateHelper.VoxelToSignalBooster(vx, vy, vz, system);
                    }
                    _locationRows.Add(new CatalogueLocationRow
                    {
                        Index = i, Name = name, RawType = type, Type = LocaliseLocationType(type), Galaxy = galaxy,
                        RealityIndex = reality, GalaxyCore = GalaxyDatabase.GetGalaxyCore(reality), PortalCode = portal,
                        PortalCodeDecimal = CoordinateHelper.PortalHexToDec(portal), SignalBooster = signal
                    });
                }
                catch { }
            }
        }
        ApplyLocationFilter(_locationFilter.Text ?? string.Empty);
        UpdateLocationDetails();
    }

    private static string TryString(JsonObject obj, string key)
    {
        try { return obj.GetString(key) ?? string.Empty; } catch { return string.Empty; }
    }

    private static int TryInt(JsonObject obj, string key)
    {
        try { return obj.GetInt(key); } catch { return 0; }
    }

    private static string LocaliseLocationType(string type) => LocationTypeKeys.TryGetValue(type, out string? key) ? UiStrings.Get(key) : type;

    private void RelocaliseLocations()
    {
        foreach (CatalogueLocationRow row in _locationRows) row.Type = LocaliseLocationType(row.RawType);
        ApplyLocationFilter(_locationFilter.Text ?? string.Empty);
    }

    private void ApplyLocationFilter(string text)
    {
        string filter = text.Trim();
        _visibleLocationRows.Clear();
        foreach (CatalogueLocationRow row in _locationRows.Where(row => filter.Length == 0 ||
                     row.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                     row.Type.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                     row.Galaxy.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                     row.PortalCode.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            _visibleLocationRows.Add(row);
    }

    private void UpdateLocationDetails()
    {
        if (_locationGrid.SelectedItem is not CatalogueLocationRow row)
        {
            _locationGlyphs.PortalCode = string.Empty;
            _locationGalaxy.Text = string.Empty;
            _locationCore.Text = string.Empty;
            return;
        }
        _locationGlyphs.PortalCode = row.PortalCode;
        _locationGalaxy.Text = row.Galaxy;
        _locationCore.Text = string.IsNullOrWhiteSpace(row.GalaxyCore)
            ? string.Empty : $"{UiStrings.Get("player.core_label")} {row.GalaxyCore}";
    }

    private async Task DeleteLocationsAsync()
    {
        if (_teleportEndpoints == null) return;
        CatalogueLocationRow[] selected = _locationGrid.SelectedItems.OfType<CatalogueLocationRow>().ToArray();
        if (selected.Length == 0) return;
        string message = selected.Length == 1
            ? UiStrings.Get("discovery.delete_location_single")
            : UiStrings.Format("discovery.delete_location_multi", selected.Length);
        if (!await AskAsync(UiStrings.Get("discovery.delete_location_title"), message)) return;
        foreach (int index in selected.Select(row => row.Index).Distinct().OrderDescending())
            if (index >= 0 && index < _teleportEndpoints.Length) _teleportEndpoints.RemoveAt(index);
        if (_playerState != null) LoadLocations(_playerState);
        RaiseModified();
    }

    private async Task TravelAsync()
    {
        if (_teleportEndpoints == null || _playerState == null || _locationGrid.SelectedItem is not CatalogueLocationRow row) return;
        if (!await AskAsync(UiStrings.Get("discovery.travel_title"), UiStrings.Get("discovery.travel_confirm"))) return;
        try
        {
            JsonObject endpoint = _teleportEndpoints.GetObject(row.Index);
            JsonObject? targetAddress = endpoint.GetObject("UniverseAddress");
            JsonObject? targetGalactic = targetAddress?.GetObject("GalacticAddress");
            if (targetAddress == null || targetGalactic == null) return;
            JsonObject? playerAddress = _playerState.GetObject("UniverseAddress");
            JsonObject? playerGalactic = playerAddress?.GetObject("GalacticAddress");
            if (playerAddress != null) playerAddress.Set("RealityIndex", TryInt(targetAddress, "RealityIndex"));
            if (playerGalactic != null)
            {
                foreach (string key in new[] { "VoxelX", "VoxelY", "VoxelZ", "SolarSystemIndex" })
                    playerGalactic.Set(key, TryInt(targetGalactic, key));
                playerGalactic.Set("PlanetIndex", 0);
            }
            _saveData?.GetObject("SpawnStateData")?.Set("LastKnownPlayerState", "InShip");
            await ShowMessageAsync(UiStrings.Get("discovery.travel_complete_title"), UiStrings.Get("discovery.travel_complete"));
            RaiseModified();
        }
        catch
        {
            await ShowMessageAsync(UiStrings.Get("common.error"), UiStrings.Get("discovery.travel_failed"));
        }
    }

    private void LoadFish(JsonObject playerState)
    {
        _fishingRecord = playerState.GetObject("FishingRecord");
        _fishRows.Clear();
        JsonArray? products = _fishingRecord?.GetArray("ProductList");
        JsonArray? counts = _fishingRecord?.GetArray("ProductCountList");
        JsonArray? largest = _fishingRecord?.GetArray("LargestCatchList");
        if (products != null)
        {
            for (int i = 0; i < products.Length; i++)
            {
                string productId;
                try { productId = products.GetString(i) ?? string.Empty; } catch { continue; }
                if (string.IsNullOrEmpty(productId) || productId == "^") continue;
                string id = CatalogueLogic.StripCaretPrefix(productId);
                GameItem? item = _items.GetItem(id);
                int count = 0;
                string largestText = "0";
                try { if (counts != null && i < counts.Length) count = counts.GetInt(i); } catch { }
                try { if (largest != null && i < largest.Length) largestText = largest.GetDoubleText(i); } catch { }
                var row = new CatalogueFishRow
                {
                    Icon = _icons?.GetIconForItem(id, _items), ProductId = productId,
                    Name = item?.NameLower ?? item?.Name ?? id, SlotIndex = i
                };
                row.LoadValues(count.ToString(CultureInfo.InvariantCulture), largestText);
                row.PropertyChanged += (_, _) => { if (!_loading) RaiseModified(); };
                _fishRows.Add(row);
            }
        }
        ApplyFishFilter(_fishFilter.Text ?? string.Empty);
    }

    private void SaveFish(JsonObject playerState)
    {
        JsonObject? record = playerState.GetObject("FishingRecord");
        JsonArray? products = record?.GetArray("ProductList");
        JsonArray? counts = record?.GetArray("ProductCountList");
        JsonArray? largest = record?.GetArray("LargestCatchList");
        if (products == null) return;
        foreach (CatalogueFishRow row in _fishRows)
        {
            int index = row.SlotIndex;
            if (index < 0 || index >= products.Length) continue;
            products.Set(index, row.ProductId);
            if (counts != null && index < counts.Length && int.TryParse(row.Count, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
                counts.Set(index, count);
            if (largest != null && index < largest.Length && row.LargestCatch != row.OriginalLargestCatch &&
                double.TryParse(row.LargestCatch, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                largest.Set(index, new RawDouble(value, row.LargestCatch));
        }
    }

    private void ApplyFishFilter(string text)
    {
        string filter = text.Trim();
        _visibleFishRows.Clear();
        foreach (CatalogueFishRow row in _fishRows.Where(row => filter.Length == 0 ||
                     row.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                     row.ProductId.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            _visibleFishRows.Add(row);
    }

    private async Task AddFishAsync()
    {
        if (_fishingRecord == null || Owner is not { } owner) return;
        JsonArray? products = _fishingRecord.GetArray("ProductList");
        if (products == null) return;
        int empty = -1;
        for (int i = 0; i < products.Length; i++)
        {
            try { string value = products.GetString(i) ?? string.Empty; if (value.Length == 0 || value == "^") { empty = i; break; } }
            catch { empty = i; break; }
        }
        if (empty < 0)
        {
            await ShowMessageAsync(UiStrings.Get("discovery.add_fish_title"), UiStrings.Get("discovery.no_fish_slots"));
            return;
        }
        CataloguePickerItem[] candidates = _items.Items.Values
            .Where(item => string.Equals(item.ItemType, "Fish", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => new CataloguePickerItem
            {
                Icon = _icons?.GetIconForItem(item.Id, _items), Name = item.Name, Id = item.Id,
                Category = string.IsNullOrWhiteSpace(item.Subtitle) ? "Fish" : item.Subtitle
            }).ToArray();
        string[]? selected = await new CataloguePickerDialog(UiStrings.Get("discovery.add_fish_title"), candidates).ShowDialog<string[]>(owner);
        string? id = selected?.FirstOrDefault();
        if (string.IsNullOrEmpty(id)) return;
        products.Set(empty, CatalogueLogic.EnsureCaretPrefix(id));
        JsonArray? counts = _fishingRecord.GetArray("ProductCountList");
        JsonArray? largest = _fishingRecord.GetArray("LargestCatchList");
        if (counts != null && empty < counts.Length) counts.Set(empty, 0);
        if (largest != null && empty < largest.Length) largest.Set(empty, 0.0);
        if (_playerState != null) LoadFish(_playerState);
        RaiseModified();
    }

    private void RemoveFish()
    {
        if (_fishingRecord == null || _fishGrid.SelectedItem is not CatalogueFishRow row) return;
        JsonArray? products = _fishingRecord.GetArray("ProductList");
        if (products == null || row.SlotIndex < 0 || row.SlotIndex >= products.Length) return;
        products.Set(row.SlotIndex, "^");
        JsonArray? counts = _fishingRecord.GetArray("ProductCountList");
        JsonArray? largest = _fishingRecord.GetArray("LargestCatchList");
        if (counts != null && row.SlotIndex < counts.Length) counts.Set(row.SlotIndex, 0);
        if (largest != null && row.SlotIndex < largest.Length) largest.Set(row.SlotIndex, 0.0);
        _fishRows.Remove(row); _visibleFishRows.Remove(row);
        RaiseModified();
    }

    private void LoadKnownRecipes(JsonObject playerState)
    {
        _recipeRows.Clear();
        JsonArray? known = playerState.GetArray("KnownRefinerRecipes");
        if (known != null)
        {
            for (int i = 0; i < known.Length; i++)
            {
                string raw;
                try { raw = known.GetString(i) ?? string.Empty; } catch { continue; }
                if (raw.Length == 0) continue;
                string id = CatalogueLogic.StripCaretPrefix(raw);
                _recipeRows.Add(CreateRecipeRow(id));
            }
        }
        ApplyRecipeFilter();
    }

    private CatalogueRecipeRow CreateRecipeRow(string id)
    {
        Recipe? recipe = _recipes.GetRecipe(id);
        if (recipe == null)
            return new CatalogueRecipeRow { Name = id, Category = string.Empty, Result = string.Empty, Ingredients = string.Empty, Id = id };
        string resultId = recipe.Result?.Id ?? string.Empty;
        return new CatalogueRecipeRow
        {
            Icon = _icons?.GetIconForItem(resultId, _items), ResultIcon = _icons?.GetIconForItem(resultId, _items),
            Name = recipe.RecipeName,
            Category = recipe.Cooking ? UiStrings.Get("recipe.type_cooking") : UiStrings.Get("recipe.type_refining"),
            Result = recipe.Result == null ? string.Empty : $"{recipe.Result.Amount}x {ItemName(recipe.Result.Id)}",
            Ingredients = string.Join(" + ", recipe.Ingredients.Select(i => $"{i.Amount}x {ItemName(i.Id)}")),
            Id = recipe.Id, Recipe = recipe
        };
    }

    private string ItemName(string id) => _items.GetItem(id)?.Name ?? id;

    private void SaveKnownRecipes(JsonObject playerState)
    {
        JsonArray? known = playerState.GetArray("KnownRefinerRecipes");
        if (known == null) { known = new JsonArray(); playerState.Set("KnownRefinerRecipes", known); }
        while (known.Length > 0) known.RemoveAt(known.Length - 1);
        foreach (CatalogueRecipeRow row in _recipeRows) known.Add(CatalogueLogic.EnsureCaretPrefix(row.Id));
    }

    private void ApplyRecipeFilter()
    {
        string filter = (_recipeFilter?.Text ?? string.Empty).Trim();
        _visibleRecipeRows.Clear();
        foreach (CatalogueRecipeRow row in _recipeRows.Where(row => filter.Length == 0 ||
                     row.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                     row.Category.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                     row.Result.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                     row.Ingredients.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                     row.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            _visibleRecipeRows.Add(row);
    }

    private async Task AddRecipeAsync()
    {
        if (Owner is not { } owner) return;
        var known = _recipeRows.Select(row => row.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        CataloguePickerItem[] candidates = _recipes.Recipes.Where(recipe => !known.Contains(recipe.Id))
            .OrderBy(recipe => recipe.Id, StringComparer.OrdinalIgnoreCase)
            .Select(recipe => new CataloguePickerItem
            {
                Icon = _icons?.GetIconForItem(recipe.Result?.Id, _items), Name = recipe.RecipeName, Id = recipe.Id,
                Category = $"{(recipe.Cooking ? UiStrings.Get("recipe.type_cooking") : UiStrings.Get("recipe.type_refining"))}  |  " +
                           $"{string.Join(", ", recipe.Ingredients.Select(i => $"{i.Amount}x {ItemName(i.Id)}"))}  →  " +
                           (recipe.Result == null ? string.Empty : $"{recipe.Result.Amount}x {ItemName(recipe.Result.Id)}")
            }).ToArray();
        string[]? selected = await new CataloguePickerDialog(UiStrings.Get("recipe.add_recipe_title"), candidates).ShowDialog<string[]>(owner);
        if (selected == null || selected.Length == 0) return;
        foreach (string id in selected) if (known.Add(id)) _recipeRows.Add(CreateRecipeRow(id));
        ApplyRecipeFilter();
        RaiseModified();
    }

    private async Task<bool> AskAsync(string title, string message)
    {
        if (ConfirmAsync != null) return await ConfirmAsync(title, message, CancellationToken.None);
        return Owner is { } owner && await CatalogueUi.ConfirmAsync(owner, title, message);
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        if (MessageAsync != null) { await MessageAsync(title, message, CancellationToken.None); return; }
        if (Owner is { } owner) await CatalogueUi.MessageAsync(owner, title, message);
    }
}
