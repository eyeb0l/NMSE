using Avalonia.Threading;
using NMSE.Core;
using NMSE.UI.Avalonia.Coordination;

namespace NMSE.UI.Avalonia.Views;

public sealed partial class MainWindow
{
    private readonly ExternalFileMonitor _externalFiles = new();
    private DispatcherTimer? _externalTimer;
    private bool _externalPromptOpen;
    private bool _externalDiverged;

    private void CaptureExternalFiles()
    {
        _externalDiverged = false;
        var paths = new List<string>();
        if (_currentSession is { Kind: SaveSessionKind.File } session)
        {
            paths.Add(session.SourcePath);
            string directory = Path.GetDirectoryName(session.SourcePath)!;
            paths.Add(Path.Combine(directory, "mf_" + Path.GetFileName(session.SourcePath)));
            if (_accountRewardsPanel?.AccountSourcePath is { Length: > 0 } account) paths.Add(account);
        }
        _externalFiles.Capture(paths);
        _externalTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            async (_, _) => await CheckExternalFilesAsync());
        _externalTimer.Start();
    }

    private async Task CheckExternalFilesAsync()
    {
        if (LoadingOverlay.IsVisible || _externalPromptOpen || _currentSession is null) return;
        var change = _externalFiles.Check();
        var action = ExternalChangeDecision.Decide(change.Changed, change.Deleted,
            _isDirty || _rawJsonPanel?.HasPendingTextEdits == true);
        if (action == ExternalChangeDecision.Action.None) return;
        _externalDiverged = true;
        _externalPromptOpen = true;
        try
        {
            if (change.Deleted)
            {
                await ShowMessageAsync("Save changed outside NMSE",
                    "A loaded file was deleted. Your edits are still available; use Save As to keep a copy.");
                return;
            }
            bool reload = await ShowMessageAsync("Save changed outside NMSE",
                action == ExternalChangeDecision.Action.Prompt
                    ? "The save or account file changed on disk. Reload and discard your unsaved edits?"
                    : "The save or account file changed on disk. Reload the updated files?", showCancel: true);
            if (reload) await LoadSaveDataAsync(_currentSession);
            else SetStatus("External changes detected; your edits are retained.");
        }
        finally { _externalPromptOpen = false; }
    }

    private async Task<bool> ConfirmExternalOverwriteAsync(SaveSession destination)
    {
        if (_currentSession is null) return true;
        var change = _externalFiles.Check();
        _externalDiverged |= change.Changed || change.Deleted;
        // Save As still writes the loaded account store, so it needs the same conflict check.
        return !_externalDiverged || await ShowMessageAsync("Save changed outside NMSE",
            "The save or account file changed on disk. Overwrite it with your edits?", showCancel: true);
    }
}
