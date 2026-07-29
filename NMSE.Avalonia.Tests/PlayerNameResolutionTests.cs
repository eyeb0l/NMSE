using Avalonia.Headless.XUnit;
using NMSE.Models;
using NMSE.UI.Avalonia.Panels.Player;
using Xunit;

namespace NMSE.Avalonia.Tests;

public sealed class PlayerNameResolutionTests
{
    [AvaloniaFact]
    public void LoadData_UsesOwnerMatchingTheSavePlatform()
    {
        var panel = new PlayerPanel();
        JsonObject save = JsonObject.Parse("""
            {
              "Platform": "Win|Final",
              "CommonStateData": {
                "UsedDiscoveryOwnersV2": [
                  { "UID": "playstation-user", "USN": "Historical Account", "PTK": "PS" },
                  { "UID": "steam-user", "USN": "Current Account", "PTK": "ST" }
                ]
              },
              "PlayerStateData": { "PersistentPlayerBases": [] }
            }
            """);

        panel.LoadData(save);

        Assert.Equal("Current Account", panel.PlayerName);
    }

    [AvaloniaFact]
    public void LoadData_UsesOnlyTheFirstOwnerWhenThePlatformHasNoTokenMapping()
    {
        var panel = new PlayerPanel();
        JsonObject save = JsonObject.Parse("""
            {
              "Platform": "Mac",
              "CommonStateData": {
                "UsedDiscoveryOwnersV2": [
                  { "UID": "current-user", "USN": "Current Account", "PTK": "ST" },
                  { "UID": "old-user", "USN": "Historical Account", "PTK": "PS" }
                ]
              },
              "PlayerStateData": { "PersistentPlayerBases": [] }
            }
            """);

        panel.LoadData(save);

        Assert.Equal("Current Account", panel.PlayerName);
    }

    [AvaloniaFact]
    public void LoadData_FallsBackToThePrimaryOwnersBaseNameByUid()
    {
        var panel = new PlayerPanel();
        JsonObject save = JsonObject.Parse("""
            {
              "Platform": "XB",
              "CommonStateData": {
                "UsedDiscoveryOwnersV2": [
                  { "UID": "playstation-user", "USN": "Historical Account", "PTK": "PS" },
                  { "UID": "xbox-user", "USN": "", "PTK": "XB" }
                ]
              },
              "PlayerStateData": {
                "PersistentPlayerBases": [
                  { "Owner": { "UID": "someone-else", "USN": "Wrong Account" } },
                  { "Owner": { "UID": "xbox-user", "USN": "Base Account" } }
                ]
              }
            }
            """);

        panel.LoadData(save);

        Assert.Equal("Base Account", panel.PlayerName);
    }
}
