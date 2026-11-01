using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.UI.Avalonia.Panels.BasesStorage;

public sealed partial class ChestsPanel
{
    private readonly GameItemDatabase _database;
    private JsonObject? _playerState;
    private readonly TabItem _allChestsPage = new();
    private readonly TextBlock _allChestsInfo = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _sortLabel = new();
    private readonly TextBlock _paddingLabel = new();
    private readonly ComboBox _sortMode = new() { Name = "AllChestsSortMode", MinWidth = 250, SelectedIndex = 0 };
    private readonly NumericUpDown _padding = new() { Name = "AllChestsPadding", Minimum = 0, Maximum = 119, Value = 0, Width = 110, FormatString = "0" };
    private readonly CheckBox _mergeOnly = new() { Name = "AllChestsMergeOnly" };
    private readonly Button _sortAll = new() { Name = "SortAllChests" };
    private readonly TextBlock _allChestsStatus = new() { Name = "AllChestsStatus", TextWrapping = TextWrapping.Wrap };

    private TabItem BuildAllChestsPage()
    {
        _mergeOnly.IsCheckedChanged += (_, _) => _sortMode.IsEnabled = _padding.IsEnabled = _mergeOnly.IsChecked != true;
        _sortAll.Click += (_, _) => ApplyAllChestsAction();
        _allChestsPage.Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(12), Spacing = 12, Children =
                {
                    _allChestsInfo,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _sortLabel, _sortMode } },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _paddingLabel, _padding } },
                    _mergeOnly, _sortAll, _allChestsStatus,
                },
            },
        };
        return _allChestsPage;
    }

    private void ApplyAllChestsLocalisation()
    {
        _allChestsPage.Header = UiStrings.Get("base.all_chests_tab");
        _allChestsInfo.Text = UiStrings.Get("base.all_chests_info");
        _sortLabel.Text = UiStrings.Get("base.all_chests_sort_label");
        _paddingLabel.Text = UiStrings.Get("base.all_chests_padding_label");
        _mergeOnly.Content = UiStrings.Get("base.all_chests_merge_only");
        _sortAll.Content = UiStrings.Get("base.all_chests_sort_button");
        int selected = Math.Max(0, _sortMode.SelectedIndex);
        _sortMode.ItemsSource = new[] { "name", "type", "rarity", "type_then_rarity" }
            .Select(mode => UiStrings.Get("base.all_chests_sort_" + mode)).ToArray();
        _sortMode.SelectedIndex = selected;
    }

    private void ApplyAllChestsAction()
    {
        if (_playerState is null) return;
        // Flush every opened grid before pooling its data. After the rewrite, invalidate
        // those grids so saving cannot put their old Slots arrays back into the save.
        for (int i = 0; i < 10; i++)
            if (_loaded[i]) _grids[i].SaveInventory(_pending[i]);

        int stacks, freed;
        if (_mergeOnly.IsChecked == true)
        {
            var result = InventoryBulkActions.MergeAllChestsInPlace(_playerState, _database);
            stacks = result.StacksRemaining; freed = result.SlotsFreed;
        }
        else
        {
            var mode = _sortMode.SelectedIndex switch
            {
                1 => ChestSortMode.Type, 2 => ChestSortMode.Rarity,
                3 => ChestSortMode.TypeThenRarity, _ => ChestSortMode.Name,
            };
            var result = InventoryBulkActions.SortAllChests(_playerState, _database, mode, (int)(_padding.Value ?? 0));
            if (!result.Success)
            {
                _allChestsStatus.Foreground = Brushes.OrangeRed;
                _allChestsStatus.Text = UiStrings.Format("base.all_chests_result_insufficient", result.StacksPlaced, result.SlotsAvailable);
                return;
            }
            stacks = result.StacksPlaced; freed = result.SlotsFreed;
        }
        for (int i = 0; i < 10; i++)
        {
            _pending[i] = _playerState.GetObject(BaseLogic.ChestInventoryKeys[i]);
            _loaded[i] = false;
        }
        EnsureLoaded(_tabs.SelectedIndex);
        _allChestsStatus.Foreground = Brushes.ForestGreen;
        _allChestsStatus.Text = UiStrings.Format("base.all_chests_result_success", stacks, freed);
        DataModified?.Invoke(this, EventArgs.Empty);
    }
}
