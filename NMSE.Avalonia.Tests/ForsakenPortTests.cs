using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using NMSE.Config;
using NMSE.Core;
using NMSE.Data;
using NMSE.UI.Avalonia.Panels.Catalogue;
using NMSE.Models;
using NMSE.UI.Avalonia.Controls;
using NMSE.UI.Avalonia.Coordination;
using NMSE.UI.Avalonia.Panels.BasesStorage;
using NMSE.UI.Avalonia.Panels.Companions;
using NMSE.UI.Avalonia.Panels.Milestones;
using NMSE.UI.Avalonia.Panels.Player;
using NMSE.UI.Avalonia.Panels.RawJson;
using Xunit;

namespace NMSE.Avalonia.Tests;

public sealed class ForsakenPortTests
{
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static object? Invoke(object owner, string name, params object?[] args) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, args);
    private static JsonObject Bases() => JsonObject.Parse("""{"PlayerStateData":{"PersistentPlayerBases":[{"Name":"Bravo","BaseVersion":3,"BaseType":{"PersistentBaseTypes":"HomePlanetBase"},"Objects":[]},{"Name":"Alpha","BaseVersion":3,"BaseType":{"PersistentBaseTypes":"HomePlanetBase"},"Objects":[]},{"Name":"Legacy","BaseVersion":2,"BaseType":{"PersistentBaseTypes":"HomePlanetBase"},"Objects":[]}]}}""");

    [AvaloniaFact]
    public async Task BaseFilteringAndLegacySelection_DoNotModifySave()
    {
        var save = Bases(); string before = save.ToString();
        var panel = new BasesEditorPanel(); int changes = 0; panel.DataModified += (_, _) => changes++;
        panel.LoadData(save);
        Field<TextBox>(panel, "_baseFilter").Text = "legacy";
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        var list = Field<ListBox>(panel, "_baseList");
        Assert.Single(list.Items);
        Assert.True(Field<TextBox>(panel, "_name").IsReadOnly);
        Field<TextBox>(panel, "_baseFilter").Text = "";
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.Equal(3, list.ItemCount);
        Assert.Equal(before, save.ToString()); Assert.Equal(0, changes);
    }

    [AvaloniaFact]
    public void BaseMultiSelectionMove_PreservesSelectedBlockAndLegacyEntry()
    {
        var save = Bases(); var panel = new BasesEditorPanel(); panel.LoadData(save);
        var list = Field<ListBox>(panel, "_baseList");
        list.SelectedItems!.Add(list.Items[1]);
        Invoke(panel, "MoveSelected", int.MaxValue);
        var bases = save.GetObject("PlayerStateData")!.GetArray("PersistentPlayerBases")!;
        Assert.Equal(new[] { "Legacy", "Bravo", "Alpha" }, Enumerable.Range(0, bases.Length).Select(i => bases.GetObject(i).GetString("Name")));
        Assert.Equal(2, list.SelectedItems.Count);
    }

    [AvaloniaFact]
    public async Task BaseImportAsNew_WorksWithoutAnExistingBaseAndKeepsPosition()
    {
        var save = JsonObject.Parse("""{"PlayerStateData":{"PersistentPlayerBases":[]}}""");
        var panel = new BasesEditorPanel { ConfirmAsync = (_, _, _) => Task.FromResult(true),
            ImportAsync = (_, _) => Task.FromResult<BasesStorageTransferContent?>(new("base.nmsbase", """{"Name":"Imported","BaseVersion":3,"BaseType":{"PersistentBaseTypes":"HomePlanetBase"},"GalacticAddress":"0x1234","Position":[12,34,56],"Objects":[]}""")) };
        panel.LoadData(save);
        await (Task)Invoke(panel, "ImportBaseAsync", false)!;
        var bases = save.GetObject("PlayerStateData")!.GetArray("PersistentPlayerBases")!;
        Assert.Equal(1, bases.Length); Assert.Equal(34, bases.GetObject(0).GetArray("Position")!.GetInt(1));
    }

    [AvaloniaFact]
    public async Task BaseImport_InvalidTerrainDoesNotChangeSave()
    {
        var save = Bases(); string before = save.ToString();
        var panel = new BasesEditorPanel { ConfirmAsync = (_, _, _) => Task.FromResult(true),
            ImportAsync = (_, _) => Task.FromResult<BasesStorageTransferContent?>(new("bad.nmsbase", "{}", InvalidTerrain: true)) };
        panel.LoadData(save);
        await (Task)Invoke(panel, "ImportBaseAsync", true)!;
        Assert.Equal(before, save.ToString());
        Assert.False(string.IsNullOrWhiteSpace(Field<TextBlock>(panel, "_status").Text));
    }

    [AvaloniaFact]
    public void Milestones_AllCatalogStatsAppearAndSharedFieldsStayInSync()
    {
        using var resources = new AvaloniaResourceCatalog();
        var panel = new MilestonePanel(resources);
        var fields = Field<Dictionary<string, List<InvariantNumericTextBox>>>(panel, "_fields");
        Assert.Equal(237, fields.Count);
        Assert.Equal(241, fields.Values.Sum(controls => controls.Count));
        Assert.Equal(4, Field<TabControl>(panel, "_tabControl").ItemCount);
        var shared = fields.First(pair => pair.Value.Count > 1);
        var save = JsonObject.Parse("""{"PlayerStateData":{"Stats":[{"GroupId":"^GLOBAL_STATS","Stats":[]}]}}""");
        var entry = new JsonObject(); entry.Set("Id", shared.Key); entry.Set("Value", JsonObject.Parse("""{"IntValue":5,"FloatValue":5.0}"""));
        save.GetObject("PlayerStateData")!.GetArray("Stats")!.GetObject(0).GetArray("Stats")!.Add(entry);
        panel.LoadData(save);
        shared.Value[0].Text = "123";
        Assert.True(shared.Value[0].TryCommit());
        Assert.All(shared.Value, field => Assert.Equal(123, field.NumericValue));
        panel.SaveData(save); Assert.Equal(123, entry.GetObject("Value")!.GetInt("IntValue"));
    }

    [AvaloniaFact]
    public async Task BookmarkNavigation_RestoresIsolatedPathWithoutChangingJson()
    {
        string mode = AppConfig.Instance.RawJsonViewMode;
        bool isolate = AppConfig.Instance.RawJsonIsolateNode;
        try
        {
            var save = JsonObject.Parse("""{"PlayerStateData":{"Nested":{"Value":7}},"Sibling":9}""");
            string before = save.ToString(); var panel = new RawJsonPanel(); panel.LoadData(save);
            await (Task)Invoke(panel, "NavigateToBookmarkAsync", new RawJsonBookmark { Name = "Value", Path = ["PlayerStateData", "Nested"], Isolated = true })!;
            Assert.True(panel.FindControl<Grid>("SplitView")!.IsVisible);
            Assert.Contains("Value", panel.FindControl<TextBox>("SplitTextEditor")!.Text);
            Assert.DoesNotContain("Sibling", panel.FindControl<TextBox>("SplitTextEditor")!.Text);
            Assert.Equal(before, save.ToString());
        }
        finally
        {
            AppConfig.Instance.RawJsonViewMode = mode;
            AppConfig.Instance.RawJsonIsolateNode = isolate;
            AppConfig.Instance.Save();
        }
    }

    [AvaloniaFact]
    public void Bookmarks_AreScopedByDocumentPathAndAccountType()
    {
        var panel = new RawJsonPanel(); panel.LoadData(new JsonObject());
        panel.SetSaveFilePath("/tmp/Forsaken-save.hg");
        string first = (string)panel.GetType().GetProperty("BookmarkScope", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
        panel.SetSaveFilePath("/tmp/Forsaken-Save.hg");
        string second = (string)panel.GetType().GetProperty("BookmarkScope", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
        if (!OperatingSystem.IsWindows()) Assert.NotEqual(first, second);
        Assert.StartsWith("save-", first);
        panel.SetAccountData(new JsonObject(), "/tmp/Forsaken-save.hg");
        panel.FindControl<ComboBox>("DocumentSelector")!.SelectedIndex = 1;
        string account = (string)panel.GetType().GetProperty("BookmarkScope", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
        Assert.StartsWith("account-", account); Assert.NotEqual(first, account);
    }

    [AvaloniaFact]
    public void UnsavedBookmarks_StaySeparateBetweenSaveAndAccount()
    {
        var panel = new RawJsonPanel(); panel.LoadData(JsonObject.Parse("""{"Save":1}"""));
        Field<List<RawJsonBookmark>>(panel, "_bookmarks").Add(new RawJsonBookmark { Name = "Save", Path = ["Save"] });
        Invoke(panel, "PersistBookmarks");
        panel.SetAccountData(JsonObject.Parse("""{"Account":2}"""));
        var selector = panel.FindControl<ComboBox>("DocumentSelector")!;
        selector.SelectedIndex = 1;
        Assert.Empty(panel.FindControl<ComboBox>("BookmarkSelector")!.Items);
        Field<List<RawJsonBookmark>>(panel, "_bookmarks").Add(new RawJsonBookmark { Name = "Account", Path = ["Account"] });
        Invoke(panel, "PersistBookmarks");
        selector.SelectedIndex = 0;
        Assert.Equal("Save", Assert.Single(panel.FindControl<ComboBox>("BookmarkSelector")!.Items)!.ToString());
    }

    [AvaloniaFact]
    public async Task ProductCompletion_UpdatesSpecialsAndAccountWithoutSavingSubstancesAsProducts()
    {
        var items = new GameItemDatabase();
        items.LoadItemsFromJsonDirectory(Path.Combine(ApplicationPaths.Current.ResourcesDirectory, "json"));
        var panel = new CataloguePanel(items, new RecipeDatabase(), new WordDatabase(), null) { ConfirmAsync = (_, _, _) => Task.FromResult(true) };
        var save = JsonObject.Parse("""{"PlayerStateData":{"KnownProducts":[],"KnownSpecials":[],"KnownTech":[]}}""");
        var account = JsonObject.Parse("""{"UserSettingsData":{}}""");
        panel.LoadData(save); panel.SetAccountData(account);
        await (Task)Invoke(panel, "CompleteItemsAsync", CatalogueTransferKind.Products)!;
        panel.SaveData(save);
        var player = save.GetObject("PlayerStateData")!;
        var products = player.GetArray("KnownProducts")!;
        var ids = Enumerable.Range(0, products.Length).Select(i => products.GetString(i)!).ToArray();
        Assert.True(ids.Length > 100);
        Assert.DoesNotContain(ids, id => items.GetItem(id)?.SourceTable == "Substance");
        var specials = player.GetArray("KnownSpecials")!;
        var specialIds = Enumerable.Range(0, specials.Length).Select(i => specials.GetString(i)!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.All(ids.Where(id => items.GetItem(id)?.TradeCategory == "SpecialShop"), id => Assert.Contains(id, specialIds));
        Assert.True(account.GetObject("UserSettingsData")!.GetArray("SeenProducts")!.Length > 100);
        Assert.True(account.GetObject("UserSettingsData")!.GetArray("SeenSubstances")!.Length > 0);
    }

    [AvaloniaFact]
    public void HotkeyLabels_MatchGameSlotZeroThroughNine()
    {
        var panel = new HotkeysPanel();
        var labels = panel.GetLogicalDescendants().OfType<TextBlock>().Select(text => text.Text).Where(text => text?.StartsWith("Ctrl+", StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(30, labels.Length);
        Assert.Equal(Enumerable.Range(0, 10).Select(i => $"Ctrl+{i}"), labels.Take(10));
    }

    [AvaloniaFact]
    public void CompanionLists_AreSeparateAndLockedPetsStayFixedDuringSorting()
    {
        var save = JsonObject.Parse("""{"PlayerStateData":{"Pets":[{"CustomName":"Zulu","CreatureSeed":[true,"0x1"]},{"CustomName":"Alpha","CreatureSeed":[true,"0x2"]},{"CustomName":"Locked","CreatureSeed":[true,"0x3"]}],"UnlockedPetSlots":[true,true,false],"PetAccessoryCustomisation":[{"Marker":1},{"Marker":2},{"Marker":3}],"PetBattleTeam":{"TeamMembers":[{"PetIndex":0},{"PetIndex":1}]},"Eggs":[{"CustomName":"Egg","CreatureSeed":[true,"0x4"]}]}}""");
        var desktop = new CompanionDesktopActions(_ => Task.FromResult<string?>(null), _ => Task.FromResult<string?>(null), (_, _) => Task.FromResult(true), (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.FromResult<global::Avalonia.Media.Color?>(null), (_, _) => Task.FromResult<int?>(null));
        var panel = new CompanionPanel(desktop); panel.LoadData(save);
        Assert.Equal(3, panel.CompanionList.ItemCount); Assert.Equal(1, panel.EggList.ItemCount);
        Invoke(panel, "ReorderList", true, false, 0);
        var player = save.GetObject("PlayerStateData")!; var pets = player.GetArray("Pets")!;
        Assert.Equal("Alpha", pets.GetObject(0).GetString("CustomName"));
        Assert.Equal("Locked", pets.GetObject(2).GetString("CustomName"));
        Assert.Equal(2, player.GetArray("PetAccessoryCustomisation")!.GetObject(0).GetInt("Marker"));
        Assert.Equal(1, player.GetObject("PetBattleTeam")!.GetArray("TeamMembers")!.GetObject(0).GetInt("PetIndex"));
    }
}
