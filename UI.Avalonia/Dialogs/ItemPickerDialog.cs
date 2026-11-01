using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using NMSE.Data;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.UI.Avalonia.Services;

namespace NMSE.UI.Avalonia.Dialogs;

/// <summary>A filterable, localized item picker supporting single and extended selection.</summary>
public sealed class ItemPickerDialog : Window
{
    private readonly ItemPickerViewModel _viewModel = new();
    private readonly TextBox _searchBox;
    private readonly ComboBox _typeCombo;
    private readonly ComboBox _categoryCombo;
    private readonly DataGrid _itemGrid;
    private readonly Button _acceptButton;
    private readonly TextBox _manualIdBox;
    private GameItemDatabase? _database;
    private readonly Dictionary<string, uint> _selectedSeeds = new(StringComparer.OrdinalIgnoreCase);
    private readonly NumericUpDown _technologySeed = new() { Minimum = 0, Maximum = 99999, FormatString = "00000", Width = 100 };
    private readonly Button _bestSeed = new() { Content = "Best roll" };
    private readonly Button _searchSeed = new() { Content = "Choose Stats…" };
    private readonly StackPanel _technologyTools = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly TextBlock _technologyPreview = new() { TextWrapping = TextWrapping.Wrap, MaxHeight = 100 };
    private bool _refreshingSeed;
    public LocalisationService? Localisation { get; set; }

    public ItemPickerDialog(string? title = null)
    {
        Title = title ?? UiStrings.Get("inventory.item_picker");
        Width = 760;
        Height = 640;
        MinWidth = 560;
        MinHeight = 400;
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        DataContext = _viewModel;

        _searchBox = new TextBox { PlaceholderText = UiStrings.Get("item_picker.filter_placeholder") };
        _searchBox.Bind(TextBox.TextProperty, new ReflectionBinding(nameof(ItemPickerViewModel.SearchText))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });

        var clearButton = new Button
        {
            Content = UiStrings.Get("item_picker.clear"),
            MinWidth = 34,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        clearButton.Click += (_, _) => _searchBox.Text = string.Empty;

        var searchRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        searchRow.Children.Add(new TextBlock
        {
            Text = UiStrings.Get("inventory.label_search"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        searchRow.Children.Add(_searchBox);
        Grid.SetColumn(_searchBox, 1);
        searchRow.Children.Add(clearButton);
        Grid.SetColumn(clearButton, 2);

        _typeCombo = new ComboBox { MinWidth = 140 };
        _typeCombo.Bind(ItemsControl.ItemsSourceProperty, new ReflectionBinding(nameof(ItemPickerViewModel.TypeFilters)));
        _typeCombo.Bind(ComboBox.SelectedIndexProperty, new ReflectionBinding(nameof(ItemPickerViewModel.SelectedTypeIndex))
        {
            Mode = BindingMode.TwoWay,
        });
        _categoryCombo = new ComboBox { MinWidth = 140 };
        _categoryCombo.Bind(ItemsControl.ItemsSourceProperty, new ReflectionBinding(nameof(ItemPickerViewModel.CategoryFilters)));
        _categoryCombo.Bind(ComboBox.SelectedIndexProperty, new ReflectionBinding(nameof(ItemPickerViewModel.SelectedCategoryIndex))
        {
            Mode = BindingMode.TwoWay,
        });

        var filterRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*"), ColumnSpacing = 8 };
        filterRow.Children.Add(CreateCenteredLabel("inventory.label_filter_type"));
        filterRow.Children.Add(_typeCombo);
        Grid.SetColumn(_typeCombo, 1);
        var categoryLabel = CreateCenteredLabel("inventory.label_filter_category");
        categoryLabel.Margin = new Thickness(12, 0, 0, 0);
        filterRow.Children.Add(categoryLabel);
        Grid.SetColumn(categoryLabel, 2);
        filterRow.Children.Add(_categoryCombo);
        Grid.SetColumn(_categoryCombo, 3);

        var filters = new StackPanel { Spacing = 8, Children = { searchRow, filterRow } };

        _itemGrid = CreateItemGrid();
        _itemGrid.Bind(DataGrid.ItemsSourceProperty, new ReflectionBinding(nameof(ItemPickerViewModel.FilteredItems)));
        _itemGrid.SelectionChanged += OnGridSelectionChanged;
        _itemGrid.DoubleTapped += OnGridDoubleTapped;

        _manualIdBox = new TextBox { PlaceholderText = UiStrings.Get("item_picker.manual_placeholder") };
        _manualIdBox.Bind(TextBox.TextProperty, new ReflectionBinding(nameof(ItemPickerViewModel.ManualItemId))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
        _manualIdBox.TextChanged += (_, _) => UpdateAcceptState();

        var manualButton = new Button { Content = UiStrings.Get("item_picker.add_manual") };
        manualButton.Click += OnConfirmManual;
        var manualRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        manualRow.Children.Add(CreateCenteredLabel("inventory.label_item_id"));
        manualRow.Children.Add(_manualIdBox);
        Grid.SetColumn(_manualIdBox, 1);
        manualRow.Children.Add(manualButton);
        Grid.SetColumn(manualButton, 2);

        var warning = new TextBlock
        {
            Text = UiStrings.Get("item_picker.manual_warning"),
            Foreground = Brushes.IndianRed,
            TextWrapping = TextWrapping.Wrap,
        };
        var generateSeed = new Button { Content = "Generate" };
        _technologyTools.Children.Add(new TextBlock { Text = "Technology seed:", VerticalAlignment = VerticalAlignment.Center });
        _technologyTools.Children.Add(_technologySeed); _technologyTools.Children.Add(generateSeed);
        _technologyTools.Children.Add(_bestSeed); _technologyTools.Children.Add(_searchSeed);
        _technologyTools.IsVisible = false;
        generateSeed.Click += (_, _) => _technologySeed.Value = uint.Parse(ProceduralSeedHelper.Generate(), CultureInfo.InvariantCulture);
        _bestSeed.Click += (_, _) => { if (ProcTechData.TryGetBestSeed(_viewModel.SelectedItem?.Id, out uint seed)) _technologySeed.Value = seed; };
        _searchSeed.Click += async (_, _) =>
        {
            if (_database?.GetItem(_viewModel.SelectedItem?.Id ?? "") is not { } item) return;
            uint? seed = await SeedSearchWindows.SearchTechnologyAsync(this, item);
            if (seed is not null) _technologySeed.Value = seed.Value;
        };
        _technologySeed.ValueChanged += (_, _) =>
        {
            if (_refreshingSeed || _viewModel.SelectedItem is not { } selected) return;
            _selectedSeeds[selected.Id] = (uint)(_technologySeed.Value ?? 0);
            RefreshTechnologyPreview();
        };
        var manualArea = new StackPanel { Spacing = 5, Children = { _technologyTools, _technologyPreview, manualRow, warning } };

        _acceptButton = new Button
        {
            Content = UiStrings.Get("item_picker.add"),
            IsDefault = true,
            IsEnabled = false,
            MinWidth = 92,
        };
        _acceptButton.Click += OnConfirmSelection;
        var cancelButton = new Button
        {
            Content = UiStrings.Get("common.cancel"),
            IsCancel = true,
            MinWidth = 92,
        };
        cancelButton.Click += (_, _) => Close(null);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancelButton, _acceptButton },
        };

        var root = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"),
            RowSpacing = 12,
            Children = { filters, _itemGrid, manualArea, buttons },
        };
        Grid.SetRow(_itemGrid, 1);
        Grid.SetRow(manualArea, 2);
        Grid.SetRow(buttons, 3);
        Content = root;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    public string? SelectedItemId => _viewModel.HasResult ? SelectedItemIds.FirstOrDefault() : null;
    public IReadOnlyList<string> SelectedItemIds => _viewModel.ResultItemIds.Select(id => _selectedSeeds.TryGetValue(id, out uint seed) ? $"{id}#{seed:D5}" : id).ToArray();
    public ItemPickerViewModel ViewModel => _viewModel;

    public void Initialize(
        GameItemDatabase database,
        AvaloniaIconProvider? iconProvider,
        string? filterCategory = null,
        string? filterType = null,
        bool multiSelect = false)
    {
        _database = database;
        _selectedSeeds.Clear();
        _itemGrid.SelectionMode = multiSelect ? DataGridSelectionMode.Extended : DataGridSelectionMode.Single;
        _viewModel.Initialize(database, iconProvider, filterCategory, filterType, multiSelect);
        _searchBox.Text = _viewModel.SearchText;
        _typeCombo.SelectedIndex = _viewModel.SelectedTypeIndex;
        _categoryCombo.SelectedIndex = _viewModel.SelectedCategoryIndex;
        _manualIdBox.Text = _viewModel.ManualItemId;
        UpdateAcceptState();
    }

    private DataGrid CreateItemGrid()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserResizeColumns = true,
            CanUserSortColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            SelectionMode = DataGridSelectionMode.Single,
        };

        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = UiStrings.Get("item_picker.col_icon"),
            Width = new DataGridLength(46),
            CellTemplate = new FuncDataTemplate<ItemPickerItemViewModel>((item, _) => new Image
            {
                Source = item?.Icon,
                Width = 28,
                Height = 28,
                Margin = new Thickness(4),
                Stretch = Stretch.Uniform,
            }),
        });
        grid.Columns.Add(CreateTextColumn("item_picker.col_name", nameof(ItemPickerItemViewModel.Name), star: true));
        grid.Columns.Add(CreateTextColumn("item_picker.col_id", nameof(ItemPickerItemViewModel.Id), 160));
        grid.Columns.Add(CreateTextColumn("item_picker.col_category", nameof(ItemPickerItemViewModel.Category), 130));
        grid.Columns.Add(CreateTextColumn("common.type", nameof(ItemPickerItemViewModel.Type), 120));
        return grid;
    }

    private static DataGridTextColumn CreateTextColumn(string headerKey, string property, double width = 0, bool star = false) =>
        new()
        {
            Header = UiStrings.Get(headerKey),
            Binding = new ReflectionBinding(property),
            Width = star
                ? new DataGridLength(1, DataGridLengthUnitType.Star)
                : new DataGridLength(width),
        };

    private static TextBlock CreateCenteredLabel(string key) => new()
    {
        Text = UiStrings.Get(key),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private void OnGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _viewModel.SetSelectedItems(_itemGrid.SelectedItems.OfType<ItemPickerItemViewModel>());
        RefreshTechnologyPicker();
        UpdateAcceptState();
    }

    private void OnGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_itemGrid.SelectedItems.Count > 0)
            ConfirmAndClose(manualOnly: false);
    }

    private void RefreshTechnologyPicker()
    {
        var selected = _viewModel.SelectedItem;
        var item = _database?.GetItem(selected?.Id ?? "");
        _technologyTools.IsVisible = item is { IsProcedural: true };
        if (item is { IsProcedural: true } && selected is not null)
        {
            if (!_selectedSeeds.TryGetValue(selected.Id, out uint seed))
                _selectedSeeds[selected.Id] = seed = uint.Parse(ProceduralSeedHelper.Generate(), CultureInfo.InvariantCulture);
            _refreshingSeed = true; _technologySeed.Value = seed; _refreshingSeed = false;
            _bestSeed.IsEnabled = ProcTechData.TryGetBestSeed(item.Id, out _);
            _searchSeed.IsEnabled = item.StatLevels.Count > 0;
        }
        RefreshTechnologyPreview();
    }

    private void RefreshTechnologyPreview()
    {
        var item = _database?.GetItem(_viewModel.SelectedItem?.Id ?? "");
        _technologyPreview.Text = ControlTokens.Resolve(item?.Description);
        if (item is { IsProcedural: true })
        {
            uint seed = (uint)(_technologySeed.Value ?? 0);
            _technologyPreview.Text = string.Join("; ", ProcTechLogic.Roll(item, seed)
                .Select(roll => ProcTechLogic.FormatRollLine(roll, item.BaseStat, Localisation, null)));
            ToolTip.SetTip(_technologyPreview, _technologyPreview.Text);
        }
    }

    private void OnConfirmSelection(object? sender, RoutedEventArgs e) => ConfirmAndClose(manualOnly: false);
    private void OnConfirmManual(object? sender, RoutedEventArgs e) => ConfirmAndClose(manualOnly: true);

    private void ConfirmAndClose(bool manualOnly)
    {
        bool confirmed = manualOnly ? _viewModel.ConfirmManualId() : _viewModel.ConfirmSelection();
        if (confirmed)
            Close(SelectedItemId);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ItemPickerViewModel.ManualItemId))
            UpdateAcceptState();
    }

    private void UpdateAcceptState()
    {
        _acceptButton.IsEnabled = _itemGrid.SelectedItems.Count > 0 ||
                                  !string.IsNullOrWhiteSpace(_manualIdBox.Text);
    }
}
