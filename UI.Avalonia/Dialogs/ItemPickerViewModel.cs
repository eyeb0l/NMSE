using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using NMSE.Data;
using NMSE.UI.Avalonia.Services;

namespace NMSE.UI.Avalonia.Dialogs;

public sealed class ItemPickerItemViewModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Type { get; init; }
    public Bitmap? Icon { get; init; }
}

/// <summary>Filter and selection state for <see cref="ItemPickerDialog"/>.</summary>
public sealed class ItemPickerViewModel : INotifyPropertyChanged
{
    private const string AllFilter = "(All)";

    private readonly List<GameItem> _allItems = [];
    private readonly List<string> _resultItemIds = [];
    private AvaloniaIconProvider? _iconProvider;
    private string _searchText = string.Empty;
    private int _selectedTypeIndex;
    private int _selectedCategoryIndex;
    private string _manualItemId = string.Empty;
    private ItemPickerItemViewModel? _selectedItem;
    private bool _allowMultiSelect;
    private bool _hasResult;
    private bool _initializing;

    public ObservableCollection<string> TypeFilters { get; } = [];
    public ObservableCollection<string> CategoryFilters { get; } = [];
    public ObservableCollection<ItemPickerItemViewModel> FilteredItems { get; } = [];
    public ObservableCollection<ItemPickerItemViewModel> SelectedItems { get; } = [];

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetField(ref _searchText, value ?? string.Empty))
                return;
            if (!_initializing)
                ApplyFilters();
        }
    }

    public int SelectedTypeIndex
    {
        get => _selectedTypeIndex;
        set
        {
            if (!SetField(ref _selectedTypeIndex, value))
                return;
            if (!_initializing)
            {
                RebuildCategoryFilters();
                ApplyFilters();
            }
        }
    }

    public int SelectedCategoryIndex
    {
        get => _selectedCategoryIndex;
        set
        {
            if (!SetField(ref _selectedCategoryIndex, value))
                return;
            if (!_initializing)
                ApplyFilters();
        }
    }

    public string ManualItemId
    {
        get => _manualItemId;
        set => SetField(ref _manualItemId, value ?? string.Empty);
    }

    public ItemPickerItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set => SetField(ref _selectedItem, value);
    }

    public bool AllowMultiSelect
    {
        get => _allowMultiSelect;
        set => SetField(ref _allowMultiSelect, value);
    }

    public bool HasResult
    {
        get => _hasResult;
        private set => SetField(ref _hasResult, value);
    }

    public string? ResultItemId => _resultItemIds.FirstOrDefault();
    public IReadOnlyList<string> ResultItemIds => _resultItemIds;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Initialize(
        GameItemDatabase database,
        AvaloniaIconProvider? iconProvider,
        string? filterCategory = null,
        string? filterType = null,
        bool allowMultiSelect = false)
    {
        ArgumentNullException.ThrowIfNull(database);
        _initializing = true;
        try
        {
            _iconProvider = iconProvider;
            AllowMultiSelect = allowMultiSelect;
            _allItems.Clear();
            _allItems.AddRange(database.Items.Values.Where(item =>
                (string.IsNullOrWhiteSpace(filterCategory) ||
                 string.Equals(item.Category, filterCategory, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(filterType) ||
                 string.Equals(item.ItemType, filterType, StringComparison.OrdinalIgnoreCase))));

            SearchText = string.Empty;
            ManualItemId = string.Empty;
            SelectedItem = null;
            SelectedItems.Clear();
            _resultItemIds.Clear();
            HasResult = false;

            ReplaceWith(TypeFilters,
                new[] { AllFilter }.Concat(_allItems
                    .Select(item => item.ItemType)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.CurrentCultureIgnoreCase)));
            _selectedTypeIndex = 0;
            OnPropertyChanged(nameof(SelectedTypeIndex));
            RebuildCategoryFilters();
        }
        finally
        {
            _initializing = false;
        }

        ApplyFilters();
    }

    public void Refresh() => ApplyFilters();

    public void SetSelectedItems(IEnumerable<ItemPickerItemViewModel> selectedItems)
    {
        SelectedItems.Clear();
        foreach (ItemPickerItemViewModel item in selectedItems.DistinctBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
            SelectedItems.Add(item);
        SelectedItem = SelectedItems.FirstOrDefault();
    }

    public bool ConfirmSelection()
    {
        IEnumerable<string> selectedIds = AllowMultiSelect
            ? SelectedItems.Select(item => item.Id)
            : SelectedItem is null ? [] : [SelectedItem.Id];

        List<string> ids = selectedIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ids.Count == 0 && !string.IsNullOrWhiteSpace(ManualItemId))
            ids.Add(ManualItemId.Trim());
        if (ids.Count == 0)
            return false;

        SetResults(ids);
        return true;
    }

    public bool ConfirmManualId()
    {
        string id = ManualItemId.Trim();
        if (id.Length == 0)
            return false;

        SetResults([id]);
        return true;
    }

    private void RebuildCategoryFilters()
    {
        string? selectedType = SelectedTypeIndex > 0 && SelectedTypeIndex < TypeFilters.Count
            ? TypeFilters[SelectedTypeIndex]
            : null;

        ReplaceWith(CategoryFilters,
            new[] { AllFilter }.Concat(_allItems
                .Where(item => selectedType is null ||
                               string.Equals(item.ItemType, selectedType, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Category)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.CurrentCultureIgnoreCase)));
        _selectedCategoryIndex = 0;
        OnPropertyChanged(nameof(SelectedCategoryIndex));
    }

    private void ApplyFilters()
    {
        string? type = SelectedTypeIndex > 0 && SelectedTypeIndex < TypeFilters.Count
            ? TypeFilters[SelectedTypeIndex]
            : null;
        string? category = SelectedCategoryIndex > 0 && SelectedCategoryIndex < CategoryFilters.Count
            ? CategoryFilters[SelectedCategoryIndex]
            : null;
        string search = SearchText.Trim();

        IEnumerable<GameItem> filtered = _allItems.Where(item =>
            (type is null || string.Equals(item.ItemType, type, StringComparison.OrdinalIgnoreCase)) &&
            (category is null || string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase)) &&
            (search.Length == 0 ||
             item.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
             item.Category.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
             item.Id.Contains(search, StringComparison.OrdinalIgnoreCase)));

        ReplaceWith(FilteredItems, filtered
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(item => new ItemPickerItemViewModel
            {
                Id = item.Id,
                Name = string.IsNullOrWhiteSpace(item.Name) ? item.Id : item.Name,
                Category = item.Category,
                Type = item.ItemType,
                Icon = _iconProvider?.GetIcon(item.Icon),
            }));

        SelectedItem = null;
        SelectedItems.Clear();
    }

    private void SetResults(IEnumerable<string> ids)
    {
        _resultItemIds.Clear();
        _resultItemIds.AddRange(ids);
        OnPropertyChanged(nameof(ResultItemIds));
        OnPropertyChanged(nameof(ResultItemId));
        HasResult = true;
    }

    private static void ReplaceWith<T>(ObservableCollection<T> collection, IEnumerable<T> values)
    {
        collection.Clear();
        foreach (T value in values)
            collection.Add(value);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
