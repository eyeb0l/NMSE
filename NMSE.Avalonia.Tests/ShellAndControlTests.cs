using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Styling;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Contracts;
using NMSE.UI.Avalonia.Controls;
using NMSE.UI.Avalonia.Coordination;
using NMSE.UI.Avalonia.Dialogs;
using NMSE.UI.Avalonia.Inventory;
using NMSE.UI.Avalonia.Panels.Exosuit;
using NMSE.UI.Avalonia.Views;
using Xunit;

namespace NMSE.Avalonia.Tests;

public sealed class ShellAndControlTests
{
    [AvaloniaFact]
    public void MainWindow_ContainsTheFifteenCurrentTopLevelAreas()
    {
        var window = new MainWindow();
        var tabs = window.FindControl<TabControl>("MainTabs");

        Assert.NotNull(tabs);
        Assert.Equal(15, tabs.ItemCount);
        Assert.DoesNotContain(
            tabs.Items.OfType<TabItem>(),
            tab => string.Equals(tab.Header?.ToString(), "Multiplayer", StringComparison.OrdinalIgnoreCase));
        CloseWithoutPrompt(window);
    }

    [AvaloniaFact]
    public void MainWindow_InitialisesEveryTopLevelAreaWithARealPanel()
    {
        var window = new MainWindow();
        Invoke(window, "InitializePortedPanels");
        var tabs = window.FindControl<TabControl>("MainTabs")!;

        string[] expectedPanelTypes =
        [
            "PlayerPanel", "ExosuitPanel", "MultitoolPanel", "StarshipPanel", "FleetPanel",
            "ExocraftPanel", "CompanionPanel", "BasesStoragePanel", "CataloguePanel",
            "MilestonePanel", "SettlementPanel", "ByteBeatPanel", "AccountRewardsPanel",
            "ExportSettingsPanel", "RawJsonPanel",
        ];
        string[] actualPanelTypes = tabs.Items.OfType<TabItem>()
            .Select(tab => tab.Content?.GetType().Name ?? string.Empty)
            .ToArray();

        Assert.Equal(expectedPanelTypes, actualPanelTypes);
        Assert.DoesNotContain(actualPanelTypes, type => type.Contains("Placeholder", StringComparison.Ordinal));
        CloseWithoutPrompt(window);
    }

    [AvaloniaFact]
    public void EverySaveBackedPanel_LoadsAndCommitsThroughTheCoordinator()
    {
        var window = new MainWindow();
        Invoke(window, "InitializePortedPanels");
        var coordinator = (SaveEditorCoordinator)GetField(window, "_panelCoordinator");
        JsonObject save = JsonObject.Parse("""
            {
              "ActiveContext": "Main",
              "PlayerStateData": {
                "Inventory": { "Width": 1, "Height": 1, "Slots": [], "ValidSlotIndices": [], "SpecialSlots": [] },
                "Inventory_TechOnly": { "Width": 1, "Height": 1, "Slots": [], "ValidSlotIndices": [], "SpecialSlots": [] },
                "Multitools": [], "ShipOwnership": [], "VehicleOwnership": [],
                "FleetFrigates": [], "SquadronPilots": [], "Pets": [], "Eggs": [],
                "PersistentPlayerBases": [], "BaseBuildingObjects": [],
                "SettlementStatesV2": [], "ByteBeatLibrary": [], "Outfits": [],
                "KnownTech": [], "KnownProducts": [], "KnownSpecials": [], "KnownWordGroups": []
              },
              "CommonStateData": {},
              "SpawnStateData": {}
            }
            """);
        bool baselineCaptured = false;

        coordinator.BeginSession(save, 0, _ => baselineCaptured = true);
        for (int index = 1; index < 15; index++)
            coordinator.Activate(index);
        coordinator.CommitLoadedPanels();

        Assert.True(baselineCaptured);
        var tabs = window.FindControl<TabControl>("MainTabs")!;
        object catalogue = ((TabItem)tabs.Items[8]!).Content!;
        Assert.True((bool)catalogue.GetType().GetProperty("IsPurged")!.GetValue(catalogue)!);
        CloseWithoutPrompt(window);
    }

    [AvaloniaFact]
    public void ThemeAndLanguageResources_CanSwitchLive()
    {
        var window = new MainWindow();
        Application app = Application.Current!;

        Invoke(window, "ApplyTheme", "Dark", false);
        Assert.Equal(ThemeVariant.Dark, app.RequestedThemeVariant);
        Invoke(window, "ApplyTheme", "Light", false);
        Assert.Equal(ThemeVariant.Light, app.RequestedThemeVariant);

        UiStrings.SetDirectory(Path.Combine(ApplicationPaths.Current.ResourcesDirectory, "ui", "lang"));
        Invoke(window, "InitializePortedPanels");
        var coordinator = (SaveEditorCoordinator)GetField(window, "_panelCoordinator");
        string[] languageTags =
        [
            "de-DE", "en-GB", "en-US", "es-419", "es-ES", "fr-FR", "it-IT", "ja-JP",
            "ko-KR", "nl-NL", "pl-PL", "pt-BR", "pt-PT", "ru-RU", "zh-CN", "zh-TW",
        ];
        foreach (string languageTag in languageTags)
        {
            Assert.True(UiStrings.Load(languageTag));
            Invoke(window, "ApplyLocalisation");
            coordinator.ApplyUiLocalisation();
            var tabs = window.FindControl<TabControl>("MainTabs")!;
            string? playerHeader = ((TabItem)tabs.Items[0]!).Header?.ToString();
            Assert.False(string.IsNullOrWhiteSpace(playerHeader));
            Assert.NotEqual("tab.player", playerHeader);
        }
        Assert.True(UiStrings.Load("en-GB"));
        CloseWithoutPrompt(window);
    }

    [AvaloniaFact]
    public void MacOsSaveRootDiscovery_EnumeratesProfilesButIgnoresSettings()
    {
        string root = Path.Combine(Path.GetTempPath(), $"nmse-ui-save-root-{Guid.NewGuid():N}");
        string profile = Path.Combine(root, "st_123456");
        string settings = Path.Combine(root, "Settings");
        try
        {
            Directory.CreateDirectory(profile);
            Directory.CreateDirectory(settings);
            File.WriteAllText(Path.Combine(profile, "save.hg"), "fixture");
            File.WriteAllText(Path.Combine(settings, "settings.dat"), "not a save");

            MethodInfo method = typeof(MainWindow).GetMethod(
                "DiscoverSaveSlots",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var slots = (System.Collections.IEnumerable)method.Invoke(null, [root])!;
            string[] labels = slots.Cast<object>().Select(slot => slot.ToString()!).ToArray();

            Assert.Single(labels);
            Assert.Contains("st_123456", labels[0]);
            Assert.DoesNotContain(labels, label => label.Contains("Settings", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void InvariantNumericTextBox_PreservesInvariantFullPrecisionText()
    {
        var editor = new InvariantNumericTextBox
        {
            NumericValue = 123456789.12345679,
        };

        Assert.Equal("123456789.12345679", editor.Text);
        Assert.Equal(123456789.12345679, editor.NumericValue);
    }

    [AvaloniaFact]
    public void InvariantNumericTextBox_SpinnerArrowsHaveAnUnclippedHitTarget()
    {
        var editor = new InvariantNumericTextBox { Width = 160, NumericValue = 10 };
        var window = new Window { Width = 220, Height = 100, Content = editor };
        window.Show();

        var up = (RepeatButton)GetField(editor, "_upButton");
        var down = (RepeatButton)GetField(editor, "_downButton");
        var upArrow = Assert.IsType<TextBlock>(up.Content);
        var downArrow = Assert.IsType<TextBlock>(down.Content);

        Assert.True(up.Bounds.Height >= 17);
        Assert.True(down.Bounds.Height >= 17);
        Assert.True(upArrow.FontSize >= 12);
        Assert.True(downArrow.FontSize >= 12);
        Assert.True(upArrow.Bounds.Height <= up.Bounds.Height);
        Assert.True(downArrow.Bounds.Height <= down.Bounds.Height);
        window.Close();
    }

    [AvaloniaFact]
    public void InventoryGrid_CentresCellsAndClipsTheLevelMeterInsideEachCell()
    {
        JsonObject inventory = JsonObject.Parse("""
            {
              "Width": 2, "Height": 1,
              "ValidSlotIndices": [{"X":0,"Y":0},{"X":1,"Y":0}],
              "SpecialSlots": [],
              "Slots": [{
                "Type":{"InventoryType":"Product"}, "Id":"^TEST", "Amount":50, "MaxAmount":100,
                "DamageFactor":0.0, "Index":{"X":0,"Y":0}
              }]
            }
            """);
        var grid = new InventoryGrid();
        grid.LoadInventory(inventory);
        var window = new Window { Width = 760, Height = 420, Content = grid };
        window.Show();

        var cells = (WrapPanel)GetField(grid, "_cellsPanel");
        Assert.Equal(HorizontalAlignment.Center, cells.HorizontalAlignment);
        Assert.Equal(180, cells.Width);
        Control cell = Assert.IsAssignableFrom<Control>(cells.Children[0]);
        Control meter = Assert.IsAssignableFrom<Control>(GetField(cell, "_meter"));

        Assert.True(cell.ClipToBounds);
        Assert.True(meter.ClipToBounds);
        Assert.True(meter.Bounds.Width <= cell.Bounds.Width);
        Assert.True(meter.Bounds.Height <= 6);
        window.Close();
    }

    [AvaloniaFact]
    public void InventoryGrid_ContextMenuUsesLocalisedBulkAndAutoStackLabels()
    {
        UiStrings.SetDirectory(Path.Combine(ApplicationPaths.Current.ResourcesDirectory, "ui", "lang"));
        Assert.True(UiStrings.Load("en-GB"));
        JsonObject inventory = JsonObject.Parse("""
            {
              "Width": 1, "Height": 1,
              "ValidSlotIndices": [{"X":0,"Y":0}],
              "SpecialSlots": [],
              "Slots": [{
                "Type":{"InventoryType":"Product"}, "Id":"^TEST", "Amount":1, "MaxAmount":10,
                "DamageFactor":0.0, "Index":{"X":0,"Y":0}
              }]
            }
            """);
        var grid = new InventoryGrid();
        grid.LoadInventory(inventory);
        var cells = (WrapPanel)GetField(grid, "_cellsPanel");
        object cell = cells.Children[0];
        var menu = Assert.IsType<ContextMenu>(Invoke(grid, "CreateContextMenu", cell));
        string[] labels = menu.Items.OfType<MenuItem>().Select(item => item.Header?.ToString() ?? string.Empty).ToArray();

        Assert.Contains("Enable All Slots", labels);
        Assert.Contains("Repair All Slots", labels);
        Assert.Contains("Auto-Stack to Chests", labels);
        Assert.Contains("Auto-Stack to Starship", labels);
        Assert.Contains("Auto-Stack to Freighter", labels);
        Assert.DoesNotContain(labels, label => label.StartsWith("inventory.", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void ItemPicker_RendersAndFiltersLoadedDatabaseItems()
    {
        var database = new GameItemDatabase();
        Assert.True(database.LoadItemsFromJsonDirectory(
            Path.Combine(ApplicationPaths.Current.ResourcesDirectory, "json")));
        var dialog = new ItemPickerDialog();

        dialog.Initialize(database, iconProvider: null);
        dialog.Show();
        try
        {
            var searchBox = (TextBox)GetField(dialog, "_searchBox");
            var typeCombo = (ComboBox)GetField(dialog, "_typeCombo");
            var categoryCombo = (ComboBox)GetField(dialog, "_categoryCombo");
            var manualIdBox = (TextBox)GetField(dialog, "_manualIdBox");
            var grid = (DataGrid)GetField(dialog, "_itemGrid");

            Assert.NotEmpty(dialog.ViewModel.FilteredItems);
            Assert.Same(dialog.ViewModel, grid.DataContext);
            Assert.Same(dialog.ViewModel.FilteredItems, grid.ItemsSource);
            Assert.Same(dialog.ViewModel.TypeFilters, typeCombo.ItemsSource);
            Assert.Same(dialog.ViewModel.CategoryFilters, categoryCombo.ItemsSource);
            Assert.Equal("(All)", typeCombo.SelectedItem);
            Assert.Equal("(All)", categoryCombo.SelectedItem);

            searchBox.Text = "carbon";
            Assert.Equal("carbon", dialog.ViewModel.SearchText);
            Assert.Contains(dialog.ViewModel.FilteredItems, item => item.Name == "Carbon");
            Assert.All(dialog.ViewModel.FilteredItems, item =>
                Assert.True(item.Name.Contains("carbon", StringComparison.OrdinalIgnoreCase) ||
                            item.Category.Contains("carbon", StringComparison.OrdinalIgnoreCase) ||
                            item.Id.Contains("carbon", StringComparison.OrdinalIgnoreCase)));

            searchBox.Text = string.Empty;
            int rawMaterialsIndex = dialog.ViewModel.TypeFilters.IndexOf("Raw Materials");
            Assert.True(rawMaterialsIndex > 0);
            typeCombo.SelectedIndex = rawMaterialsIndex;
            Assert.Equal(rawMaterialsIndex, dialog.ViewModel.SelectedTypeIndex);
            Assert.NotEmpty(dialog.ViewModel.FilteredItems);
            Assert.All(dialog.ViewModel.FilteredItems, item =>
                Assert.Equal("Raw Materials", item.Type, ignoreCase: true));

            manualIdBox.Text = "^TEST";
            Assert.Equal("^TEST", dialog.ViewModel.ManualItemId);
            Assert.All(grid.Columns.OfType<DataGridTextColumn>(), column =>
                Assert.IsType<ReflectionBinding>(column.Binding));
        }
        finally
        {
            dialog.Close();
        }
    }

    [AvaloniaFact]
    public void ItemPicker_MultiSelectTracksEverySelectedItem()
    {
        var items = new[]
        {
            new ItemPickerItemViewModel { Id = "^A", Name = "Alpha", Category = "Product", Type = "Product" },
            new ItemPickerItemViewModel { Id = "^B", Name = "Beta", Category = "Product", Type = "Product" },
            new ItemPickerItemViewModel { Id = "^C", Name = "Gamma", Category = "Product", Type = "Product" },
        };
        var viewModel = new ItemPickerViewModel { AllowMultiSelect = true };

        viewModel.SetSelectedItems([items[0], items[2]]);

        Assert.True(viewModel.ConfirmSelection());
        Assert.Equal(["^A", "^C"], viewModel.ResultItemIds);
    }

    [AvaloniaFact]
    public void Coordinator_CapturesBaselineBeforeDeferredPanelLoadAndPurgesHeavyPanel()
    {
        var calls = new List<string>();
        var first = new RecordingPanel("first", calls);
        var catalogue = new PurgeableRecordingPanel("catalogue", calls);
        var coordinator = new SaveEditorCoordinator();
        coordinator.Register(0, first);
        coordinator.Register(8, catalogue, purgeWhenInactive: true);
        var data = new JsonObject();

        coordinator.BeginSession(data, 8, _ => calls.Add("baseline"));
        coordinator.Activate(0);

        Assert.Equal(["baseline", "catalogue.load", "catalogue.save", "catalogue.purge", "first.load"], calls);
        Assert.Equal([0], coordinator.LoadedPanelIndices);
    }

    [AvaloniaFact]
    public void Exosuit_LoadEditSave_ProvidesCompleteInventoryVerticalSlice()
    {
        JsonObject save = JsonObject.Parse("""
            {
              "PlayerStateData": {
                "Inventory": {
                  "Width": 1, "Height": 1, "Slots": [],
                  "ValidSlotIndices": [{"X": 0, "Y": 0}], "SpecialSlots": []
                },
                "Inventory_TechOnly": {
                  "Width": 1, "Height": 1, "Slots": [],
                  "ValidSlotIndices": [{"X": 0, "Y": 0}], "SpecialSlots": []
                }
              }
            }
            """);
        var panel = new ExosuitPanel();
        bool modified = false;
        panel.DataModified += (_, _) => modified = true;

        panel.LoadData(save);
        panel.CargoGrid.ResizeInventory(2, 2);
        panel.SaveData(save);

        JsonObject cargo = save.GetObject("PlayerStateData")!.GetObject("Inventory")!;
        Assert.Equal(2, cargo.GetInt("Width"));
        Assert.Equal(2, cargo.GetInt("Height"));
        Assert.True(modified);
    }

    private class RecordingPanel : ISaveEditorPanel
    {
        private readonly string _name;
        protected readonly List<string> Calls;

        public RecordingPanel(string name, List<string> calls)
        {
            _name = name;
            Calls = calls;
        }

        public event EventHandler? DataModified;
        public void LoadData(JsonObject saveData) => Calls.Add($"{_name}.load");
        public void SaveData(JsonObject saveData) => Calls.Add($"{_name}.save");
        public void ApplyUiLocalisation() { }
        protected void RaiseModified() => DataModified?.Invoke(this, EventArgs.Empty);
    }

    private sealed class PurgeableRecordingPanel : RecordingPanel, IPurgeableSaveEditorPanel
    {
        private readonly string _name;

        public PurgeableRecordingPanel(string name, List<string> calls) : base(name, calls)
        {
            _name = name;
        }

        public void PurgeData() => Calls.Add($"{_name}.purge");
    }

    private static object GetField(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static void CloseWithoutPrompt(MainWindow window)
    {
        window.GetType().GetField("_allowClose", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
        window.Close();
    }

    private static object? Invoke(object target, string name, params object?[]? arguments) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, arguments);
}
