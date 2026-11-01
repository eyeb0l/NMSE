using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NMSE.Core;
using NMSE.Config;
using NMSE.Data;
using NMSE.IO;
using NMSE.Models;
using NMSE.UI.Avalonia.Coordination;
using NMSE.UI.Avalonia.Panels.AccountRewards;
using NMSE.UI.Avalonia.Panels.RawJson;
using NMSE.UI.Avalonia.Panels.Starships;
using NMSE.UI.Avalonia.Panels.Exocraft;
using NMSE.UI.Avalonia.Views;
using Xunit;

namespace NMSE.Avalonia.Tests;

public sealed class Cosmos704PortTests
{
    private static string[] Values(JsonArray? array) => array is null ? [] : Enumerable.Range(0, array.Length).Select(i => array.GetString(i)!).ToArray();
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    [AvaloniaFact]
    public async Task ShipTransfer_PreservesLegacyFlagOutsideShipObject()
    {
        var save = JsonObject.Parse("""
            {"PlayerStateData":{"PrimaryShip":0,"ShipUsesLegacyColours":[true,false],"ShipOwnership":[
              {"Name":"Fighter","Resource":{"Filename":"MODELS/SPACECRAFT/FIGHTERS/FIGHTER_PROC.SCENE.MBIN","Seed":[true,"0x1"]},"Inventory":{"Class":{"InventoryClass":"C"},"BaseStatValues":[],"Slots":[],"ValidSlotIndices":[]},"Inventory_TechOnly":{"Slots":[],"ValidSlotIndices":[]}},
              {"Name":"","Resource":{"Filename":"","Seed":[false,"0x0"]},"Inventory":{"Class":{"InventoryClass":"C"},"BaseStatValues":[],"Slots":[],"ValidSlotIndices":[]},"Inventory_TechOnly":{"Slots":[],"ValidSlotIndices":[]}}
            ]}}
            """);
        var panel = new StarshipPanel(new GameItemDatabase(), null);
        JsonObject? exported = null;
        panel.ExportShipAsync = (value, _) => { exported = value; return Task.CompletedTask; };
        panel.LoadData(save);
        Assert.True(await panel.ExportSelectedAsync());
        Assert.True(exported!.GetBool("UsesLegacyColours"));
        panel.ImportShipDataAsync = _ => Task.FromResult<StarshipImportPayload?>(new(exported.GetObject("Ship")!, UsesLegacyColours: true));
        Assert.True(await panel.ImportShipAsync());
        var player = save.GetObject("PlayerStateData")!;
        Assert.True(player.GetArray("ShipUsesLegacyColours")!.Get(1) is true);
        Assert.False(player.GetArray("ShipOwnership")!.GetObject(1).Contains("UsesLegacyColours"));
    }

    [AvaloniaFact]
    public void ExocraftSwitch_CommitsNameToPreviousVehicle()
    {
        var panel = new ExocraftPanel(null, null, new(_ => Task.FromResult<string?>(null), _ => Task.FromResult<string?>(null),
            (_, _) => Task.FromResult(true), (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask));
        var save = JsonObject.Parse("""
            {"PlayerStateData":{"VehicleOwnership":[{"Name":"First","Inventory":{"Slots":[],"ValidSlotIndices":[]}},{"Name":"Second","Inventory":{"Slots":[],"ValidSlotIndices":[]}}]}}
            """);
        panel.LoadData(save);
        Field<TextBox>(panel, "_name").Text = "Edited first";
        Field<ComboBox>(panel, "_vehicleSelector").SelectedIndex = 1;
        Assert.Equal("Edited first", save.GetObject("PlayerStateData")!.GetArray("VehicleOwnership")!.GetObject(0).GetString("Name"));
        Assert.Equal("Second", save.GetObject("PlayerStateData")!.GetArray("VehicleOwnership")!.GetObject(1).GetString("Name"));
    }

    [AvaloniaFact]
    public void SplitIsolatedEdit_ReplacesMemberAndPreservesSiblings()
    {
        string mode = AppConfig.Instance.RawJsonViewMode;
        bool isolate = AppConfig.Instance.RawJsonIsolateNode;
        try
        {
            AppConfig.Instance.RawJsonViewMode = "split"; AppConfig.Instance.RawJsonIsolateNode = true;
            var save = JsonObject.Parse("{\"First\":1,\"Object\":{\"Nested\":2},\"Last\":3}");
            var panel = new RawJsonPanel(); panel.LoadData(save);
            var tree = panel.FindControl<TreeView>("SplitTree")!;
            var root = tree.Items.Cast<JsonTreeNode>().Single(); root.IsExpanded = true;
            tree.SelectedItem = root.Children.Single(node => node.Key == "Object");
            Dispatcher.UIThread.RunJobs();
            panel.FindControl<TextBox>("SplitTextEditor")!.Text = "\"Renamed\": {\"Nested\":4}";
            Dispatcher.UIThread.RunJobs();
            panel.SaveData(save);
            Assert.False(save.Contains("Object"));
            Assert.Equal(4, save.GetObject("Renamed")!.GetInt("Nested"));
            Assert.Equal(3, save.GetInt("Last"));
        }
        finally { AppConfig.Instance.RawJsonViewMode = mode; AppConfig.Instance.RawJsonIsolateNode = isolate; }
    }
    [AvaloniaFact]
    public void XboxDiscoveryAndSession_LoadAndWriteXgsWithoutContainersIndex()
    {
        string root = Path.Combine(Path.GetTempPath(), "NMSE-XGS-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            string directory = Path.Combine(root, "Slot1Manual");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "data"), "{\"PlayerStateData\":{\"Units\":123}}");
            var discover = typeof(MainWindow).GetMethod("DiscoverXboxSlots", BindingFlags.Static | BindingFlags.NonPublic)!;
            var slots = (System.Collections.ICollection)discover.Invoke(null, [root])!;
            Assert.Single(slots.Cast<object>());
            var session = SaveSession.ForXbox(root, "Slot1Manual");
            var save = session.Load(); save.GetObject("PlayerStateData")!.Set("Units", 456);
            session.Save(save);
            Assert.Equal(456, session.Load().GetObject("PlayerStateData")!.GetInt("Units"));
            Assert.False(File.Exists(Path.Combine(root, "containers.index")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public void AccountCommit_KeepsEntitlementProductsInSpecialsAndTechnologies()
    {
        var resources = new AvaloniaResourceCatalog();
        resources.Items.LoadItemsFromJsonDirectory(Path.Combine(ApplicationPaths.Current.ResourcesDirectory, "json"));
        RewardDatabase.LoadFromJsonDirectory(Path.Combine(ApplicationPaths.Current.ResourcesDirectory, "json"));
        var panel = new AccountRewardsPanel(resources, new(_ => Task.FromResult<string?>(null), (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask));
        var entitlement = RewardDatabase.EntitlementRewards.First(reward => resources.Items.GetItem(reward.ProductId)?.SourceTable == "Technology");
        string product = "^" + entitlement.ProductId.TrimStart('^');
        var account = JsonObject.Parse("{\"UserSettingsData\":{\"UnlockedSpecials\":[\"^UNMANAGED\"],\"SeenTechnologies\":[],\"UnlockedPlatformRewards\":[]}}");
        account.GetObject("UserSettingsData")!.GetArray("UnlockedSpecials")!.Add(product);
        panel.SetPlatform(SaveFileManager.Platform.XboxGamePass);
        panel.SetAccountData(account);
        var save = JsonObject.Parse("{\"PlayerStateData\":{\"KnownTech\":[],\"KnownSpecials\":[],\"KnownProducts\":[]}}");
        panel.LoadData(save); panel.SaveData(save);
        var settings = account.GetObject("UserSettingsData")!;
        Assert.Contains(product, Values(settings.GetArray("UnlockedSpecials")));
        Assert.Contains("^UNMANAGED", Values(settings.GetArray("UnlockedSpecials")));
        Assert.Contains(product, Values(settings.GetArray("SeenTechnologies")));
        Assert.DoesNotContain(entitlement.Id, Values(settings.GetArray("UnlockedPlatformRewards")));
    }

    [AvaloniaFact]
    public void RawJson_RestoresSplitIsolationAndBlocksInvalidAccountText()
    {
        string mode = AppConfig.Instance.RawJsonViewMode;
        bool isolate = AppConfig.Instance.RawJsonIsolateNode;
        try
        {
            AppConfig.Instance.RawJsonViewMode = "split"; AppConfig.Instance.RawJsonIsolateNode = true;
            var panel = new RawJsonPanel();
            panel.LoadData(JsonObject.Parse("{\"A\":1}"));
            Assert.True(panel.FindControl<CheckBox>("IsolateCheck")!.IsChecked);
            Assert.True(panel.FindControl<Grid>("SplitView")!.IsVisible);
            panel.SetAccountData(JsonObject.Parse("{\"UserSettingsData\":{}}"));
            panel.FindControl<ComboBox>("DocumentSelector")!.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            panel.FindControl<TextBox>("SplitTextEditor")!.Text = "{invalid";
            Dispatcher.UIThread.RunJobs();
            Assert.Throws<JsonException>(() => panel.GetEditedAccountData());
        }
        finally { AppConfig.Instance.RawJsonViewMode = mode; AppConfig.Instance.RawJsonIsolateNode = isolate; }
    }

    [Fact]
    public void ExternalMonitor_DetectsAccountChangesDeletionAndCaseDistinctPaths()
    {
        string root = Path.Combine(Path.GetTempPath(), "NMSE-monitor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string save = Path.Combine(root, "save.hg"), account = Path.Combine(root, "GcUserSettingsData.mxml");
            File.WriteAllText(save, "save"); File.WriteAllText(account, "account");
            var monitor = new ExternalFileMonitor(); monitor.Capture([save, account]);
            Assert.Equal((false, false), monitor.Check());
            File.WriteAllText(account, "updated account");
            Assert.Equal((true, false), monitor.Check());
            Assert.Equal((false, false), monitor.Check());
            File.Delete(save);
            Assert.Equal((true, true), monitor.Check());
            if (!OperatingSystem.IsWindows())
            {
                string upper = Path.Combine(root, "Save.hg");
                monitor.Capture([save, upper]);
                File.WriteAllText(upper, "case-distinct file");
                Assert.Equal((true, false), monitor.Check());
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
