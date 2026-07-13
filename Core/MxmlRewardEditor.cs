using System.Globalization;
using System.Xml.Linq;
using NMSE.Models;

namespace NMSE.Core;

/// <summary>
/// Adapts the array-like account fields in <c>GcUserSettingsData.mxml</c> to the
/// JSON account model used by the editor. macOS stores these fields in the user
/// settings MXML instead of a profile-local <c>accountdata.hg</c> file.
/// </summary>
internal static class MxmlRewardEditor
{
    private const string PropertyElementName = "Property";
    private const string PlatformRewardPropertyName = "UnlockedPlatformRewards";
    private const string SteamMxmlFileName = "GCUSERSETTINGSDATA.MXML";
    private const string MacOsMxmlFileName = "GcUserSettingsData.mxml";

    internal static readonly string[] AccountArrayPropertyNames =
    [
        "SeenProducts",
        "SeenTechnologies",
        "SeenSubstances",
        "UnlockedSpecials",
        "UnlockedSeasonRewards",
        "UnlockedTwitchRewards",
        "UnlockedPlatformRewards",
        "SeenWikiTopics",
        "UnlockedWikiTopics",
        "UnlockedTitles",
    ];

    private static readonly HashSet<string> CaretPrefixedJsonProperties = new(StringComparer.Ordinal)
    {
        "SeenProducts",
        "SeenTechnologies",
        "SeenSubstances",
        "UnlockedSpecials",
        "UnlockedSeasonRewards",
        "UnlockedTwitchRewards",
        "UnlockedPlatformRewards",
        "UnlockedTitles",
    };

    /// <summary>The expected path below a Windows/macOS Steam installation.</summary>
    internal static readonly string SteamRelativePath = Path.Combine(
        "steamapps", "common", "No Man's Sky", "Binaries", "SETTINGS", SteamMxmlFileName);

    /// <summary>Attempts to locate the active user-settings MXML for this platform.</summary>
    internal static string? AutoDetectMxmlPath()
    {
        if (OperatingSystem.IsMacOS())
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string? macPath = ResolveExistingMxml(Path.GetDirectoryName(GetMacOsUserSettingsPath(home))!);
            if (macPath is not null)
                return macPath;
        }

        return FindMxmlPath(GetSteamInstallPaths());
    }

    /// <summary>Returns the first existing MXML file below the supplied Steam roots.</summary>
    internal static string? FindMxmlPath(IEnumerable<string> steamInstallPaths)
    {
        foreach (string steamPath in steamInstallPaths)
        {
            if (string.IsNullOrWhiteSpace(steamPath))
                continue;

            string mxmlPath = Path.Combine(steamPath, SteamRelativePath);
            if (File.Exists(mxmlPath))
                return mxmlPath;
        }

        return null;
    }

    /// <summary>
    /// Finds the user-settings MXML from an NMS root, save-profile directory, SETTINGS
    /// directory, or a path nested immediately below one of those locations.
    /// </summary>
    internal static string? FindUserSettingsPath(string saveDirectory)
    {
        if (string.IsNullOrWhiteSpace(saveDirectory))
            return null;

        try
        {
            string fullPath = Path.GetFullPath(saveDirectory);
            if (File.Exists(fullPath) &&
                string.Equals(Path.GetExtension(fullPath), ".mxml", StringComparison.OrdinalIgnoreCase))
            {
                return fullPath;
            }

            DirectoryInfo? current = new(fullPath);
            for (int depth = 0; current is not null && depth < 5; depth++, current = current.Parent)
            {
                string settingsDirectory = string.Equals(current.Name, "SETTINGS", StringComparison.OrdinalIgnoreCase)
                    ? current.FullName
                    : Path.Combine(current.FullName, "SETTINGS");
                string? candidate = ResolveExistingMxml(settingsDirectory);
                if (candidate is not null)
                    return candidate;
            }
        }
        catch
        {
            // Invalid/inaccessible paths are treated as not found.
        }

        return null;
    }

    internal static string GetMacOsUserSettingsPath(string homeDirectory) => Path.Combine(
        homeDirectory,
        "Library", "Application Support", "HelloGames", "NMS", "SETTINGS", MacOsMxmlFileName);

    internal static string GetMacOsSteamInstallPath(string homeDirectory) =>
        Path.Combine(homeDirectory, "Library", "Application Support", "Steam");

    /// <summary>Loads the supported MXML arrays into the existing JSON account shape.</summary>
    internal static JsonObject LoadAccountObject(string mxmlPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mxmlPath);
        var document = XDocument.Load(mxmlPath);
        XElement root = document.Root ?? throw new InvalidDataException("The MXML document has no root element.");
        var settings = new JsonObject();

        foreach (string propertyName in AccountArrayPropertyNames)
        {
            var values = new JsonArray();
            foreach (string value in ReadPropertyValues(root, propertyName))
                values.Add(NormalizeForJson(propertyName, value));
            settings.Set(propertyName, values);
        }

        var account = new JsonObject();
        account.Set("UserSettingsData", settings);
        return account;
    }

    /// <summary>
    /// Writes selected JSON account arrays back to MXML in one parse/save pass,
    /// preserving all unrelated settings and property containers.
    /// </summary>
    internal static bool WriteAccountObject(
        string mxmlPath,
        JsonObject accountObject,
        IEnumerable<string> propertyNames)
    {
        if (string.IsNullOrWhiteSpace(mxmlPath) || !File.Exists(mxmlPath))
            return false;

        try
        {
            var document = XDocument.Load(mxmlPath);
            XElement root = document.Root ?? throw new InvalidDataException("The MXML document has no root element.");
            JsonObject settings = accountObject.GetObject("UserSettingsData") ?? accountObject;

            foreach (string propertyName in propertyNames.Distinct(StringComparer.Ordinal))
            {
                JsonArray? array = settings.GetArray(propertyName);
                var values = new List<string>();
                if (array is not null)
                {
                    for (int index = 0; index < array.Length; index++)
                    {
                        string? value = array.GetString(index);
                        if (!string.IsNullOrWhiteSpace(value))
                            values.Add(NormalizeForMxml(value));
                    }
                }
                UpdatePropertyContainer(root, propertyName, values);
            }

            document.Save(mxmlPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Reads a named MXML array and returns values in JSON/account ID form.</summary>
    internal static HashSet<string> ReadPropertyValues(string mxmlPath, string propertyName)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(mxmlPath) || !File.Exists(mxmlPath))
            return result;

        try
        {
            XElement? root = XDocument.Load(mxmlPath).Root;
            if (root is null)
                return result;
            foreach (string value in ReadPropertyValues(root, propertyName))
                result.Add(NormalizeForJson(propertyName, value));
        }
        catch
        {
            // Graceful read failure preserves the established API contract.
        }
        return result;
    }

    /// <summary>Compatibility wrapper for the platform-reward UI and existing tests.</summary>
    internal static HashSet<string> ReadUnlockedRewards(string mxmlPath) =>
        ReadPropertyValues(mxmlPath, PlatformRewardPropertyName);

    /// <summary>Compatibility wrapper that writes platform rewards only.</summary>
    internal static bool WriteUnlockedRewards(string mxmlPath, List<(string Id, bool Unlocked)> rewards)
    {
        var account = new JsonObject();
        var settings = new JsonObject();
        var values = new JsonArray();
        foreach ((string id, bool unlocked) in rewards)
        {
            if (unlocked && !string.IsNullOrWhiteSpace(id))
                values.Add(id);
        }
        settings.Set(PlatformRewardPropertyName, values);
        account.Set("UserSettingsData", settings);
        return WriteAccountObject(mxmlPath, account, [PlatformRewardPropertyName]);
    }

    internal static bool SyncPlatformRewards(string? mxmlPath, List<(string Id, bool Unlocked)> rewards) =>
        string.IsNullOrWhiteSpace(mxmlPath) || WriteUnlockedRewards(mxmlPath, rewards);

    private static IEnumerable<string> ReadPropertyValues(XElement root, string propertyName) =>
        root.Descendants(PropertyElementName)
            .Where(element => string.Equals(
                element.Attribute("name")?.Value,
                propertyName,
                StringComparison.Ordinal))
            .Select(element => element.Attribute("value")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!);

    private static void UpdatePropertyContainer(
        XElement root,
        string propertyName,
        IEnumerable<string> requestedValues)
    {
        XElement container = FindOrCreateContainer(root, propertyName);
        foreach (XElement element in container.Elements(PropertyElementName)
                     .Where(element => string.Equals(
                         element.Attribute("name")?.Value,
                         propertyName,
                         StringComparison.Ordinal))
                     .ToList())
        {
            element.Remove();
        }

        string[] values = requestedValues
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeForMxml)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (int index = 0; index < values.Length; index++)
        {
            container.Add(new XElement(PropertyElementName,
                new XAttribute("name", propertyName),
                new XAttribute("value", values[index]),
                new XAttribute("_index", index.ToString(CultureInfo.InvariantCulture))));
        }
    }

    private static XElement FindOrCreateContainer(XElement root, string propertyName)
    {
        XElement? container = root.Elements(PropertyElementName)
            .FirstOrDefault(element => string.Equals(
                    element.Attribute("name")?.Value,
                    propertyName,
                    StringComparison.Ordinal) &&
                element.Attribute("value") is null);
        if (container is not null)
            return container;

        foreach (XElement flatEntry in root.Elements(PropertyElementName)
                     .Where(element => string.Equals(
                             element.Attribute("name")?.Value,
                             propertyName,
                             StringComparison.Ordinal) &&
                         element.Attribute("value") is not null)
                     .ToList())
        {
            flatEntry.Remove();
        }

        container = new XElement(PropertyElementName, new XAttribute("name", propertyName));
        root.Add(container);
        return container;
    }

    private static string NormalizeForJson(string propertyName, string value) =>
        CaretPrefixedJsonProperties.Contains(propertyName) && !value.StartsWith('^')
            ? "^" + value
            : value;

    private static string NormalizeForMxml(string value) => value.StartsWith('^') ? value[1..] : value;

    private static string? ResolveExistingMxml(string settingsDirectory)
    {
        if (!Directory.Exists(settingsDirectory))
            return null;

        string expected = Path.Combine(settingsDirectory, MacOsMxmlFileName);
        if (File.Exists(expected))
            return expected;

        try
        {
            return Directory.EnumerateFiles(settingsDirectory)
                .FirstOrDefault(path => string.Equals(
                    Path.GetFileName(path),
                    MacOsMxmlFileName,
                    StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> GetSteamInstallPaths()
    {
        if (OperatingSystem.IsWindows())
        {
            string? registryPath = ReadRegistryValue(@"SOFTWARE\Wow6432Node\Valve\Steam", "InstallPath")
                ?? ReadRegistryValue(@"SOFTWARE\Valve\Steam", "InstallPath");
            if (!string.IsNullOrWhiteSpace(registryPath))
                yield return registryPath;
            yield break;
        }

        if (OperatingSystem.IsMacOS())
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
                yield return GetMacOsSteamInstallPath(home);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? ReadRegistryValue(string subKey, string valueName)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(subKey);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }
}
