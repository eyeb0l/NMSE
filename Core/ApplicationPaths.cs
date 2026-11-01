namespace NMSE.Core;

/// <summary>
/// Resolves read-only application resources and user-writable application data.
/// </summary>
/// <remarks>
/// Windows keeps the existing portable layout for backwards compatibility. A
/// packaged macOS application reads resources from <c>Contents/Resources</c>
/// and stores mutable data outside the application bundle.
/// </remarks>
public sealed class ApplicationPaths
{
    private const string ApplicationName = "NMSE";
    private const string ConfigFileName = "NMSE.conf";
    private const string ExportConfigFileName = "export_config.json";

    private readonly string[] _legacyDataDirectories;

    /// <summary>Paths for the current process and operating system.</summary>
    public static ApplicationPaths Current { get; } = CreateDefault();

    /// <summary>Directory containing the running application binary.</summary>
    public string ApplicationDirectory { get; }

    /// <summary>Directory containing the JSON, image, map, and UI resources.</summary>
    public string ResourcesDirectory { get; }

    /// <summary>Directory for configuration and other mutable application data.</summary>
    public string UserDataDirectory { get; }

    /// <summary>Directory for application log files.</summary>
    public string LogsDirectory { get; }

    public string ConfigurationFilePath => Path.Combine(UserDataDirectory, ConfigFileName);
    public string ExportConfigurationFilePath => Path.Combine(UserDataDirectory, ExportConfigFileName);
    public string BackupsDirectory => Path.Combine(UserDataDirectory, "Save Backups");
    public string CrashLogPath => Path.Combine(LogsDirectory, "crash.log");

    private ApplicationPaths(
        string applicationDirectory,
        string userDataDirectory,
        string logsDirectory,
        IEnumerable<string> legacyDataDirectories)
    {
        ApplicationDirectory = NormalizeDirectory(applicationDirectory);
        UserDataDirectory = NormalizeDirectory(userDataDirectory);
        LogsDirectory = NormalizeDirectory(logsDirectory);
        ResourcesDirectory = ResolveResourcesDirectory(ApplicationDirectory);
        _legacyDataDirectories = legacyDataDirectories
            .Select(NormalizeDirectory)
            .Where(path => !string.Equals(path, UserDataDirectory, PathComparison))
            .Distinct(PathComparer)
            .ToArray();
    }

    /// <summary>
    /// Creates the current platform layout. This intentionally retains the
    /// executable-relative Windows paths used by existing installations.
    /// </summary>
    public static ApplicationPaths CreateDefault()
    {
        string appDirectory = AppContext.BaseDirectory;
        string homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        if (OperatingSystem.IsMacOS())
        {
            string userData = Path.Combine(homeDirectory, "Library", "Application Support", ApplicationName);
            string logs = Path.Combine(homeDirectory, "Library", "Logs", ApplicationName);
            return new ApplicationPaths(
                appDirectory,
                userData,
                logs,
                [
                    appDirectory,
                    Path.Combine(homeDirectory, ".config", ApplicationName),
                    Path.Combine(applicationData, ApplicationName),
                ]);
        }

        if (OperatingSystem.IsWindows())
        {
            return new ApplicationPaths(
                appDirectory,
                appDirectory,
                appDirectory,
                [Path.Combine(applicationData, ApplicationName)]);
        }

        // Follow the common XDG layout on Unix-like desktop systems. The
        // Avalonia frontend can use these paths without writing beside its
        // executable, which may be installed read-only.
        string configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
            ?? Path.Combine(homeDirectory, ".config");
        string stateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME")
            ?? Path.Combine(homeDirectory, ".local", "state");
        return new ApplicationPaths(
            appDirectory,
            Path.Combine(configHome, ApplicationName),
            Path.Combine(stateHome, ApplicationName),
            [appDirectory, Path.Combine(applicationData, ApplicationName)]);
    }

    /// <summary>
    /// Creates a deterministic layout for characterization tests and hosts
    /// that need to resolve paths for a platform other than the current one.
    /// </summary>
    internal static ApplicationPaths CreateForPlatform(
        string applicationDirectory,
        string homeDirectory,
        ApplicationPathPlatform platform,
        params string[] legacyDataDirectories)
    {
        string userData;
        string logs;

        switch (platform)
        {
            case ApplicationPathPlatform.MacOS:
                userData = Path.Combine(homeDirectory, "Library", "Application Support", ApplicationName);
                logs = Path.Combine(homeDirectory, "Library", "Logs", ApplicationName);
                break;
            case ApplicationPathPlatform.Windows:
                userData = applicationDirectory;
                logs = applicationDirectory;
                break;
            default:
                userData = Path.Combine(homeDirectory, ".config", ApplicationName);
                logs = Path.Combine(homeDirectory, ".local", "state", ApplicationName);
                break;
        }

        var legacy = new List<string> { applicationDirectory };
        legacy.AddRange(legacyDataDirectories);
        return new ApplicationPaths(applicationDirectory, userData, logs, legacy);
    }

    /// <summary>
    /// Creates writable directories and copies legacy settings when the new
    /// destination does not yet exist. Migration failures are non-fatal.
    /// </summary>
    public void PrepareUserData()
    {
        try
        {
            Directory.CreateDirectory(UserDataDirectory);
            Directory.CreateDirectory(LogsDirectory);
        }
        catch
        {
            // Callers retain their existing graceful handling of write errors.
        }

        TryMigrateFile(ConfigFileName);
        TryMigrateFile(ExportConfigFileName);
    }

    private void TryMigrateFile(string fileName)
    {
        string destination = Path.Combine(UserDataDirectory, fileName);
        if (File.Exists(destination))
            return;

        foreach (string legacyDirectory in _legacyDataDirectories)
        {
            string source = Path.Combine(legacyDirectory, fileName);
            if (!File.Exists(source))
                continue;

            try
            {
                Directory.CreateDirectory(UserDataDirectory);
                File.Copy(source, destination, overwrite: false);
                return;
            }
            catch
            {
                // Migration is best-effort; try any remaining legacy paths.
            }
        }
    }

    private static string ResolveResourcesDirectory(string applicationDirectory)
    {
        var macOsDirectory = new DirectoryInfo(applicationDirectory);
        if (macOsDirectory.Name.Equals("MacOS", StringComparison.OrdinalIgnoreCase)
            && macOsDirectory.Parent?.Name.Equals("Contents", StringComparison.OrdinalIgnoreCase) == true
            && macOsDirectory.Parent.Parent?.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Path.Combine(macOsDirectory.Parent.FullName, "Resources");
        }

        return Path.Combine(applicationDirectory, "Resources");
    }

    private static string NormalizeDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, root, PathComparison)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}

internal enum ApplicationPathPlatform
{
    Windows,
    MacOS,
    Linux,
}
