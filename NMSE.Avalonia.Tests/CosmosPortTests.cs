using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NMSE.UI.Avalonia.Coordination;
using NMSE.UI.Avalonia.Views;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Panels.BasesStorage;
using NMSE.UI.Avalonia.Panels.Catalogue;
using NMSE.UI.Avalonia.Panels.Multitools;
using NMSE.UI.Avalonia.Panels.Player;
using NMSE.UI.Avalonia.Panels.RawJson;
using Xunit;

namespace NMSE.Avalonia.Tests;

public sealed class CosmosPortTests
{
    [AvaloniaFact]
    public void DifficultyPreset_UpdatesModeOnlyAfterUserChangesPreset()
    {
        var save = JsonObject.Parse("""{"BaseContext":{"GameMode":3},"PlayerStateData":{"DifficultyState":{"Preset":{"DifficultyPresetType":"Normal"}}}}""");
        var panel = new PlayerPanel();
        panel.LoadData(save);
        panel.SaveData(save);
        Assert.Equal(3, save.GetObject("BaseContext")!.GetInt("GameMode"));
        var combo = Field<ComboBox>(panel, "_currentPreset");
        var presets = (string[])typeof(PlayerPanel).GetField("DifficultyPresets", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        combo.SelectedIndex = Array.IndexOf(presets, "Creative");
        panel.SaveData(save);
        Assert.Equal(4, save.GetObject("BaseContext")!.GetInt("GameMode"));
    }

    [AvaloniaFact]
    public void Multitool_UnchangedUnknownModelSurvivesCommit()
    {
        const string filename = "MODELS/FUTURE/UNKNOWN.SCENE.MBIN";
        var save = JsonObject.Parse("""{"PlayerStateData":{"ActiveMultioolIndex":0,"Multitools":[{"Name":"Future","Resource":{"Filename":"MODELS/FUTURE/UNKNOWN.SCENE.MBIN","Seed":[true,"0x123"]},"Store":{"Class":{"InventoryClass":"C"},"BaseStatValues":[],"Slots":[],"ValidSlotIndices":[]}}]}}""");
        var panel = new MultitoolPanel(new GameItemDatabase(), null);
        panel.LoadData(save); panel.SaveData(save);
        Assert.Equal(filename, save.GetObject("PlayerStateData")!.GetArray("Multitools")!.GetObject(0).GetObject("Resource")!.GetString("Filename"));
    }

    [AvaloniaFact]
    public void CosmosBases_AppearAndDisablePlanetaryTerrainActions()
    {
        var save = JsonObject.Parse("""{"PlayerStateData":{"PersistentPlayerBases":[{"Name":"Station","BaseType":{"PersistentBaseTypes":"PlayerSpaceStationBase"},"BaseVersion":3,"Objects":[]},{"Name":"Asteroid","BaseType":{"PersistentBaseTypes":"PlayerSpaceBase"},"BaseVersion":3,"Objects":[]},{"Name":"Corvette","BaseType":{"PersistentBaseTypes":"PlayerShipBase"},"BaseVersion":3,"Objects":[]}]}}""");
        var panel = new BasesEditorPanel(); panel.LoadData(save);
        var list = Field<ListBox>(panel, "_baseList");
        Assert.Equal(3, list.ItemCount);
        for (int i = 0; i < 3; i++)
        {
            list.SelectedIndex = i;
            Assert.False(Field<Button>(panel, "_clearTerrain").IsEnabled);
            Assert.False(Field<Button>(panel, "_moveComputer").IsEnabled);
        }
        Assert.True(Field<TextBox>(panel, "_name").IsReadOnly);
    }

    [AvaloniaFact]
    public void IsolatedJsonMember_RenamesAndUndoesWithoutLosingSiblings()
    {
        var save = JsonObject.Parse("""{"First":1,"Object":{"Nested":[1,2]},"Last":3}""");
        var panel = new RawJsonPanel(); panel.LoadData(save);
        var root = JsonTreeNode.CreateRoot(save); root.IsExpanded = true;
        var node = root.Children.Single(x => x.Key == "Object");
        Invoke(panel, "ApplyNodeSnippet", node, """ "Renamed": {"Nested":[4,5]} """);
        Assert.False(save.Contains("Object"));
        Assert.Equal(4, save.GetObject("Renamed")!.GetArray("Nested")!.GetInt(0));
        Assert.Equal(3, save.GetInt("Last"));
        Invoke(panel, "OnUndo", null, new global::Avalonia.Interactivity.RoutedEventArgs());
        Assert.True(save.Contains("Object")); Assert.False(save.Contains("Renamed"));
        Assert.Equal(1, save.GetObject("Object")!.GetArray("Nested")!.GetInt(0));
    }

    [AvaloniaFact]
    public async Task DatabaseSearch_IsAvailableWithoutSaveAndMatchesWildcards()
    {
        var db = new GameItemDatabase();
        var items = (Dictionary<string, GameItem>)typeof(GameItemDatabase).GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(db)!;
        items["TEST"] = new GameItem { Id = "TEST", Name = "Cosmos Test", SourceTable = "Technology" };
        var panel = new DatabaseSearchPanel(db);
        Field<TextBox>(panel, "_query").Text = "Cos* TEST";
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.Single(Field<DataGrid>(panel, "_grid").ItemsSource.Cast<object>());
        Field<TextBox>(panel, "_query").Text = "absent";
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.Empty(Field<DataGrid>(panel, "_grid").ItemsSource.Cast<object>());
    }

    [AvaloniaFact]
    public void SwitchSlots_ExcludeSettingsAndUseSwitchWriter()
    {
        string directory = Path.Combine(Path.GetTempPath(), "NMSE Switch " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "manifestaccountdata.hg"), []);
            File.WriteAllText(Path.Combine(directory, "savedata00.hg"), "{}");
            File.WriteAllText(Path.Combine(directory, "savedata02.hg"), "{}");
            File.WriteAllText(Path.Combine(directory, "savedata03.hg"), "{}");
            var slots = (System.Collections.IEnumerable)typeof(MainWindow).GetMethod("DiscoverSaveSlots", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [directory])!;
            object slot = Assert.Single(slots.Cast<object>());
            Assert.StartsWith("Switch: Slot 1", slot.ToString());
            var session = SaveSession.ForFile(Path.Combine(directory, "savedata02.hg"));
            Assert.Equal(NMSE.IO.SaveFileManager.Platform.Switch, session.Platform);
            Assert.Null(session.AccountPath);
        }
        finally { Directory.Delete(directory, true); }
    }

    [AvaloniaFact]
    public void StationPanel_LoadDoesNotMutateAndMissionSelectionWritesThrough()
    {
        var save = JsonObject.Parse("""{"PlayerStateData":{"Stats":[],"MissionProgress":[]}}""");
        string before = save.ToString();
        var panel = new SpaceStationPanel();
        int changes = 0; panel.DataModified += (_, _) => changes++;
        panel.LoadData(save);
        Assert.Equal(before, save.ToString()); Assert.Equal(0, changes);
        var missions = Field<Dictionary<string, ComboBox>>(panel, "_missions");
        missions["^CLAIM_STAT_TUT"].SelectedIndex = 2;
        Assert.Equal(1, changes);
        var mission = save.GetObject("PlayerStateData")!.GetArray("MissionProgress")!.GetObject(0);
        Assert.Equal(int.MaxValue, mission.GetInt("Progress"));
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static object? Invoke(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
}
