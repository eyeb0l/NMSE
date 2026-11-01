using System.Xml.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Coordination;
using NMSE.UI.Avalonia.Panels.AccountRewards;
using Xunit;

namespace NMSE.Avalonia.Tests;

public sealed class MacOsAccountMxmlTests
{
    [AvaloniaFact]
    public void AccountPanel_CommitsRewardsGuidesAndTitlesToPrimaryMacMxmlStore()
    {
        string mxmlPath = Path.Combine(Path.GetTempPath(), $"NMSE-panel-account-{Guid.NewGuid():N}.mxml");
        try
        {
            File.WriteAllText(mxmlPath, """
                <?xml version="1.0" encoding="utf-8"?>
                <Data template="GcUserSettingsData">
                  <Property name="MusicVolume" value="80" />
                  <Property name="UnlockedSeasonRewards" />
                  <Property name="UnlockedTwitchRewards" />
                  <Property name="UnlockedPlatformRewards" />
                  <Property name="SeenWikiTopics" />
                  <Property name="UnlockedWikiTopics" />
                  <Property name="UnlockedTitles" />
                </Data>
                """);
            JsonObject account = JsonObject.Parse("""
                {
                  "UserSettingsData": {
                    "SeenProducts": [],
                    "SeenTechnologies": [],
                    "SeenSubstances": [],
                    "UnlockedSpecials": [],
                    "UnlockedSeasonRewards": ["^EXPD_BACKPACK01"],
                    "UnlockedTwitchRewards": ["^TWITCH_1"],
                    "UnlockedPlatformRewards": ["^SW_PREORDER"],
                    "SeenWikiTopics": ["UI_GUIDE_TOPIC_PORTALS"],
                    "UnlockedWikiTopics": ["UI_GUIDE_TOPIC_PORTALS"],
                    "UnlockedTitles": ["^T_EXP1"]
                  }
                }
                """);
            var panel = new AccountRewardsPanel(
                new AvaloniaResourceCatalog(),
                new AccountRewardsDesktopActions(
                    _ => Task.FromResult<string?>(null),
                    (_, _) => Task.CompletedTask,
                    (_, _) => Task.CompletedTask));
            panel.SetPlatform(NMSE.IO.SaveFileManager.Platform.Steam);
            panel.SetAccountData(account, mxmlPath);

            object seasonTab = panel.GetType()
                .GetField("_seasonTab", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(panel)!;
            var rewardGrid = (DataGrid)seasonTab.GetType().GetProperty("Grid")!.GetValue(seasonTab)!;
            var visibleRows = (System.Collections.IEnumerable)seasonTab.GetType()
                .GetProperty("VisibleRows")!.GetValue(seasonTab)!;
            var window = new Window { Width = 1200, Height = 720, Content = panel };
            window.Show();

            Assert.NotEmpty(visibleRows.Cast<object>());
            Assert.True(rewardGrid.Bounds.Height > 100);
            Assert.NotEmpty(rewardGrid.GetVisualDescendants());

            Assert.True(panel.CommitAccountSource(account));

            XDocument document = XDocument.Load(mxmlPath);
            Assert.Equal("80", Scalar(document, "MusicVolume"));
            Assert.Equal("EXPD_BACKPACK01", FirstArrayValue(document, "UnlockedSeasonRewards"));
            Assert.Equal("TWITCH_1", FirstArrayValue(document, "UnlockedTwitchRewards"));
            Assert.Equal("SW_PREORDER", FirstArrayValue(document, "UnlockedPlatformRewards"));
            Assert.Equal("UI_GUIDE_TOPIC_PORTALS", FirstArrayValue(document, "UnlockedWikiTopics"));
            Assert.Equal("T_EXP1", FirstArrayValue(document, "UnlockedTitles"));
            Assert.True(File.Exists(mxmlPath + ".nmse.bak"));
            window.Close();
        }
        finally
        {
            try { File.Delete(mxmlPath); } catch { }
            try { File.Delete(mxmlPath + ".nmse.bak"); } catch { }
        }
    }

    private static string? Scalar(XDocument document, string propertyName) => document.Root!
        .Elements("Property")
        .Single(element => element.Attribute("name")?.Value == propertyName)
        .Attribute("value")?.Value;

    private static string? FirstArrayValue(XDocument document, string propertyName) => document.Root!
        .Elements("Property")
        .Single(element => element.Attribute("name")?.Value == propertyName && element.Attribute("value") is null)
        .Elements("Property")
        .Single()
        .Attribute("value")?.Value;
}
