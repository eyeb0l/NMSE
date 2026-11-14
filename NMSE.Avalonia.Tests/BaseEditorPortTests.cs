using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Controls;
using NMSE.UI.Avalonia.Panels.BasesStorage;
using Xunit;

namespace NMSE.Avalonia.Tests;

public sealed class BaseEditorPortTests
{
    [AvaloniaFact]
    public async Task ExportBase_WithUnusedTerrainPool_WritesSelectedTerrainSidecar()
    {
        var save = CreateSave();
        var player = save.GetObject("PlayerStateData")!;
        player.Set("TerrainEditData", JsonObject.Parse("""
            { "GalacticAddresses": [ "ABC123" ], "BufferSizes": [ 2 ], "Edits": [ 10, 11 ] }
            """));
        var edits = player.GetObject("TerrainEditData")!.GetArray("Edits")!;
        while (edits.Length < 30_000)
            edits.Add(0);
        string directory = Path.Combine(Path.GetTempPath(), "NMSE Base Export " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "Planet Haven.nmsbase");
        bool exported = false;
        try
        {
            var panel = new BasesEditorPanel
            {
                ExportAsync = (request, json, _) =>
                {
                    Assert.Same(player, request.BasePlayerState);
                    typeof(BasesEditorPanel).Assembly.GetType("NMSE.Core.BaseExportLogic")!
                        .GetMethod("WriteExport", BindingFlags.Static | BindingFlags.NonPublic)!
                        .Invoke(null, [request.BasePlayerState, JsonObject.Parse(json), path]);
                    exported = true;
                    return Task.FromResult(true);
                }
            };
            panel.LoadData(save);

            await (Task)panel.GetType().GetMethod("ExportBaseAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, null)!;

            Assert.True(exported);
            Assert.Equal(UiStrings.Get("base.export_success"), GetField<TextBlock>(panel, "_status").Text);
            Assert.Equal("Planet Haven", JsonObject.ImportFromFile(path).GetString("Name"));
            var sidecar = JsonObject.ImportFromFile(path + ".terrain.json");
            Assert.Equal("NMSETerrain", sidecar.GetString("Format"));
            var exportedTerrain = sidecar.GetObject("TerrainEditData")!;
            Assert.Equal(2, exportedTerrain.GetArray("Edits")!.Length);
            Assert.Equal(10, exportedTerrain.GetArray("Edits")!.GetInt(0));
            Assert.Equal(11, exportedTerrain.GetArray("Edits")!.GetInt(1));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [AvaloniaFact]
    public void LoadData_ListsCurrentLegacyAndUnrecognisedBases()
    {
        var panel = new BasesEditorPanel();

        panel.LoadData(CreateSave());

        var list = GetField<ListBox>(panel, "_baseList");
        string[] labels = list.Items.Cast<object>().Select(item => item.ToString()!).ToArray();

        Assert.Equal(new[] { "[0] Planet Haven", "[1] [F] Sky Whale", "[2] Old Home", "[3] [F] Old Freighter", "[4] Settlement Base" }, labels);
    }

    [AvaloniaFact]
    public void SelectingFreighter_UsesFreighterIdentityAndGuardsPlanetOnlyActions()
    {
        var panel = new BasesEditorPanel();
        panel.LoadData(CreateSave());
        var bases = GetField<ListBox>(panel, "_baseList");

        bases.SelectedIndex = 1;

        Assert.Equal("[1] [F] Sky Whale", bases.SelectedItem!.ToString());
        var name = GetField<TextBox>(panel, "_name");
        Assert.Equal("Sky Whale", name.Text);
        Assert.True(name.IsReadOnly);
        Assert.False(GetField<Button>(panel, "_moveComputer").IsEnabled);
        Assert.False(GetField<Button>(panel, "_clearTerrain").IsEnabled);
        Assert.False(GetField<Button>(panel, "_clearAllTerrain").IsEnabled);
        Assert.False(GetField<Button>(panel, "_clearOutsideBases").IsEnabled);

        bases.SelectedIndex = 0;

        Assert.Equal("Planet Haven", name.Text);
        Assert.False(name.IsReadOnly);
        Assert.True(GetField<Button>(panel, "_clearTerrain").IsEnabled);
        Assert.True(GetField<Button>(panel, "_clearAllTerrain").IsEnabled);
        Assert.True(GetField<Button>(panel, "_clearOutsideBases").IsEnabled);
    }

    [AvaloniaFact]
    public async Task ObjectSearch_CanNarrowThenRestoreTheLoadedObjectList()
    {
        var panel = new BasesEditorPanel();
        panel.LoadData(CreateSave());
        var objects = GetField<ListBox>(panel, "_objectList");
        var search = GetField<TextBox>(panel, "_objectSearchBox");
        string[] original = objects.Items.Cast<object>().Select(item => item.ToString()!).ToArray();

        Assert.Equal(3, original.Length);

        search.Text = "beacon";
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        string narrowed = Assert.Single(objects.Items.Cast<object>()).ToString()!;
        Assert.Contains("^BEACON", narrowed, StringComparison.Ordinal);

        search.Text = string.Empty;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        Assert.Equal(original, objects.Items.Cast<object>().Select(item => item.ToString()!).ToArray());
    }

    [AvaloniaFact]
    public async Task ObjectNavigationAndFiltering_DoNotMarkTheSaveModified()
    {
        var panel = new BasesEditorPanel();
        int modified = 0;
        panel.DataModified += (_, _) => modified++;
        panel.LoadData(CreateSave());
        var objects = GetField<ListBox>(panel, "_objectList");
        var search = GetField<TextBox>(panel, "_objectSearchBox");

        objects.SelectedIndex = 2;
        search.Text = "beacon";
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        search.Text = string.Empty;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        Assert.Equal(0, modified);
    }

    [AvaloniaFact]
    public void SaveData_WritesEditableBaseAndObjectFieldsThrough()
    {
        JsonObject save = CreateSave();
        var panel = new BasesEditorPanel();
        panel.LoadData(save);
        var objects = GetField<ListBox>(panel, "_objectList");
        objects.SelectedIndex = 2;

        GetField<TextBox>(panel, "_name").Text = "Renamed Haven";
        GetField<InvariantNumericTextBox>(panel, "_originalBaseVersion").NumericValue = 8;
        GetField<TextBox>(panel, "_rid").Text = "RID-NEW";
        GetField<TextBox>(panel, "_objectId").Text = "^EDITED_BEACON";
        GetField<InvariantNumericTextBox>(panel, "_objectTimestamp").NumericValue = 99.25;
        GetField<InvariantNumericTextBox[]>(panel, "_objectPosition")[0].NumericValue = 123.5;

        panel.SaveData(save);

        JsonObject home = save.GetObject("PlayerStateData")!.GetArray("PersistentPlayerBases")!.GetObject(0);
        JsonObject edited = home.GetArray("Objects")!.GetObject(2);
        Assert.Equal("Renamed Haven", home.GetString("Name"));
        Assert.Equal(8, home.GetDouble("OriginalBaseVersion"));
        Assert.Equal("RID-NEW", home.GetString("RID"));
        Assert.Equal("^EDITED_BEACON", edited.GetString("ObjectID"));
        Assert.Equal(99.25, edited.GetDouble("Timestamp"));
        Assert.Equal(123.5, edited.GetArray("Position")!.GetDouble(0));
    }

    [AvaloniaFact]
    public void SaveData_DoesNotCreateAbsentOptionalAdvancedFieldsWithoutEdits()
    {
        JsonObject save = JsonObject.Parse("""
            {
              "PlayerStateData": {
                "NPCWorkers": [],
                "PersistentPlayerBases": [{
                  "Name": "Minimal Base",
                  "BaseType": { "PersistentBaseTypes": "HomePlanetBase" },
                  "BaseVersion": 3,
                  "Position": [0.0, 0.0, 0.0],
                  "Forward": [0.0, 0.0, 1.0],
                  "Owner": {},
                  "Objects": [{ "ObjectID": "^BEACON" }]
                }]
              }
            }
            """);
        var panel = new BasesEditorPanel();
        panel.LoadData(save);

        panel.SaveData(save);

        JsonObject value = save.GetObject("PlayerStateData")!.GetArray("PersistentPlayerBases")!.GetObject(0);
        Assert.False(value.Contains("RID"));
        Assert.False(value.Contains("AutoPowerSetting"));
        Assert.False(value.Contains("IsReported"));
        Assert.False(value.Contains("IsFeatured"));
    }

    private static T GetField<T>(object target, string name) where T : class =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static JsonObject CreateSave() => JsonObject.Parse("""
        {
          "PlayerStateData": {
            "PlayerFreighterName": "Sky Whale",
            "NPCWorkers": [],
            "PersistentPlayerBases": [
              {
                "Name": "Planet Haven",
                "BaseType": { "PersistentBaseTypes": "HomePlanetBase" },
                "BaseVersion": 3,
                "OriginalBaseVersion": 2,
                "GalacticAddress": "ABC123",
                "Position": [1.0, 2.0, 3.0],
                "Forward": [0.0, 0.0, 1.0],
                "UserData": 4,
                "LastUpdateTimestamp": 5,
                "RID": "RID-OLD",
                "ScreenshotAt": [6.0, 7.0, 8.0],
                "ScreenshotPos": [9.0, 10.0, 11.0],
                "IsReported": false,
                "IsFeatured": false,
                "AutoPowerSetting": { "BaseAutoPowerSetting": "On" },
                "GameMode": { "PresetGameMode": "Normal" },
                "Difficulty": { "DifficultyPreset": { "DifficultyPresetType": "Normal" } },
                "PlatformToken": "ST",
                "Owner": { "LID": "lid", "UID": "uid", "USN": "owner", "PTK": "ST", "TS": 12 },
                "LastEditedById": "editor-id",
                "LastEditedByUsername": "editor-name",
                "Timestamp": 13,
                "RealityIndex": 0,
                "Objects": [
                  {
                    "ObjectID": "^BASE_FLAG", "Timestamp": 1, "UserData": 10,
                    "Position": [0.0, 0.0, 0.0], "Up": [0.0, 1.0, 0.0], "At": [0.0, 0.0, 1.0]
                  },
                  {
                    "ObjectID": "^BUILDSIGNAL", "Timestamp": 2, "UserData": 20,
                    "Position": [10.0, 0.0, 0.0], "Up": [0.0, 1.0, 0.0], "At": [0.0, 0.0, 1.0]
                  },
                  {
                    "ObjectID": "^BEACON", "Timestamp": 3, "UserData": 30,
                    "Position": [20.0, 0.0, 0.0], "Up": [0.0, 1.0, 0.0], "At": [0.0, 0.0, 1.0]
                  }
                ]
              },
              {
                "Name": "Internal Freighter Base Name",
                "BaseType": { "PersistentBaseTypes": "FreighterBase" },
                "BaseVersion": 3,
                "Position": [0.0, 0.0, 0.0],
                "Forward": [0.0, 0.0, 1.0],
                "Owner": { "TS": 1 },
                "Objects": [
                  { "ObjectID": "^FRE_ROOM_SCAN" },
                  { "ObjectID": "^FRE_ROOM_SHOP" },
                  { "ObjectID": "^FRE_CORR_A" }
                ]
              },
              {
                "Name": "Old Home",
                "BaseType": { "PersistentBaseTypes": "HomePlanetBase" },
                "BaseVersion": 2,
                "Objects": []
              },
              {
                "Name": "Old Freighter",
                "BaseType": { "PersistentBaseTypes": "FreighterBase" },
                "BaseVersion": 2,
                "Objects": []
              },
              {
                "Name": "Settlement Base",
                "BaseType": { "PersistentBaseTypes": "ExternalPlanetBase" },
                "BaseVersion": 3,
                "Objects": []
              }
            ]
          }
        }
        """);
}
