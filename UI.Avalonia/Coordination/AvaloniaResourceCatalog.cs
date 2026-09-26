using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.UI.Avalonia.Coordination;

/// <summary>
/// Loads the framework-neutral databases shared by both frontends from the
/// read-only application resource tree. UI panels receive this object through
/// constructor injection instead of discovering files themselves.
/// </summary>
public sealed class AvaloniaResourceCatalog : IDisposable
{
    public GameItemDatabase Items { get; } = new();
    public RecipeDatabase Recipes { get; } = new();
    public LocalisationService Localisation { get; } = new();
    public WordDatabase Words { get; } = new();
    public Services.AvaloniaIconProvider? Icons { get; private set; }
    public int TotalItemCount { get; private set; }

    public async Task InitializeAsync(
        IProgress<ResourceLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string resources = ApplicationPaths.Current.ResourcesDirectory;
        string json = Path.Combine(resources, "json");

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ResourceLoadProgress(5, "Loading item database…"));
            Items.LoadItemsFromJsonDirectory(json);
            StarshipDatabase.LoadFromDatabase(Items);
            SpacePoiTableDatabase.LoadFromFile(Path.Combine(json, "Space POI.json"));
            TechPacks.RegisterGeneratedPacks();

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ResourceLoadProgress(20, "Loading recipes and titles…"));
            Recipes.LoadFromFile(Path.Combine(json, "Recipes.json"));
            TitleDatabase.LoadFromFile(Path.Combine(json, "Titles.json"));

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ResourceLoadProgress(30, "Loading supplementary databases…"));
            FrigateTraitDatabase.LoadFromFile(Path.Combine(json, "Frigate Traits.json"));
            SettlementDatabase.LoadFromFile(Path.Combine(json, "Settlement Perks.json"));
            WikiGuideDatabase.LoadFromFile(Path.Combine(json, "Wiki Guide.json"));
            CompanionAccessoryDatabase.LoadFromFile(Path.Combine(json, "Companion Accessories.json"));
            ShipCustomisationDatabase.LoadFromFile(Path.Combine(json, "Ship Customisation.json"));
            NmsColourPalette.LoadShipPalettes(Path.Combine(json, "Colour Palettes.json"));
            PetBattleMoveDatabase.LoadFromFile(Path.Combine(json, "Pet Battle Moves.json"));
            PetBattleMovesetDatabase.LoadFromFile(Path.Combine(json, "Pet Battle Movesets.json"));
            PetBiomeAffinityMap.LoadFromFile(Path.Combine(json, "Game Table Globals.json"));
            CompanionDatabase.LoadFromFile(Path.Combine(json, "Creature Species.json"));
            CreaturePartDatabase.LoadFromFile(Path.Combine(json, "Creature Descriptors.json"));

            progress?.Report(new ResourceLoadProgress(45, "Loading words and localisation…"));
            Words.LoadFromFile(Path.Combine(json, "Words.json"));
            Localisation.SetLangDirectory(Path.Combine(json, "lang"));
            UiStrings.SetDirectory(Path.Combine(resources, "ui", "lang"));

            string mappingPath = Path.Combine(resources, "map", "mapping.json");
            if (File.Exists(mappingPath))
            {
                var mapper = new JsonNameMapper();
                mapper.Load(mappingPath);
                JsonParser.SetDefaultMapper(mapper);
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ResourceLoadProgress(65, "Loading icons…"));
            string images = Path.Combine(resources, "images");
            if (Directory.Exists(images))
            {
                Icons = new Services.AvaloniaIconProvider(images);
                Icons.PreloadIcons(Items, cancellationToken);
            }

            string exportConfigPath = ApplicationPaths.Current.ExportConfigurationFilePath;
            ExportConfig.LoadFromFile(exportConfigPath);
            TotalItemCount = Items.Items.Count
                + Words.Count
                + FrigateTraitDatabase.Traits.Count
                + SettlementDatabase.Perks.Count
                + RewardDatabase.Count
                + InventoryStackDatabase.Count
                + UiStrings.TotalKeyCount;
            progress?.Report(new ResourceLoadProgress(100, "Ready"));
        }, cancellationToken).ConfigureAwait(false);
    }

    public bool ApplyLanguage(string? languageTag)
    {
        bool uiLoaded = UiStrings.Load(languageTag);
        bool dataLoaded = Localisation.LoadLanguage(languageTag);
        if (!dataLoaded)
        {
            Localisation.LoadLanguage(null);
            Items.RevertLocalisation();
            RewardDatabase.RevertLocalisation();
            Words.RevertLocalisation();
            Recipes.RevertLocalisation();
            TitleDatabase.RevertLocalisation();
            FrigateTraitDatabase.RevertLocalisation();
            SettlementDatabase.RevertLocalisation();
            WikiGuideDatabase.RevertLocalisation();
            SpacePoiTableDatabase.RevertLocalisation();
        }
        else
        {
            Items.ApplyLocalisation(Localisation);
            RewardDatabase.ApplyLocalisation(Localisation);
            Words.ApplyLocalisation(Localisation);
            Recipes.ApplyLocalisation(Localisation);
            TitleDatabase.ApplyLocalisation(Localisation);
            FrigateTraitDatabase.ApplyLocalisation(Localisation);
            SettlementDatabase.ApplyLocalisation(Localisation);
            WikiGuideDatabase.ApplyLocalisation(Localisation);
            CompanionAccessoryDatabase.ApplyLocalisation(Localisation);
            SpacePoiTableDatabase.ApplyLocalisation(Localisation);
        }
        return uiLoaded && dataLoaded;
    }

    public void Dispose() => Icons?.Dispose();
}

public readonly record struct ResourceLoadProgress(int Percentage, string Message);
