using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;

namespace NMSE.Core;

/// <summary>
/// Prepares and launches a native macOS whole-bundle update. The actual bundle
/// swap runs from a temporary external helper after NMSE exits.
/// </summary>
public sealed class MacOSUpdateInstaller
{
    public const string ExpectedBundleIdentifier = "com.vectorcmdr.nmse";

    private const long MaximumExpandedArchiveBytes = 4L * 1024 * 1024 * 1024;
    private const int MaximumArchiveEntries = 100_000;

    private static readonly Uri ReleasesPage = new(
        $"https://github.com/{UpdateService.GitHubOwner}/{UpdateService.GitHubRepo}/releases/latest");

    /// <summary>
    /// Downloads the ZIP selected by <see cref="UpdateService"/>, verifies its
    /// bundle, stages it beside the installed app, and starts a helper that waits
    /// for <paramref name="currentProcessId"/> to exit before swapping bundles.
    /// </summary>
    /// <remarks>
    /// A <see cref="MacOSUpdateInstallStatus.HelperStarted"/> result means the
    /// caller must promptly terminate the normal Avalonia desktop lifetime. No
    /// installed files are changed before that termination.
    /// </remarks>
    public async Task<MacOSUpdateInstallResult> PrepareAndStartAsync(
        UpdateInfo update,
        string? currentBundlePath = null,
        int? currentProcessId = null,
        IProgress<(long received, long? total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        MacOSUpdateFallback fallback = CreateFallback(update.DownloadUrl, "The native updater is unavailable.");
        if (!OperatingSystem.IsMacOS())
        {
            return new MacOSUpdateInstallResult(
                MacOSUpdateInstallStatus.UnsupportedPlatform,
                null,
                fallback with { Reason = "Native bundle replacement is supported only on macOS." });
        }

        currentBundlePath ??= FindContainingAppBundle(AppContext.BaseDirectory);
        if (currentBundlePath is null || !Directory.Exists(currentBundlePath))
        {
            return Fallback(update, "NMSE is not running from an installed .app bundle.");
        }

        currentBundlePath = Path.GetFullPath(currentBundlePath);
        if (!currentBundlePath.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
            || File.GetAttributes(currentBundlePath).HasFlag(FileAttributes.ReparsePoint))
        {
            return Fallback(update, "The current application bundle path is not safe to replace automatically.");
        }

        int targetProcessId = currentProcessId ?? Environment.ProcessId;
        if (targetProcessId <= 0)
            return Fallback(update, "The updater could not identify the running NMSE process.");

        string? destinationParent = Directory.GetParent(currentBundlePath)?.FullName;
        if (destinationParent is null || !CanWriteDirectory(destinationParent))
        {
            return Fallback(update, "The application location is not writable. Install the update from the DMG instead.");
        }

        string token = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        string workingDirectory = Path.Combine(Path.GetTempPath(), $"NMSE-update-{token}");
        string zipPath = Path.Combine(workingDirectory, "update.zip");
        string extractDirectory = Path.Combine(workingDirectory, "extract");
        string stagedBundlePath = Path.Combine(destinationParent, $".NMSE.update-{token}.app");
        string backupBundlePath = Path.Combine(destinationParent, $".NMSE.backup-{token}.app");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(workingDirectory);

            await ValidateCurrentBundleIdentityAsync(
                currentBundlePath,
                cancellationToken).ConfigureAwait(false);

            await UpdateService.DownloadFileAsync(update.DownloadUrl, zipPath, progress).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            ValidateArchiveLayout(zipPath);
            Directory.CreateDirectory(extractDirectory);
            await RunToolAsync(
                "/usr/bin/ditto",
                ["-x", "-k", zipPath, extractDirectory],
                cancellationToken).ConfigureAwait(false);

            string extractedBundle = Path.Combine(extractDirectory, "NMSE.app");
            await ValidateBundleAsync(
                extractedBundle,
                currentBundlePath,
                update.RemoteVersion,
                cancellationToken).ConfigureAwait(false);

            // The stage is deliberately adjacent to the installed app so the helper's
            // rename operations stay on one volume and are atomic directory renames.
            await RunToolAsync(
                "/usr/bin/ditto",
                [extractedBundle, stagedBundlePath],
                cancellationToken).ConfigureAwait(false);
            await ValidateBundleAsync(
                stagedBundlePath,
                currentBundlePath,
                update.RemoteVersion,
                cancellationToken).ConfigureAwait(false);

            string helperPath = StageExternalHelper(workingDirectory);
            string logPath = Path.Combine(Path.GetTempPath(), $"NMSE-update-{token}.log");
            StartHelper(
                helperPath,
                targetProcessId,
                currentBundlePath,
                stagedBundlePath,
                backupBundlePath,
                logPath,
                workingDirectory);

            return new MacOSUpdateInstallResult(
                MacOSUpdateInstallStatus.HelperStarted,
                helperPath,
                null);
        }
        catch (OperationCanceledException)
        {
            CleanupPath(stagedBundlePath);
            CleanupPath(workingDirectory);
            throw;
        }
        catch (Exception ex)
        {
            CleanupPath(stagedBundlePath);
            CleanupPath(backupBundlePath);
            CleanupPath(workingDirectory);
            return Fallback(update, $"The update could not be verified or staged: {ex.Message}");
        }
    }

    /// <summary>Finds the outermost <c>.app</c> containing a runtime directory.</summary>
    internal static string? FindContainingAppBundle(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var current = new DirectoryInfo(Path.GetFullPath(path));
        while (current is not null)
        {
            if (current.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                return current.FullName;
            current = current.Parent;
        }

        return null;
    }

    /// <summary>
    /// Rejects rooted paths, dot-dot traversal, links/special files, oversized
    /// payloads, and archives whose application payload is not exactly
    /// <c>NMSE.app</c> at the root.
    /// </summary>
    internal static void ValidateArchiveLayout(string zipPath)
    {
        using var stream = File.OpenRead(zipPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        if (archive.Entries.Count == 0)
            throw new InvalidDataException("The update archive is empty.");
        if (archive.Entries.Count > MaximumArchiveEntries)
            throw new InvalidDataException("The update archive contains too many entries.");

        long expandedBytes = 0;
        bool foundBundle = false;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string normalizedName = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrEmpty(normalizedName))
                throw new InvalidDataException("The update archive contains an unnamed entry.");
            if (normalizedName.StartsWith("/", StringComparison.Ordinal)
                || HasWindowsDrivePrefix(normalizedName))
                throw new InvalidDataException($"The update archive contains a rooted path: {entry.FullName}");

            string[] components = normalizedName.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (components.Any(component => component is "." or ".."))
                throw new InvalidDataException($"The update archive contains path traversal: {entry.FullName}");

            if (components.Length == 0)
                throw new InvalidDataException($"The update archive contains an invalid path: {entry.FullName}");

            bool isAppleMetadata = components[0].Equals("__MACOSX", StringComparison.Ordinal);
            bool isBundlePayload = components[0].Equals("NMSE.app", StringComparison.Ordinal);
            if (!isAppleMetadata && !isBundlePayload)
                throw new InvalidDataException($"Unexpected top-level update payload: {components[0]}");
            foundBundle |= isBundlePayload;

            int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType is 0xA000 or 0x6000 or 0x2000 or 0x1000 or 0xC000)
                throw new InvalidDataException($"Links and special files are not permitted: {entry.FullName}");

            try
            {
                expandedBytes = checked(expandedBytes + entry.Length);
            }
            catch (OverflowException ex)
            {
                throw new InvalidDataException("The update archive size is invalid.", ex);
            }

            if (expandedBytes > MaximumExpandedArchiveBytes)
                throw new InvalidDataException("The expanded update archive is too large.");
        }

        if (!foundBundle)
            throw new InvalidDataException("The update archive does not contain NMSE.app.");
    }

    private static async Task ValidateBundleAsync(
        string bundlePath,
        string currentBundlePath,
        Version expectedVersion,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(bundlePath))
            throw new InvalidDataException("The update archive does not contain NMSE.app.");

        string plistPath = Path.Combine(bundlePath, "Contents", "Info.plist");
        if (!File.Exists(plistPath))
            throw new InvalidDataException("The update bundle has no Info.plist.");

        string bundleIdentifier = await ReadPlistValueAsync(
            plistPath, "CFBundleIdentifier", cancellationToken).ConfigureAwait(false);
        if (!bundleIdentifier.Equals(ExpectedBundleIdentifier, StringComparison.Ordinal))
            throw new InvalidDataException($"Unexpected bundle identifier '{bundleIdentifier}'.");

        string versionText = await ReadPlistValueAsync(
            plistPath, "CFBundleShortVersionString", cancellationToken).ConfigureAwait(false);
        if (!Version.TryParse(versionText, out Version? bundleVersion)
            || bundleVersion.CompareTo(expectedVersion) != 0)
        {
            throw new InvalidDataException(
                $"Update bundle version '{versionText}' does not match expected version '{expectedVersion}'.");
        }

        string executableName = await ReadPlistValueAsync(
            plistPath, "CFBundleExecutable", cancellationToken).ConfigureAwait(false);
        if (Path.GetFileName(executableName) != executableName)
            throw new InvalidDataException("The bundle executable name is invalid.");
        string executablePath = Path.Combine(bundlePath, "Contents", "MacOS", executableName);
        if (!File.Exists(executablePath))
            throw new InvalidDataException("The update bundle's executable is missing.");

        if (!File.Exists("/usr/bin/codesign"))
            return;

        await RunToolAsync(
            "/usr/bin/codesign",
            ["--verify", "--deep", "--strict", "--verbose=2", bundlePath],
            cancellationToken).ConfigureAwait(false);

        string currentTeam = await ReadTeamIdentifierAsync(currentBundlePath, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(currentTeam))
        {
            string updateTeam = await ReadTeamIdentifierAsync(bundlePath, cancellationToken).ConfigureAwait(false);
            if (!currentTeam.Equals(updateTeam, StringComparison.Ordinal))
                throw new InvalidDataException("The update bundle is signed by a different Apple team.");
        }
    }

    private static async Task ValidateCurrentBundleIdentityAsync(
        string bundlePath,
        CancellationToken cancellationToken)
    {
        string plistPath = Path.Combine(bundlePath, "Contents", "Info.plist");
        if (!File.Exists(plistPath))
            throw new InvalidDataException("The installed app has no Info.plist.");

        string bundleIdentifier = await ReadPlistValueAsync(
            plistPath,
            "CFBundleIdentifier",
            cancellationToken).ConfigureAwait(false);
        if (!bundleIdentifier.Equals(ExpectedBundleIdentifier, StringComparison.Ordinal))
            throw new InvalidDataException("The installed app has an unexpected bundle identifier.");
    }

    private static async Task<string> ReadPlistValueAsync(
        string plistPath,
        string key,
        CancellationToken cancellationToken)
    {
        ToolResult result = await RunToolAsync(
            "/usr/bin/plutil",
            ["-extract", key, "raw", "-o", "-", plistPath],
            cancellationToken).ConfigureAwait(false);
        string value = result.StandardOutput.Trim();
        if (value.Length == 0)
            throw new InvalidDataException($"Info.plist is missing {key}.");
        return value;
    }

    private static async Task<string> ReadTeamIdentifierAsync(
        string bundlePath,
        CancellationToken cancellationToken)
    {
        ToolResult result = await RunToolAsync(
            "/usr/bin/codesign",
            ["-dvv", bundlePath],
            cancellationToken,
            allowNonZeroExit: true).ConfigureAwait(false);

        foreach (string line in result.StandardError.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            const string prefix = "TeamIdentifier=";
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            string identifier = line[prefix.Length..].Trim();
            return identifier.Equals("not set", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : identifier;
        }

        return string.Empty;
    }

    private static bool CanWriteDirectory(string directory)
    {
        string probe = Path.Combine(directory, $".nmse-write-test-{Guid.NewGuid():N}");
        try
        {
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            File.Delete(probe);
            return true;
        }
        catch
        {
            try { File.Delete(probe); } catch { }
            return false;
        }
    }

    private static string StageExternalHelper(string workingDirectory)
    {
        string helperPath = Path.Combine(workingDirectory, "nmse-update-helper");
        File.WriteAllText(helperPath, HelperScript);
        if (OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(
                helperPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        else
        {
            throw new PlatformNotSupportedException("The external updater helper requires macOS.");
        }
        return helperPath;
    }

    private static void StartHelper(
        string helperPath,
        int processId,
        string currentBundlePath,
        string stagedBundlePath,
        string backupBundlePath,
        string logPath,
        string helperDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = helperPath,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(processId.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(currentBundlePath);
        startInfo.ArgumentList.Add(stagedBundlePath);
        startInfo.ArgumentList.Add(backupBundlePath);
        startInfo.ArgumentList.Add(logPath);
        startInfo.ArgumentList.Add(helperDirectory);

        if (Process.Start(startInfo) is null)
            throw new InvalidOperationException("The external update helper could not be started.");
    }

    private static async Task<ToolResult> RunToolAsync(
        string toolPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool allowNonZeroExit = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = toolPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {toolPath}.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        string output = await outputTask.ConfigureAwait(false);
        string error = await errorTask.ConfigureAwait(false);

        if (!allowNonZeroExit && process.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(error) ? output : error;
            throw new InvalidOperationException(
                $"{Path.GetFileName(toolPath)} failed with exit code {process.ExitCode}: {detail.Trim()}");
        }

        return new ToolResult(process.ExitCode, output, error);
    }

    private static MacOSUpdateInstallResult Fallback(UpdateInfo update, string reason)
        => new(
            MacOSUpdateInstallStatus.FallbackRequired,
            null,
            CreateFallback(update.DownloadUrl, reason));

    private static MacOSUpdateFallback CreateFallback(string zipUrl, string reason)
        => new(TryBuildDmgUri(zipUrl), ReleasesPage, reason);

    internal static Uri? TryBuildDmgUri(string zipUrl)
    {
        if (!Uri.TryCreate(zipUrl, UriKind.Absolute, out Uri? uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return null;

        var builder = new UriBuilder(uri)
        {
            Path = uri.AbsolutePath[..^4] + ".dmg",
        };
        return builder.Uri;
    }

    private static bool HasWindowsDrivePrefix(string path)
        => path.Length >= 3
           && char.IsAsciiLetter(path[0])
           && path[1] == ':'
           && path[2] == '/';

    private static void CleanupPath(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup must not hide the verification/staging result.
        }
    }

    private readonly record struct ToolResult(int ExitCode, string StandardOutput, string StandardError);

    private const string HelperScript = """
        #!/bin/zsh
        set -u

        PID="$1"
        CURRENT="$2"
        STAGED="$3"
        BACKUP="$4"
        LOG="$5"
        HELPER_DIR="$6"

        exec >>"$LOG" 2>&1
        echo "NMSE updater helper started"

        for _ in {1..300}; do
          if ! /bin/kill -0 "$PID" 2>/dev/null; then
            break
          fi
          /bin/sleep 1
        done

        if /bin/kill -0 "$PID" 2>/dev/null; then
          echo "Timed out waiting for NMSE to exit"
          /bin/rm -rf "$STAGED"
          /bin/rm -rf "$HELPER_DIR"
          exit 1
        fi

        if ! /bin/mv "$CURRENT" "$BACKUP"; then
          echo "Could not move the installed bundle to its rollback path"
          /bin/rm -rf "$STAGED"
          /bin/rm -rf "$HELPER_DIR"
          exit 1
        fi

        if ! /bin/mv "$STAGED" "$CURRENT"; then
          echo "Could not install the staged bundle; restoring the original"
          /bin/mv "$BACKUP" "$CURRENT" || true
          /usr/bin/open -n "$CURRENT" || true
          /bin/rm -rf "$HELPER_DIR"
          exit 1
        fi

        if /usr/bin/open -n "$CURRENT"; then
          /bin/rm -rf "$BACKUP"
          /bin/rm -f "$LOG"
          /bin/rm -rf "$HELPER_DIR"
          exit 0
        fi

        echo "The updated bundle did not relaunch; rolling back"
        FAILED="${STAGED}.failed"
        /bin/mv "$CURRENT" "$FAILED" || true
        if /bin/mv "$BACKUP" "$CURRENT"; then
          /usr/bin/open -n "$CURRENT" || true
        fi
        /bin/rm -rf "$FAILED"
        /bin/rm -rf "$HELPER_DIR"
        exit 1
        """;
}

public enum MacOSUpdateInstallStatus
{
    HelperStarted,
    FallbackRequired,
    UnsupportedPlatform,
}

public sealed record MacOSUpdateFallback(Uri? DmgUri, Uri ReleasesPageUri, string Reason)
{
    public Uri PreferredUri => DmgUri ?? ReleasesPageUri;
}

public sealed record MacOSUpdateInstallResult(
    MacOSUpdateInstallStatus Status,
    string? HelperPath,
    MacOSUpdateFallback? Fallback);
