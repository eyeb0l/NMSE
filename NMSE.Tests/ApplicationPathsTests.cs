using NMSE.Config;
using NMSE.Core;
using NMSE.IO;

namespace NMSE.Tests;

public class ApplicationPathsTests
{
    [Fact]
    public void AppConfig_FirstRunAppearanceFollowsTheSystem()
    {
        Assert.Equal("System", new AppConfig().Theme);
    }

    [Fact]
    public void MacOSLayout_UsesBundleResourcesAndUserWritableDirectories()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string appDirectory = Path.Combine(root, "NMSE.app", "Contents", "MacOS");
            string home = Path.Combine(root, "home");
            var paths = ApplicationPaths.CreateForPlatform(
                appDirectory, home, ApplicationPathPlatform.MacOS);

            Assert.Equal(
                Path.Combine(root, "NMSE.app", "Contents", "Resources"),
                paths.ResourcesDirectory);
            Assert.Equal(
                Path.Combine(home, "Library", "Application Support", "NMSE"),
                paths.UserDataDirectory);
            Assert.Equal(Path.Combine(home, "Library", "Logs", "NMSE"), paths.LogsDirectory);
            Assert.Equal(Path.Combine(paths.UserDataDirectory, "NMSE.conf"), paths.ConfigurationFilePath);
            Assert.Equal(Path.Combine(paths.UserDataDirectory, "export_config.json"), paths.ExportConfigurationFilePath);
            Assert.Equal(Path.Combine(paths.UserDataDirectory, "Save Backups"), paths.BackupsDirectory);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WindowsLayout_PreservesExecutableRelativeConfiguration()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string appDirectory = Path.Combine(root, "app");
            var paths = ApplicationPaths.CreateForPlatform(
                appDirectory, Path.Combine(root, "home"), ApplicationPathPlatform.Windows);

            Assert.Equal(Path.Combine(appDirectory, "NMSE.conf"), paths.ConfigurationFilePath);
            Assert.Equal(Path.Combine(appDirectory, "export_config.json"), paths.ExportConfigurationFilePath);
            Assert.Equal(Path.Combine(appDirectory, "Resources"), paths.ResourcesDirectory);
            Assert.Equal(appDirectory, paths.LogsDirectory);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AppConfigInitialize_MigratesLegacyConfigurationWithoutOverwritingDestination()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string appDirectory = Path.Combine(root, "NMSE.app", "Contents", "MacOS");
            string home = Path.Combine(root, "home");
            string legacy = Path.Combine(root, "legacy");
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "NMSE.conf"), "{\"Language\":\"ja-JP\"}");
            File.WriteAllText(Path.Combine(legacy, "export_config.json"), "legacy export config");

            var paths = ApplicationPaths.CreateForPlatform(
                appDirectory, home, ApplicationPathPlatform.MacOS, legacy);
            var config = new AppConfig();
            config.Initialize(paths);

            Assert.Equal("ja-JP", config.Language);
            Assert.Equal("legacy export config", File.ReadAllText(paths.ExportConfigurationFilePath));

            config.Language = "fr-FR";
            config.Save();
            File.WriteAllText(Path.Combine(legacy, "NMSE.conf"), "{\"Language\":\"de-DE\"}");

            var reloaded = new AppConfig();
            reloaded.Initialize(paths);
            Assert.Equal("fr-FR", reloaded.Language);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MacOSSaveDiscovery_ReturnsProfileContainingRootAndNeverSettings()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string first = Path.Combine(root, "Library", "Application Support", "HelloGames", "NMS", "st_100");
            string second = Path.Combine(root, "Library", "Application Support", "HelloGames", "NMS", "st_200");
            string settings = Path.Combine(root, "Library", "Application Support", "HelloGames", "NMS", "Settings");
            Directory.CreateDirectory(second);
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(settings);

            string nmsRoot = Path.GetDirectoryName(first)!;
            Assert.Equal(nmsRoot, SaveFileManager.FindMacOsSaveDirectory(root));
            Assert.Equal(nmsRoot, SaveFileManager.NormalizeMacOsSaveDirectory(settings));
            Assert.Equal(first, SaveFileManager.NormalizeMacOsSaveDirectory(first));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MacOSMxmlDiscovery_UsesSteamApplicationSupportTree()
    {
        string home = Path.Combine(Path.GetTempPath(), "nmse-home");
        string steam = MxmlRewardEditor.GetMacOsSteamInstallPath(home);

        Assert.Equal(Path.Combine(home, "Library", "Application Support", "Steam"), steam);
        Assert.EndsWith(
            Path.Combine("steamapps", "common", "No Man's Sky", "Binaries", "SETTINGS", "GCUSERSETTINGSDATA.MXML"),
            Path.Combine(steam, MxmlRewardEditor.SteamRelativePath));
    }

    [Fact]
    public void MacOSAccountMxmlDiscovery_UsesHelloGamesSettingsTree()
    {
        string home = Path.Combine(Path.GetTempPath(), "nmse-home");

        Assert.Equal(
            Path.Combine(home, "Library", "Application Support", "HelloGames", "NMS", "SETTINGS", "GcUserSettingsData.mxml"),
            MxmlRewardEditor.GetMacOsUserSettingsPath(home));
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"NMSE-paths-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
