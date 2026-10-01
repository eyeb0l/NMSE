using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using NMSE.Config;
using NMSE.Core;
using NMSE.Data;
using NMSE.IO;
using NMSE.Models;
using NMSE.UI.Avalonia.Coordination;
using NMSE.UI.Avalonia.Inventory;
using NMSE.UI.Avalonia.Panels.BasesStorage;
using NMSE.UI.Avalonia.Panels.Catalogue;
using NMSE.UI.Avalonia.Panels.ExportSettings;
using NMSE.UI.Avalonia.Panels.Player;
using NMSE.UI.Avalonia.Platform;
using NMSE.UI.Avalonia.Views;
using Xunit;

namespace NMSE.Avalonia.Tests;

public sealed class Cosmos705PortTests
{
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static object? Invoke(object owner, string name, params object?[] args) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, args);
    private static string[] Values(JsonArray? array) => array == null ? [] : Enumerable.Range(0, array.Length).Select(i => array.GetString(i)!).ToArray();

    [AvaloniaFact]
    public void Hotkeys_LoadMissingDataDoesNotMutateAndChangingBindingWritesCorrectContext()
    {
        var player = new JsonObject();
        var panel = new HotkeysPanel();
        int changes = 0; panel.DataModified += (_, _) => changes++;
        panel.LoadData(player);
        panel.ApplyUiLocalisation();
        Assert.False(player.Contains("HotActions")); Assert.Equal(0, changes);
        Field<ComboBox[,]>(panel, "_actions")[2, 9].SelectedIndex = 4; // CallShip
        var hot = player.GetArray("HotActions")!;
        Assert.Equal(3, hot.Length);
        var slot = hot.GetObject(2).GetArray("KeyActions")!.GetObject(9);
        Assert.Equal("CallShip", slot.GetObject("Action")!.GetString("QuickMenuActions"));
        Assert.Equal(-1, slot.GetObject("InventoryIndex")!.GetInt("X"));
        Assert.True(Field<Button[,]>(panel, "_parameters")[2, 9].IsEnabled);
        Assert.Equal(1, changes);
    }

    [AvaloniaFact]
    public void Hotkeys_UnknownActionAndParametersSurviveLoadingAndLocalisation()
    {
        var player = JsonObject.Parse("""{"HotActions":[{"KeyActions":[{"Action":{"QuickMenuActions":"FutureAction"},"Id":"^CUSTOM","Number":42,"InventoryIndex":{"X":7,"Y":3},"UnknownField":true}]}]}""");
        string before = player.ToString();
        var panel = new HotkeysPanel(); panel.LoadData(player); panel.ApplyUiLocalisation();
        Assert.Equal(before, player.ToString());
        Assert.Equal("FutureAction", Field<ComboBox[,]>(panel, "_actions")[0, 0].SelectedItem!.ToString());
    }

    [AvaloniaFact]
    public async Task Hotkeys_ExportImportRoundTripKeepsBindingsAndConfiguredExtension()
    {
        string path = Path.Combine(Path.GetTempPath(), "NMSE hotkeys " + Guid.NewGuid().ToString("N") + ".nmshk");
        try
        {
            var desktop = new TestDesktop(path);
            var player = JsonObject.Parse("""{"HotActions":[{"KeyActions":[{"Action":{"QuickMenuActions":"Emote"},"Id":"^EMOTE_WAVE","Number":0,"InventoryIndex":{"X":-1,"Y":-1},"Extra":123}]}]}""");
            var panel = new HotkeysPanel(desktop); panel.LoadData(player);
            await (Task)Invoke(panel, "ExportAsync")!;
            Assert.EndsWith(ExportConfig.Instance.HotkeysExt, desktop.SuggestedName);
            var target = new JsonObject(); panel.LoadData(target);
            int changes = 0; panel.DataModified += (_, _) => changes++;
            await (Task)Invoke(panel, "ImportAsync")!;
            var first = target.GetArray("HotActions")!.GetObject(0).GetArray("KeyActions")!.GetObject(0);
            Assert.Equal("^EMOTE_WAVE", first.GetString("Id")); Assert.Equal(123, first.GetInt("Extra"));
            Assert.Equal(10, target.GetArray("HotActions")!.GetObject(2).GetArray("KeyActions")!.Length);
            Assert.Equal(1, changes);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static JsonObject ChestSave() => JsonObject.Parse("""
        {"PlayerStateData":{
          "Chest1Inventory":{"Width":2,"Height":1,"ValidSlotIndices":[{"X":0,"Y":0},{"X":1,"Y":0}],"Slots":[{"Id":"^UNKNOWN","Type":{"InventoryType":"Product"},"Amount":50,"MaxAmount":100,"Index":{"X":0,"Y":0}}]},
          "Chest2Inventory":{"Width":2,"Height":1,"ValidSlotIndices":[{"X":0,"Y":0},{"X":1,"Y":0}],"Slots":[{"Id":"^UNKNOWN","Type":{"InventoryType":"Product"},"Amount":25,"MaxAmount":100,"Index":{"X":0,"Y":0}}]}
        }}
        """);

    [AvaloniaFact]
    public void SortAllChests_CommitsGridEditsAndReloadsEveryOpenedChestBeforeSaving()
    {
        var save = ChestSave(); var panel = new ChestsPanel(new GameItemDatabase(), null); panel.LoadData(save);
        var tabs = Field<TabControl>(panel, "_tabs");
        Assert.Equal(0, tabs.SelectedIndex);
        tabs.SelectedIndex = 1;
        var grids = Field<InventoryGrid[]>(panel, "_grids");
        Assert.True(grids[1].AddOrReplaceItem(1, 0, "^EXTRA"));
        tabs.SelectedIndex = 10;
        Invoke(panel, "ApplyAllChestsAction"); panel.SaveData(save);
        var player = save.GetObject("PlayerStateData")!;
        Assert.Equal(0, player.GetObject("Chest2Inventory")!.GetArray("Slots")!.Length);
        tabs.SelectedIndex = 1;
        Assert.Equal(0, grids[1].LoadedInventory!.GetArray("Slots")!.Length);
        tabs.SelectedIndex = 0;
        var slots = grids[0].LoadedInventory!.GetArray("Slots")!;
        Assert.Equal(2, slots.Length);
        var merged = Enumerable.Range(0, slots.Length).Select(slots.GetObject).Single(slot => slot.GetString("Id") == "^UNKNOWN");
        Assert.Equal(75, merged.GetInt("Amount"));
    }

    [AvaloniaFact]
    public void AllChests_MergeOnlyDisablesSortControlsAndPreservesSurvivingPosition()
    {
        var save = ChestSave(); var panel = new ChestsPanel(new GameItemDatabase(), null); panel.LoadData(save);
        Field<CheckBox>(panel, "_mergeOnly").IsChecked = true;
        Assert.False(Field<ComboBox>(panel, "_sortMode").IsEnabled);
        Assert.False(Field<NumericUpDown>(panel, "_padding").IsEnabled);
        Invoke(panel, "ApplyAllChestsAction"); panel.SaveData(save);
        var slot = save.GetObject("PlayerStateData")!.GetObject("Chest1Inventory")!.GetArray("Slots")!.GetObject(0);
        Assert.Equal(75, slot.GetInt("Amount"));
        Assert.Equal(0, slot.GetObject("Index")!.GetInt("X"));
        Assert.Equal(0, save.GetObject("PlayerStateData")!.GetObject("Chest2Inventory")!.GetArray("Slots")!.Length);
    }

    [AvaloniaFact]
    public void AllChests_InsufficientCapacityLeavesDataUnchanged()
    {
        var save = ChestSave(); var panel = new ChestsPanel(new GameItemDatabase(), null); panel.LoadData(save);
        Field<NumericUpDown>(panel, "_padding").Value = 2;
        string before = save.ToString(); int changes = 0; panel.DataModified += (_, _) => changes++;
        Invoke(panel, "ApplyAllChestsAction");
        Assert.Equal(before, save.ToString()); Assert.Equal(0, changes);
    }

    [Fact]
    public void XboxExternalMonitorIncludesSlotAndAccountAndDetectsAccountChanges()
    {
        string root = Path.Combine(Path.GetTempPath(), "NMSE Xbox watching " + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (string identifier in new[] { "Slot1Manual", "AccountData" })
            {
                Directory.CreateDirectory(Path.Combine(root, identifier));
                File.WriteAllText(Path.Combine(root, identifier, "data"), "{}");
                File.WriteAllText(Path.Combine(root, identifier, "meta"), "meta");
            }
            var paths = ExternalFileMonitor.GetPaths(SaveSession.ForXbox(root, "Slot1Manual"), null);
            Assert.Equal(4, paths.Count);
            var monitor = new ExternalFileMonitor(); monitor.Capture(paths);
            File.WriteAllText(Path.Combine(root, "AccountData", "data"), "{\"changed\":true}");
            Assert.Equal((true, false), monitor.Check());
            File.Delete(Path.Combine(root, "Slot1Manual", "data"));
            Assert.Equal((true, true), monitor.Check());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public void WatchMenusReflectConfiguredModesInNativeAndWindowMenus()
    {
        string original = AppConfig.Instance.ExternalChangeMode;
        var window = new MainWindow();
        try
        {
            foreach (string mode in new[] { "prompt", "auto", "ignore" })
            {
                AppConfig.Instance.ExternalChangeMode = mode; Invoke(window, "UpdateWatchMenus");
                var native = Field<NativeMenuItem>(window, "_watchNativeMenu").Menu!.Items.OfType<NativeMenuItem>().ToArray();
                var fallback = Field<MenuItem>(window, "_watchMenu").Items.OfType<MenuItem>().ToArray();
                int index = Array.IndexOf(new[] { "prompt", "auto", "ignore" }, mode);
                Assert.True(native[index].IsChecked); Assert.True(fallback[index].IsChecked);
                Assert.Single(native, item => item.IsChecked); Assert.Single(fallback, item => item.IsChecked);
            }
        }
        finally
        {
            AppConfig.Instance.ExternalChangeMode = original;
            window.GetType().GetField("_allowClose", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, true);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task TechnologyCompletionExcludesBrokenPlaceholdersAndRetainsExistingEntries()
    {
        var items = new GameItemDatabase(); items.LoadItemsFromJsonDirectory(Path.Combine(ApplicationPaths.Current.ResourcesDirectory, "json"));
        Assert.True(items.GetItem("SHIPSLOT_DMG1")!.BrokenSlotTech);
        var panel = new CataloguePanel(items, new RecipeDatabase(), new WordDatabase(), null) { ConfirmAsync = (_, _, _) => Task.FromResult(true) };
        var save = JsonObject.Parse("""{"PlayerStateData":{"KnownTech":["^CUSTOM","^SHIPSLOT_DMG1"],"KnownProducts":[],"KnownSpecials":[]}}""");
        panel.LoadData(save);
        await (Task)Invoke(panel, "CompleteTechnologiesAsync")!;
        panel.SaveData(save);
        var ids = Values(save.GetObject("PlayerStateData")!.GetArray("KnownTech"));
        Assert.Contains("^CUSTOM", ids); Assert.Contains("^SHIPSLOT_DMG1", ids); Assert.DoesNotContain("^OBSOLETE", ids);
        Assert.True(ids.Length > 100);
        Assert.DoesNotContain(ids.Where(id => id != "^SHIPSLOT_DMG1"), id => items.GetItem(id)?.BrokenSlotTech == true);
    }

    [AvaloniaFact]
    public void ExportSettingsIncludesHotkeyExtensionAndTemplate()
    {
        var panel = new ExportSettingsPanel();
        Assert.Equal(ExportConfig.Instance.HotkeysExt, Field<Dictionary<string, TextBox>>(panel, "_extensionFields")["Hotkeys"].Text);
        Assert.Equal(ExportConfig.Instance.HotkeysTemplate, Field<Dictionary<string, TextBox>>(panel, "_templateFields")["Hotkeys"].Text);
    }

    private sealed class TestDesktop(string path) : IDesktopServices
    {
        public string? SuggestedName { get; private set; }
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>> PickFilesAsync(string title, bool allowMultiple, params DesktopFileType[] types) => Task.FromResult<IReadOnlyList<string>>([path]);
        public Task<string?> PickSaveFileAsync(string title, string suggestedName, string? defaultExtension, params DesktopFileType[] types) { SuggestedName = suggestedName; return Task.FromResult<string?>(path); }
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
        public Task ShowErrorAsync(string title, string message) => Task.FromException(new InvalidOperationException(message));
        public Task<string?> GetClipboardTextAsync() => Task.FromResult<string?>(null);
        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
        public Task OpenUrlAsync(Uri uri) => Task.CompletedTask;
        public ThemeVariant SystemTheme => ThemeVariant.Default;
        public void Shutdown() { }
    }
}
