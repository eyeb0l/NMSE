using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NMSE.Models;
using NMSE.UI.Avalonia.Panels.ByteBeats;
using Xunit;

namespace NMSE.Avalonia.Tests;

public sealed class ByteBeatSelectionTests
{
    [AvaloniaFact]
    public void SaveData_DoesNotModifySongSourceDuringSelectionUpdate()
    {
        JsonObject save = CreateSave();
        var panel = new ByteBeatPanel(new ByteBeatDesktopActions(
            _ => Task.FromResult<string?>(null),
            _ => Task.FromResult<string?>(null),
            (_, _) => Task.FromResult(true),
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask));

        panel.LoadData(save);

        // MainWindow commits every loaded panel while processing a top-level tab
        // selection. This must not re-enter the ByteBeat ListBox selection model.
        panel.SaveData(save);

        JsonArray songs = save.GetObject("CommonStateData")!
            .GetObject("ByteBeatLibrary")!.GetArray("MySongs")!;
        Assert.Equal("First song", songs.GetObject(0).GetString("Name"));
    }

    [AvaloniaFact]
    public async Task SelectingAnotherSong_DefersEditedDisplayNameUntilSelectionCompletes()
    {
        JsonObject save = CreateSave();
        var panel = new ByteBeatPanel(new ByteBeatDesktopActions(
            _ => Task.FromResult<string?>(null),
            _ => Task.FromResult<string?>(null),
            (_, _) => Task.FromResult(true),
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask));
        panel.LoadData(save);
        var name = GetField<TextBox>(panel, "_nameField");
        var songs = GetField<ListBox>(panel, "_songList");

        name.Text = "Renamed first song";
        songs.SelectedIndex = 1;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        JsonArray data = save.GetObject("CommonStateData")!
            .GetObject("ByteBeatLibrary")!.GetArray("MySongs")!;
        Assert.Equal("Renamed first song", data.GetObject(0).GetString("Name"));
        Assert.Equal(1, songs.SelectedIndex);
    }

    private static T GetField<T>(object target, string name) where T : class =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static JsonObject CreateSave() => JsonObject.Parse("""
        {
          "CommonStateData": {
            "ByteBeatLibrary": {
              "MySongs": [
                {
                  "Name": "First song",
                  "AuthorUsername": "Traveller",
                  "AuthorOnlineID": "",
                  "AuthorPlatform": "Steam",
                  "Data": ["0", "1", "2", "3", "4", "5", "6", "7"]
                },
                {
                  "Name": "Second song",
                  "AuthorUsername": "Traveller",
                  "AuthorOnlineID": "",
                  "AuthorPlatform": "Steam",
                  "Data": ["7", "6", "5", "4", "3", "2", "1", "0"]
                }
              ],
              "Shuffle": false,
              "AutoplayOnFoot": false,
              "AutoplayInShip": false,
              "AutoplayInVehicle": false
            }
          }
        }
        """);
}
