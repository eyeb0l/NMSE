using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NMSE.Core;
using NMSE.Data;
using NMSE.IO;
using NMSE.Models;
using NMSE.UI.Avalonia.Contracts;
using NMSE.UI.Avalonia.Coordination;

namespace NMSE.UI.Avalonia.Panels.AccountRewards;

public sealed record AccountRewardsDesktopActions(
    Func<AccountRewardsFileRequest, Task<string?>> PickOpenFileAsync,
    Func<string, string, Task> ShowInfoAsync,
    Func<string, string, Task> ShowErrorAsync);

public sealed record AccountRewardsFileRequest(string Title, string SuggestedFileName, params string[] Extensions);

/// <summary>Edits account unlocks, save redemption state, and PC platform-reward MXML state.</summary>
public sealed class AccountRewardsPanel : UserControl, ISaveEditorPanel
{
    private readonly AvaloniaResourceCatalog _resources;
    private readonly AccountRewardsDesktopActions _desktop;
    private readonly List<AccountLogic.RewardDbEntry> _seasonRewards = [];
    private readonly List<AccountLogic.RewardDbEntry> _twitchRewards = [];
    private readonly List<AccountLogic.RewardDbEntry> _platformRewards = [];
    private readonly Dictionary<string, string> _productIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBlock _statusLabel;
    private readonly TextBlock _warningLabel;
    private readonly Button _consistencyButton;
    private readonly RewardTab _seasonTab;
    private readonly RewardTab _twitchTab;
    private readonly RewardTab _platformTab;
    private readonly TextBlock _mxmlInfo;
    private readonly Grid _mxmlFilePanel;
    private readonly TextBox _mxmlPathBox;
    private readonly TextBlock _mxmlStatus;
    private readonly Button _mxmlBrowse;
    private JsonObject? _accountData;
    private JsonObject? _currentSaveData;
    private string? _accountFilePath;
    private string? _mxmlFilePath;
    private bool _mxmlIsAccountStore;
    private SaveFileManager.Platform _platform = SaveFileManager.Platform.Unknown;
    private HashSet<string> _originalSeasonRedeemed = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _originalTwitchRedeemed = new(StringComparer.OrdinalIgnoreCase);
    private bool _loading;

    public AccountRewardsPanel(AvaloniaResourceCatalog resources, AccountRewardsDesktopActions desktop)
    {
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _desktop = desktop ?? throw new ArgumentNullException(nameof(desktop));
        _statusLabel = new TextBlock { Margin = new Thickness(5), TextWrapping = TextWrapping.Wrap };
        _warningLabel = new TextBlock
        {
            Margin = new Thickness(5, 2, 5, 5),
            Foreground = Brushes.DarkOrange,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        _consistencyButton = new Button { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(5, 2) };
        _consistencyButton.Click += OnConsistencyCheck;

        _seasonTab = CreateRewardTab(includeExpedition: true);
        _twitchTab = CreateRewardTab(includeExpedition: false);
        _platformTab = CreateRewardTab(includeExpedition: false);

        _mxmlInfo = new TextBlock
        {
            Foreground = Brushes.DarkOrange,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        _mxmlPathBox = new TextBox { IsReadOnly = true, MinWidth = 420 };
        _mxmlBrowse = new Button();
        _mxmlBrowse.Click += OnBrowseMxml;
        _mxmlStatus = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        _mxmlFilePanel = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8,
            Children = { _mxmlPathBox, _mxmlBrowse, _mxmlStatus },
        };
        Grid.SetColumn(_mxmlBrowse, 1);
        Grid.SetColumn(_mxmlStatus, 2);
        var platformContent = (Grid)_platformTab.Page.Content!;
        platformContent.RowDefinitions.Insert(0, new RowDefinition(GridLength.Auto));
        platformContent.RowDefinitions.Insert(1, new RowDefinition(GridLength.Auto));
        foreach (Control child in platformContent.Children)
            Grid.SetRow(child, Grid.GetRow(child) + 2);
        platformContent.Children.Add(_mxmlInfo);
        platformContent.Children.Add(_mxmlFilePanel);
        Grid.SetRow(_mxmlFilePanel, 1);

        var tabs = new TabControl { Items = { _seasonTab.Page, _twitchTab.Page, _platformTab.Page } };
        Content = new Grid
        {
            Margin = new Thickness(6),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
            RowSpacing = 4,
            Children = { _statusLabel, _warningLabel, _consistencyButton, tabs },
        };
        Grid.SetRow(_warningLabel, 1);
        Grid.SetRow(_consistencyButton, 2);
        Grid.SetRow(tabs, 3);

        LoadRewardsDatabase();
        ApplyUiLocalisation();
        SetPlatform(SaveFileManager.Platform.Unknown);
    }

    public event EventHandler? DataModified;

    public JsonObject? AccountData => _accountData;
    public string? AccountFilePath => _accountFilePath;
    public string? AccountSourcePath => _accountFilePath;
    public string? MxmlFilePath => _mxmlFilePath;

    private bool UsesMxml => _platform is SaveFileManager.Platform.Steam
        or SaveFileManager.Platform.GOG
        or SaveFileManager.Platform.Unknown;

    public void SetPlatform(SaveFileManager.Platform platform)
    {
        _platform = platform;
        _mxmlInfo.IsVisible = UsesMxml;
        _mxmlFilePanel.IsVisible = UsesMxml;
        if (UsesMxml)
            return;
        _mxmlFilePath = null;
        _mxmlIsAccountStore = false;
        _mxmlPathBox.Text = string.Empty;
        _mxmlStatus.Text = string.Empty;
    }

    public void LoadRewardsDatabase()
    {
        string jsonDirectory = Path.Combine(ApplicationPaths.Current.ResourcesDirectory, "json");
        RewardDatabase.LoadFromJsonDirectory(jsonDirectory);
        RebuildRewardDefinitions();
    }

    public void RefreshRewardNames()
    {
        RebuildRewardDefinitions();
        if (_accountData is null)
            return;
        JsonObject settings = _accountData.GetObject("UserSettingsData") ?? _accountData;
        PopulateRows(_seasonTab, _seasonRewards, AccountLogic.GetUnlockedSet(settings.GetArray("UnlockedSeasonRewards")),
            _currentSaveData is null ? null : AccountLogic.GetRedeemedSets(_currentSaveData).SeasonRedeemed);
        PopulateRows(_twitchTab, _twitchRewards, AccountLogic.GetUnlockedSet(settings.GetArray("UnlockedTwitchRewards")),
            _currentSaveData is null ? null : AccountLogic.GetRedeemedSets(_currentSaveData).TwitchRedeemed);
        PopulateRows(_platformTab, _platformRewards, AccountLogic.GetUnlockedSet(settings.GetArray("UnlockedPlatformRewards")));
    }

    public void LoadAccountFile(string saveDirectory)
    {
        AccountLogic.AccountData data = AccountLogic.LoadAccountData(saveDirectory);
        ApplyAccountData(data, useMxml: UsesMxml);
    }

    public void LoadXboxAccountData(XboxSlotInfo accountSlot)
    {
        SetPlatform(SaveFileManager.Platform.XboxGamePass);
        ApplyAccountData(AccountLogic.LoadXboxAccountData(accountSlot), useMxml: false);
    }

    /// <summary>
    /// Supplies account JSON already decoded by the shell, such as PS4 memory.dat slot 0
    /// or a Raw JSON account document. The optional source path remains available to the
    /// platform-specific writer through <see cref="AccountSourcePath"/>.
    /// </summary>
    public void SetAccountData(JsonObject? accountData, string? accountPath = null)
    {
        if (accountData is null)
        {
            _accountData = null;
            _accountFilePath = accountPath;
            _mxmlIsAccountStore = false;
            ClearRows();
            _statusLabel.Text = UiStrings.Get("account.status_not_loaded");
            return;
        }

        JsonObject settings = accountData.GetObject("UserSettingsData") ?? accountData;
        HashSet<string> season = AccountLogic.GetUnlockedSet(settings.GetArray("UnlockedSeasonRewards"));
        HashSet<string> twitch = AccountLogic.GetUnlockedSet(settings.GetArray("UnlockedTwitchRewards"));
        HashSet<string> platform = AccountLogic.GetUnlockedSet(settings.GetArray("UnlockedPlatformRewards"));
        ApplyAccountData(new AccountLogic.AccountData
        {
            AccountObject = accountData,
            AccountFilePath = accountPath,
            IsMxmlAccountStore = string.Equals(Path.GetExtension(accountPath), ".mxml", StringComparison.OrdinalIgnoreCase),
            SeasonUnlocked = season,
            TwitchUnlocked = twitch,
            PlatformUnlocked = platform,
            StatusMessage = UiStrings.Format("account.status_loaded", season.Count + twitch.Count + platform.Count),
        }, useMxml: UsesMxml);
    }

    public void LoadData(JsonObject saveData)
    {
        _currentSaveData = saveData ?? throw new ArgumentNullException(nameof(saveData));
        (HashSet<string> season, HashSet<string> twitch) = AccountLogic.GetRedeemedSets(saveData);
        _originalSeasonRedeemed = new HashSet<string>(season, StringComparer.OrdinalIgnoreCase);
        _originalTwitchRedeemed = new HashSet<string>(twitch, StringComparer.OrdinalIgnoreCase);
        _loading = true;
        try
        {
            SetRedeemed(_seasonTab, season);
            SetRedeemed(_twitchTab, twitch);
            SetRedeemed(_platformTab, []);
        }
        finally
        {
            _loading = false;
        }
    }

    public void SaveData(JsonObject saveData)
    {
        if (_accountData is null)
            return;
        JsonObject settings = _accountData.GetObject("UserSettingsData") ?? _accountData;
        List<(string Id, bool Unlocked, bool Redeemed)> season = CollectRows(_seasonTab);
        List<(string Id, bool Unlocked, bool Redeemed)> twitch = CollectRows(_twitchTab);
        List<(string Id, bool Unlocked, bool Redeemed)> platform = CollectRows(_platformTab);

        AccountLogic.SaveRewardList(season.Select(row => (row.Id, row.Unlocked)).ToList(), settings, "UnlockedSeasonRewards");
        AccountLogic.SaveRewardList(twitch.Select(row => (row.Id, row.Unlocked)).ToList(), settings, "UnlockedTwitchRewards");
        AccountLogic.SaveRewardList(platform.Select(row => (row.Id, row.Unlocked)).ToList(), settings, "UnlockedPlatformRewards");
        AccountLogic.SaveRedeemedRewards(saveData,
            season.Select(row => (row.Id, row.Redeemed)).ToList(),
            twitch.Select(row => (row.Id, row.Redeemed)).ToList(),
            _resources.Items);

        List<(string Id, bool Redeemed)> seasonChanged = GetChangedRewards(season, _originalSeasonRedeemed);
        List<(string Id, bool Redeemed)> twitchChanged = GetChangedRewards(twitch, _originalTwitchRedeemed);
        AccountLogic.SyncKnownArraysForChangedRewards(saveData, seasonChanged, _resources.Items, _productIds);
        AccountLogic.SyncKnownArraysForChangedRewards(saveData, twitchChanged, _resources.Items, _productIds);
        AccountLogic.CleanStaleKnownEntries(
            saveData,
            GetChangedStaleRows(season, _originalSeasonRedeemed),
            GetChangedStaleRows(twitch, _originalTwitchRedeemed),
            _resources.Items,
            _productIds);

        DataModified?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Persists the account model after every panel, including Player guides and
    /// titles, has committed its changes. For macOS this writes the supported arrays
    /// back to GcUserSettingsData.mxml; other PC platforms retain platform-only MXML sync.
    /// </summary>
    public bool CommitAccountSource(JsonObject accountData)
    {
        if (!UsesMxml || string.IsNullOrWhiteSpace(_mxmlFilePath))
            return true;

        IEnumerable<string> properties = _mxmlIsAccountStore
            ? MxmlRewardEditor.AccountArrayPropertyNames
            : ["UnlockedPlatformRewards"];
        bool saved = SyncMxmlSafely(accountData, properties);
        _mxmlStatus.Text = UiStrings.Get(saved ? "account.file_found" : "account.file_not_found");
        _mxmlStatus.Foreground = saved ? Brushes.Green : Brushes.IndianRed;
        return saved;
    }

    public void ApplyUiLocalisation()
    {
        if (_accountData is null)
            _statusLabel.Text = UiStrings.Get("account.status_not_loaded");
        _warningLabel.Text = UiStrings.Get("account.twitch_warning");
        _consistencyButton.Content = UiStrings.Get("account.consistency_check");
        ApplyTabLocalisation(_seasonTab, "account.tab_season");
        ApplyTabLocalisation(_twitchTab, "account.tab_twitch");
        ApplyTabLocalisation(_platformTab, "account.tab_platform");
        _mxmlInfo.Text = UiStrings.Get(_mxmlIsAccountStore
            ? "account.macos_mxml_info"
            : "account.platform_mxml_info");
        _mxmlPathBox.PlaceholderText = UiStrings.Get("account.platform_mxml_placeholder");
        _mxmlBrowse.Content = UiStrings.Get("common.browse");
    }

    private RewardTab CreateRewardTab(bool includeExpedition)
    {
        var tab = new RewardTab(includeExpedition);
        tab.UnlockAll.Click += (_, _) => SetAll(tab, unlocked: true, redeemed: null);
        tab.LockAll.Click += (_, _) => SetAll(tab, unlocked: false, redeemed: null);
        tab.RedeemAll.Click += (_, _) => SetAll(tab, unlocked: null, redeemed: true);
        tab.RemoveAll.Click += (_, _) => SetAll(tab, unlocked: null, redeemed: false);
        tab.Filter.TextChanged += (_, _) => ApplyFilter(tab);
        tab.ClearFilter.Click += (_, _) => tab.Filter.Text = string.Empty;
        return tab;
    }

    private void RebuildRewardDefinitions()
    {
        _seasonRewards.Clear();
        _twitchRewards.Clear();
        _platformRewards.Clear();
        _productIds.Clear();
        AddDefinitions(_seasonRewards, RewardDatabase.SeasonRewards, includeSeason: true);
        AddDefinitions(_twitchRewards, RewardDatabase.TwitchRewards, includeSeason: false);
        AddDefinitions(_platformRewards, RewardDatabase.PlatformRewards, includeSeason: false);
    }

    private void AddDefinitions(List<AccountLogic.RewardDbEntry> destination, IEnumerable<RewardEntry> rewards, bool includeSeason)
    {
        foreach (RewardEntry reward in rewards)
        {
            destination.Add(new AccountLogic.RewardDbEntry
            {
                Id = reward.Id,
                Name = ResolveDisplayName(reward),
                SeasonId = includeSeason ? reward.SeasonId : -1,
                StageId = includeSeason ? reward.StageId : -1,
                MustBeUnlocked = includeSeason && reward.Unlock,
            });
            if (!string.IsNullOrWhiteSpace(reward.ProductId))
                _productIds[reward.Id] = reward.ProductId;
        }
    }

    private string ResolveDisplayName(RewardEntry reward)
    {
        GameItem? item = string.IsNullOrWhiteSpace(reward.ProductId)
            ? null
            : _resources.Items.GetItem(reward.ProductId);
        return string.IsNullOrWhiteSpace(item?.Name) ? reward.Name : item.Name;
    }

    private void ApplyAccountData(AccountLogic.AccountData data, bool useMxml)
    {
        _loading = true;
        try
        {
            _accountData = data.AccountObject;
            _accountFilePath = data.AccountFilePath;
            _mxmlIsAccountStore = data.IsMxmlAccountStore;
            _mxmlInfo.Text = UiStrings.Get(_mxmlIsAccountStore
                ? "account.macos_mxml_info"
                : "account.platform_mxml_info");
            if (data.ErrorMessage is not null)
            {
                ClearRows();
                _statusLabel.Text = data.ErrorMessage;
                return;
            }

            if (useMxml && data.IsMxmlAccountStore && !string.IsNullOrWhiteSpace(data.AccountFilePath))
            {
                SetMxmlPath(data.AccountFilePath);
            }
            else if (useMxml && string.IsNullOrWhiteSpace(_mxmlFilePath))
            {
                string? detected = MxmlRewardEditor.AutoDetectMxmlPath();
                if (detected is not null)
                    SetMxmlPath(detected);
            }
            HashSet<string> platformUnlocked = data.PlatformUnlocked;
            if (useMxml && !_mxmlIsAccountStore && !string.IsNullOrWhiteSpace(_mxmlFilePath))
            {
                HashSet<string> mxml = MxmlRewardEditor.ReadUnlockedRewards(_mxmlFilePath);
                platformUnlocked = new HashSet<string>(
                    platformUnlocked.Where(mxml.Contains), StringComparer.OrdinalIgnoreCase);
            }
            PopulateRows(_seasonTab, _seasonRewards, data.SeasonUnlocked);
            PopulateRows(_twitchTab, _twitchRewards, data.TwitchUnlocked);
            PopulateRows(_platformTab, _platformRewards, platformUnlocked);
            _statusLabel.Text = data.StatusMessage ?? string.Empty;
        }
        finally
        {
            _loading = false;
        }
    }

    private void PopulateRows(
        RewardTab tab,
        List<AccountLogic.RewardDbEntry> definitions,
        HashSet<string> unlocked,
        HashSet<string>? redeemed = null)
    {
        tab.AllRows.Clear();
        foreach (AccountLogic.RewardRowData row in AccountLogic.BuildRewardRows(definitions, unlocked, redeemed))
        {
            var viewModel = new RewardRowViewModel(
                row.Id,
                row.Name,
                row.SeasonId,
                ResolveIcon(row.Id, row.Name),
                row.Unlocked,
                row.Redeemed);
            viewModel.Changed += OnRewardChanged;
            tab.AllRows.Add(viewModel);
        }
        ApplyFilter(tab);
    }

    private Bitmap? ResolveIcon(string rewardId, string rewardName)
    {
        if (_productIds.TryGetValue(rewardId, out string? productId))
        {
            Bitmap? productIcon = _resources.Icons?.GetIconForItem(productId, _resources.Items);
            if (productIcon is not null)
                return productIcon;
        }
        Bitmap? direct = _resources.Icons?.GetIconForItem(rewardId, _resources.Items);
        if (direct is not null)
            return direct;
        GameItem? byName = _resources.Items.Items.Values.FirstOrDefault(item =>
            string.Equals(item.Name, rewardName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.NameLower, rewardName, StringComparison.OrdinalIgnoreCase));
        return byName is null ? null : _resources.Icons?.GetIconForItem(byName.Id, _resources.Items);
    }

    private void ApplyFilter(RewardTab tab)
    {
        string filter = tab.Filter.Text?.Trim() ?? string.Empty;
        tab.VisibleRows.Clear();
        foreach (RewardRowViewModel row in tab.AllRows.Where(row =>
                     filter.Length == 0 ||
                     row.Id.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                     row.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)))
            tab.VisibleRows.Add(row);
    }

    private void SetAll(RewardTab tab, bool? unlocked, bool? redeemed)
    {
        foreach (RewardRowViewModel row in tab.VisibleRows)
        {
            if (unlocked.HasValue)
                row.Unlocked = unlocked.Value;
            if (redeemed.HasValue)
                row.Redeemed = redeemed.Value;
        }
    }

    private void SetRedeemed(RewardTab tab, HashSet<string> redeemed)
    {
        foreach (RewardRowViewModel row in tab.AllRows)
            row.Redeemed = redeemed.Contains(row.Id);
    }

    private static List<(string Id, bool Unlocked, bool Redeemed)> CollectRows(RewardTab tab) =>
        tab.AllRows.Select(row => (row.Id, row.Unlocked, row.Redeemed)).ToList();

    private static List<(string Id, bool Redeemed)> GetChangedRewards(
        IEnumerable<(string Id, bool Unlocked, bool Redeemed)> rows,
        HashSet<string> original) => rows
        .Where(row => !string.IsNullOrWhiteSpace(row.Id) && row.Redeemed != original.Contains(row.Id))
        .Select(row => (row.Id, row.Redeemed))
        .ToList();

    private static List<(string Id, bool Unlocked, bool Redeemed)> GetChangedStaleRows(
        IEnumerable<(string Id, bool Unlocked, bool Redeemed)> rows,
        HashSet<string> original) => rows
        .Where(row => !string.IsNullOrWhiteSpace(row.Id) && original.Contains(row.Id) && !row.Redeemed)
        .ToList();

    private async void OnBrowseMxml(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!UsesMxml)
            return;
        string? path = await _desktop.PickOpenFileAsync(new(
            "Select GCUSERSETTINGSDATA.MXML",
            "GCUSERSETTINGSDATA.MXML",
            ".MXML"));
        if (string.IsNullOrWhiteSpace(path))
            return;
        SetMxmlPath(path);
        if (_accountData is null)
        {
            ApplyAccountData(AccountLogic.LoadMxmlAccountData(path), useMxml: true);
            return;
        }
        JsonObject settings = _accountData.GetObject("UserSettingsData") ?? _accountData;
        HashSet<string> unlocked = AccountLogic.GetUnlockedSet(settings.GetArray("UnlockedPlatformRewards"));
        unlocked.UnionWith(MxmlRewardEditor.ReadUnlockedRewards(path));
        PopulateRows(_platformTab, _platformRewards, unlocked);
    }

    private void SetMxmlPath(string path)
    {
        _mxmlFilePath = path;
        _mxmlPathBox.Text = path;
        bool exists = File.Exists(path);
        _mxmlStatus.Text = UiStrings.Get(exists ? "account.file_found" : "account.file_not_found");
        _mxmlStatus.Foreground = exists ? Brushes.Green : Brushes.IndianRed;
    }

    private bool SyncMxmlSafely(JsonObject accountData, IEnumerable<string> propertyNames)
    {
        if (string.IsNullOrWhiteSpace(_mxmlFilePath))
            return true;
        if (!File.Exists(_mxmlFilePath))
            return false;

        string temporary = _mxmlFilePath + ".nmse.tmp";
        string backup = _mxmlFilePath + ".nmse.bak";
        string[] properties = propertyNames.Distinct(StringComparer.Ordinal).ToArray();
        try
        {
            File.Copy(_mxmlFilePath, temporary, overwrite: true);
            if (!MxmlRewardEditor.WriteAccountObject(temporary, accountData, properties))
                return false;

            JsonObject settings = accountData.GetObject("UserSettingsData") ?? accountData;
            foreach (string property in properties)
            {
                HashSet<string> desired = AccountLogic.GetUnlockedSet(settings.GetArray(property));
                HashSet<string> written = MxmlRewardEditor.ReadPropertyValues(temporary, property);
                if (!desired.SetEquals(written))
                    return false;
            }

            File.Copy(_mxmlFilePath, backup, overwrite: true);
            File.Move(temporary, _mxmlFilePath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    private async void OnConsistencyCheck(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_currentSaveData is null)
        {
            await _desktop.ShowInfoAsync(UiStrings.Get("account.consistency_check"), UiStrings.Get("account.consistency_no_save"));
            return;
        }
        List<AccountLogic.ConsistencyIssue> issues = AccountLogic.CheckConsistencyStructured(_currentSaveData, _resources.Items);
        if (issues.Count == 0)
        {
            await _desktop.ShowInfoAsync(UiStrings.Get("account.consistency_check"), UiStrings.Get("account.consistency_ok"));
            return;
        }
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            await _desktop.ShowErrorAsync(UiStrings.Get("account.consistency_check"), UiStrings.Format("account.consistency_issues_found", issues.Count));
            return;
        }

        var dialog = new AccountConsistencyDialog(issues, _currentSaveData, _resources);
        await dialog.ShowDialog(owner);
        if (dialog.HasChanges) DataModified?.Invoke(this, EventArgs.Empty);
    }

    private void OnRewardChanged(object? sender, EventArgs e)
    {
        if (!_loading)
            DataModified?.Invoke(this, EventArgs.Empty);
    }

    private void ClearRows()
    {
        foreach (RewardTab tab in new[] { _seasonTab, _twitchTab, _platformTab })
        {
            tab.AllRows.Clear();
            tab.VisibleRows.Clear();
        }
    }

    private static void ApplyTabLocalisation(RewardTab tab, string tabKey)
    {
        tab.Page.Header = UiStrings.Get(tabKey);
        tab.UnlockAll.Content = UiStrings.Get("account.unlock_all");
        tab.LockAll.Content = UiStrings.Get("account.lock_all");
        tab.RedeemAll.Content = UiStrings.Get("account.redeem_all");
        tab.RemoveAll.Content = UiStrings.Get("account.remove_all");
        tab.FilterLabel.Text = UiStrings.Get("common.filter");
        tab.IdColumn.Header = UiStrings.Get("account.col_reward_id");
        tab.NameColumn.Header = UiStrings.Get("account.col_name");
        if (tab.ExpeditionColumn is not null)
            tab.ExpeditionColumn.Header = UiStrings.Get("account.col_expedition");
        tab.UnlockedColumn.Header = UiStrings.Get("account.col_unlocked");
        tab.RedeemedColumn.Header = UiStrings.Get("account.col_redeemed");
    }

    private sealed class RewardTab
    {
        public RewardTab(bool includeExpedition)
        {
            UnlockAll = new Button();
            LockAll = new Button();
            RedeemAll = new Button();
            RemoveAll = new Button();
            FilterLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            Filter = new TextBox { Width = 200 };
            ClearFilter = new Button { Content = "X", MinWidth = 30 };
            var toolbar = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { UnlockAll, LockAll, RedeemAll, RemoveAll, FilterLabel, Filter, ClearFilter },
            };

            Grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserResizeColumns = true,
                CanUserSortColumns = true,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                ItemsSource = VisibleRows,
            };
            Grid.Columns.Add(new DataGridTemplateColumn
            {
                Width = new DataGridLength(42),
                CellTemplate = new FuncDataTemplate<RewardRowViewModel>((row, _) => new Image
                {
                    Source = row?.Icon,
                    Width = 28,
                    Height = 28,
                    Stretch = Stretch.Uniform,
                }),
            });
            IdColumn = new DataGridTextColumn
            {
                Binding = new Binding(nameof(RewardRowViewModel.Id)),
                IsReadOnly = true,
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            };
            NameColumn = new DataGridTextColumn
            {
                Binding = new Binding(nameof(RewardRowViewModel.Name)),
                IsReadOnly = true,
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            };
            Grid.Columns.Add(IdColumn);
            Grid.Columns.Add(NameColumn);
            if (includeExpedition)
            {
                ExpeditionColumn = new DataGridTextColumn
                {
                    Binding = new Binding(nameof(RewardRowViewModel.Expedition)),
                    IsReadOnly = true,
                    Width = new DataGridLength(100),
                };
                Grid.Columns.Add(ExpeditionColumn);
            }
            UnlockedColumn = new DataGridCheckBoxColumn
            {
                Binding = new Binding(nameof(RewardRowViewModel.Unlocked)) { Mode = BindingMode.TwoWay },
                Width = new DataGridLength(135),
            };
            RedeemedColumn = new DataGridCheckBoxColumn
            {
                Binding = new Binding(nameof(RewardRowViewModel.Redeemed)) { Mode = BindingMode.TwoWay },
                Width = new DataGridLength(130),
            };
            Grid.Columns.Add(UnlockedColumn);
            Grid.Columns.Add(RedeemedColumn);
            Page = new TabItem
            {
                Content = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,*"),
                    Children = { toolbar, Grid },
                },
            };
            global::Avalonia.Controls.Grid.SetRow(Grid, 1);
        }

        public ObservableCollection<RewardRowViewModel> AllRows { get; } = [];
        public ObservableCollection<RewardRowViewModel> VisibleRows { get; } = [];
        public TabItem Page { get; }
        public DataGrid Grid { get; }
        public Button UnlockAll { get; }
        public Button LockAll { get; }
        public Button RedeemAll { get; }
        public Button RemoveAll { get; }
        public TextBlock FilterLabel { get; }
        public TextBox Filter { get; }
        public Button ClearFilter { get; }
        public DataGridTextColumn IdColumn { get; }
        public DataGridTextColumn NameColumn { get; }
        public DataGridTextColumn? ExpeditionColumn { get; }
        public DataGridCheckBoxColumn UnlockedColumn { get; }
        public DataGridCheckBoxColumn RedeemedColumn { get; }
    }

    private sealed class RewardRowViewModel : INotifyPropertyChanged
    {
        private bool _unlocked;
        private bool _redeemed;

        public RewardRowViewModel(string id, string name, int seasonId, Bitmap? icon, bool unlocked, bool redeemed)
        {
            Id = id;
            Name = name;
            Expedition = seasonId >= 0 ? seasonId.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
            Icon = icon;
            _unlocked = unlocked;
            _redeemed = redeemed;
        }

        public string Id { get; }
        public string Name { get; }
        public string Expedition { get; }
        public Bitmap? Icon { get; }
        public bool Unlocked
        {
            get => _unlocked;
            set => SetField(ref _unlocked, value);
        }
        public bool Redeemed
        {
            get => _redeemed;
            set => SetField(ref _redeemed, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? Changed;

        private void SetField(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
        {
            if (field == value)
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
