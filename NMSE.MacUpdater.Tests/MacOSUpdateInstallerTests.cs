using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using NMSE.Core;

namespace NMSE.MacUpdater.Tests;

public sealed class MacOSUpdateInstallerTests
{
    [Fact]
    public void FindContainingAppBundle_FindsBundleAboveMacOsDirectory()
    {
        string bundle = Path.Combine(Path.GetTempPath(), "NMSE.app");
        string runtimeDirectory = Path.Combine(bundle, "Contents", "MacOS");

        Assert.Equal(
            Path.GetFullPath(bundle),
            MacOSUpdateInstaller.FindContainingAppBundle(runtimeDirectory));
    }

    [Fact]
    public void FindContainingAppBundle_PortableDirectory_ReturnsNull()
    {
        string portableDirectory = Path.Combine(Path.GetTempPath(), "NMSE", "bin");

        Assert.Null(MacOSUpdateInstaller.FindContainingAppBundle(portableDirectory));
    }

    [Fact]
    public void TryBuildDmgUri_ArchitectureZip_ReturnsMatchingDmg()
    {
        Uri? result = MacOSUpdateInstaller.TryBuildDmgUri(
            "https://github.com/vectorcmdr/NMSE/releases/download/v1.2.8/NMSE-1.2.8-macOS-arm64.zip");

        Assert.Equal(
            "https://github.com/vectorcmdr/NMSE/releases/download/v1.2.8/NMSE-1.2.8-macOS-arm64.dmg",
            result?.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://example.com/NMSE.zip")]
    [InlineData("http://github.com/vectorcmdr/NMSE/NMSE.zip")]
    [InlineData("https://github.com/vectorcmdr/NMSE/NMSE.exe")]
    public void TryBuildDmgUri_UntrustedOrNonZipUrl_ReturnsNull(string url)
    {
        Assert.Null(MacOSUpdateInstaller.TryBuildDmgUri(url));
    }

    [Fact]
    public void ValidateArchiveLayout_ExpectedBundle_IsAccepted()
    {
        using TemporaryZip zip = CreateZip(
            ("NMSE.app/Contents/Info.plist", "plist"),
            ("NMSE.app/Contents/MacOS/NMSE.Avalonia", "binary"),
            ("__MACOSX/._NMSE.app", "metadata"));

        MacOSUpdateInstaller.ValidateArchiveLayout(zip.Path);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("NMSE.app/../../escape")]
    [InlineData("/tmp/escape")]
    [InlineData("C:\\escape")]
    [InlineData("Other.app/Contents/Info.plist")]
    public void ValidateArchiveLayout_UnsafeOrUnexpectedPath_IsRejected(string entryName)
    {
        using TemporaryZip zip = CreateZip((entryName, "payload"));

        Assert.Throws<InvalidDataException>(
            () => MacOSUpdateInstaller.ValidateArchiveLayout(zip.Path));
    }

    [Fact]
    public void ValidateArchiveLayout_SymbolicLink_IsRejected()
    {
        string path = NewZipPath();
        using (FileStream stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("NMSE.app/Contents/MacOS/link");
            entry.ExternalAttributes = unchecked((int)0xA1FF0000);
            using StreamWriter writer = new(entry.Open());
            writer.Write("target");
        }

        using var zip = new TemporaryZip(path);
        Assert.Throws<InvalidDataException>(
            () => MacOSUpdateInstaller.ValidateArchiveLayout(zip.Path));
    }

    [Fact]
    public void ExternalHelper_HasValidZshSyntax()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        FieldInfo? field = typeof(MacOSUpdateInstaller).GetField(
            "HelperScript",
            BindingFlags.Static | BindingFlags.NonPublic);
        string script = Assert.IsType<string>(field?.GetRawConstantValue());
        string path = Path.Combine(Path.GetTempPath(), $"nmse-helper-{Guid.NewGuid():N}");

        try
        {
            File.WriteAllText(path, script);
            using Process process = Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/zsh",
                UseShellExecute = false,
                RedirectStandardError = true,
                ArgumentList = { "-n", path },
            })!;
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.True(process.ExitCode == 0, error);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    private static TemporaryZip CreateZip(params (string Name, string Content)[] entries)
    {
        string path = NewZipPath();
        using (FileStream stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach ((string name, string content) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name);
                using StreamWriter writer = new(entry.Open());
                writer.Write(content);
            }
        }

        return new TemporaryZip(path);
    }

    private static string NewZipPath()
        => Path.Combine(Path.GetTempPath(), $"nmse-mac-updater-{Guid.NewGuid():N}.zip");

    private sealed class TemporaryZip : IDisposable
    {
        public TemporaryZip(string path) => Path = path;
        public string Path { get; }
        public void Dispose()
        {
            try { File.Delete(Path); } catch { }
        }
    }
}
