using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Coordination;
using NMSE.UI.Avalonia.Panels.AccountRewards;
using NMSE.UI.Avalonia.Panels.Catalogue;
using NMSE.UI.Avalonia.Panels.Milestones;
using Xunit;

namespace NMSE.Avalonia.Tests;

public sealed class CatalogueUnlockPortTests
{
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static object? Invoke(object owner, string name, params object?[] args) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, args);
    private static string[] Values(JsonObject owner, string key)
    {
        var array = owner.GetArray(key);
        return array == null ? [] : Enumerable.Range(0, array.Length).Select(i => array.GetString(i)!).ToArray();
    }
    private static (CataloguePanel Panel, JsonObject Save, JsonObject Account) Create(string player = "{}", bool account = true)
    {
        var items = new GameItemDatabase();
        items.LoadItemsFromJsonDirectory(Path.Combine(ApplicationPaths.Current.ResourcesDirectory, "json"));
        var panel = new CataloguePanel(items, new RecipeDatabase(), new WordDatabase(), null)
        { ConfirmAsync = (_, _, _) => Task.FromResult(true) };
        var save = JsonObject.Parse("{\"PlayerStateData\":" + player + "}");
        var settings = JsonObject.Parse("""{"UserSettingsData":{}}""");
        panel.LoadData(save);
        if (account) panel.SetAccountData(settings);
        return (panel, save, settings);
    }
    private static CatalogueItemRow Row(CataloguePanel panel, string field, string id) => Field<IEnumerable<CatalogueItemRow>>(panel, field).Single(row => row.Id == id);

    [AvaloniaFact]
    public void KnownTechnologyAndSeenAreIndependent_ExportIncludesOnlyKnown()
    {
        var (panel, save, account) = Create();
        var row = Row(panel, "_techRows", "HYPERDRIVE");
        int changes = 0; panel.DataModified += (_, _) => changes++;
        row.Known = true;
        Assert.True(row.Seen);
        Assert.Contains("^HYPERDRIVE", Values(save.GetObject("PlayerStateData")!, "KnownTech"));
        row.Known = false;
        Assert.True(row.Seen);
        row.Seen = false;
        Assert.Empty(Values(account.GetObject("UserSettingsData")!, "SeenTechnologies"));
        row.Known = true;
        var exported = (JsonObject)Invoke(panel, "BuildExport", CatalogueTransferKind.Technologies)!;
        Assert.Equal(new[] { "HYPERDRIVE" }, Values(exported, "KnownTechnologies"));
        Assert.Equal(4, changes);
        panel.SaveData(save);
        Assert.Single(Values(save.GetObject("PlayerStateData")!, "KnownTech"));
    }

    [AvaloniaFact]
    public async Task RenderedKnownCheckboxWritesSaveAndRefreshesSeen()
    {
        var (panel, save, _) = Create();
        Field<TextBox>(panel, "_techFilter").Text = "HYPERDRIVE";
        var window = new Window { Width = 2200, Height = 700, Content = panel };
        try
        {
            window.Show();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            window.UpdateLayout();
            var grid = Field<DataGrid>(panel, "_techGrid");
            var check = Assert.Single(grid.GetVisualDescendants().OfType<CheckBox>(),
                check => check.DataContext is CatalogueItemRow row && row.Id == "HYPERDRIVE");
            Assert.True(check.IsEnabled);
            check.IsChecked = true;
            Assert.Contains("^HYPERDRIVE", Values(save.GetObject("PlayerStateData")!, "KnownTech"));
            Assert.True(Row(panel, "_techRows", "HYPERDRIVE").Seen);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SpecialBuildingsRefreshBothTabs_AndLegacySpecialProductsCannotBeReadded()
    {
        var (panel, save, account) = Create("""{"KnownProducts":["^JETS_WORM"]}""");
        var special = Row(panel, "_specialRows", "EXPD_FLAG_06E");
        var product = Row(panel, "_productRows", "EXPD_FLAG_06E");
        special.Known = true;
        Assert.True(product.Known);
        Assert.True(product.Seen);
        Assert.Contains("EXPD_FLAG_06E", Values(account.GetObject("UserSettingsData")!, "UnlockedSpecials"));
        product.Known = false;
        Assert.Null(special.Known);
        special.Known = false;
        Assert.DoesNotContain("^EXPD_FLAG_06E", Values(save.GetObject("PlayerStateData")!, "KnownSpecials"));
        var legacy = Row(panel, "_productRows", "JETS_WORM");
        Assert.False(legacy.CanAddKnown);
        legacy.Known = false;
        legacy.Known = true;
        Assert.False(legacy.Known);
        Assert.Empty(Values(save.GetObject("PlayerStateData")!, "KnownProducts"));
    }

    [AvaloniaFact]
    public async Task BulkActionsRespectTextAndCatalogueFilters_AndKeepSeenOnRemoval()
    {
        var (panel, save, _) = Create();
        var filter = Field<TextBox>(panel, "_productFilter");
        filter.Text = "GRAV_FLOOR";
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.Single(Field<IEnumerable<CatalogueItemRow>>(panel, "_visibleProductRows"));
        var catalogueOnly = Field<Dictionary<CatalogueTransferKind, CheckBox>>(panel, "_catalogueOnly")[CatalogueTransferKind.Products];
        catalogueOnly.IsChecked = true;
        Assert.Empty(Field<IEnumerable<CatalogueItemRow>>(panel, "_visibleProductRows"));
        await (Task)Invoke(panel, "SetVisibleItemsAsync", CatalogueTransferKind.Products, true)!;
        Assert.Empty(Values(save.GetObject("PlayerStateData")!, "KnownProducts"));
        catalogueOnly.IsChecked = false;
        await (Task)Invoke(panel, "SetVisibleItemsAsync", CatalogueTransferKind.Products, true)!;
        Assert.Equal(new[] { "^GRAV_FLOOR" }, Values(save.GetObject("PlayerStateData")!, "KnownProducts"));
        await (Task)Invoke(panel, "SetVisibleItemsAsync", CatalogueTransferKind.Products, false)!;
        Assert.False(Row(panel, "_productRows", "GRAV_FLOOR").Known);
        Assert.True(Row(panel, "_productRows", "GRAV_FLOOR").Seen);
    }

    [AvaloniaFact]
    public async Task MissingAccountAndCancelledBulkActionDoNotWriteSeenOrKnown()
    {
        var (panel, save, _) = Create(account: false);
        var row = Row(panel, "_techRows", "HYPERDRIVE");
        row.Seen = true;
        Assert.False(row.HasAccount);
        Assert.False(row.Seen);
        Field<TextBox>(panel, "_techFilter").Text = "HYPERDRIVE";
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        panel.ConfirmAsync = (_, _, _) => Task.FromResult(false);
        await (Task)Invoke(panel, "SetVisibleItemsAsync", CatalogueTransferKind.Technologies, true)!;
        Assert.Empty(Values(save.GetObject("PlayerStateData")!, "KnownTech"));
        var showSeen = Field<Dictionary<CatalogueTransferKind, CheckBox>>(panel, "_showSeen")[CatalogueTransferKind.Technologies];
        var grid = Field<DataGrid>(panel, "_techGrid");
        Assert.False(grid.Columns[8].IsVisible);
        showSeen.IsChecked = true;
        Assert.True(grid.Columns[8].IsVisible);
    }

    [AvaloniaFact]
    public void ImportUsesCanonicalRules_AndPurgeDoesNotEraseKnownArrays()
    {
        var (panel, save, account) = Create();
        var imported = JsonObject.Parse("""{"KnownProducts":["GRUB_HAT","BASE_AQUARIUM","NOT_A_REAL_ITEM"]}""");
        Assert.Equal(1, Invoke(panel, "ImportItems", imported, CatalogueTransferKind.Products));
        Assert.True(Row(panel, "_productRows", "BASE_AQUARIUM").Seen);
        Assert.Equal(new[] { "^BASE_AQUARIUM" }, Values(save.GetObject("PlayerStateData")!, "KnownProducts"));
        Row(panel, "_productRows", "BASE_AQUARIUM").Seen = false;
        Assert.Equal(0, Invoke(panel, "ImportItems", imported, CatalogueTransferKind.Products));
        Assert.True(Row(panel, "_productRows", "BASE_AQUARIUM").Seen);
        panel.PurgeData();
        panel.SaveData(save);
        Assert.Equal(0, panel.LoadedRowCount);
        Assert.Equal(new[] { "^BASE_AQUARIUM" }, Values(save.GetObject("PlayerStateData")!, "KnownProducts"));
        Assert.Contains("^BASE_AQUARIUM", Values(account.GetObject("UserSettingsData")!, "SeenProducts"));
    }

    [AvaloniaFact]
    public async Task CompleteWithoutAccount_DoesNotPromptOrModifyAgain()
    {
        var (panel, save, _) = Create(account: false);
        int prompts = 0, changes = 0;
        panel.ConfirmAsync = (_, _, _) => { prompts++; return Task.FromResult(true); };
        panel.DataModified += (_, _) => changes++;
        await (Task)Invoke(panel, "CompleteItemsAsync", CatalogueTransferKind.Specials)!;
        string completed = save.ToString();
        Assert.Equal(1, prompts);
        Assert.Equal(1, changes);

        await (Task)Invoke(panel, "CompleteItemsAsync", CatalogueTransferKind.Specials)!;

        Assert.Equal(1, prompts);
        Assert.Equal(1, changes);
        Assert.Equal(completed, save.ToString());
        Assert.DoesNotContain("^JETS_WORM", Values(save.GetObject("PlayerStateData")!, "KnownProducts"));
    }

    [AvaloniaFact]
    public void ExportSpecials_PreservesPartialEntriesFromPrimaryArray()
    {
        var (panel, save, _) = Create("""{"KnownSpecials":["^EXPD_FLAG_06E","^LEGACY_SPECIAL"]}""");
        Assert.Null(Row(panel, "_specialRows", "EXPD_FLAG_06E").Known);
        Field<TextBox>(panel, "_specialFilter").Text = "nothing matches";
        string before = save.ToString();
        var exported = (JsonObject)Invoke(panel, "BuildExport", CatalogueTransferKind.Specials)!;
        Assert.Equal(new[] { "EXPD_FLAG_06E", "LEGACY_SPECIAL" }, Values(exported, "KnownSpecials"));
        Assert.Equal(before, save.ToString());

        var (importedPanel, importedSave, _) = Create();
        Assert.Equal(1, Invoke(importedPanel, "ImportItems", exported, CatalogueTransferKind.Specials));
        Assert.Contains("^EXPD_FLAG_06E", Values(importedSave.GetObject("PlayerStateData")!, "KnownSpecials"));
        Assert.Contains("^EXPD_FLAG_06E", Values(importedSave.GetObject("PlayerStateData")!, "KnownProducts"));

        var (productOnly, _, _) = Create("""{"KnownProducts":["^EXPD_FLAG_06E"]}""");
        Assert.Null(Row(productOnly, "_specialRows", "EXPD_FLAG_06E").Known);
        var noSpecials = (JsonObject)Invoke(productOnly, "BuildExport", CatalogueTransferKind.Specials)!;
        Assert.Empty(Values(noSpecials, "KnownSpecials"));
    }

    [AvaloniaFact]
    public void RewardCanBeRedeemedAndUnredeemedAcrossSavesInOneSession()
    {
        using var resources = new AvaloniaResourceCatalog();
        resources.Items.LoadItemsFromJsonDirectory(Path.Combine(ApplicationPaths.Current.ResourcesDirectory, "json"));
        var panel = new AccountRewardsPanel(resources, new AccountRewardsDesktopActions(
            _ => Task.FromResult<string?>(null), (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask));
        var save = JsonObject.Parse("""{"PlayerStateData":{}}""");
        panel.SetAccountData(JsonObject.Parse("""{"UserSettingsData":{}}"""));
        panel.LoadData(save);
        var tab = Field<object>(panel, "_seasonTab");
        var rows = (IEnumerable)tab.GetType().GetProperty("AllRows")!.GetValue(tab)!;
        var row = rows.Cast<object>().Single(r => (string)r.GetType().GetProperty("Id")!.GetValue(r)! == "^SWARM_GUN");
        var redeemed = row.GetType().GetProperty("Redeemed")!;
        redeemed.SetValue(row, true);
        panel.SaveData(save);
        Assert.Contains("^SWARM_GUN", Values(save.GetObject("PlayerStateData")!, "KnownSpecials"));
        redeemed.SetValue(row, false);
        panel.SaveData(save);
        Assert.DoesNotContain("^SWARM_GUN", Values(save.GetObject("PlayerStateData")!, "KnownSpecials"));
        Assert.DoesNotContain("^SWARM_GUN", Values(save.GetObject("PlayerStateData")!, "RedeemedSeasonRewards"));
    }

    [AvaloniaFact]
    public void MilestoneLabelsKeepInheritedDarkThemeAfterLocalisation()
    {
        using var resources = new AvaloniaResourceCatalog();
        var panel = new MilestonePanel(resources);
        var window = new Window { RequestedThemeVariant = ThemeVariant.Dark, Content = panel };
        try
        {
            window.Show();
            panel.ApplyUiLocalisation();
            var labels = panel.GetLogicalDescendants().OfType<TextBlock>().Where(t => !string.IsNullOrWhiteSpace(t.Text)).ToArray();
            Assert.NotEmpty(labels);
            Assert.All(labels, label =>
            {
                Assert.Equal(ThemeVariant.Dark, label.ActualThemeVariant);
                Assert.False(label.IsSet(TextBlock.ForegroundProperty));
                Assert.NotEqual(Colors.Black, (label.Foreground as ISolidColorBrush)?.Color);
            });
        }
        finally { window.Close(); }
    }
}
