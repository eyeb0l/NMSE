using Avalonia.Threading;
using Avalonia.Controls;
using NMSE.Config;
using NMSE.Core;
using NMSE.Data;
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
        _externalFiles.Capture(_currentSession is { } session
            ? ExternalFileMonitor.GetPaths(session, _accountRewardsPanel?.AccountSourcePath) : []);
        _externalTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            async (_, _) => await CheckExternalFilesAsync());
        _externalTimer.Start();
    }

    private async Task CheckExternalFilesAsync()
    {
        if (LoadingOverlay.IsVisible || _externalPromptOpen || _currentSession is null) return;
        var change = _externalFiles.Check();
        var action = ExternalChangeDecision.Decide(change.Changed, change.Deleted, GetExternalChangeMode());
        if (action == ExternalChangeDecision.Action.None) return;
        // Keep the retained copy after a declined prompt without asking again on every autosave.
        if (_externalDiverged && action == ExternalChangeDecision.Action.Prompt) return;
        _externalDiverged = true;
        _externalPromptOpen = true;
        try
        {
            bool dirty = _isDirty || _rawJsonPanel?.HasPendingTextEdits == true;
            if (action == ExternalChangeDecision.Action.Reload)
            {
                await ReloadExternalChangesAsync(dirty);
                return;
            }
            if (change.Deleted)
            {
                await ShowMessageAsync(UiStrings.Get("external.deleted_title"), UiStrings.Get("external.deleted_message"));
                return;
            }
            bool reload = await ShowMessageAsync(UiStrings.Get("external.changed_title"),
                UiStrings.Get(dirty ? "external.changed_message" : "external.changed_clean_message"), showCancel: true);
            if (reload) await ReloadExternalChangesAsync(dirty);
            else SetStatus(UiStrings.Get("status.external_diverged"));
        }
        finally { _externalPromptOpen = false; }
    }

    private async Task ReloadExternalChangesAsync(bool discardedEdits)
    {
        if (_currentSession is null) return;
        await LoadSaveDataAsync(_currentSession);
        if (!_externalDiverged)
            SetStatus(UiStrings.Get(discardedEdits ? "status.external_reloaded_discarded" : "status.external_reloaded"));
    }

    private static ExternalChangeDecision.Mode GetExternalChangeMode() => AppConfig.Instance.ExternalChangeMode switch
    {
        "auto" => ExternalChangeDecision.Mode.AutoReload,
        "ignore" => ExternalChangeDecision.Mode.Ignore,
        _ => ExternalChangeDecision.Mode.Prompt,
    };

    private readonly NativeMenuItem _watchNativeMenu = new();
    private readonly MenuItem _watchMenu = new();
    private readonly Dictionary<string, (NativeMenuItem Native, MenuItem Window)> _watchItems = new();

    private void ConfigureWatchMenus()
    {
        var native = new NativeMenu();
        _watchNativeMenu.Menu = native;
        string[] modes = ["prompt", "auto", "ignore"];
        foreach (string mode in modes)
        {
            var nativeItem = new NativeMenuItem { ToggleType = MenuItemToggleType.Radio };
            var windowItem = new MenuItem { ToggleType = MenuItemToggleType.Radio, GroupName = "saveWatching" };
            nativeItem.Click += (_, _) => SetExternalChangeMode(mode);
            windowItem.Click += (_, _) => SetExternalChangeMode(mode);
            native.Items.Add(nativeItem); _watchMenu.Items.Add(windowItem);
            _watchItems[mode] = (nativeItem, windowItem);
        }
        if (NativeMenu.GetMenu(this)?.Items[1] is NativeMenuItem { Menu: { } edit })
        {
            edit.Items.Add(new NativeMenuItemSeparator()); edit.Items.Add(_watchNativeMenu);
        }
        if (this.FindControl<Menu>("WindowMenu")?.Items[1] is MenuItem windowEdit)
        {
            windowEdit.Items.Add(new Separator()); windowEdit.Items.Add(_watchMenu);
        }
    }

    private void SetExternalChangeMode(string mode)
    {
        AppConfig.Instance.ExternalChangeMode = mode;
        AppConfig.Instance.Save();
        UpdateWatchMenus();
    }

    private void UpdateWatchMenus()
    {
        _watchNativeMenu.Header = UiStrings.Get("menu.edit.save_watching").Replace("&", string.Empty);
        _watchMenu.Header = _watchNativeMenu.Header;
        string current = AppConfig.Instance.ExternalChangeMode;
        if (!_watchItems.ContainsKey(current)) current = "prompt";
        foreach (var (mode, items) in _watchItems)
        {
            items.Native.Header = UiStrings.Get("settings.watch." + mode).Replace("&", string.Empty);
            items.Window.Header = items.Native.Header;
            items.Native.IsChecked = items.Window.IsChecked = mode == current;
        }
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
