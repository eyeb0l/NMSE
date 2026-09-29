using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Contracts;
using NMSE.UI.Avalonia.Controls;
using NMSE.UI.Avalonia.Coordination;
using NMSE.UI.Avalonia.Services;

namespace NMSE.UI.Avalonia.Panels.Catalogue;

/// <summary>The complete save Catalogue and embedded recipe browser.</summary>
public sealed partial class CataloguePanel : UserControl, ISaveEditorPanel, IGoToJsonSource, IPurgeableSaveEditorPanel
{
    private static readonly (string Name, int Race)[] Races =
    {
        ("Gek", 0), ("Vy'keen", 1), ("Korvax", 2), ("Atlas", 4), ("Autophage", 8)
    };

    private readonly GameItemDatabase _items;
    private readonly RecipeDatabase _recipes;
    private readonly WordDatabase _words;
    private readonly AvaloniaIconProvider? _icons;
    private readonly TabControl _tabs;
    private readonly TabItem[] _tabItems;
    private readonly TabItem _knownRecipesTab;
    private readonly TabItem _recipeBrowserTab;
    private readonly CompletionPanel[] _completionPanels;
    private readonly TabItem[] _completionTabs;
    private static readonly string[] CompletionKeys = ["discovery.tab_wonders", "discovery.tab_knowledge", "discovery.group_fossils", "discovery.group_raw_materials", "discovery.tab_stats"];

    private readonly ObservableCollection<CatalogueItemRow> _techRows = [];
    private readonly ObservableCollection<CatalogueItemRow> _visibleTechRows = [];
    private readonly ObservableCollection<CatalogueItemRow> _productRows = [];
    private readonly ObservableCollection<CatalogueItemRow> _visibleProductRows = [];
    private readonly ObservableCollection<CatalogueItemRow> _specialRows = [];
    private readonly ObservableCollection<CatalogueItemRow> _visibleSpecialRows = [];
    private readonly ObservableCollection<CatalogueWordRow> _wordRows = [];
    private readonly ObservableCollection<CatalogueWordRow> _visibleWordRows = [];
    private readonly ObservableCollection<CatalogueLocationRow> _locationRows = [];
    private readonly ObservableCollection<CatalogueLocationRow> _visibleLocationRows = [];
    private readonly ObservableCollection<CatalogueFishRow> _fishRows = [];
    private readonly ObservableCollection<CatalogueFishRow> _visibleFishRows = [];
    private readonly ObservableCollection<CatalogueRecipeRow> _recipeRows = [];
    private readonly ObservableCollection<CatalogueRecipeRow> _visibleRecipeRows = [];

    private readonly DataGrid _techGrid;
    private readonly DataGrid _productGrid;
    private readonly DataGrid _specialGrid;
    private readonly DataGrid _wordGrid;
    private readonly DataGrid _locationGrid;
    private readonly DataGrid _fishGrid;
    private readonly DataGrid _recipeGrid;
    private readonly TextBox _techFilter;
    private readonly TextBox _productFilter;
    private readonly TextBox _specialFilter;
    private readonly TextBox _wordFilter;
    private readonly TextBox _locationFilter;
    private readonly TextBox _fishFilter;
    private readonly TextBox _recipeFilter;
    private readonly CheckBox[] _glyphChecks = new CheckBox[16];
    private readonly TextBlock[] _raceLabels = new TextBlock[5];
    private readonly Button[] _raceLearnButtons = new Button[5];
    private readonly Button[] _raceUnlearnButtons = new Button[5];
    private readonly GlyphRenderer _locationGlyphs = new() { GlyphSize = 22 };
    private readonly TextBlock _locationGalaxy = new() { FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _locationCore = new() { FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _portalGlyphsCaption = new();
    private readonly TextBlock _galaxyCaption = new();
    private readonly RecipeBrowserPanel _recipeBrowser;

    private readonly Dictionary<string, Button> _buttons = new(StringComparer.Ordinal);
    private JsonObject? _saveData;
    private JsonObject? _playerState;
    private JsonArray? _knownWordGroups;
    private JsonArray? _teleportEndpoints;
    private JsonObject? _fishingRecord;
    private bool _loading;

    public event EventHandler? DataModified;
    public event EventHandler<GoToJsonRequestEventArgs>? GoToJsonRequested;

    public Func<CatalogueTransferRequest, CancellationToken, Task<CatalogueTransferContent?>>? ImportAsync { get; set; }
    public Func<CatalogueTransferRequest, string, CancellationToken, Task<bool>>? ExportAsync { get; set; }
    public Func<string, string, CancellationToken, Task<bool>>? ConfirmAsync { get; set; }
    public Func<string, string, CancellationToken, Task>? MessageAsync { get; set; }
    public Func<string, CancellationToken, Task<bool>>? OpenUrlAsync
    {
        get => _recipeBrowser.OpenUrlAsync;
        set => _recipeBrowser.OpenUrlAsync = value;
    }

    public bool IsPurged => _saveData == null;
    public int TabCount => _tabItems.Length;
    public int SelectedTabIndex { get => _tabs.SelectedIndex; set => _tabs.SelectedIndex = Math.Clamp(value, 0, _tabItems.Length - 1); }
    public int LoadedRowCount => _techRows.Count + _productRows.Count + _specialRows.Count + _wordRows.Count +
                                 _locationRows.Count + _fishRows.Count + _recipeRows.Count;
    public RecipeBrowserPanel RecipeBrowser => _recipeBrowser;

    public CataloguePanel(GameItemDatabase items, RecipeDatabase recipes, WordDatabase words, AvaloniaIconProvider? icons)
    {
        _items = items;
        _recipes = recipes;
        _words = words;
        _icons = icons;
        _recipeBrowser = new RecipeBrowserPanel(recipes, items);

        (_techGrid, _techFilter, Control techPage) = BuildItemPage(
            _techRows, _visibleTechRows, CatalogueTransferKind.Technologies, "PlayerStateData.KnownTech", AddTechnologyAsync);
        (_productGrid, _productFilter, Control productsPage) = BuildItemPage(
            _productRows, _visibleProductRows, CatalogueTransferKind.Products, "PlayerStateData.KnownProducts", AddProductAsync);
        (_specialGrid, _specialFilter, Control specialsPage) = BuildItemPage(
            _specialRows, _visibleSpecialRows, CatalogueTransferKind.Specials, "PlayerStateData.KnownSpecials", AddSpecialAsync);
        (_wordGrid, _wordFilter, Control wordsPage) = BuildWordsPage();
        Control glyphsPage = BuildGlyphsPage();
        (_locationGrid, _locationFilter, Control locationsPage) = BuildLocationsPage();
        (_fishGrid, _fishFilter, Control fishPage) = BuildFishPage();
        (_recipeGrid, _recipeFilter, Control knownRecipesPage) = BuildKnownRecipesPage();

        _knownRecipesTab = new TabItem { Content = knownRecipesPage };
        _recipeBrowserTab = new TabItem { Content = _recipeBrowser };
        var recipeTabs = new TabControl { ItemsSource = new[] { _knownRecipesTab, _recipeBrowserTab } };
        _tabItems =
        [
            new TabItem { Content = techPage },
            new TabItem { Content = productsPage },
            new TabItem { Content = specialsPage },
            new TabItem { Content = wordsPage },
            new TabItem { Content = glyphsPage },
            new TabItem { Content = locationsPage },
            new TabItem { Content = fishPage },
            new TabItem { Content = recipeTabs }
        ];
        _completionPanels = new[] { "wonders", "knowledge", "fossils", "materials", "stats" }.Select(kind => new CompletionPanel(kind, items)).ToArray();
        _completionTabs = _completionPanels.Select(panel => new TabItem { Content = panel }).ToArray();
        foreach (var panel in _completionPanels)
        {
            panel.DataModified += (_, _) => DataModified?.Invoke(this, EventArgs.Empty);
            panel.ConfirmAsync = (title, message, token) => ConfirmAsync?.Invoke(title, message, token) ?? Task.FromResult(false);
        }
        _tabs = new TabControl { ItemsSource = _tabItems.Concat(_completionTabs).ToArray() };
        Content = _tabs;
        ApplyUiLocalisation();
    }

    public void LoadData(JsonObject saveData)
    {
        _loading = true;
        try
        {
            _saveData = saveData;
            _playerState = saveData.GetObject("PlayerStateData");
            if (_playerState == null) { PurgeData(); return; }
            LoadKnownItems(_playerState, "KnownTech", _techRows, _visibleTechRows, _techFilter.Text);
            LoadKnownItems(_playerState, "KnownProducts", _productRows, _visibleProductRows, _productFilter.Text);
            LoadKnownItems(_playerState, "KnownSpecials", _specialRows, _visibleSpecialRows, _specialFilter.Text);
            LoadWords(_playerState);
            LoadGlyphs(_playerState);
            LoadLocations(_playerState);
            LoadFish(_playerState);
            LoadKnownRecipes(_playerState);
            foreach (var panel in _completionPanels) panel.LoadData(saveData);
        }
        finally { _loading = false; }
    }

    public void ReloadData(JsonObject saveData) => LoadData(saveData);

    public void SetAccountData(JsonObject? accountData)
    {
        foreach (var panel in _completionPanels) panel.SetAccountData(accountData);
    }

    public void SaveData(JsonObject saveData)
    {
        // A purged Catalogue is intentionally not a loaded editor. Saving its empty
        // view collections would erase discovery data after navigation away.
        if (IsPurged) return;
        JsonObject? player = saveData.GetObject("PlayerStateData");
        if (player == null) return;
        CatalogueLogic.SaveKnownItemIds(player, "KnownTech", _techRows.Select(row => row.Id).ToList());
        CatalogueLogic.SaveKnownItemIds(player, "KnownProducts", _productRows.Select(row => row.Id).ToList());
        CatalogueLogic.SaveKnownItemIds(player, "KnownSpecials", _specialRows.Select(row => row.Id).ToList());
        if (_knownWordGroups != null)
        {
            player.Set("KnownWordGroups", _knownWordGroups);
            CatalogueLogic.SyncWordStats(saveData, _knownWordGroups);
        }
        SaveGlyphs(player);
        SaveFish(player);
        SaveKnownRecipes(player);
    }

    /// <summary>Releases heavy catalogue rows and save graph references until the next LoadData call.</summary>
    public void PurgeData()
    {
        foreach (var panel in _completionPanels) panel.LoadData(null);
        Clear(_techRows, _visibleTechRows);
        Clear(_productRows, _visibleProductRows);
        Clear(_specialRows, _visibleSpecialRows);
        Clear(_wordRows, _visibleWordRows);
        Clear(_locationRows, _visibleLocationRows);
        Clear(_fishRows, _visibleFishRows);
        Clear(_recipeRows, _visibleRecipeRows);
        _saveData = null;
        _playerState = null;
        _knownWordGroups = null;
        _teleportEndpoints = null;
        _fishingRecord = null;
        _locationGlyphs.PortalCode = string.Empty;
        _locationGalaxy.Text = string.Empty;
        _locationCore.Text = string.Empty;
    }

    public void SetLocalisationService(LocalisationService service)
    {
        foreach (var panel in _completionPanels) panel.Localisation = service;
    }

    public void ApplyUiLocalisation()
    {
        for (int i = 0; i < _completionTabs.Length; i++)
        {
            _completionTabs[i].Header = UiStrings.Get(CompletionKeys[i]);
            _completionPanels[i].ApplyUiLocalisation();
        }
        string[] tabKeys =
        [
            "discovery.tab_tech", "discovery.tab_products", "discovery.tab_specials", "discovery.tab_words",
            "discovery.tab_glyphs", "discovery.tab_locations", "discovery.tab_fish", "discovery.tab_recipes"
        ];
        for (int i = 0; i < _tabItems.Length; i++) _tabItems[i].Header = UiStrings.Get(tabKeys[i]);
        _knownRecipesTab.Header = UiStrings.Get("recipe.tab_known_recipes");
        _recipeBrowserTab.Header = UiStrings.Get("recipe.tab_recipe_info");
        LocaliseButtons();
        _techFilter.PlaceholderText = UiStrings.Get("discovery.filter_items");
        _productFilter.PlaceholderText = UiStrings.Get("discovery.filter_items");
        _specialFilter.PlaceholderText = UiStrings.Get("discovery.filter_items");
        _wordFilter.PlaceholderText = UiStrings.Get("discovery.filter_words");
        _locationFilter.PlaceholderText = UiStrings.Get("discovery.filter_locations");
        _fishFilter.PlaceholderText = UiStrings.Get("discovery.filter_fish");
        _recipeFilter.PlaceholderText = UiStrings.Get("recipe.filter_placeholder");
        _portalGlyphsCaption.Text = UiStrings.Get("discovery.portal_glyphs");
        _galaxyCaption.Text = UiStrings.Get("discovery.galaxy");
        string[] raceKeys = { "common.race_gek", "common.race_vykeen", "common.race_korvax", "discovery.race_atlas", "discovery.race_autophage" };
        for (int i = 0; i < _raceLabels.Length; i++) _raceLabels[i].Text = UiStrings.Get(raceKeys[i]);
        for (int i = 0; i < _glyphChecks.Length; i++) _glyphChecks[i].Content = UiStrings.Format("discovery.glyph_n", i + 1);
        LocaliseColumns();
        RelocaliseLocations();
        _recipeBrowser.ApplyUiLocalisation();
        _recipeBrowser.RefreshLanguage();
        if (!IsPurged)
        {
            ApplyItemFilter(_techRows, _visibleTechRows, _techFilter.Text);
            ApplyItemFilter(_productRows, _visibleProductRows, _productFilter.Text);
            ApplyItemFilter(_specialRows, _visibleSpecialRows, _specialFilter.Text);
            ApplyRecipeFilter();
        }
    }

    private (DataGrid Grid, TextBox Filter, Control Page) BuildItemPage(
        ObservableCollection<CatalogueItemRow> rows,
        ObservableCollection<CatalogueItemRow> visible,
        CatalogueTransferKind kind,
        string jsonPath,
        Func<Task> add)
    {
        var grid = CatalogueUi.Grid();
        grid.ItemsSource = visible;
        grid.Columns.Add(CatalogueUi.ImageColumn("⚙", nameof(CatalogueItemRow.Icon)));
        grid.Columns.Add(CatalogueUi.TextColumn("", nameof(CatalogueItemRow.Name), star: true));
        grid.Columns.Add(CatalogueUi.TextColumn("", nameof(CatalogueItemRow.Category), 220));
        grid.Columns.Add(CatalogueUi.TextColumn("", nameof(CatalogueItemRow.Id), 180));
        var filter = CatalogueUi.FilterBox(string.Empty, value => ApplyItemFilter(rows, visible, value));
        var top = CatalogueUi.Toolbar(filter, CatalogueUi.Button("×", () => filter.Text = string.Empty),
            CatalogueUi.JsonButton(() => RequestGoTo(jsonPath)));
        var addButton = RegisterButton($"{kind}.add", CatalogueUi.AsyncButton(string.Empty, add));
        var remove = RegisterButton($"{kind}.remove", CatalogueUi.Button(string.Empty, () => RemoveSelected(grid, rows, visible)));
        var export = RegisterButton($"{kind}.export", CatalogueUi.AsyncButton(string.Empty, () => ExportListAsync(kind)));
        var import = RegisterButton($"{kind}.import", CatalogueUi.AsyncButton(string.Empty, () => ImportListAsync(kind)));
        return (grid, filter, CatalogueUi.PageGrid(top, grid, CatalogueUi.Toolbar(addButton, remove, export, import)));
    }

    private (DataGrid Grid, TextBox Filter, Control Page) BuildWordsPage()
    {
        var grid = CatalogueUi.Grid(readOnly: false);
        grid.ItemsSource = _visibleWordRows;
        grid.Columns.Add(CatalogueUi.TextColumn("", nameof(CatalogueWordRow.Text), star: true));
        grid.Columns.Add(CatalogueUi.TextColumn("", nameof(CatalogueWordRow.Id), 190));
        grid.Columns.Add(WordCheckColumn("Gek", nameof(CatalogueWordRow.Gek), nameof(CatalogueWordRow.HasGek)));
        grid.Columns.Add(WordCheckColumn("Vy'keen", nameof(CatalogueWordRow.Vykeen), nameof(CatalogueWordRow.HasVykeen)));
        grid.Columns.Add(WordCheckColumn("Korvax", nameof(CatalogueWordRow.Korvax), nameof(CatalogueWordRow.HasKorvax)));
        grid.Columns.Add(WordCheckColumn("Atlas", nameof(CatalogueWordRow.Atlas), nameof(CatalogueWordRow.HasAtlas)));
        grid.Columns.Add(WordCheckColumn("Autophage", nameof(CatalogueWordRow.Autophage), nameof(CatalogueWordRow.HasAutophage)));
        var filter = CatalogueUi.FilterBox(string.Empty, ApplyWordFilter);
        var top = CatalogueUi.Toolbar(filter, CatalogueUi.Button("×", () => filter.Text = string.Empty),
            CatalogueUi.JsonButton(() => RequestGoTo("PlayerStateData.KnownWordGroups")));

        var raceHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*"), ColumnSpacing = 8 };
        for (int i = 0; i < Races.Length; i++)
        {
            int race = Races[i].Race;
            _raceLabels[i] = new TextBlock { FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
            _raceLearnButtons[i] = CatalogueUi.Button("✓", () => SetRaceWords(race, true));
            _raceUnlearnButtons[i] = CatalogueUi.Button("✗", () => SetRaceWords(race, false));
            var cell = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center, Spacing = 4,
                Children =
                {
                    _raceLabels[i],
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 3, Children = { _raceLearnButtons[i], _raceUnlearnButtons[i] } }
                }
            };
            raceHeader.Children.Add(cell);
            Grid.SetColumn(cell, i);
        }
        var center = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 5, Children = { raceHeader, grid } };
        Grid.SetRow(grid, 1);
        var footer = CatalogueUi.Toolbar(
            RegisterButton("Words.learnAll", CatalogueUi.Button(string.Empty, () => SetAllWords(true))),
            RegisterButton("Words.unlearnAll", CatalogueUi.Button(string.Empty, () => SetAllWords(false))),
            RegisterButton("Words.learnSelected", CatalogueUi.Button(string.Empty, () => SetSelectedWords(true))),
            RegisterButton("Words.unlearnSelected", CatalogueUi.Button(string.Empty, () => SetSelectedWords(false))),
            RegisterButton("Words.export", CatalogueUi.AsyncButton(string.Empty, () => ExportListAsync(CatalogueTransferKind.Words))),
            RegisterButton("Words.import", CatalogueUi.AsyncButton(string.Empty, () => ImportListAsync(CatalogueTransferKind.Words))));
        return (grid, filter, CatalogueUi.PageGrid(top, center, footer));
    }

    private Control BuildGlyphsPage()
    {
        var glyphGrid = new Grid
        {
            Margin = new Thickness(20),
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            RowDefinitions = new RowDefinitions("*,*,*,*"),
            ColumnSpacing = 8,
            RowSpacing = 8
        };
        for (int i = 0; i < 16; i++)
        {
            var image = new Image
            {
                Source = _icons?.GetIcon($"UI-GLYPH{i + 1}.PNG"), Width = 58, Height = 58,
                Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center
            };
            _glyphChecks[i] = new CheckBox { HorizontalAlignment = HorizontalAlignment.Center };
            _glyphChecks[i].IsCheckedChanged += (_, _) => { if (!_loading) RaiseModified(); };
            var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Spacing = 3, Children = { image, _glyphChecks[i] } };
            glyphGrid.Children.Add(cell);
            Grid.SetRow(cell, i / 4);
            Grid.SetColumn(cell, i % 4);
        }
        var footer = CatalogueUi.Toolbar(
            RegisterButton("Glyphs.learnAll", CatalogueUi.Button(string.Empty, () => SetAllGlyphs(true))),
            RegisterButton("Glyphs.unlearnAll", CatalogueUi.Button(string.Empty, () => SetAllGlyphs(false))),
            RegisterButton("Glyphs.export", CatalogueUi.AsyncButton(string.Empty, () => ExportListAsync(CatalogueTransferKind.Glyphs))),
            RegisterButton("Glyphs.import", CatalogueUi.AsyncButton(string.Empty, () => ImportListAsync(CatalogueTransferKind.Glyphs))),
            CatalogueUi.JsonButton(() => RequestGoTo("PlayerStateData.KnownPortalRunes")));
        return CatalogueUi.PageGrid(new Border(), glyphGrid, footer);
    }

    private (DataGrid Grid, TextBox Filter, Control Page) BuildLocationsPage()
    {
        var grid = CatalogueUi.Grid();
        grid.ItemsSource = _visibleLocationRows;
        grid.Columns.Add(CatalogueUi.TextColumn("", nameof(CatalogueLocationRow.Index), 50));
        grid.Columns.Add(CatalogueUi.TextColumn("", nameof(CatalogueLocationRow.Name), star: true));
        grid.Columns.Add(CatalogueUi.TextColumn("", nameof(CatalogueLocationRow.Type), 170));
        grid.Columns.Add(CatalogueUi.TextColumn("", nameof(CatalogueLocationRow.Galaxy), 230));
        grid.Columns.Add(CatalogueUi.TextColumn("", nameof(CatalogueLocationRow.PortalCode), 145));
        grid.Columns.Add(CatalogueUi.TextColumn("", nameof(CatalogueLocationRow.PortalCodeDecimal), 225));
        grid.Columns.Add(CatalogueUi.TextColumn("", nameof(CatalogueLocationRow.SignalBooster), 220));
        grid.SelectionChanged += (_, _) => UpdateLocationDetails();
        var filter = CatalogueUi.FilterBox(string.Empty, ApplyLocationFilter);
        var top = CatalogueUi.Toolbar(filter, CatalogueUi.Button("×", () => filter.Text = string.Empty),
            RegisterButton("Locations.delete", CatalogueUi.AsyncButton(string.Empty, DeleteLocationsAsync)),
            RegisterButton("Locations.travel", CatalogueUi.AsyncButton(string.Empty, TravelAsync)),
            RegisterButton("Locations.export", CatalogueUi.AsyncButton(string.Empty, () => ExportListAsync(CatalogueTransferKind.Locations))),
            RegisterButton("Locations.import", CatalogueUi.AsyncButton(string.Empty, () => ImportListAsync(CatalogueTransferKind.Locations))),
            CatalogueUi.JsonButton(() => RequestGoTo("PlayerStateData.TeleportEndpoints")));
        var details = CatalogueUi.Toolbar(_portalGlyphsCaption, _locationGlyphs, _galaxyCaption, _locationGalaxy, _locationCore);
        return (grid, filter, CatalogueUi.PageGrid(top, grid, details));
    }

    private (DataGrid Grid, TextBox Filter, Control Page) BuildFishPage()
    {
        var grid = CatalogueUi.Grid(readOnly: false);
        grid.ItemsSource = _visibleFishRows;
        grid.Columns.Add(CatalogueUi.ImageColumn("🐟", nameof(CatalogueFishRow.Icon)));
        grid.Columns.Add(CatalogueUi.TextColumn("", nameof(CatalogueFishRow.ProductId), 180));
        grid.Columns.Add(CatalogueUi.TextColumn("", nameof(CatalogueFishRow.Name), star: true));
        grid.Columns.Add(CatalogueUi.TextColumn("", nameof(CatalogueFishRow.Count), 110, readOnly: false));
        grid.Columns.Add(CatalogueUi.TextColumn("", nameof(CatalogueFishRow.LargestCatch), 150, readOnly: false));
        var filter = CatalogueUi.FilterBox(string.Empty, ApplyFishFilter);
        var top = CatalogueUi.Toolbar(filter, CatalogueUi.Button("×", () => filter.Text = string.Empty),
            CatalogueUi.JsonButton(() => RequestGoTo("PlayerStateData.FishingRecord")));
        var footer = CatalogueUi.Toolbar(
            RegisterButton("Fish.add", CatalogueUi.AsyncButton(string.Empty, AddFishAsync)),
            RegisterButton("Fish.remove", CatalogueUi.Button(string.Empty, RemoveFish)),
            CatalogueUi.AsyncButton("Complete fish catalogue", () => CompleteFishAsync(true)),
            CatalogueUi.AsyncButton("Repair fishing records", () => CompleteFishAsync(false)),
            RegisterButton("Fish.export", CatalogueUi.AsyncButton(string.Empty, () => ExportListAsync(CatalogueTransferKind.Fish))),
            RegisterButton("Fish.import", CatalogueUi.AsyncButton(string.Empty, () => ImportListAsync(CatalogueTransferKind.Fish))));
        return (grid, filter, CatalogueUi.PageGrid(top, grid, footer));
    }

    private (DataGrid Grid, TextBox Filter, Control Page) BuildKnownRecipesPage()
    {
        var grid = CatalogueUi.Grid();
        grid.ItemsSource = _visibleRecipeRows;
        grid.Columns.Add(CatalogueUi.ImageColumn("⚙", nameof(CatalogueRecipeRow.Icon)));
        grid.Columns.Add(CatalogueUi.TextColumn("", nameof(CatalogueRecipeRow.Name), 230));
        grid.Columns.Add(CatalogueUi.TextColumn("", nameof(CatalogueRecipeRow.Category), 120));
        grid.Columns.Add(CatalogueUi.ImageColumn("⚙", nameof(CatalogueRecipeRow.ResultIcon)));
        grid.Columns.Add(CatalogueUi.TextColumn("", nameof(CatalogueRecipeRow.Result), 230));
        grid.Columns.Add(CatalogueUi.TextColumn("", nameof(CatalogueRecipeRow.Ingredients), star: true));
        grid.Columns.Add(CatalogueUi.TextColumn("", nameof(CatalogueRecipeRow.Id), 170));
        var filter = CatalogueUi.FilterBox(string.Empty, _ => ApplyRecipeFilter());
        var top = CatalogueUi.Toolbar(filter, CatalogueUi.Button("×", () => filter.Text = string.Empty),
            CatalogueUi.JsonButton(() => RequestGoTo("PlayerStateData.KnownRefinerRecipes")));
        var footer = CatalogueUi.Toolbar(
            RegisterButton("Recipes.add", CatalogueUi.AsyncButton(string.Empty, AddRecipeAsync)),
            RegisterButton("Recipes.remove", CatalogueUi.Button(string.Empty, () => RemoveSelected(grid, _recipeRows, _visibleRecipeRows))),
            RegisterButton("Recipes.export", CatalogueUi.AsyncButton(string.Empty, () => ExportListAsync(CatalogueTransferKind.Recipes))),
            RegisterButton("Recipes.import", CatalogueUi.AsyncButton(string.Empty, () => ImportListAsync(CatalogueTransferKind.Recipes))));
        return (grid, filter, CatalogueUi.PageGrid(top, grid, footer));
    }

    private static DataGridTemplateColumn WordCheckColumn(string header, string value, string enabled) => new()
    {
        Header = header,
        Width = new DataGridLength(100),
        CellTemplate = new FuncDataTemplate<CatalogueWordRow>((_, _) =>
        {
            var check = new CheckBox { HorizontalAlignment = HorizontalAlignment.Center };
            check.Bind(global::Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(value) { Mode = BindingMode.TwoWay });
            check.Bind(IsEnabledProperty, new Binding(enabled));
            return check;
        })
    };

    private Button RegisterButton(string key, Button button) { _buttons[key] = button; return button; }
    private void RequestGoTo(string path) => GoToJsonRequested?.Invoke(this, new GoToJsonRequestEventArgs(path));
    private void RaiseModified() => DataModified?.Invoke(this, EventArgs.Empty);
    private Window? Owner => TopLevel.GetTopLevel(this) as Window;

    private static void Clear<T>(ObservableCollection<T> source, ObservableCollection<T> visible)
    {
        source.Clear();
        visible.Clear();
    }
}
