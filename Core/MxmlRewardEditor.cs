using System.Globalization;
using System.Xml.Linq;

namespace NMSE.Core;

/// <summary>
/// Reads and writes UnlockedPlatformRewards entries in GCUSERSETTINGSDATA.MXML.
/// The MXML file uses a container Property element with child Property elements:
/// <code>
///   &lt;Property name="UnlockedPlatformRewards"&gt;
///     &lt;Property name="UnlockedPlatformRewards" value="SW_PREORDER" _index="0" /&gt;
///     &lt;Property name="UnlockedPlatformRewards" value="SW_PREORDER2" _index="1" /&gt;
///   &lt;/Property&gt;
/// </code>
/// </summary>
internal static class MxmlRewardEditor
{
    private const string PropertyElementName = "Property";
    private const string RewardPropertyName = "UnlockedPlatformRewards";
    private const string MxmlFileName = "GCUSERSETTINGSDATA.MXML";

    /// <summary>
    /// The expected relative path from the Steam install directory to the MXML settings file.
    /// </summary>
    internal static readonly string SteamRelativePath = Path.Combine(
        "steamapps", "common", "No Man's Sky", "Binaries", "SETTINGS", MxmlFileName);

    /// <summary>
    /// Attempts to auto-detect the full path to GCUSERSETTINGSDATA.MXML from
    /// the platform's Steam installation.
    /// </summary>
    /// <returns>The full path if found and the file exists, otherwise null.</returns>
    internal static string? AutoDetectMxmlPath()
    {
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
    /// Gets candidate Steam roots from the Windows registry or the standard
    /// macOS user installation directory.
    /// </summary>
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

    internal static string GetMacOsSteamInstallPath(string homeDirectory) =>
        Path.Combine(homeDirectory, "Library", "Application Support", "Steam");

    /// <summary>
    /// Reads a string value from the Windows registry (HKLM).
    /// </summary>
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

    /// <summary>
    /// Reads the currently unlocked platform reward IDs from the MXML file.
    /// Supports the correct nested format (child entries inside a container element)
    /// as well as legacy flat format for backwards compatibility.
    /// </summary>
    /// <param name="mxmlPath">Full path to GCUSERSETTINGSDATA.MXML.</param>
    /// <returns>A set of reward value strings (with ^ prefix to match RewardDatabase IDs), or empty set on error.</returns>
    internal static HashSet<string> ReadUnlockedRewards(string mxmlPath)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(mxmlPath) || !File.Exists(mxmlPath))
            return result;

        try
        {
            var doc = XDocument.Load(mxmlPath);
            if (doc.Root == null) return result;

            foreach (var prop in doc.Root.Descendants(PropertyElementName))
            {
                var nameAttr = prop.Attribute("name");
                var valueAttr = prop.Attribute("value");
                if (nameAttr?.Value == RewardPropertyName && valueAttr != null
                    && !string.IsNullOrEmpty(valueAttr.Value))
                {
                    // Store as ^VALUE to match the reward database ID format
                    string val = valueAttr.Value;
                    if (!val.StartsWith('^'))
                        val = "^" + val;
                    result.Add(val);
                }
            }
        }
        catch
        {
            // Graceful failure - return what we have
        }

        return result;
    }

    /// <summary>
    /// Writes unlocked platform rewards to the MXML file.
    /// Uses the correct nested format with a container Property element wrapping child entries:
    /// <code>
    ///   &lt;Property name="UnlockedPlatformRewards"&gt;
    ///     &lt;Property name="UnlockedPlatformRewards" value="SW_PREORDER" _index="0" /&gt;
    ///   &lt;/Property&gt;
    /// </code>
    /// </summary>
    /// <param name="mxmlPath">Full path to GCUSERSETTINGSDATA.MXML.</param>
    /// <param name="rewards">The reward IDs (with ^ prefix) and their unlock state.</param>
    /// <returns>True if the file was written successfully, false otherwise.</returns>
    internal static bool WriteUnlockedRewards(string mxmlPath, List<(string Id, bool Unlocked)> rewards)
    {
        if (string.IsNullOrEmpty(mxmlPath) || !File.Exists(mxmlPath))
            return false;

        try
        {
            var doc = XDocument.Load(mxmlPath);
            if (doc.Root == null) return false;

            // Find or create the container element for UnlockedPlatformRewards.
            var container = FindOrCreateRewardContainer(doc.Root);

            // Remove all existing child entries inside the container
            var existing = container.Elements(PropertyElementName)
                .Where(e => e.Attribute("name")?.Value == RewardPropertyName)
                .ToList();
            foreach (var el in existing)
                el.Remove();

            // Build set of IDs to unlock (strip ^ prefix for MXML value)
            var toUnlock = new List<string>();
            foreach (var (id, unlocked) in rewards)
            {
                if (unlocked && !string.IsNullOrEmpty(id))
                {
                    string val = id.StartsWith('^') ? id[1..] : id;
                    toUnlock.Add(val);
                }
            }

            // Add new child entries with sequential _index inside the container
            for (int i = 0; i < toUnlock.Count; i++)
            {
                var elem = new XElement(PropertyElementName,
                    new XAttribute("name", RewardPropertyName),
                    new XAttribute("value", toUnlock[i]),
                    new XAttribute("_index", i.ToString(CultureInfo.InvariantCulture)));
                container.Add(elem);
            }

            doc.Save(mxmlPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Finds the container &lt;Property name="UnlockedPlatformRewards"&gt; element that holds
    /// the child reward entries, or creates one if it doesn't exist.
    /// Also handles migrating legacy flat format (entries as direct children of root)
    /// into the correct nested container format.
    /// </summary>
    private static XElement FindOrCreateRewardContainer(XElement root)
    {
        // Look for a container element: a Property with name=UnlockedPlatformRewards
        // that does NOT have a value attribute (i.e. it's a parent container, not an entry).
        var container = root.Elements(PropertyElementName)
            .FirstOrDefault(e => e.Attribute("name")?.Value == RewardPropertyName
                              && e.Attribute("value") == null);

        if (container != null)
            return container;

        // Handle legacy flat format: remove any flat entries from root before
        // creating the container (they will be re-added as children).
        var legacyFlat = root.Elements(PropertyElementName)
            .Where(e => e.Attribute("name")?.Value == RewardPropertyName
                     && e.Attribute("value") != null)
            .ToList();
        foreach (var el in legacyFlat)
            el.Remove();

        // Create a new container element
        container = new XElement(PropertyElementName,
            new XAttribute("name", RewardPropertyName));
        root.Add(container);
        return container;
    }

    /// <summary>
    /// Reads unlocked rewards from MXML and writes rewards back in a single operation.
    /// Convenience method for save operations.
    /// </summary>
    /// <param name="mxmlPath">Full path to GCUSERSETTINGSDATA.MXML.</param>
    /// <param name="rewards">The reward rows from the grid.</param>
    /// <returns>True if save succeeded or was skipped (no path), false on error.</returns>
    internal static bool SyncPlatformRewards(string? mxmlPath, List<(string Id, bool Unlocked)> rewards)
    {
        if (string.IsNullOrEmpty(mxmlPath))
            return true; // Gracefully skip if no file selected

        return WriteUnlockedRewards(mxmlPath, rewards);
    }
}
