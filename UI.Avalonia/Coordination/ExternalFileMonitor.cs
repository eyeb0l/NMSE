using NMSE.IO;

namespace NMSE.UI.Avalonia.Coordination;

/// <summary>Tracks the files belonging to one save, including a sibling macOS MXML.</summary>
public sealed class ExternalFileMonitor
{
    private readonly Dictionary<string, (bool Exists, DateTime Written, long Length)> _files =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public static IReadOnlyList<string> GetPaths(SaveSession session, string? accountPath)
    {
        var paths = new List<string>();
        void Add(string? path) { if (!string.IsNullOrWhiteSpace(path)) paths.Add(path); }
        if (session.Kind == SaveSessionKind.XboxContainer)
        {
            string directory = File.Exists(session.SourcePath) ? Path.GetDirectoryName(session.SourcePath)! : session.SourcePath;
            var (data, meta) = SaveFileManager.GetXboxSlotFiles(directory, session.ContainerIdentifier!);
            Add(data); Add(meta);
            var (account, accountMeta) = SaveFileManager.GetXboxSlotFiles(directory, ContainersIndexManager.AccountDataIdentifier);
            Add(account); Add(accountMeta);
            string index = Path.Combine(directory, "containers.index");
            if (File.Exists(index)) Add(index);
        }
        else
        {
            Add(session.SourcePath);
            if (session.Kind == SaveSessionKind.File)
                Add(Path.Combine(Path.GetDirectoryName(session.SourcePath)!, "mf_" + Path.GetFileName(session.SourcePath)));
            Add(accountPath);
        }
        return paths;
    }

    public void Capture(IEnumerable<string> paths)
    {
        _files.Clear();
        foreach (string path in paths) _files[Path.GetFullPath(path)] = Read(path);
    }

    public (bool Changed, bool Deleted) Check()
    {
        bool changed = false, deleted = false;
        foreach (string path in _files.Keys.ToArray())
        {
            var previous = _files[path];
            var current = Read(path);
            changed |= previous != current;
            deleted |= previous.Exists && !current.Exists;
            _files[path] = current;
        }
        return (changed, deleted);
    }

    private static (bool Exists, DateTime Written, long Length) Read(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? (true, info.LastWriteTimeUtc, info.Length) : default;
        }
        catch (IOException) { return default; }
        catch (UnauthorizedAccessException) { return default; }
    }
}
