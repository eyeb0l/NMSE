using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.LogicalTree;
using NMSE.UI.Avalonia.Inventory;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using NMSE.Config;
using NMSE.Core;
using NMSE.Data;
using NMSE.IO;
using NMSE.Models;
using NMSE.UI.Avalonia.Contracts;
using NMSE.UI.Avalonia.Controls;
using NMSE.UI.Avalonia.Coordination;
using NMSE.UI.Avalonia.Dialogs;
using NMSE.UI.Avalonia.Panels.ExportSettings;
using NMSE.UI.Avalonia.Panels.Exosuit;
using NMSE.UI.Avalonia.Panels.Fleet;
using NMSE.UI.Avalonia.Panels.AccountRewards;
using NMSE.UI.Avalonia.Panels.BasesStorage;
using NMSE.UI.Avalonia.Panels.ByteBeats;
using NMSE.UI.Avalonia.Panels.Catalogue;
using NMSE.UI.Avalonia.Panels.Companions;
using NMSE.UI.Avalonia.Panels.Exocraft;
using NMSE.UI.Avalonia.Panels.Milestones;
using NMSE.UI.Avalonia.Panels.Multitools;
using NMSE.UI.Avalonia.Panels.Player;
using NMSE.UI.Avalonia.Panels.RawJson;
using NMSE.UI.Avalonia.Panels.Settlements;
using NMSE.UI.Avalonia.Panels.Starships;
using NMSE.UI.Avalonia.Platform;

namespace NMSE.UI.Avalonia.Views;

public sealed partial class MainWindow : Window
{
    private sealed record EditorLock(
        Control Editor,
        NoSaveOverlay Overlay,
        bool IsEnabledWhenUnlocked);

    private const string GitHubUrl = "https://github.com/vectorcmdr/NMSE";
    private const string UserGuideUrl = "https://github.com/vectorcmdr/NMSE/wiki";
    private const string SponsorUrl = "https://github.com/sponsors/vectorcmdr";
    private const string ReleaseNotesUrl = "https://github.com/vectorcmdr/NMSE/releases/latest";

    private readonly List<ISaveEditorPanel> _loadedPanels = [];
    private readonly List<EditorLock> _editorLocks = [];
    private readonly AvaloniaResourceCatalog _resources = new();
    private readonly SaveEditorCoordinator _panelCoordinator = new();
    private readonly IDesktopServices _desktopServices;
    private JsonObject? _currentSaveData;
    private JsonObject? _rawJsonBaseline;
    private MilestonePanel? _milestonePanel;
    private ExportSettingsPanel? _exportSettingsPanel;
    private RawJsonPanel? _rawJsonPanel;
    private ExosuitPanel? _exosuitPanel;
    private ByteBeatPanel? _byteBeatPanel;
    private SettlementPanel? _settlementPanel;
    private AccountRewardsPanel? _accountRewardsPanel;
    private FleetPanel? _fleetPanel;
    private MultitoolPanel? _multitoolPanel;
    private StarshipPanel? _starshipPanel;
    private CompanionPanel? _companionPanel;
    private BasesStoragePanel? _basesStoragePanel;
    private ExocraftPanel? _exocraftPanel;
    private CataloguePanel? _cataloguePanel;
    private PlayerPanel? _playerPanel;
    private IReadOnlyList<SaveSlotChoice> _saveSlots = [];
    private string? _currentFilePath;
    private SaveSession? _currentSession;
    private bool _isDirty;
    private bool _allowClose;
    private bool _updatingPathSelectors;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        MainTabs = this.FindControl<TabControl>(nameof(MainTabs))!;
        DirectoryPathBox = this.FindControl<TextBox>(nameof(DirectoryPathBox))!;
        BackupPathBox = this.FindControl<TextBox>(nameof(BackupPathBox))!;
        RecentDirectoryCombo = this.FindControl<ComboBox>(nameof(RecentDirectoryCombo))!;
        RecentBackupCombo = this.FindControl<ComboBox>(nameof(RecentBackupCombo))!;
        DatabaseCountText = this.FindControl<TextBlock>(nameof(DatabaseCountText))!;
        SaveSlotCombo = this.FindControl<ComboBox>(nameof(SaveSlotCombo))!;
        SaveFileCombo = this.FindControl<ComboBox>(nameof(SaveFileCombo))!;
        SaveButton = this.FindControl<Button>(nameof(SaveButton))!;
        SaveMenuItem = this.FindControl<MenuItem>(nameof(SaveMenuItem))!;
        SaveAsMenuItem = this.FindControl<MenuItem>(nameof(SaveAsMenuItem))!;
        LoadingOverlay = this.FindControl<Border>(nameof(LoadingOverlay))!;
        LoadingText = this.FindControl<TextBlock>(nameof(LoadingText))!;
        StatusText = this.FindControl<TextBlock>(nameof(StatusText))!;
        LoadedFileText = this.FindControl<TextBlock>(nameof(LoadedFileText))!;
        MilestonesTab = this.FindControl<TabItem>(nameof(MilestonesTab))!;
        ExportTab = this.FindControl<TabItem>(nameof(ExportTab))!;
        RawJsonTab = this.FindControl<TabItem>(nameof(RawJsonTab))!;
        ExosuitTab = this.FindControl<TabItem>(nameof(ExosuitTab))!;
        ByteBeatsTab = this.FindControl<TabItem>(nameof(ByteBeatsTab))!;
        SettlementsTab = this.FindControl<TabItem>(nameof(SettlementsTab))!;
        AccountTab = this.FindControl<TabItem>(nameof(AccountTab))!;
        FleetTab = this.FindControl<TabItem>(nameof(FleetTab))!;
        MultitoolsTab = this.FindControl<TabItem>(nameof(MultitoolsTab))!;
        StarshipsTab = this.FindControl<TabItem>(nameof(StarshipsTab))!;
        CompanionsTab = this.FindControl<TabItem>(nameof(CompanionsTab))!;
        BasesTab = this.FindControl<TabItem>(nameof(BasesTab))!;
        ExocraftTab = this.FindControl<TabItem>(nameof(ExocraftTab))!;
        CatalogueTab = this.FindControl<TabItem>(nameof(CatalogueTab))!;
        PlayerTab = this.FindControl<TabItem>(nameof(PlayerTab))!;
        _desktopServices = new AvaloniaDesktopServices(this);
        this.FindControl<Menu>("WindowMenu")!.IsVisible = !OperatingSystem.IsMacOS();
        ConfigureWatchMenus();
        Opened += OnOpened;
        Closing += OnClosing;
        Closed += (_, _) => { _externalTimer?.Stop(); _resources.Dispose(); };
        MainTabs.SelectionChanged += OnTabSelectionChanged;
        KeyDown += OnWindowKeyDown;
        _panelCoordinator.DataModified += (_, _) => MarkDirty();
        UpdateSaveActions();
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        SetLoading(true, "Loading application resources…");
        try
        {
            AppConfig.Instance.Initialize();
            MigrateMacOsSaveDirectorySetting();
            ApplyWindowState();
            ApplyTheme(AppConfig.Instance.Theme, save: false);
            RefreshPathSelectors();
            var progress = new Progress<ResourceLoadProgress>(value =>
            {
                LoadingText.Text = value.Message;
                SetStatus(value.Message);
            });
            await _resources.InitializeAsync(progress);
            _resources.ApplyLanguage(AppConfig.Instance.Language);
            InitializePortedPanels();
            ApplyLocalisation();
            DatabaseCountText.Text = UiStrings.Format("status.total_db_items", _resources.TotalItemCount);

            string? initialDirectory = RecentDirectoryCombo.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            {
                DirectoryPathBox.Text = initialDirectory;
                PopulateSaveFiles(initialDirectory);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Startup warning: {ex.Message}");
        }
        finally
        {
            SetLoading(false);
        }
    }

    private static void MigrateMacOsSaveDirectorySetting()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        bool changed = false;
        if (!string.IsNullOrWhiteSpace(AppConfig.Instance.LastDirectory))
        {
            string normalized = SaveFileManager.NormalizeMacOsSaveDirectory(AppConfig.Instance.LastDirectory);
            if (!string.Equals(normalized, AppConfig.Instance.LastDirectory, StringComparison.Ordinal))
            {
                AppConfig.Instance.LastDirectory = normalized;
                changed = true;
            }
        }

        List<string> recent = AppConfig.Instance.RecentDirectories;
        List<string> normalizedRecent = recent
            .Select(SaveFileManager.NormalizeMacOsSaveDirectory)
            .Distinct(StringComparer.Ordinal)
            .Take(AppConfig.MaxRecentDirectories)
            .ToList();
        if (!recent.SequenceEqual(normalizedRecent, StringComparer.Ordinal))
        {
            AppConfig.Instance.RecentDirectories = normalizedRecent;
            changed = true;
        }

        if (changed)
            AppConfig.Instance.Save();
    }

    private void ApplyLocalisation()
    {
        UpdateWatchMenus();
        string[] keys =
        [
            "tab.player", "tab.exosuit", "tab.multitools", "tab.starships", "tab.fleet",
            "tab.exocraft", "tab.companions", "tab.bases_storage", "tab.discoveries",
            "tab.milestones", "tab.settlements", "tab.bytebeats", "tab.account_rewards",
            "tab.export_settings", "tab.raw_json_editor", "tab.database_search",
        ];

        if (this.FindControl<TabControl>("MainTabs") is { } tabs)
        {
            for (int i = 0; i < tabs.ItemCount && i < keys.Length; i++)
            {
                if ((tabs.Items[i] as TabItem ?? tabs.ContainerFromIndex(i) as TabItem) is { } item)
                    item.Header = UiStrings.Get(keys[i]);
            }
        }

        _exportSettingsPanel?.ApplyUiLocalisation();
        _rawJsonPanel?.ApplyUiLocalisation();
        (this.FindControl<TabItem>("DatabaseSearchTab")?.Content as DatabaseSearchPanel)?.ApplyUiLocalisation();
        foreach (EditorLock editorLock in _editorLocks)
            editorLock.Overlay.RefreshLocalisation();
    }

    private void InitializePortedPanels()
    {
        if (_playerPanel is null)
        {
            _playerPanel = new PlayerPanel(_desktopServices, _resources.Icons);
            _playerPanel.SetLocalisationService(_resources.Localisation);
            InstallEditorLock(PlayerTab, _playerPanel);
            _panelCoordinator.Register(0, _playerPanel);
            _playerPanel.GoToJsonRequested += OnGoToJsonRequested;
            _playerPanel.ReloadRequested += (_, _) => OnReload(this, EventArgs.Empty);
            _playerPanel.SaveUtilityCompleted += (_, _) =>
            {
                string? directory = DirectoryPathBox.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    PopulateSaveFiles(directory);
            };
        }

        if (_milestonePanel is null)
        {
            _milestonePanel = new MilestonePanel(_resources);
            InstallEditorLock(MilestonesTab, _milestonePanel);
            _panelCoordinator.Register(9, _milestonePanel);
            _milestonePanel.GoToJsonRequested += OnGoToJsonRequested;
        }

        if (_exosuitPanel is null)
        {
            _exosuitPanel = new ExosuitPanel(_resources.Items, _resources.Icons, _desktopServices);
            InstallEditorLock(ExosuitTab, _exosuitPanel);
            _panelCoordinator.Register(1, _exosuitPanel);
            _exosuitPanel.GoToJsonRequested += OnGoToJsonRequested;
            _exosuitPanel.CrossInventoryTransferCompleted += (_, _) =>
                _panelCoordinator.CommitLoadedPanels();
                _panelCoordinator.RefreshLoaded(3, 4, 5, 7);
        }

        if (_multitoolPanel is null)
        {
            _multitoolPanel = new MultitoolPanel(_resources.Items, _resources.Icons, _desktopServices);
            InstallEditorLock(MultitoolsTab, _multitoolPanel);
            _panelCoordinator.Register(2, _multitoolPanel);
            _multitoolPanel.GoToJsonRequested += OnGoToJsonRequested;
        }

        if (_starshipPanel is null)
        {
            _starshipPanel = new StarshipPanel(_resources.Items, _resources.Icons, _desktopServices);
            InstallEditorLock(StarshipsTab, _starshipPanel);
            _panelCoordinator.Register(3, _starshipPanel);
            _starshipPanel.GoToJsonRequested += OnGoToJsonRequested;
            _starshipPanel.CrossInventoryTransferCompleted += (_, _) =>
                _panelCoordinator.CommitLoadedPanels();
                _panelCoordinator.RefreshLoaded(1, 4, 5, 7);
        }

        if (_exocraftPanel is null)
        {
            _exocraftPanel = new ExocraftPanel(
                _resources.Items,
                _resources.Icons,
                new ExocraftDesktopActions(
                    request => PickOpenFileAsync(request.Title, request.Extensions),
                    request => PickSaveFileAsync(request.Title, request.SuggestedFileName, request.Extensions),
                    (title, message) => ShowMessageAsync(title, message, showCancel: true),
                    async (title, message) => { await ShowMessageAsync(title, message); },
                    async (title, message) => { await ShowMessageAsync(title, message); }));
            InstallEditorLock(ExocraftTab, _exocraftPanel);
            _panelCoordinator.Register(5, _exocraftPanel);
            _exocraftPanel.GoToJsonRequested += OnGoToJsonRequested;
            _exocraftPanel.CrossInventoryTransferCompleted += (_, _) =>
                _panelCoordinator.CommitLoadedPanels();
                _panelCoordinator.RefreshLoaded(1, 3, 4, 7);
        }

        if (_exportSettingsPanel is null)
        {
            _exportSettingsPanel = new ExportSettingsPanel();
            ExportTab.Content = _exportSettingsPanel;
        }

        if (_rawJsonPanel is null)
        {
            _rawJsonPanel = new RawJsonPanel
            {
                ImportJsonAsync = ImportJsonAsync,
                ExportJsonAsync = ExportJsonAsync,
                ConfirmAsync = async (title, message, _) =>
                    await ShowMessageAsync(title, message, showCancel: true),
            };
            InstallEditorLock(RawJsonTab, _rawJsonPanel);
            _panelCoordinator.Register(14, _rawJsonPanel);
        }

        if (_byteBeatPanel is null)
        {
            _byteBeatPanel = new ByteBeatPanel(new ByteBeatDesktopActions(
                request => PickOpenFileAsync(request.Title, request.Extensions),
                request => PickSaveFileAsync(request.Title, request.SuggestedFileName, request.Extensions),
                (title, message) => ShowMessageAsync(title, message, showCancel: true),
                async (title, message) => { await ShowMessageAsync(title, message); },
                async (title, message) => { await ShowMessageAsync(title, message); }));
            InstallEditorLock(ByteBeatsTab, _byteBeatPanel);
            _panelCoordinator.Register(11, _byteBeatPanel);
            _byteBeatPanel.GoToJsonRequested += OnGoToJsonRequested;
        }

        if (_settlementPanel is null)
        {
            _settlementPanel = new SettlementPanel(_resources, new SettlementDesktopActions(
                request => PickOpenFileAsync(request.Title, request.Extensions),
                request => PickSaveFileAsync(request.Title, request.SuggestedFileName, request.Extensions),
                (title, message) => ShowMessageAsync(title, message, showCancel: true),
                async (title, message) => { await ShowMessageAsync(title, message); }));
            InstallEditorLock(SettlementsTab, _settlementPanel);
            _panelCoordinator.Register(10, _settlementPanel);
            _settlementPanel.GoToJsonRequested += OnGoToJsonRequested;
        }

        if (_accountRewardsPanel is null)
        {
            _accountRewardsPanel = new AccountRewardsPanel(_resources, new AccountRewardsDesktopActions(
                request => PickOpenFileAsync(request.Title, request.Extensions),
                async (title, message) => { await ShowMessageAsync(title, message); },
                async (title, message) => { await ShowMessageAsync(title, message); }));
            InstallEditorLock(AccountTab, _accountRewardsPanel);
            _panelCoordinator.Register(12, _accountRewardsPanel);
        }

        if (_fleetPanel is null)
        {
            _fleetPanel = new FleetPanel(_resources.Items, _resources.Icons)
            {
                ImportAsync = ImportFleetAsync,
                ExportAsync = ExportFleetAsync,
                ConfirmAsync = async (title, message, _) =>
                    await ShowMessageAsync(title, message, showCancel: true),
            };
            InstallEditorLock(FleetTab, _fleetPanel);
            _panelCoordinator.Register(4, _fleetPanel);
            _fleetPanel.GoToJsonRequested += OnGoToJsonRequested;
        }

        if (_companionPanel is null)
        {
            _companionPanel = new CompanionPanel(new CompanionDesktopActions(
                request => PickOpenFileAsync(request.Title, request.Extensions),
                request => PickSaveFileAsync(request.Title, request.SuggestedFileName, request.Extensions),
                (title, message) => ShowMessageAsync(title, message, showCancel: true),
                async (title, message) => { await ShowMessageAsync(title, message); },
                async (title, message) => { await ShowMessageAsync(title, message); },
                url => _desktopServices.OpenUrlAsync(new Uri(url)),
                colour => ColourDialogWindow.ShowAsync(this, colour),
                (title, choices) => ChoiceDialogWindow.ShowAsync(this, title, choices)));
            InstallEditorLock(CompanionsTab, _companionPanel);
            _panelCoordinator.Register(6, _companionPanel);
            _companionPanel.GoToJsonRequested += OnGoToJsonRequested;
            _companionPanel.ExosuitCargoModified += (_, _) => _exosuitPanel?.RefreshAfterExternalTransfer();
        }

        if (_basesStoragePanel is null)
        {
            _basesStoragePanel = new BasesStoragePanel(_resources.Items, _resources.Icons)
            {
                ImportAsync = ImportBasesStorageAsync,
                ExportAsync = ExportBasesStorageAsync,
                ConfirmAsync = async (title, message, _) =>
                    await ShowMessageAsync(title, message, showCancel: true),
            };
            InstallEditorLock(BasesTab, _basesStoragePanel);
            _panelCoordinator.Register(7, _basesStoragePanel);
            _basesStoragePanel.GoToJsonRequested += OnGoToJsonRequested;
        }

        if (_cataloguePanel is null)
        {
            _cataloguePanel = new CataloguePanel(
                _resources.Items,
                _resources.Recipes,
                _resources.Words,
                _resources.Icons)
            {
                ImportAsync = ImportCatalogueAsync,
                ExportAsync = ExportCatalogueAsync,
                ConfirmAsync = async (title, message, _) =>
                    await ShowMessageAsync(title, message, showCancel: true),
                MessageAsync = async (title, message, _) =>
                    await ShowMessageAsync(title, message),
                OpenUrlAsync = OpenCatalogueUrlAsync,
            };
            _cataloguePanel.SetLocalisationService(_resources.Localisation);
            InstallEditorLock(CatalogueTab, _cataloguePanel);
            _panelCoordinator.Register(8, _cataloguePanel, purgeWhenInactive: true);
            _cataloguePanel.GoToJsonRequested += OnGoToJsonRequested;
        }

        var searchTab = this.FindControl<TabItem>("DatabaseSearchTab");
        if (searchTab != null && searchTab.Content == null)
            searchTab.Content = new DatabaseSearchPanel(_resources.Items);
        UpdateEditorLockState();
    }

    /// <summary>
    /// Wraps a save-backed editor with a localized overlay while keeping the tab
    /// header selectable. Export Settings is intentionally installed directly and
    /// remains available before a save is loaded.
    /// </summary>
    private void InstallEditorLock(TabItem tab, Control editor)
    {
        var overlay = new NoSaveOverlay();
        var host = new Grid();
        host.Children.Add(editor);
        host.Children.Add(overlay);
        tab.Content = host;
        _editorLocks.Add(new EditorLock(editor, overlay, editor.IsEnabled));
    }

    private void UpdateEditorLockState()
    {
        bool locked = _currentSaveData is null;
        foreach (EditorLock editorLock in _editorLocks)
        {
            editorLock.Editor.IsEnabled = locked ? false : editorLock.IsEnabledWhenUnlocked;
            editorLock.Overlay.IsVisible = locked;
        }
    }

    private async void OnOpenDirectory(object? sender, EventArgs e)
    {
        string? start = DirectoryPathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start)) start = SaveFileManager.FindDefaultSaveDirectory();
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select No Man's Sky save directory",
            AllowMultiple = false,
            SuggestedStartLocation = start is null ? null : await StorageProvider.TryGetFolderFromPathAsync(start),
        });

        string? path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        path = ResolveToolbarDirectory(path);

        DirectoryPathBox.Text = path;
        AppConfig.Instance.AddRecentDirectory(path, SaveFileManager.FindDefaultSaveDirectory());
        AppConfig.Instance.Save();
        RefreshPathSelectors();
        PopulateSaveFiles(path);
    }

    private async void OnBrowseBackup(object? sender, RoutedEventArgs e)
    {
        string? path = await _desktopServices.PickFolderAsync("Select NMSE backup directory");
        if (string.IsNullOrWhiteSpace(path))
            return;
        AppConfig.Instance.AddRecentBackupDirectory(path);
        AppConfig.Instance.Save();
        RefreshPathSelectors();
    }

    private void OnRecentDirectoryChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingPathSelectors || RecentDirectoryCombo.SelectedItem is not string path || !Directory.Exists(path))
            return;
        path = ResolveToolbarDirectory(path);
        DirectoryPathBox.Text = path;
        AppConfig.Instance.AddRecentDirectory(path, SaveFileManager.FindDefaultSaveDirectory());
        AppConfig.Instance.Save();
        PopulateSaveFiles(path);
    }

    private void OnRecentBackupChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingPathSelectors || RecentBackupCombo.SelectedItem is not string path)
            return;
        BackupPathBox.Text = path;
        AppConfig.Instance.AddRecentBackupDirectory(path);
        AppConfig.Instance.Save();
    }

    private void OnDirectoryPathCommitted(object? sender, RoutedEventArgs e)
    {
        string? path = DirectoryPathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;
        path = ResolveToolbarDirectory(path);
        DirectoryPathBox.Text = path;
        AppConfig.Instance.AddRecentDirectory(path, SaveFileManager.FindDefaultSaveDirectory());
        AppConfig.Instance.Save();
        RefreshPathSelectors();
        PopulateSaveFiles(path);
    }

    private void OnBackupPathCommitted(object? sender, RoutedEventArgs e)
    {
        string? path = BackupPathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(path))
            AppConfig.Instance.BackupDirectory = null;
        else
            AppConfig.Instance.AddRecentBackupDirectory(path);
        AppConfig.Instance.Save();
        RefreshPathSelectors();
    }

    private void RefreshPathSelectors()
    {
        _updatingPathSelectors = true;
        try
        {
            var config = AppConfig.Instance;
            string? defaultSave = SaveFileManager.FindDefaultSaveDirectory();
            List<string> savePaths = config.RecentDirectories
                .Concat(string.IsNullOrWhiteSpace(defaultSave) ? [] : [defaultSave])
                .Concat(SaveFileManager.FindDefaultSaveDirectories())
                .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .ToList();
            RecentDirectoryCombo.ItemsSource = savePaths;
            RecentDirectoryCombo.SelectedItem = savePaths.FirstOrDefault(path =>
                Directory.Exists(path) &&
                string.Equals(path, config.LastDirectory,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                ?? savePaths.FirstOrDefault(Directory.Exists);

            string defaultBackup = SaveFileManager.ResolveBackupRoot();
            List<string> backupPaths = config.RecentBackupDirectories
                .Append(defaultBackup)
                .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .ToList();
            RecentBackupCombo.ItemsSource = backupPaths;
            BackupPathBox.Text = config.BackupDirectory ?? defaultBackup;
            RecentBackupCombo.SelectedItem = backupPaths.FirstOrDefault(path =>
                string.Equals(path, BackupPathBox.Text,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        }
        finally
        {
            _updatingPathSelectors = false;
        }
    }

    private void OnOpenDirectoryRouted(object? sender, RoutedEventArgs e) => OnOpenDirectory(sender, EventArgs.Empty);

    private static string ResolveToolbarDirectory(string path)
    {
        if (OperatingSystem.IsMacOS())
        {
            path = SaveFileManager.NormalizeMacOsSaveDirectory(path);
            // The macOS NMS root offers every profile together, with SETTINGS alongside.
            if (string.Equals(new DirectoryInfo(path).Name, "NMS", StringComparison.OrdinalIgnoreCase))
                return path;
        }
        return SaveFileManager.ResolveSaveDirectory(path);
    }

    private async void OnOpenFile(object? sender, EventArgs e)
    {
        if (!await CanAbandonUnsavedAsync("load another save"))
            return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open No Man's Sky save",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("No Man's Sky saves") { Patterns = ["*.hg", "*.dat", "*.json", "containers.index"] },
                FilePickerFileTypes.All,
            ],
        });

        string? path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        string fileName = Path.GetFileName(path);
        if (fileName.Equals("memory.dat", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("containers.index", StringComparison.OrdinalIgnoreCase))
        {
            string? directory = Path.GetDirectoryName(path);
            if (directory is not null)
            {
                DirectoryPathBox.Text = directory;
                PopulateSaveFiles(directory);
            }
            return;
        }

        await LoadSaveDataAsync(path);
    }

    private void OnOpenFileRouted(object? sender, RoutedEventArgs e) => OnOpenFile(sender, EventArgs.Empty);

    private async void OnLoadSelected(object? sender, RoutedEventArgs e)
    {
        if (SaveFileCombo.SelectedItem is SaveFileChoice choice
            && await CanAbandonUnsavedAsync("load another save"))
            await LoadSaveDataAsync(choice.Session);
    }

    private void OnSaveSlotChanged(object? sender, SelectionChangedEventArgs e) => PopulateSelectedSaveFiles();

    private async Task LoadSaveDataAsync(string path)
        => await LoadSaveDataAsync(SaveSession.ForFile(path));

    private async Task LoadSaveDataAsync(SaveSession session)
    {
        SetLoading(true, "Loading and parsing save…");
        try
        {
            JsonObject data = await Task.Run(session.Load);
            SaveFileManager.TryDetectActiveContext(data);

            _currentSaveData = data;
            _currentSession = session;
            _currentFilePath = session.SourcePath;
            ControlTokens.SetScheme(ControlTokens.SchemeForPlatform(session.Platform));
            _isDirty = false;

            _exosuitPanel?.SetSaveScopeKey(AppConfig.BuildSaveScopeKey(session.SourcePath));
            _starshipPanel?.SetSaveScopeKey(AppConfig.BuildSaveScopeKey(session.SourcePath));
            LoadAccountData(session);
            _rawJsonPanel?.SetAccountData(
                _accountRewardsPanel?.AccountData,
                _accountRewardsPanel?.AccountSourcePath);
            _playerPanel?.SetSaveFilePath(session.SourcePath);
            _playerPanel?.SetAccountData(_accountRewardsPanel?.AccountData);
            _cataloguePanel?.SetAccountData(_accountRewardsPanel?.AccountData);

            _panelCoordinator.BeginSession(
                data,
                MainTabs.SelectedIndex,
                root =>
                {
                    _rawJsonBaseline = root.DeepClone();
                    _rawJsonPanel?.CaptureBaseline(root);
                });
            _rawJsonPanel?.SetSaveFilePath(session.SourcePath);

            // Temporary compatibility for panels registered before the coordinator
            // was introduced. New Avalonia panels are registered with the coordinator.
            foreach (ISaveEditorPanel panel in _loadedPanels)
                panel.LoadData(data);

            LoadedFileText.Text = Path.GetFileName(session.SourcePath);
            SetStatus($"Loaded {Path.GetFileName(session.SourcePath)}");
            _isDirty = false;
            CaptureExternalFiles();
            ConfigureTechnologyActions();
            UpdateSaveActions();
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Unable to load save", ex.Message);
            SetStatus("Load failed");
        }
        finally
        {
            SetLoading(false);
        }
    }

    private async void OnSave(object? sender, EventArgs e) => await SaveAsync(_currentSession);
    private void OnSaveRouted(object? sender, RoutedEventArgs e) => OnSave(sender, EventArgs.Empty);

    private async void OnSaveAs(object? sender, EventArgs e)
    {
        if (_currentSaveData is null) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save No Man's Sky save as",
            SuggestedFileName = _currentFilePath is null ? "save.hg" : Path.GetFileName(_currentFilePath),
            DefaultExtension = "hg",
            FileTypeChoices = [new FilePickerFileType("No Man's Sky save") { Patterns = ["*.hg"] }],
        });

        string? path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            SaveSession session = _currentSession?.SaveAs(path) ?? SaveSession.ForFile(path);
            await SaveAsync(session);
        }
    }

    private void OnSaveAsRouted(object? sender, RoutedEventArgs e) => OnSaveAs(sender, EventArgs.Empty);

    private async Task SaveAsync(SaveSession? session)
    {
        if (_currentSaveData is null || session is null) return;

        if (!await ConfirmExternalOverwriteAsync(session)) return;
        SetLoading(true, "Saving…");
        try
        {
            FocusManager?.Focus(null);
            _panelCoordinator.CommitLoadedPanels();
            foreach (ISaveEditorPanel panel in _loadedPanels)
                panel.SaveData(_currentSaveData);

            string? directory = Directory.Exists(session.SourcePath) ? session.SourcePath : Path.GetDirectoryName(session.SourcePath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                SaveFileManager.BackupSaveDirectory(directory);

            JsonObject? accountData = _rawJsonPanel?.GetEditedAccountData()
                ?? _accountRewardsPanel?.AccountData;
            if (accountData is not null)
            {
                _playerPanel?.SaveAccountData(accountData);
                if (_accountRewardsPanel?.CommitAccountSource(accountData) == false)
                    throw new IOException("Unable to validate and save GcUserSettingsData.mxml.");
            }
            await Task.Run(() => session.Save(_currentSaveData, accountData));
            _currentSession = session;
            _currentFilePath = session.SourcePath;
            ControlTokens.SetScheme(ControlTokens.SchemeForPlatform(session.Platform));
            _isDirty = false;
            LoadedFileText.Text = Path.GetFileName(session.SourcePath);
            SetStatus($"Saved {Path.GetFileName(session.SourcePath)}");
            CaptureExternalFiles();
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Unable to save", ex.Message);
            SetStatus("Save failed");
        }
        finally
        {
            SetLoading(false);
            UpdateSaveActions();
        }
    }

    private async void OnReload(object? sender, EventArgs e)
    {
        if (_currentSession is not null && await CanAbandonUnsavedAsync("reload this save"))
            await LoadSaveDataAsync(_currentSession);
    }

    private void OnReloadRouted(object? sender, RoutedEventArgs e) => OnReload(sender, EventArgs.Empty);

    private async Task<bool> CanAbandonUnsavedAsync(string action)
    {
        if (!_isDirty)
            return true;
        return await ShowMessageAsync(
            "Unsaved Changes",
            $"Discard unsaved changes and {action}?",
            showCancel: true);
    }

    private async void OnRestoreBackupAll(object? sender, EventArgs e) => await RestoreLatestBackupAsync(currentOnly: false);
    private async void OnRestoreBackupCurrent(object? sender, EventArgs e) => await RestoreLatestBackupAsync(currentOnly: true);
    private void OnRestoreBackupAllRouted(object? sender, RoutedEventArgs e) => OnRestoreBackupAll(sender, EventArgs.Empty);
    private void OnRestoreBackupCurrentRouted(object? sender, RoutedEventArgs e) => OnRestoreBackupCurrent(sender, EventArgs.Empty);

    private async Task RestoreLatestBackupAsync(bool currentOnly)
    {
        SaveSession? session = _currentSession;
        if (session is null)
            return;
        string? saveDirectory = Directory.Exists(session.SourcePath) ? session.SourcePath : Path.GetDirectoryName(session.SourcePath);
        if (string.IsNullOrWhiteSpace(saveDirectory))
            return;

        bool loading = false;
        try
        {
            List<string> backups = SaveFileManager.FindBackupZips(saveDirectory);
            if (backups.Count == 0)
            {
                await ShowMessageAsync(
                    UiStrings.Get("dialog.restore_backup"),
                    UiStrings.Get("dialog.no_backup_zips"));
                return;
            }

            string? backup = await BackupPickerDialog.ShowAsync(this, backups);
            if (backup is null)
                return;

            string backupName = Path.GetFileName(backup);
            string fileName = Path.GetFileName(session.SourcePath);
            bool restoreSingleFile = currentOnly && session.Kind != SaveSessionKind.XboxContainer;
            if (!SaveFileManager.BackupContainsFile(backup, fileName))
            {
                await ShowMessageAsync(
                    UiStrings.Get(restoreSingleFile ? "dialog.restore_backup_single" : "dialog.restore_backup"),
                    UiStrings.Format("dialog.restore_file_not_found", fileName, backupName));
                return;
            }

            List<string> entryNames = SaveFileManager.GetBackupEntryNames(backup);
            string fileList = restoreSingleFile
                ? BuildBackupFileList([fileName], 1)
                : BuildBackupFileList(entryNames, 15);
            string created = File.GetCreationTime(backup).ToString("g", CultureInfo.CurrentCulture);
            bool confirmed = await ShowMessageAsync(
                UiStrings.Get(restoreSingleFile ? "dialog.restore_backup_single" : "dialog.restore_backup"),
                UiStrings.Format(
                    restoreSingleFile ? "dialog.restore_single_confirm" : "dialog.restore_all_confirm",
                    fileList,
                    backupName,
                    created),
                showCancel: true);
            if (!confirmed)
                return;

            SetLoading(true, "Restoring backup…");
            loading = true;
            int restoredCount;
            if (restoreSingleFile)
            {
                restoredCount = SaveFileManager.RestoreFileFromBackup(
                    backup,
                    fileName,
                    session.SourcePath)
                    ? 1
                    : 0;
            }
            else
            {
                restoredCount = SaveFileManager.RestoreBackupToDirectory(backup, saveDirectory).Count;
            }

            if (restoredCount == 0)
                throw new InvalidDataException(
                    UiStrings.Format("dialog.restore_file_not_found", fileName, backupName));

            await LoadSaveDataAsync(session);
            SetStatus(restoreSingleFile
                ? UiStrings.Format("status.restored_file", fileName, backupName)
                : UiStrings.Format("status.restored_folder", restoredCount, backupName));
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(
                UiStrings.Get("dialog.restore_backup"),
                UiStrings.Format("dialog.restore_failed", ex.Message));
        }
        finally
        {
            if (loading)
                SetLoading(false);
        }
    }

    private static string BuildBackupFileList(IReadOnlyList<string> entryNames, int maxItems)
    {
        string[] files = entryNames
            .Where(name => !string.IsNullOrWhiteSpace(name)
                && !name.EndsWith("/", StringComparison.Ordinal)
                && !name.EndsWith("\\", StringComparison.Ordinal))
            .ToArray();
        var lines = files
            .Take(maxItems)
            .Select(name => $"• {name}")
            .ToList();
        if (files.Length > maxItems)
            lines.Add(UiStrings.Format("dialog.restore_more_files", files.Length - maxItems));
        return string.Join(Environment.NewLine, lines);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        KeyModifiers command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (!e.KeyModifiers.HasFlag(command))
        {
            if (e.Key == Key.F5)
            {
                OnReload(this, EventArgs.Empty);
                e.Handled = true;
            }
            return;
        }

        switch (e.Key)
        {
            case Key.O:
                OnOpenDirectory(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Key.L:
                OnOpenFile(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Key.S when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                OnSaveAs(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Key.S:
                OnSave(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Key.R:
                OnReload(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Key.W:
                Close();
                e.Handled = true;
                break;
        }
    }

    private async void OnExportJson(object? sender, EventArgs e)
    {
        if (_currentSaveData is null)
            return;
        _panelCoordinator.CommitLoadedPanels();
        var request = new JsonTransferRequest(JsonTransferScope.Document, "Export Save JSON", "save.json");
        bool exported = await ExportJsonAsync(
            request,
            RawJsonLogic.ToDisplayString(_currentSaveData),
            CancellationToken.None);
        if (exported)
            SetStatus("Save JSON exported");
    }

    private async void OnImportJson(object? sender, EventArgs e)
    {
        var request = new JsonTransferRequest(JsonTransferScope.Document, "Import Save JSON", "save.json");
        JsonTransferContent? content = await ImportJsonAsync(request, CancellationToken.None);
        if (content is null)
            return;
        bool confirmed = await ShowMessageAsync(
            "Import Save JSON",
            "Replace the loaded in-memory save with this JSON? The file on disk is not changed until Save.",
            showCancel: true);
        if (!confirmed)
            return;

        try
        {
            JsonObject imported = JsonParser.ParseObject(content.JsonText);
            SaveFileManager.TryDetectActiveContext(imported);
            _currentSaveData = imported;
            _panelCoordinator.BeginSession(imported, MainTabs.SelectedIndex, root =>
            {
                _rawJsonBaseline = root.DeepClone();
                _rawJsonPanel?.CaptureBaseline(root);
            });
            MarkDirty();
            SetStatus($"Imported {content.DisplayName}");
        }
        catch (Exception ex) when (ex is NMSE.Models.JsonException or FormatException)
        {
            await ShowMessageAsync("Import Failed", ex.Message);
        }
    }

    private void OnRechargeAllTechnology(object? sender, EventArgs e) =>
        RunBulkInventoryAction("technology items recharged", InventoryBulkActions.RechargeAllTechnology);

    private void OnRefillAllStacks(object? sender, EventArgs e) =>
        RunBulkInventoryAction("stacks refilled", InventoryBulkActions.RefillAllStacks);

    private void OnRepairAllSlots(object? sender, EventArgs e) =>
        RunBulkInventoryAction("slots repaired", InventoryBulkActions.RepairAllSlots);

    private void OnRepairAllTechnology(object? sender, EventArgs e) =>
        RunBulkInventoryAction("technology items repaired", InventoryBulkActions.RepairAllTechnology);

    private void RunBulkInventoryAction(
        string resultDescription,
        Func<JsonObject, GameItemDatabase, int> action)
    {
        JsonObject? playerState = _currentSaveData?.GetObject("PlayerStateData");
        if (playerState is null)
            return;
        _panelCoordinator.CommitLoadedPanels();
        int changed = action(playerState, _resources.Items);
        if (changed > 0)
        {
            MarkDirty();
            _panelCoordinator.RefreshLoaded(Enumerable.Range(0, 15).ToArray());
        }
        SetStatus($"{changed} {resultDescription}");
    }

    private void OnExportJsonRouted(object? sender, RoutedEventArgs e) => OnExportJson(sender, EventArgs.Empty);
    private void OnImportJsonRouted(object? sender, RoutedEventArgs e) => OnImportJson(sender, EventArgs.Empty);
    private void OnRechargeAllTechnologyRouted(object? sender, RoutedEventArgs e) => OnRechargeAllTechnology(sender, EventArgs.Empty);
    private void OnRefillAllStacksRouted(object? sender, RoutedEventArgs e) => OnRefillAllStacks(sender, EventArgs.Empty);
    private void OnRepairAllSlotsRouted(object? sender, RoutedEventArgs e) => OnRepairAllSlots(sender, EventArgs.Empty);
    private void OnRepairAllTechnologyRouted(object? sender, RoutedEventArgs e) => OnRepairAllTechnology(sender, EventArgs.Empty);

    private void PopulateSaveFiles(string directory)
    {
        try
        {
            _saveSlots = DiscoverSaveSlots(directory);
        }
        catch (Exception ex)
        {
            _saveSlots = [];
            SetStatus($"Unable to enumerate saves: {ex.Message}");
        }

        SaveSlotCombo.ItemsSource = _saveSlots;
        SaveSlotCombo.SelectedIndex = _saveSlots.Count > 0 ? 0 : -1;
        PopulateSelectedSaveFiles();
        int fileCount = _saveSlots.Sum(slot => slot.Files.Count);
        SetStatus(fileCount == 0 ? "No saves found" : $"Found {_saveSlots.Count} save slots ({fileCount} files)");
    }

    private void LoadAccountData(SaveSession session)
    {
        if (_accountRewardsPanel is null)
            return;
        _accountRewardsPanel.SetPlatform(session.Platform);
        try
        {
            switch (session.Kind)
            {
                case SaveSessionKind.XboxContainer:
                    Dictionary<string, XboxSlotInfo> slots = SaveFileManager.EnumerateXboxSlots(session.SourcePath);
                    if (slots.TryGetValue(ContainersIndexManager.AccountDataIdentifier, out XboxSlotInfo? accountSlot))
                        _accountRewardsPanel.LoadXboxAccountData(accountSlot);
                    else
                        _accountRewardsPanel.SetAccountData(null);
                    break;
                case SaveSessionKind.PlayStationMemoryDat:
                    _accountRewardsPanel.SetAccountData(
                        SaveFileManager.LoadPS4MemoryDatSave(session.SourcePath, 0),
                        session.SourcePath);
                    break;
                default:
                    string? directory = Path.GetDirectoryName(session.SourcePath);
                    if (directory is not null)
                        _accountRewardsPanel.LoadAccountFile(directory);
                    break;
            }
        }
        catch (Exception ex)
        {
            _accountRewardsPanel.SetAccountData(null);
            SetStatus($"Account data warning: {ex.Message}");
        }
    }

    private void PopulateSelectedSaveFiles()
    {
        IReadOnlyList<SaveFileChoice> choices = SaveSlotCombo.SelectedItem is SaveSlotChoice slot
            ? slot.Files
            : [];
        SaveFileCombo.ItemsSource = choices;
        SaveFileCombo.SelectedIndex = choices.Count == 0
            ? -1
            : Enumerable.Range(0, choices.Count)
                .OrderByDescending(index => choices[index].LastWriteTime)
                .First();
    }

    private static IReadOnlyList<SaveSlotChoice> DiscoverSaveSlots(string directory)
    {
        SaveFileManager.Platform platform = SaveFileManager.DetectPlatform(directory);
        if (platform == SaveFileManager.Platform.Unknown)
        {
            string resolved = SaveFileManager.ResolveSaveDirectory(directory);
            if (SaveFileManager.DetectPlatform(resolved) == SaveFileManager.Platform.XboxGamePass)
                return DiscoverXboxSlots(resolved);
        }

        // macOS presents the stable NMS root in the toolbar. Its Steam/GOG profile
        // directories are enumerated here, while the sibling Settings directory is
        // deliberately ignored because it never contains save data.
        if (platform == SaveFileManager.Platform.Unknown)
        {
            var profileSlots = new List<SaveSlotChoice>();
            foreach (string profileDirectory in Directory.EnumerateDirectories(directory)
                         .Order(StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(Path.GetFileName(profileDirectory), "Settings", StringComparison.OrdinalIgnoreCase) ||
                    SaveFileManager.DetectPlatform(profileDirectory) == SaveFileManager.Platform.Unknown)
                {
                    continue;
                }

                string profileName = Path.GetFileName(profileDirectory);
                foreach (SaveSlotChoice slot in DiscoverSaveSlots(profileDirectory))
                    profileSlots.Add(new SaveSlotChoice($"{profileName} — {slot.Label}", slot.Files));
            }

            if (profileSlots.Count > 0)
                return profileSlots;
        }

        if (platform == SaveFileManager.Platform.XboxGamePass)
            return DiscoverXboxSlots(directory);

        string memoryDat = Path.Combine(directory, "memory.dat");
        if (platform == SaveFileManager.Platform.PS4 && File.Exists(memoryDat))
            return DiscoverPlayStationMemorySlots(memoryDat);

        var result = new List<SaveSlotChoice>();
        for (int index = 0; index < 15; index++)
        {
            string autoName = index == 0 ? "save.hg" : $"save{index * 2 + 1}.hg";
            string manualName = $"save{index * 2 + 2}.hg";
            AddFileSlot(result, $"Slot {index + 1}", Path.Combine(directory, autoName), Path.Combine(directory, manualName));
        }

        if (result.Count == 0 && platform is SaveFileManager.Platform.PS4 or SaveFileManager.Platform.Switch)
        {
            for (int index = 0; index < 15; index++)
            {
                AddFileSlot(
                    result,
                    $"{(platform == SaveFileManager.Platform.Switch ? "Switch" : "PS4")}: Slot {index + 1}",
                    Path.Combine(directory, $"savedata{index * 2 + 2:D2}.hg"),
                    Path.Combine(directory, $"savedata{index * 2 + 3:D2}.hg"));
            }
        }

        if (result.Count == 0)
        {
            foreach (string path in Directory.EnumerateFiles(directory, "*.dat").Order(StringComparer.OrdinalIgnoreCase))
            {
                if (Path.GetFileName(path).Equals("memory.dat", StringComparison.OrdinalIgnoreCase))
                    continue;
                var choice = NewFileChoice(Path.GetFileName(path), SaveSession.ForFile(path));
                result.Add(new SaveSlotChoice(Path.GetFileNameWithoutExtension(path), [choice]));
            }
        }

        return result;
    }

    private static IReadOnlyList<SaveSlotChoice> DiscoverXboxSlots(string directory)
    {
        var groups = SaveFileManager.EnumerateXboxSlots(directory)
            .Where(entry => ContainersIndexManager.IsSaveSlot(entry.Key))
            .GroupBy(entry => ContainersIndexManager.ExtractSlotNumber(entry.Key))
            .OrderBy(group => group.Key);
        var result = new List<SaveSlotChoice>();
        foreach (var group in groups)
        {
            SaveFileChoice[] files = group
                .OrderByDescending(entry => ContainersIndexManager.IsAutoSave(entry.Key))
                .Select(entry => new SaveFileChoice(
                    ContainersIndexManager.IsAutoSave(entry.Key) ? "Auto" : "Manual",
                    SaveSession.ForXbox(directory, entry.Key),
                    entry.Value.DataFilePath is { } path && File.Exists(path)
                        ? File.GetLastWriteTime(path)
                        : DateTime.MinValue))
                .ToArray();
            result.Add(new SaveSlotChoice($"Xbox: Slot {group.Key}", files));
        }
        return result;
    }

    private static IReadOnlyList<SaveSlotChoice> DiscoverPlayStationMemorySlots(string memoryDatPath)
    {
        MemoryDatSlot[] metadata = MemoryDatManager.ReadSlots(memoryDatPath);
        var result = new List<SaveSlotChoice>();
        for (int slot = 1; slot <= 5; slot++)
        {
            var files = new List<SaveFileChoice>();
            foreach (int subSlot in new[] { slot * 2 - 1, slot * 2 })
            {
                if (subSlot >= metadata.Length || !metadata[subSlot].Exists)
                    continue;
                bool isAuto = subSlot % 2 == 1;
                files.Add(new SaveFileChoice(
                    isAuto ? "Auto" : "Manual",
                    SaveSession.ForPlayStationMemoryDat(memoryDatPath, subSlot),
                    metadata[subSlot].Timestamp?.LocalDateTime ?? DateTime.MinValue));
            }
            if (files.Count > 0)
                result.Add(new SaveSlotChoice($"PS4: Slot {slot}", files));
        }
        return result;
    }

    private static void AddFileSlot(List<SaveSlotChoice> slots, string label, string autoPath, string manualPath)
    {
        var files = new List<SaveFileChoice>();
        if (File.Exists(autoPath)) files.Add(NewFileChoice("Auto", SaveSession.ForFile(autoPath)));
        if (File.Exists(manualPath)) files.Add(NewFileChoice("Manual", SaveSession.ForFile(manualPath)));
        if (files.Count > 0) slots.Add(new SaveSlotChoice(label, files));
    }

    private static SaveFileChoice NewFileChoice(string label, SaveSession session) =>
        new(label, session, File.GetLastWriteTime(session.SourcePath));

    private void ApplyTheme(string theme, bool save = true)
    {
        if (Application.Current is not { } app) return;

        app.RequestedThemeVariant = theme.ToUpperInvariant() switch
        {
            "DARK" => ThemeVariant.Dark,
            "LIGHT" => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };

        if (save)
        {
            AppConfig.Instance.Theme = theme;
            AppConfig.Instance.Save();
        }
    }

    private void OnSystemTheme(object? sender, EventArgs e) => ApplyTheme("System");
    private void OnLightTheme(object? sender, EventArgs e) => ApplyTheme("Light");
    private void OnDarkTheme(object? sender, EventArgs e) => ApplyTheme("Dark");
    private void OnSystemThemeRouted(object? sender, RoutedEventArgs e) => OnSystemTheme(sender, EventArgs.Empty);
    private void OnLightThemeRouted(object? sender, RoutedEventArgs e) => OnLightTheme(sender, EventArgs.Empty);
    private void OnDarkThemeRouted(object? sender, RoutedEventArgs e) => OnDarkTheme(sender, EventArgs.Empty);

    public async Task ShowPreferencesAsync()
    {
        var settings = new PreferencesWindow(AppConfig.Instance.Theme, theme => ApplyTheme(theme));
        await settings.ShowDialog<bool>(this);
    }

    private async void OnLanguageSelected(object? sender, EventArgs e)
    {
        string? languageTag = sender switch
        {
            NativeMenuItem nativeItem => nativeItem.Header,
            MenuItem menuItem => menuItem.Header?.ToString(),
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(languageTag))
            return;

        SetLoading(true, "Switching language…");
        try
        {
            bool loaded = await Task.Run(() => _resources.ApplyLanguage(languageTag));
            ApplyLocalisation();
            _panelCoordinator.ApplyUiLocalisation();
            AppConfig.Instance.Language = languageTag;
            AppConfig.Instance.Save();
            SetStatus(loaded
                ? UiStrings.Format("status.language_set", languageTag)
                : UiStrings.Format("status.language_not_found", languageTag));
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void OnLanguageSelectedRouted(object? sender, RoutedEventArgs e) =>
        OnLanguageSelected(sender, EventArgs.Empty);

    private void OnGitHub(object? sender, EventArgs e) => OpenExternal(GitHubUrl);
    private void OnUserGuide(object? sender, EventArgs e) => OpenExternal(UserGuideUrl);
    private void OnSponsor(object? sender, EventArgs e) => OpenExternal(SponsorUrl);
    private void OnReleaseNotes(object? sender, EventArgs e) => OpenExternal(ReleaseNotesUrl);
    private void OnGitHubRouted(object? sender, RoutedEventArgs e) => OnGitHub(sender, EventArgs.Empty);
    private void OnUserGuideRouted(object? sender, RoutedEventArgs e) => OnUserGuide(sender, EventArgs.Empty);
    private void OnSponsorRouted(object? sender, RoutedEventArgs e) => OnSponsor(sender, EventArgs.Empty);
    private void OnReleaseNotesRouted(object? sender, RoutedEventArgs e) => OnReleaseNotes(sender, EventArgs.Empty);

    private async void OnCheckForUpdates(object? sender, EventArgs e)
    {
        SetStatus("Checking for updates…");
        UpdateInfo? update = await UpdateService.CheckForUpdateAsync(ReadCurrentVersion());
        if (update is null)
        {
            await ShowMessageAsync("NMSE Updates", "NMSE is up to date, or the update service is unavailable.");
            SetStatus("Update check complete");
            return;
        }

        bool open = await ShowMessageAsync(
            "NMSE Update Available",
            OperatingSystem.IsMacOS()
                ? $"{update.Title} is available. Download, verify, and install it now?"
                : $"{update.Title} is available. Open the release page?",
            showCancel: true);
        if (!open)
            return;

        if (!OperatingSystem.IsMacOS())
        {
            OpenExternal(GitHubUrl + "/releases/latest");
            return;
        }

        SetLoading(true, "Downloading and verifying update…");
        try
        {
            var progress = new Progress<(long received, long? total)>(value =>
            {
                LoadingText.Text = value.total is > 0
                    ? $"Downloading update… {value.received * 100 / value.total.Value}%"
                    : $"Downloading update… {value.received / 1024 / 1024} MB";
            });
            MacOSUpdateInstallResult result = await new MacOSUpdateInstaller()
                .PrepareAndStartAsync(update, progress: progress);

            if (result.Status == MacOSUpdateInstallStatus.HelperStarted)
            {
                _allowClose = true;
                Close();
                return;
            }

            if (result.Fallback is { } fallback)
            {
                await ShowMessageAsync("Manual Update Required", fallback.Reason);
                OpenExternal(fallback.PreferredUri.ToString());
            }
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void OnCheckForUpdatesRouted(object? sender, RoutedEventArgs e) => OnCheckForUpdates(sender, EventArgs.Empty);

    public Task ShowAboutAsync() => ShowMessageAsync(
        "About NMSE",
        $"No Man's Save Editor\nVersion {ReadCurrentVersion()}\n\nNative Avalonia macOS migration build.");

    private async void OnAbout(object? sender, EventArgs e) => await ShowAboutAsync();
    private void OnAboutRouted(object? sender, RoutedEventArgs e) => OnAbout(sender, EventArgs.Empty);

    private static Version ReadCurrentVersion()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "version.json");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            return new Version(
                root.GetProperty("major").GetInt32(),
                root.GetProperty("minor").GetInt32(),
                root.GetProperty("patch").GetInt32());
        }
        catch
        {
            return new Version(0, 0, 0);
        }
    }

    private void OpenExternal(string url)
    {
        try
        {
            _ = _desktopServices.OpenUrlAsync(new Uri(url));
        }
        catch
        {
            // External navigation is best effort.
        }
    }

    private async Task<bool> ShowMessageAsync(string title, string message, bool showCancel = false)
    {
        var dialog = new MessageDialogWindow(title, message, showCancel);
        return await dialog.ShowDialog<bool>(this);
    }

    private void SetLoading(bool visible, string? message = null)
    {
        LoadingOverlay.IsVisible = visible;
        if (message is not null) LoadingText.Text = message;
    }

    private void SetStatus(string status) => StatusText.Text = status;

    private void MarkDirty()
    {
        _isDirty = true;
        UpdateSaveActions();
    }

    private void ConfigureTechnologyActions()
    {
        foreach (InventoryGrid grid in this.GetLogicalDescendants().OfType<InventoryGrid>())
        {
            grid.ActiveLocalisation = _resources.Localisation;
            grid.ConfigureTechnologyActions(() => _currentSaveData, _panelCoordinator.CommitLoadedPanels,
                () => { _panelCoordinator.RefreshLoaded(1, 2, 3, 4, 5, 7); MarkDirty(); });
        }
    }

    private void OnTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MainTabs)) return;
        if (_currentSaveData is null)
            return;

        if (MainTabs.SelectedIndex == 14)
            _panelCoordinator.CommitLoadedPanels();
        _panelCoordinator.Activate(MainTabs.SelectedIndex);
        ConfigureTechnologyActions();
        if (MainTabs.SelectedIndex == 14)
            _rawJsonPanel?.RefreshTree(_currentSaveData);
    }

    private async void OnGoToJsonRequested(object? sender, GoToJsonRequestEventArgs e)
    {
        if (_currentSaveData is null || _rawJsonPanel is null)
            return;
        bool confirmed = await ShowMessageAsync(
            UiStrings.Get("common.goto_json_title"),
            UiStrings.Get("common.goto_json_confirm"),
            showCancel: true);
        if (!confirmed)
            return;

        _panelCoordinator.CommitLoadedPanels();
        MainTabs.SelectedIndex = 14;
        _rawJsonPanel.RefreshTree(_currentSaveData);
        _rawJsonPanel.NavigateToPath(e.JsonPath);
    }

    private async Task<JsonTransferContent?> ImportJsonAsync(
        JsonTransferRequest request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = request.Title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("JSON") { Patterns = ["*.json"] },
                FilePickerFileTypes.All,
            ],
        });
        string? path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return null;
        string json = await File.ReadAllTextAsync(path, cancellationToken);
        return new JsonTransferContent(Path.GetFileName(path), json);
    }

    private async Task<bool> ExportJsonAsync(
        JsonTransferRequest request,
        string json,
        CancellationToken cancellationToken)
    {
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = request.Title,
            SuggestedFileName = request.SuggestedFileName,
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
        });
        string? path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return false;
        await File.WriteAllTextAsync(path, json, cancellationToken);
        return true;
    }

    private async Task<string?> PickOpenFileAsync(string title, IReadOnlyList<string> extensions)
    {
        IReadOnlyList<string> paths = await _desktopServices.PickFilesAsync(
            title,
            allowMultiple: false,
            new DesktopFileType("NMSE data", extensions.Select(ToFilePattern).ToArray()),
            new DesktopFileType("All files", "*"));
        return paths.FirstOrDefault();
    }

    private Task<string?> PickSaveFileAsync(
        string title,
        string suggestedFileName,
        IReadOnlyList<string> extensions)
    {
        string defaultExtension = extensions.FirstOrDefault()?.TrimStart('.', '*') ?? string.Empty;
        return _desktopServices.PickSaveFileAsync(
            title,
            suggestedFileName,
            defaultExtension,
            new DesktopFileType("NMSE data", extensions.Select(ToFilePattern).ToArray()),
            new DesktopFileType("All files", "*"));
    }

    private static string ToFilePattern(string extension)
    {
        string value = extension.Trim();
        if (value.Contains('*', StringComparison.Ordinal))
            return value;
        return value.StartsWith(".", StringComparison.Ordinal) ? $"*{value}" : $"*.{value}";
    }

    private async Task<FleetTransferContent?> ImportFleetAsync(
        FleetTransferRequest request,
        CancellationToken cancellationToken)
    {
        string? path = await PickOpenFileAsync(request.Title, request.Extensions);
        if (path is null)
            return null;
        return new FleetTransferContent(
            Path.GetFileName(path),
            await File.ReadAllTextAsync(path, cancellationToken));
    }

    private async Task<bool> ExportFleetAsync(
        FleetTransferRequest request,
        string json,
        CancellationToken cancellationToken)
    {
        string? path = await PickSaveFileAsync(request.Title, request.SuggestedFileName, request.Extensions);
        if (path is null)
            return false;
        await File.WriteAllTextAsync(path, json, cancellationToken);
        return true;
    }

    private async Task<BasesStorageTransferContent?> ImportBasesStorageAsync(
        BasesStorageTransferRequest request, CancellationToken cancellationToken)
    {
        string? path = await PickOpenFileAsync(request.Title, request.Extensions);
        if (path is null) return null;
        if (request.Kind == BasesStorageTransferKind.Base) path = BaseExportLogic.ResolveBaseFilePath(path);
        string json = await File.ReadAllTextAsync(path, cancellationToken);
        if (request.Kind != BasesStorageTransferKind.Base) return new(Path.GetFileName(path), json);
        var read = BaseExportLogic.ReadExport(JsonObject.Parse(json));
        var terrain = read.Payload == null ? null : BaseExportLogic.ReadTerrainSidecar(path, read.Payload.Base);
        return new(Path.GetFileName(path), json, terrain?.TerrainEditData,
            terrain?.Status == BaseExportLogic.TerrainSidecarStatus.Invalid);
    }

    private async Task<bool> ExportBasesStorageAsync(
        BasesStorageTransferRequest request, string json, CancellationToken cancellationToken)
    {
        string? path = await PickSaveFileAsync(request.Title, request.SuggestedFileName, request.Extensions);
        if (path is null) return false;
        if (request.Kind == BasesStorageTransferKind.Base)
            BaseExportLogic.WriteExport(request.BasePlayerState, JsonObject.Parse(json), path);
        else await File.WriteAllTextAsync(path, json, cancellationToken);
        return true;
    }

    private async Task<CatalogueTransferContent?> ImportCatalogueAsync(
        CatalogueTransferRequest request,
        CancellationToken cancellationToken)
    {
        string? path = await PickOpenFileAsync(request.Title, request.Extensions);
        if (path is null)
            return null;
        return new CatalogueTransferContent(
            Path.GetFileName(path),
            await File.ReadAllTextAsync(path, cancellationToken));
    }

    private async Task<bool> ExportCatalogueAsync(
        CatalogueTransferRequest request,
        string json,
        CancellationToken cancellationToken)
    {
        string? path = await PickSaveFileAsync(request.Title, request.SuggestedFileName, request.Extensions);
        if (path is null)
            return false;
        await File.WriteAllTextAsync(path, json, cancellationToken);
        return true;
    }

    private async Task<bool> OpenCatalogueUrlAsync(string url, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await _desktopServices.OpenUrlAsync(new Uri(url));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateSaveActions()
    {
        bool enabled = _currentSaveData is not null;
        SaveButton.IsEnabled = enabled;
        SaveMenuItem.IsEnabled = enabled;
        SaveAsMenuItem.IsEnabled = enabled;
        UpdateEditorLockState();
        Title = _isDirty ? "NMSE — No Man's Save Editor *" : "NMSE — No Man's Save Editor";
    }

    private void ApplyWindowState()
    {
        var config = AppConfig.Instance;
        if (config.MainFrameWidth > 0 && config.MainFrameHeight > 0)
        {
            Width = Math.Max(MinWidth, config.MainFrameWidth);
            Height = Math.Max(MinHeight, config.MainFrameHeight);
            Position = new PixelPoint(config.MainFrameX, config.MainFrameY);
        }
    }

    private void SaveWindowState()
    {
        var config = AppConfig.Instance;
        config.MainFrameX = Position.X;
        config.MainFrameY = Position.Y;
        config.MainFrameWidth = (int)Bounds.Width;
        config.MainFrameHeight = (int)Bounds.Height;
        config.Save();
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose)
        {
            SaveWindowState();
            SaveContext.Reset();
            return;
        }

        if (!_isDirty)
        {
            _allowClose = true;
            SaveWindowState();
            SaveContext.Reset();
            return;
        }

        e.Cancel = true;
        bool close = await ShowMessageAsync(
            "Unsaved Changes",
            "Discard unsaved changes and close NMSE?",
            showCancel: true);
        if (close)
        {
            _allowClose = true;
            Close();
        }
    }

    private void OnClose(object? sender, EventArgs e) => Close();
    private void OnCloseRouted(object? sender, RoutedEventArgs e) => OnClose(sender, EventArgs.Empty);

    private sealed record SaveSlotChoice(string Label, IReadOnlyList<SaveFileChoice> Files)
    {
        public override string ToString() => Label;
    }

    private sealed record SaveFileChoice(string Label, SaveSession Session, DateTime LastWriteTime)
    {
        public override string ToString() => Label;
    }
}
