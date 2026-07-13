using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Dialogs;
using NMSE.UI.Avalonia.Services;

namespace NMSE.UI.Avalonia.Inventory;

/// <summary>
/// Cross-platform, JSON-backed NMS inventory editor. Mutations are applied to the
/// loaded inventory immediately; <see cref="CommitPendingEdits"/> commits the focused
/// detail controls before a save coordinator serializes the tree.
/// </summary>
public sealed class InventoryGrid : UserControl
{
    private const int DefaultWidth = 10;
    private const double DragThreshold = 6;
    private const double CellPitchX = 90;
    private const double CellPitchY = 126;

    private readonly WrapPanel _cellsPanel = new()
    {
        Orientation = Orientation.Horizontal,
        ItemWidth = CellPitchX,
        ItemHeight = CellPitchY,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(8, 4, 8, 12),
    };
    private readonly TextBox _widthBox = new() { Width = 52, MinHeight = 34, Text = "10", TextAlignment = TextAlignment.Center };
    private readonly TextBox _heightBox = new() { Width = 52, MinHeight = 34, Text = "6", TextAlignment = TextAlignment.Center };
    private readonly Button _resizeButton = new();
    private readonly Button _pickerButton = new();
    private readonly Button _exportButton = new();
    private readonly Button _importButton = new();
    private readonly Button _autoStackButton = new();
    private readonly ComboBox _sortMode = new() { Width = 155, MinHeight = 34 };
    private readonly TextBlock _maxSupported = new() { Foreground = Brushes.IndianRed, FontWeight = FontWeight.Bold };
    private readonly TextBlock _selectionTitle = new() { FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _selectionPosition = new() { Foreground = Brushes.Gray };
    private readonly TextBox _itemId = new();
    private readonly TextBox _amount = new();
    private readonly TextBox _maxAmount = new();
    private readonly TextBox _damage = new();
    private readonly Button _applyButton = new();
    private readonly Image _detailIcon = new() { Width = 56, Height = 56, Stretch = Stretch.Uniform };
    private readonly TextBlock _detailMetadata = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray };

    private readonly List<InventoryCellState> _states = [];
    private readonly Dictionary<(int X, int Y), InventorySlotCell> _cellViews = [];
    private readonly HashSet<(int X, int Y)> _pinnedSlots = [];
    private readonly List<CorvettePartState> _corvetteParts = [];
    private JsonObject? _inventory;
    private JsonArray? _slots;
    private GameItemDatabase? _database;
    private AvaloniaIconProvider? _iconProvider;
    private InventorySlotCell? _selectedCell;
    private InventorySlotCell? _copiedCell;
    private InventorySlotCell? _dragSource;
    private Point _dragStart;
    private bool _dragModifierDuplicate;
    private string _inventoryGroup = "Default";
    private string _ownerType = string.Empty;
    private string _exportFileName = "inventory.json";
    private string _exportExtension = "json";
    private bool _isTechInventory;
    private bool _isCargoInventory;
    private bool _isStorageInventory;
    private bool _isChestInventory;
    private bool _sortingEnabled;
    private bool _pinningEnabled;
    private bool _superchargeDisabled;
    private bool _slotToggleDisabled;
    private int _maxSuperchargedSlots = -1;
    private int _maxSuperchargeRow = -1;
    private int _gridWidth = DefaultWidth;
    private int _gridHeight = 1;
    private int _corvetteShipIndex = -1;
    private JsonObject? _corvetteSaveRoot;

    public InventoryGrid()
    {
        foreach (Button button in new[] { _resizeButton, _pickerButton, _exportButton, _importButton, _autoStackButton, _applyButton })
            ConfigureActionButton(button);
        _cellsPanel.PointerReleased += OnCellsPointerReleased;
        _sortMode.ItemsSource = new[]
        {
            UiStrings.Get("inventory.sort_none"),
            UiStrings.Get("inventory.sort_name"),
            UiStrings.Get("inventory.sort_category"),
        };
        _sortMode.SelectedIndex = 0;
        _sortMode.SelectionChanged += (_, _) =>
        {
            if (_sortMode.SelectedIndex > 0)
                Sort((InventorySortMode)_sortMode.SelectedIndex);
        };

        _resizeButton.Click += (_, _) =>
        {
            if (TryReadPositiveInt(_widthBox, out int width) && TryReadPositiveInt(_heightBox, out int height))
                ResizeInventory(width, height);
        };
        _pickerButton.Click += async (_, _) => await PickItemsAsync().ConfigureAwait(true);
        _applyButton.Click += (_, _) => CommitPendingEdits();
        _exportButton.Click += async (_, _) => await ExportAsync().ConfigureAwait(true);
        _importButton.Click += async (_, _) => await ImportAsync().ConfigureAwait(true);
        _autoStackButton.Click += (_, _) => AutoStackToStorageRequested?.Invoke(this, EventArgs.Empty);

        Content = BuildLayout();
        ApplyUiLocalisation();
        UpdateControlState();
    }

    public event EventHandler? DataModified;
    public event EventHandler? PinnedSlotsChanged;
    public event EventHandler? AutoStackToStorageRequested;
    public event EventHandler? AutoStackToStarshipRequested;
    public event EventHandler? AutoStackToFreighterRequested;
    public event EventHandler<AutoStackSlotRequestEventArgs>? AutoStackSelectedSlotToStorageRequested;
    public event EventHandler<AutoStackSlotRequestEventArgs>? AutoStackSelectedSlotToStarshipRequested;
    public event EventHandler<AutoStackSlotRequestEventArgs>? AutoStackSelectedSlotToFreighterRequested;
    public event EventHandler<InventoryTransferEventArgs>? ExportRequested;
    public event EventHandler<InventoryImportRequestedEventArgs>? ImportRequested;

    public Func<JsonObject, string, Task>? ExportInventoryAsync { get; set; }
    public Func<string, Task<JsonObject?>>? ImportInventoryAsync { get; set; }

    public JsonObject? LoadedInventory => _inventory;
    public IReadOnlyCollection<(int X, int Y)> PinnedSlots => _pinnedSlots.ToArray();

    public JsonObject? GetLoadedInventory() => _inventory;
    public IReadOnlyCollection<(int X, int Y)> GetPinnedSlots() => PinnedSlots;
    public void RefreshToolbarActions() => UpdateControlState();

    public void SetDatabase(GameItemDatabase? database)
    {
        _database = database;
        if (_inventory is not null)
            RebuildCells();
        UpdateControlState();
    }

    public void SetIconProvider(AvaloniaIconProvider? provider)
    {
        _iconProvider = provider;
        if (_inventory is not null)
            RebuildCells();
    }
    public void SetInventoryGroup(string group) => _inventoryGroup = string.IsNullOrWhiteSpace(group) ? "Default" : group;
    public void SetInventoryOwnerType(string ownerType) => _ownerType = ownerType ?? string.Empty;
    public void SetIsTechInventory(bool value) => _isTechInventory = value;
    public void SetIsCargoInventory(bool value) { _isCargoInventory = value; UpdateControlState(); }
    public void SetIsStorageInventory(bool value) { _isStorageInventory = value; UpdateControlState(); }
    public void SetIsChestInventory(bool value) { _isChestInventory = value; UpdateControlState(); }
    public void SetSortingEnabled(bool value) { _sortingEnabled = value; UpdateControlState(); }
    public void SetPinSlotFeatureEnabled(bool value) { _pinningEnabled = value; RefreshViews(); }
    public void SetSuperchargeDisabled(bool value) => _superchargeDisabled = value;
    public void SetSlotToggleDisabled(bool value) => _slotToggleDisabled = value;
    public void SetSuperchargeConstraints(int maxSlots, int maxRow) { _maxSuperchargedSlots = maxSlots; _maxSuperchargeRow = maxRow; }
    public void SetExportFileName(string value) => _exportFileName = string.IsNullOrWhiteSpace(value) ? "inventory.json" : value;
    public void SetExportFileFilter(string exportFilter, string importFilter, string defaultExtension) =>
        _exportExtension = string.IsNullOrWhiteSpace(defaultExtension) ? "json" : defaultExtension.TrimStart('.');
    public void SetMaxSupportedLabel(string value) { _maxSupported.Text = value; _maxSupported.IsVisible = !string.IsNullOrWhiteSpace(value); }

    public void ConfigureTransfer(
        Func<JsonObject, string, Task>? exportInventory,
        Func<string, Task<JsonObject?>>? importInventory)
    {
        ExportInventoryAsync = exportInventory;
        ImportInventoryAsync = importInventory;
        UpdateControlState();
    }

    public void SetPinnedSlots(IEnumerable<(int x, int y)> slots)
    {
        _pinnedSlots.Clear();
        foreach ((int x, int y) in slots)
            _pinnedSlots.Add((x, y));
        foreach (InventoryCellState state in _states)
            state.IsPinned = state.IsActive && _pinnedSlots.Contains((state.X, state.Y));
        RefreshViews();
    }

    public void SetCorvetteContext(JsonObject? saveRoot, int shipIndex)
    {
        _corvetteSaveRoot = saveRoot;
        _corvetteShipIndex = shipIndex;
        BuildCorvettePartList();
    }

    public void ClearCorvetteContext()
    {
        _corvetteSaveRoot = null;
        _corvetteShipIndex = -1;
        _corvetteParts.Clear();
    }

    public void LoadInventory(JsonObject? inventory)
    {
        _inventory = inventory;
        _slots = inventory?.GetArray("Slots");
        _selectedCell = null;
        _copiedCell = null;
        BuildCorvettePartList();
        RebuildCells();
        UpdateControlState();
    }

    public void SaveInventory(JsonObject? target)
    {
        CommitPendingEdits();
        if (_inventory is null || target is null || ReferenceEquals(_inventory, target))
            return;

        foreach (string key in new[] { "Slots", "ValidSlotIndices", "SpecialSlots", "Width", "Height" })
        {
            if (_inventory.Contains(key))
                target.Set(key, _inventory.Get(key) switch
                {
                    JsonObject value => value.DeepClone(),
                    JsonArray value => value.DeepClone(),
                    object value => value,
                    _ => null,
                });
        }
    }

    public bool CommitPendingEdits()
    {
        if (_selectedCell?.State is not { } state || !state.HasItem)
            return false;
        if (!TryReadInt(_amount, out int amount) || !TryReadInt(_maxAmount, out int maxAmount) ||
            !double.TryParse(_damage.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double damage))
            return false;

        string itemId = InventoryJson.EnsureCaret(_itemId.Text ?? string.Empty);
        if (itemId == "^")
            return false;

        bool changed = itemId != state.ItemId || amount != state.Amount || maxAmount != state.MaxAmount || Math.Abs(damage - state.DamageFactor) > double.Epsilon;
        if (!changed || state.Slot is null)
            return true;

        InventoryJson.SetItemId(state.Slot, itemId, state.OriginalBinaryIdBody);
        var resolved = InventoryItemResolver.Resolve(_database, itemId, _isTechInventory);
        if (resolved.Item is not null)
        {
            string inventoryType = InventoryStackDatabase.ResolveInventoryTypeForItem(resolved.Item, _isTechInventory);
            if (InventoryJson.IsTechPackHash(itemId))
                inventoryType = "Product";
            JsonObject type = state.Slot.GetObject("Type") ?? new JsonObject();
            type.Set("InventoryType", inventoryType);
            state.Slot.Set("Type", type);
        }
        RawNumberGuard.SetInt(state.Slot, "Amount", amount);
        RawNumberGuard.SetInt(state.Slot, "MaxAmount", maxAmount);
        object? oldDamage = state.Slot.Get("DamageFactor");
        if (oldDamage is not RawDouble raw || raw.Value != damage)
            state.Slot.Set("DamageFactor", damage);
        ResolveCellState(state);
        ComputeAdjacency();
        SelectCell(_cellViews[(state.X, state.Y)]);
        RaiseModified();
        return true;
    }

    public bool AddOrReplaceItem(int x, int y, string rawItemId)
    {
        if (_inventory is null || _slots is null || !_cellViews.TryGetValue((x, y), out InventorySlotCell? view))
            return false;
        InventoryCellState state = view.State;
        if (!state.IsActive)
            return false;

        string itemId = InventoryJson.EnsureCaret(rawItemId);
        var (item, _, _, _) = InventoryItemResolver.Resolve(_database, itemId, _isTechInventory);
        if (item is not null && !CanAddItem(item))
            return false;

        string inventoryType = item is null ? (_isTechInventory ? "Technology" : "Product") :
            InventoryStackDatabase.ResolveInventoryTypeForItem(item, _isTechInventory);
        if (InventoryJson.IsTechPackHash(itemId))
            inventoryType = "Product";
        if (_isTechInventory && item is not null && IsFigurine(item.Id))
        {
            itemId = InventoryJson.EnsureCaret($"T_{item.Id.TrimStart('^')}");
            inventoryType = "Technology";
        }
        if (item?.IsProcedural == true && !itemId.Contains('#'))
            itemId += $"#{ProceduralSeedHelper.Generate()}";

        int max = item is null ? 1 : InventoryStackDatabase.GetMaxAmount(item, inventoryType, _inventoryGroup);
        if (max <= 0 && inventoryType == "Technology")
            max = 100;
        int amount = inventoryType == "Technology" ? item?.BuildFullyCharged == true ? max : 0 : max;

        JsonObject slot;
        if (state.Slot is null)
        {
            slot = InventoryJson.CreateSlot(itemId, inventoryType, amount, max, 0, x, y);
            _slots.Add(slot);
            state.SlotArrayIndex = _slots.Length - 1;
            state.Slot = slot;
        }
        else
        {
            slot = state.Slot;
            InventoryJson.SetItemId(slot, itemId, state.OriginalBinaryIdBody);
            RawNumberGuard.SetInt(slot, "Amount", amount);
            RawNumberGuard.SetInt(slot, "MaxAmount", max);
            slot.Set("DamageFactor", 0.0);
            JsonObject type = slot.GetObject("Type") ?? new JsonObject();
            type.Set("InventoryType", inventoryType);
            slot.Set("Type", type);
            InventorySlotHelper.UpdateSlotIndex(slot, x, y);
        }

        ResolveCellState(state);
        ComputeAdjacency();
        SelectCell(view);
        RaiseModified();
        return true;
    }

    public bool RemoveItem(int x, int y)
    {
        if (_slots is null || !_cellViews.TryGetValue((x, y), out InventorySlotCell? cell) || cell.State.Slot is null)
            return false;
        int index = FindSlotIndex(cell.State.Slot);
        if (index >= 0)
            _slots.RemoveAt(index);
        ClearItemState(cell.State);
        ReindexSlots();
        ComputeAdjacency();
        SelectCell(cell);
        RaiseModified();
        return true;
    }

    public bool MoveOrSwap(int sourceX, int sourceY, int targetX, int targetY, bool duplicate)
    {
        if (_slots is null || !_cellViews.TryGetValue((sourceX, sourceY), out InventorySlotCell? source) ||
            !_cellViews.TryGetValue((targetX, targetY), out InventorySlotCell? target) ||
            !source.State.HasItem || !target.State.IsActive)
            return false;

        if (duplicate)
        {
            if (target.State.HasItem || source.State.Slot is null)
                return false;
            JsonObject copy = InventoryJson.DuplicateSlot(source.State.Slot, targetX, targetY);
            _slots.Add(copy);
            target.State.Slot = copy;
            target.State.SlotArrayIndex = _slots.Length - 1;
            target.State.CorvetteName = source.State.CorvetteName;
            target.State.CorvetteIcon = source.State.CorvetteIcon;
        }
        else if (target.State.HasItem && source.State.Slot is not null && target.State.Slot is not null)
        {
            InventorySlotHelper.SwapSlotIndices(source.State.Slot, sourceX, sourceY, target.State.Slot, targetX, targetY);
            (source.State.Slot, target.State.Slot) = (target.State.Slot, source.State.Slot);
            (source.State.SlotArrayIndex, target.State.SlotArrayIndex) = (target.State.SlotArrayIndex, source.State.SlotArrayIndex);
            (source.State.CorvetteName, target.State.CorvetteName) = (target.State.CorvetteName, source.State.CorvetteName);
            (source.State.CorvetteIcon, target.State.CorvetteIcon) = (target.State.CorvetteIcon, source.State.CorvetteIcon);
        }
        else if (source.State.Slot is not null)
        {
            InventorySlotHelper.UpdateSlotIndex(source.State.Slot, targetX, targetY);
            target.State.Slot = source.State.Slot;
            target.State.SlotArrayIndex = source.State.SlotArrayIndex;
            target.State.CorvetteName = source.State.CorvetteName;
            target.State.CorvetteIcon = source.State.CorvetteIcon;
            source.State.Slot = null;
            source.State.SlotArrayIndex = -1;
            source.State.CorvetteName = null;
            source.State.CorvetteIcon = null;
        }

        ResolveCellState(source.State);
        ResolveCellState(target.State);
        ComputeAdjacency();
        SelectCell(target);
        RaiseModified();
        return true;
    }

    public bool ToggleSlotActive(int x, int y)
    {
        if (_slotToggleDisabled || _inventory is null || !_cellViews.TryGetValue((x, y), out InventorySlotCell? cell))
            return false;
        JsonArray valid = GetOrCreateArray(_inventory, "ValidSlotIndices");
        int existing = FindCoordinate(valid, x, y);
        if (existing >= 0)
        {
            valid.RemoveAt(existing);
            cell.State.IsActive = false;
            _pinnedSlots.Remove((x, y));
            cell.State.IsPinned = false;
            PinnedSlotsChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            valid.Add(CreateIndex(x, y));
            cell.State.IsActive = true;
        }
        cell.Refresh();
        RaiseModified();
        return true;
    }

    public int EnableAllSlots()
    {
        if (_slotToggleDisabled || _inventory is null)
            return 0;
        JsonArray valid = GetOrCreateArray(_inventory, "ValidSlotIndices");
        var positions = ReadCoordinates(valid);
        int added = 0;
        foreach (InventoryCellState state in _states)
        {
            if (positions.Add((state.X, state.Y)))
            {
                valid.Add(CreateIndex(state.X, state.Y));
                state.IsActive = true;
                added++;
            }
        }
        if (added > 0)
        {
            RefreshViews();
            RaiseModified();
        }
        return added;
    }

    public bool ToggleSupercharge(int x, int y)
    {
        if (_superchargeDisabled || _inventory is null || !_cellViews.TryGetValue((x, y), out InventorySlotCell? cell))
            return false;
        JsonArray special = GetOrCreateArray(_inventory, "SpecialSlots");
        int existing = FindSpecialSlot(special, x, y, "TechBonus");
        if (existing >= 0)
        {
            special.RemoveAt(existing);
            cell.State.IsSupercharged = false;
        }
        else
        {
            if (_maxSuperchargeRow >= 0 && y > _maxSuperchargeRow)
                return false;
            if (_maxSuperchargedSlots >= 0 && CountSpecialSlots(special, "TechBonus") >= _maxSuperchargedSlots)
                return false;
            special.Add(CreateSpecialSlot(x, y, "TechBonus"));
            cell.State.IsSupercharged = true;
        }
        cell.Refresh();
        RaiseModified();
        return true;
    }

    public bool TogglePinned(int x, int y)
    {
        if (!_pinningEnabled || !_cellViews.TryGetValue((x, y), out InventorySlotCell? cell) || !cell.State.IsActive)
            return false;
        if (!_pinnedSlots.Add((x, y)))
            _pinnedSlots.Remove((x, y));
        cell.State.IsPinned = _pinnedSlots.Contains((x, y));
        cell.Refresh();
        PinnedSlotsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public int FillAllStacks(bool onlyChargeableTechnology = false)
    {
        int changed = 0;
        foreach (InventoryCellState state in _states)
        {
            if (!state.HasItem || state.Slot is null || state.MaxAmount <= 0 ||
                (onlyChargeableTechnology && !state.IsChargeable) || state.Amount == state.MaxAmount)
                continue;
            state.Slot.Set("Amount", state.MaxAmount);
            state.Amount = state.MaxAmount;
            changed++;
        }
        if (changed > 0)
        {
            RefreshViews();
            PopulateDetails(_selectedCell?.State);
            RaiseModified();
        }
        return changed;
    }

    public int RepairAll()
    {
        if (_slots is null || _inventory is null)
            return 0;
        int changed = 0;
        for (int i = _slots.Length - 1; i >= 0; i--)
        {
            JsonObject slot = _slots.GetObject(i);
            string itemId = InventoryJson.ExtractItemId(slot, out _);
            if (InventorySlotHelper.IsDamageSlotItem(itemId))
            {
                _slots.RemoveAt(i);
                changed++;
                continue;
            }
            double damage = TryGetDouble(slot, "DamageFactor");
            bool installed = slot.Get("FullyInstalled") as bool? ?? true;
            if (damage != 0 || !installed)
            {
                slot.Set("DamageFactor", 0.0);
                slot.Set("FullyInstalled", true);
                changed++;
            }
        }
        JsonArray? special = _inventory.GetArray("SpecialSlots");
        if (special is not null)
        {
            for (int i = special.Length - 1; i >= 0; i--)
            {
                if (GetSpecialType(special.GetObject(i)) == "BlockedByBrokenTech")
                {
                    special.RemoveAt(i);
                    changed++;
                }
            }
        }
        if (changed > 0)
        {
            RebuildCells();
            RaiseModified();
        }
        return changed;
    }

    public void ResizeInventory(int width, int height)
    {
        if (_inventory is null)
            return;
        int maxWidth = _isChestInventory ? 10 : 20;
        int maxHeight = _isChestInventory ? 12 : 20;
        width = Math.Clamp(width, 1, maxWidth);
        height = Math.Clamp(height, 1, maxHeight);

        JsonArray valid = GetOrCreateArray(_inventory, "ValidSlotIndices");
        for (int i = valid.Length - 1; i >= 0; i--)
        {
            JsonObject index = valid.GetObject(i);
            if (TryGetInt(index, "X") >= width || TryGetInt(index, "Y") >= height)
                valid.RemoveAt(i);
        }
        HashSet<(int X, int Y)> positions = ReadCoordinates(valid);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            if (positions.Add((x, y)))
                valid.Add(CreateIndex(x, y));
        }

        // Keep out-of-bounds slot/special-slot records, matching the current editor.
        // Re-expanding the grid therefore restores items that were temporarily hidden.
        _inventory.Set("Width", width);
        _inventory.Set("Height", height);
        RebuildCells();
        PinnedSlotsChanged?.Invoke(this, EventArgs.Empty);
        RaiseModified();
    }

    public void Sort(InventorySortMode mode)
    {
        if (!_sortingEnabled || mode == InventorySortMode.None || _inventory is null || _slots is null)
            return;
        var entries = new List<(JsonObject Slot, string Name, string Category, string Id, int X, int Y)>();
        for (int i = 0; i < _slots.Length; i++)
        {
            JsonObject slot = _slots.GetObject(i);
            string id = InventoryJson.ExtractItemId(slot, out _);
            if (id is "" or "^" or "^YOURSLOTITEM")
                continue;
            JsonObject? index = slot.GetObject("Index");
            var resolved = InventoryItemResolver.Resolve(_database, id, _isTechInventory);
            entries.Add((slot, resolved.Name, resolved.Item?.Category ?? string.Empty, id,
                index is null ? 0 : TryGetInt(index, "X"), index is null ? 0 : TryGetInt(index, "Y")));
        }
        if (entries.Count < 2)
            return;
        List<(int X, int Y)> targets = ReadCoordinates(_inventory.GetArray("ValidSlotIndices"))
            .OrderBy(position => position.Y).ThenBy(position => position.X).ToList();
        if (targets.Count == 0)
        {
            for (int y = 0; y < _gridHeight; y++)
            for (int x = 0; x < _gridWidth; x++)
                targets.Add((x, y));
        }
        IOrderedEnumerable<(JsonObject Slot, string Name, string Category, string Id, int X, int Y)> sorted = mode == InventorySortMode.Name
            ? entries.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(entry => entry.Category, StringComparer.CurrentCultureIgnoreCase)
            : entries.OrderBy(entry => entry.Category, StringComparer.CurrentCultureIgnoreCase).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase);
        int target = 0;
        foreach (var entry in sorted.ThenBy(entry => entry.Y).ThenBy(entry => entry.X))
        {
            if (target >= targets.Count)
                break;
            InventorySlotHelper.UpdateSlotIndex(entry.Slot, targets[target].X, targets[target].Y);
            target++;
        }
        RebuildCells();
        RaiseModified();
    }

    public bool ImportInventory(JsonObject imported)
    {
        if (_inventory is null)
            return false;
        JsonObject? source = InventoryImportHelper.FindInventoryObject(imported);
        if (source?.GetArray("Slots") is not { } slots)
            return false;
        _inventory.Set("Slots", slots);
        foreach (string key in new[] { "ValidSlotIndices", "SpecialSlots", "Width", "Height" })
        {
            if (source.Contains(key))
                _inventory.Set(key, source.Get(key));
        }
        _slots = slots;
        RebuildCells();
        RaiseModified();
        return true;
    }

    public void ApplyUiLocalisation()
    {
        _resizeButton.Content = $"↔  {UiStrings.Get("inventory.resize")}";
        _pickerButton.Content = $"＋  {UiStrings.Get("inventory.ctx_add_item")}";
        _exportButton.Content = $"⇧  {UiStrings.Get("common.export")}";
        _importButton.Content = $"⇩  {UiStrings.Get("common.import")}";
        _autoStackButton.Content = $"⇅  {UiStrings.Get("inventory.auto_stack")}";
        _applyButton.Content = UiStrings.Get("common.apply");
        _sortMode.ItemsSource = new[]
        {
            UiStrings.Get("inventory.sort_none"),
            UiStrings.Get("inventory.sort_name"),
            UiStrings.Get("inventory.sort_category"),
        };
        RefreshViews();
    }

    private Control BuildLayout()
    {
        var resize = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = "W", VerticalAlignment = VerticalAlignment.Center }, _widthBox,
                new TextBlock { Text = "H", VerticalAlignment = VerticalAlignment.Center }, _heightBox,
                _resizeButton, _maxSupported,
            },
        };
        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            ItemSpacing = 6,
            LineSpacing = 6,
            Children = { _pickerButton, _sortMode, _autoStackButton, _importButton, _exportButton },
        };
        var toolbar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 12,
            Margin = new Thickness(0, 0, 0, 8),
            Children = { resize, actions },
        };
        Grid.SetColumn(actions, 1);

        var scroller = new ScrollViewer
        {
            Content = _cellsPanel,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var detailFields = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 8,
            RowSpacing = 6,
        };
        AddLabeledField(detailFields, "ID", _itemId, 0);
        AddLabeledField(detailFields, UiStrings.Get("inventory.amount"), _amount, 1);
        AddLabeledField(detailFields, UiStrings.Get("inventory.max"), _maxAmount, 2);
        AddLabeledField(detailFields, UiStrings.Get("inventory.label_damage"), _damage, 3);
        detailFields.Children.Add(_applyButton);
        Grid.SetColumn(_applyButton, 1);
        Grid.SetRow(_applyButton, 4);

        var detailHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 8,
            Children =
            {
                _detailIcon,
                new StackPanel { Spacing = 3, Children = { _selectionTitle, _selectionPosition, _detailMetadata } },
            },
        };
        Grid.SetColumn(detailHeader.Children[1], 1);
        var detail = new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Width = 280,
            MinWidth = 260,
            MaxWidth = 300,
            Child = new StackPanel { Spacing = 10, Children = { detailHeader, detailFields } },
        };
        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10,
            Children = { scroller, detail },
        };
        Grid.SetColumn(detail, 1);
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children = { toolbar, body },
        };
        Grid.SetRow(body, 1);
        return root;
    }

    private static void AddLabeledField(Grid grid, string label, Control field, int row)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(text);
        Grid.SetRow(text, row);
        grid.Children.Add(field);
        Grid.SetColumn(field, 1);
        Grid.SetRow(field, row);
    }

    private void RebuildCells()
    {
        _cellsPanel.Children.Clear();
        _cellViews.Clear();
        _states.Clear();
        _selectedCell = null;
        ClearDetails();
        if (_inventory is null)
            return;

        _slots = _inventory.GetArray("Slots") ?? new JsonArray();
        if (_inventory.GetArray("Slots") is null)
            _inventory.Set("Slots", _slots);
        (_gridWidth, _gridHeight) = ResolveDimensions(_inventory, _slots);
        _widthBox.Text = _gridWidth.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _heightBox.Text = _gridHeight.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _cellsPanel.Width = _gridWidth * CellPitchX;
        _cellsPanel.MaxWidth = _gridWidth * CellPitchX;

        HashSet<(int X, int Y)> active = ReadCoordinates(_inventory.GetArray("ValidSlotIndices"));
        bool allActive = active.Count == 0;
        var slotMap = new Dictionary<(int X, int Y), (int Index, JsonObject Slot)>();
        for (int i = 0; i < _slots.Length; i++)
        {
            JsonObject slot = _slots.GetObject(i);
            JsonObject? index = slot.GetObject("Index");
            int x = index is null ? i % _gridWidth : TryGetInt(index, "X");
            int y = index is null ? i / _gridWidth : TryGetInt(index, "Y");
            slotMap[(x, y)] = (i, slot);
        }

        for (int y = 0; y < _gridHeight; y++)
        for (int x = 0; x < _gridWidth; x++)
        {
            var state = new InventoryCellState(x, y)
            {
                IsActive = allActive || active.Contains((x, y)),
                IsSupercharged = IsSpecialSlot(x, y, "TechBonus"),
                IsPinned = _pinningEnabled && _pinnedSlots.Contains((x, y)),
            };
            if (slotMap.TryGetValue((x, y), out var slot))
            {
                state.SlotArrayIndex = slot.Index;
                state.Slot = slot.Slot;
            }
            ResolveCellState(state);
            _states.Add(state);
        }
        ComputeAdjacency();
        foreach (InventoryCellState state in _states)
        {
            var cell = new InventorySlotCell(state) { Margin = new Thickness(3) };
            AttachCellHandlers(cell);
            cell.ContextMenu = CreateContextMenu(cell);
            _cellViews[(state.X, state.Y)] = cell;
            _cellsPanel.Children.Add(cell);
        }
    }

    private static void ConfigureActionButton(Button button)
    {
        button.MinHeight = 34;
        button.Padding = new Thickness(10, 5);
        button.FontWeight = FontWeight.SemiBold;
        button.Foreground = Brushes.White;
        button.Background = new SolidColorBrush(Color.Parse("#344454"));
        button.BorderBrush = new SolidColorBrush(Color.Parse("#8FA3B8"));
        button.BorderThickness = new Thickness(1);
    }

    private void ResolveCellState(InventoryCellState state)
    {
        state.ItemId = string.Empty;
        state.Name = string.Empty;
        state.Category = string.Empty;
        state.ItemType = string.Empty;
        state.ItemClass = string.Empty;
        state.ElementSymbol = string.Empty;
        state.Description = string.Empty;
        state.Amount = 0;
        state.MaxAmount = 0;
        state.DamageFactor = 0;
        state.IsChargeable = false;
        state.Icon = null;
        state.ClassIcon = null;
        state.OriginalBinaryIdBody = null;
        if (state.Slot is null)
            return;

        state.ItemId = InventoryJson.ExtractItemId(state.Slot, out byte[]? binaryBody);
        state.OriginalBinaryIdBody = binaryBody;
        state.Amount = TryGetInt(state.Slot, "Amount");
        state.MaxAmount = TryGetInt(state.Slot, "MaxAmount");
        state.DamageFactor = TryGetDouble(state.Slot, "DamageFactor");
        if (!state.HasItem)
            return;

        var resolved = InventoryItemResolver.Resolve(_database, state.ItemId, _isTechInventory);
        GameItem? item = resolved.Item;
        state.Name = resolved.Name;
        state.Category = item?.Category ?? string.Empty;
        state.ItemType = item?.ItemType ?? state.Slot.GetObject("Type")?.GetString("InventoryType") ?? string.Empty;
        state.ItemClass = InventoryItemResolver.ResolveClass(item, resolved.Class);
        state.ElementSymbol = item?.Symbol ?? string.Empty;
        state.Description = item?.Description ?? string.Empty;
        state.IsChargeable = item?.IsChargeable ?? false;
        string? iconName = resolved.Icon ?? item?.Icon;
        ResolveCorvetteOverride(state, ref iconName);
        state.Icon = _iconProvider?.GetIcon(iconName);
        if (ShouldShowClassIcon(state.ItemType, state.ItemId, state.ItemClass))
            state.ClassIcon = _iconProvider?.GetIcon($"CLASSMINI.{(state.ItemClass == "?" ? "Sentinel" : state.ItemClass)}.png");
    }

    private void ComputeAdjacency()
    {
        Dictionary<(int X, int Y), InventoryCellState> items = _states.Where(state => state.HasItem).ToDictionary(state => (state.X, state.Y));
        foreach (InventoryCellState state in _states)
        {
            state.HasAdjacency = false;
            if (!state.HasItem)
                continue;
            TechAdjacencyDatabase.TechAdjacencyInfo? info = TechAdjacencyDatabase.GetAdjacencyInfo(state.ItemId);
            if (info is null || info.BaseStatType == 0)
                continue;
            foreach ((int x, int y) in new[] { (state.X - 1, state.Y), (state.X + 1, state.Y), (state.X, state.Y - 1), (state.X, state.Y + 1) })
            {
                if (!items.TryGetValue((x, y), out InventoryCellState? neighbor))
                    continue;
                TechAdjacencyDatabase.TechAdjacencyInfo? neighborInfo = TechAdjacencyDatabase.GetAdjacencyInfo(neighbor.ItemId);
                if (neighborInfo?.BaseStatType != info.BaseStatType)
                    continue;
                state.HasAdjacency = true;
                if (InventoryJson.TryParseHtmlColor(TechAdjacencyDatabase.GetBaseStatTypeColour(info.BaseStatType), out Color color))
                    state.AdjacencyColor = color;
                break;
            }
        }
        RefreshViews();
    }

    private void AttachCellHandlers(InventorySlotCell cell)
    {
        cell.PointerPressed += (_, args) =>
        {
            SelectCell(cell);
            PointerPoint point = args.GetCurrentPoint(cell);
            if (!point.Properties.IsLeftButtonPressed)
                return;
            _dragSource = cell;
            _dragStart = args.GetPosition(_cellsPanel);
            _dragModifierDuplicate = (args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
            args.Pointer.Capture(_cellsPanel);
        };
        cell.KeyDown += (_, args) =>
        {
            bool command = (args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
            if (command && args.Key == Key.C && cell.State.HasItem)
            {
                _copiedCell = cell;
                args.Handled = true;
                return;
            }
            if (command && args.Key == Key.V && !cell.State.HasItem && cell.State.IsActive && _copiedCell?.State.HasItem == true)
            {
                MoveOrSwap(_copiedCell.State.X, _copiedCell.State.Y, cell.State.X, cell.State.Y, true);
                args.Handled = true;
                return;
            }
            if (args.Key == Key.Delete && cell.State.HasItem)
            {
                RemoveItem(cell.State.X, cell.State.Y);
                args.Handled = true;
            }
        };
    }

    private void OnCellsPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (_dragSource is null)
            return;
        Point end = args.GetPosition(_cellsPanel);
        InventorySlotCell? target = _cellViews.Values.FirstOrDefault(cell => cell.Bounds.Contains(end));
        double distance = Math.Sqrt(Math.Pow(end.X - _dragStart.X, 2) + Math.Pow(end.Y - _dragStart.Y, 2));
        bool duplicate = _dragModifierDuplicate || (args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        if (target is not null && !ReferenceEquals(_dragSource, target) && distance >= DragThreshold)
            MoveOrSwap(_dragSource.State.X, _dragSource.State.Y, target.State.X, target.State.Y, duplicate);
        _dragSource = null;
        args.Pointer.Capture(null);
    }

    private ContextMenu CreateContextMenu(InventorySlotCell cell)
    {
        var add = CreateMenuItem("inventory.ctx_add_item", async () =>
        {
            SelectCell(cell);
            await PickItemsAsync(false).ConfigureAwait(true);
        });
        var remove = CreateMenuItem("inventory.ctx_remove_item", () => RemoveItem(cell.State.X, cell.State.Y));
        var copy = CreateMenuItem("common.copy", () => _copiedCell = cell.State.HasItem ? cell : _copiedCell);
        var paste = CreateMenuItem("common.paste", () =>
        {
            if (_copiedCell?.State.Slot is null || cell.State.HasItem)
                return;
            MoveOrSwap(_copiedCell.State.X, _copiedCell.State.Y, cell.State.X, cell.State.Y, true);
        });
        var active = CreateMenuItem("inventory.ctx_enable_slot", () => ToggleSlotActive(cell.State.X, cell.State.Y));
        var enableAll = CreateMenuItem("inventory.ctx_enable_all_slots", () => { EnableAllSlots(); });
        var pin = CreateMenuItem("inventory.ctx_pin_slot", () => TogglePinned(cell.State.X, cell.State.Y));
        var supercharge = CreateMenuItem("inventory.ctx_supercharge", () => ToggleSupercharge(cell.State.X, cell.State.Y));
        var fill = CreateMenuItem("inventory.ctx_fill_stack", () => FillStack(cell.State));
        var repair = CreateMenuItem("inventory.ctx_repair_slot", () => RepairSlot(cell.State));
        var repairAll = CreateMenuItem("inventory.ctx_repair_all_slots", () => { RepairAll(); });
        var storage = CreateMenuItem("inventory.auto_stack_to_storage", () => RaiseSelectedAutoStack(cell.State, AutoStackSelectedSlotToStorageRequested, AutoStackToStorageRequested));
        var starship = CreateMenuItem("inventory.auto_stack_to_starship", () => RaiseSelectedAutoStack(cell.State, AutoStackSelectedSlotToStarshipRequested, AutoStackToStarshipRequested));
        var freighter = CreateMenuItem("inventory.auto_stack_to_freighter", () => RaiseSelectedAutoStack(cell.State, AutoStackSelectedSlotToFreighterRequested, AutoStackToFreighterRequested));

        var menu = new ContextMenu
        {
            ItemsSource = new object[] { add, remove, new Separator(), copy, paste, new Separator(), active, enableAll, pin, supercharge,
                new Separator(), fill, repair, repairAll, new Separator(), storage, starship, freighter },
        };
        menu.Opening += (_, _) =>
        {
            bool hasItem = cell.State.HasItem;
            add.Header = UiStrings.Get(hasItem ? "inventory.ctx_replace_item" : "inventory.ctx_add_item");
            add.IsEnabled = cell.State.IsActive || hasItem;
            remove.IsVisible = hasItem;
            copy.IsVisible = hasItem;
            paste.IsVisible = _copiedCell?.State.HasItem == true && !hasItem && cell.State.IsActive;
            active.IsVisible = !_slotToggleDisabled;
            active.Header = UiStrings.Get(cell.State.IsActive ? "inventory.ctx_disable_slot" : "inventory.ctx_enable_slot");
            enableAll.IsVisible = !_slotToggleDisabled;
            pin.IsVisible = _pinningEnabled && cell.State.IsActive;
            pin.Header = UiStrings.Get(cell.State.IsPinned ? "inventory.ctx_unpin_slot" : "inventory.ctx_pin_slot");
            supercharge.IsVisible = !_superchargeDisabled;
            supercharge.Header = UiStrings.Get(cell.State.IsSupercharged ? "inventory.ctx_remove_supercharge" : "inventory.ctx_supercharge");
            fill.IsVisible = hasItem && cell.State.MaxAmount > 0;
            repair.IsVisible = hasItem;
            storage.IsVisible = _isCargoInventory && !_isStorageInventory && (AutoStackSelectedSlotToStorageRequested is not null || AutoStackToStorageRequested is not null);
            starship.IsVisible = _isCargoInventory && !_isStorageInventory && (AutoStackSelectedSlotToStarshipRequested is not null || AutoStackToStarshipRequested is not null);
            freighter.IsVisible = _isCargoInventory && !_isStorageInventory && (AutoStackSelectedSlotToFreighterRequested is not null || AutoStackToFreighterRequested is not null);
        };
        return menu;
    }

    private static MenuItem CreateMenuItem(string key, Action action)
    {
        var item = new MenuItem { Header = UiStrings.Get(key) };
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem CreateMenuItem(string key, Func<Task> action)
    {
        var item = new MenuItem { Header = UiStrings.Get(key) };
        item.Click += async (_, _) => await action().ConfigureAwait(true);
        return item;
    }

    private async Task PickItemsAsync(bool multiSelect = true)
    {
        if (_database is null || TopLevel.GetTopLevel(this) is not Window owner)
            return;
        InventorySlotCell? start = _selectedCell ?? _cellViews.Values.FirstOrDefault(cell => cell.State.IsActive && !cell.State.HasItem);
        if (start is null)
            return;
        var dialog = new ItemPickerDialog();
        dialog.Initialize(_database, _iconProvider, multiSelect: multiSelect);
        string? result = await dialog.ShowDialog<string?>(owner);
        IReadOnlyList<string> selected = dialog.SelectedItemIds.Count > 0 ? dialog.SelectedItemIds : result is null ? [] : [result];
        if (selected.Count == 0)
            return;

        List<InventorySlotCell> targets = _cellViews.Values
            .Where(cell => ReferenceEquals(cell, start) || cell.State.IsActive && !cell.State.HasItem)
            .OrderBy(cell => cell.State.Y).ThenBy(cell => cell.State.X).ToList();
        int startIndex = targets.IndexOf(start);
        if (startIndex > 0)
            targets = targets.Skip(startIndex).Concat(targets.Take(startIndex)).ToList();
        int target = 0;
        foreach (string itemId in selected)
        {
            if (target >= targets.Count)
                break;
            GameItem? item = _database.GetItem(itemId) ?? _database.GetItem(itemId.TrimStart('^'));
            if (item is not null && !CanAddItem(item))
                continue;
            AddOrReplaceItem(targets[target].State.X, targets[target].State.Y, itemId);
            target++;
        }
    }

    private async Task ExportAsync()
    {
        if (_inventory is null)
            return;
        CommitPendingEdits();
        if (ExportInventoryAsync is not null)
            await ExportInventoryAsync(_inventory, _exportFileName).ConfigureAwait(true);
        else
            ExportRequested?.Invoke(this, new InventoryTransferEventArgs(_inventory, _exportFileName));
    }

    private async Task ImportAsync()
    {
        if (_inventory is null)
            return;
        JsonObject? imported;
        if (ImportInventoryAsync is not null)
            imported = await ImportInventoryAsync(_exportExtension).ConfigureAwait(true);
        else
        {
            var args = new InventoryImportRequestedEventArgs(_exportExtension);
            ImportRequested?.Invoke(this, args);
            imported = args.ImportedInventory;
        }
        if (imported is not null)
            ImportInventory(imported);
    }

    private void SelectCell(InventorySlotCell cell)
    {
        if (_selectedCell is not null)
            _selectedCell.IsSelected = false;
        _selectedCell = cell;
        cell.IsSelected = true;
        cell.Focus();
        PopulateDetails(cell.State);
    }

    private void PopulateDetails(InventoryCellState? state)
    {
        if (state is null)
        {
            ClearDetails();
            return;
        }
        _selectionTitle.Text = state.HasItem ? state.Name : state.IsActive ? UiStrings.Get("inventory.empty_slot") : UiStrings.Get("inventory.disabled_slot");
        _selectionPosition.Text = $"({state.X}, {state.Y})";
        _itemId.Text = state.ItemId;
        _amount.Text = state.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _maxAmount.Text = state.MaxAmount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _damage.Text = state.DamageFactor.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
        _detailIcon.Source = state.Icon;
        _detailMetadata.Text = string.Join(" · ", new[] { state.ItemType, state.Category, state.ItemClass }.Where(value => !string.IsNullOrWhiteSpace(value)));
        bool editable = state.HasItem;
        _itemId.IsEnabled = editable;
        _amount.IsEnabled = editable;
        _maxAmount.IsEnabled = editable;
        _damage.IsEnabled = editable;
        _applyButton.IsEnabled = editable;
    }

    private void ClearDetails()
    {
        _selectionTitle.Text = UiStrings.Get("inventory.no_slot_selected");
        _selectionPosition.Text = string.Empty;
        _itemId.Text = string.Empty;
        _amount.Text = "0";
        _maxAmount.Text = "0";
        _damage.Text = "0";
        _detailIcon.Source = null;
        _detailMetadata.Text = string.Empty;
        _itemId.IsEnabled = false;
        _amount.IsEnabled = false;
        _maxAmount.IsEnabled = false;
        _damage.IsEnabled = false;
        _applyButton.IsEnabled = false;
    }

    private void UpdateControlState()
    {
        bool loaded = _inventory is not null;
        bool canResize = loaded && (!_isStorageInventory || _isChestInventory);
        _widthBox.IsEnabled = canResize;
        _heightBox.IsEnabled = canResize;
        _resizeButton.IsEnabled = canResize;
        _pickerButton.IsEnabled = loaded && _database is not null;
        _sortMode.IsVisible = _sortingEnabled;
        _sortMode.IsEnabled = loaded && _sortingEnabled;
        _autoStackButton.IsVisible = _isCargoInventory && !_isStorageInventory;
        _autoStackButton.IsEnabled = loaded && AutoStackToStorageRequested is not null;
        _exportButton.IsEnabled = loaded && (ExportInventoryAsync is not null || ExportRequested is not null);
        _importButton.IsEnabled = loaded && (ImportInventoryAsync is not null || ImportRequested is not null);
    }

    private bool CanAddItem(GameItem item)
    {
        if (!InventoryStackDatabase.CanAddItemToInventory(item, _isTechInventory, _isCargoInventory))
            return false;
        return _isCargoInventory || string.IsNullOrWhiteSpace(_ownerType) || string.IsNullOrWhiteSpace(item.TechnologyCategory) ||
               item.TechnologyCategory == "None" || !GameItemDatabase.IsTechnologyRelatedType(item.ItemType) || item.IsValidForOwner(_ownerType);
    }

    private void FillStack(InventoryCellState state)
    {
        if (state.Slot is null || state.MaxAmount <= 0 || state.Amount == state.MaxAmount)
            return;
        state.Slot.Set("Amount", state.MaxAmount);
        state.Amount = state.MaxAmount;
        _cellViews[(state.X, state.Y)].Refresh();
        PopulateDetails(state);
        RaiseModified();
    }

    private void RepairSlot(InventoryCellState state)
    {
        if (state.Slot is null || _inventory is null)
            return;
        state.Slot.Set("DamageFactor", 0.0);
        state.Slot.Set("FullyInstalled", true);
        JsonArray? special = _inventory.GetArray("SpecialSlots");
        if (special is not null)
        {
            int blocked = FindSpecialSlot(special, state.X, state.Y, "BlockedByBrokenTech");
            if (blocked >= 0)
                special.RemoveAt(blocked);
        }
        state.DamageFactor = 0;
        _cellViews[(state.X, state.Y)].Refresh();
        PopulateDetails(state);
        RaiseModified();
    }

    private void RaiseSelectedAutoStack(
        InventoryCellState state,
        EventHandler<AutoStackSlotRequestEventArgs>? selectedHandler,
        EventHandler? allHandler)
    {
        if (state.HasItem && selectedHandler is not null)
            selectedHandler.Invoke(this, new AutoStackSlotRequestEventArgs(state.X, state.Y, state.ItemId));
        else
            allHandler?.Invoke(this, EventArgs.Empty);
    }

    private void BuildCorvettePartList()
    {
        _corvetteParts.Clear();
        if (_corvetteSaveRoot is null || _corvetteShipIndex < 0)
            return;
        JsonObject? player = _corvetteSaveRoot.GetObject("PlayerStateData") ?? _corvetteSaveRoot;
        JsonArray? bases = player.GetArray("PersistentPlayerBases");
        if (bases is null)
            return;
        for (int i = 0; i < bases.Length; i++)
        {
            JsonObject entry = bases.GetObject(i);
            if (entry.GetObject("BaseType")?.GetString("PersistentBaseTypes") != "PlayerShipBase" || TryGetInt(entry, "UserData") != _corvetteShipIndex)
                continue;
            JsonArray? objects = entry.GetArray("Objects");
            if (objects is null)
                return;
            for (int j = 0; j < objects.Length; j++)
            {
                string id = objects.GetObject(j).GetString("ObjectID")?.TrimStart('^') ?? string.Empty;
                if (id.Length > 0)
                    _corvetteParts.Add(new CorvettePartState(id));
            }
            return;
        }
    }

    private void ResolveCorvetteOverride(InventoryCellState state, ref string? iconName)
    {
        (string baseId, string variant) = InventoryJson.SplitVariant(state.ItemId);
        baseId = baseId.TrimStart('^');
        if (_database is null || !baseId.StartsWith("CV_", StringComparison.OrdinalIgnoreCase) || _corvetteParts.Count == 0)
            return;
        if (state.CorvetteName is not null)
        {
            state.Name = state.CorvetteName;
            iconName = state.CorvetteIcon;
            return;
        }
        if (!_database.CorvetteBasePartTechMap.TryGetValue(baseId, out List<string>? candidates))
            return;
        CorvettePartState? matched = _corvetteParts.FirstOrDefault(part => !part.Claimed && candidates.Contains(part.Id, StringComparer.OrdinalIgnoreCase));
        GameItem? partItem = matched is null ? null : _database.GetItem(matched.Id);
        if (matched is null || partItem is null)
            return;
        matched.Claimed = true;
        state.CorvetteName = variant.Length == 0 ? partItem.Name : $"{partItem.Name} [{variant}]";
        state.CorvetteIcon = partItem.Icon;
        state.Name = state.CorvetteName;
        iconName = state.CorvetteIcon;
    }

    private bool IsSpecialSlot(int x, int y, string type) =>
        _inventory?.GetArray("SpecialSlots") is { } special && FindSpecialSlot(special, x, y, type) >= 0;

    private static int FindSpecialSlot(JsonArray special, int x, int y, string type)
    {
        for (int i = 0; i < special.Length; i++)
        {
            JsonObject entry = special.GetObject(i);
            JsonObject? index = entry.GetObject("Index");
            if (GetSpecialType(entry) == type && index is not null && TryGetInt(index, "X") == x && TryGetInt(index, "Y") == y)
                return i;
        }
        return -1;
    }

    private static int CountSpecialSlots(JsonArray special, string type)
    {
        int count = 0;
        for (int i = 0; i < special.Length; i++)
            if (GetSpecialType(special.GetObject(i)) == type)
                count++;
        return count;
    }

    private static string GetSpecialType(JsonObject entry) => entry.GetObject("Type")?.GetString("InventorySpecialSlotType") ?? string.Empty;

    private static JsonObject CreateSpecialSlot(int x, int y, string type)
    {
        var typeObject = new JsonObject();
        typeObject.Add("InventorySpecialSlotType", type);
        var result = new JsonObject();
        result.Add("Type", typeObject);
        result.Add("Index", CreateIndex(x, y));
        return result;
    }

    private static JsonObject CreateIndex(int x, int y)
    {
        var index = new JsonObject();
        index.Add("X", x);
        index.Add("Y", y);
        return index;
    }

    private static JsonArray GetOrCreateArray(JsonObject owner, string key)
    {
        JsonArray? array = owner.GetArray(key);
        if (array is not null)
            return array;
        array = new JsonArray();
        owner.Set(key, array);
        return array;
    }

    private static HashSet<(int X, int Y)> ReadCoordinates(JsonArray? array)
    {
        var result = new HashSet<(int X, int Y)>();
        if (array is null)
            return result;
        for (int i = 0; i < array.Length; i++)
        {
            JsonObject value = array.GetObject(i);
            result.Add((TryGetInt(value, "X"), TryGetInt(value, "Y")));
        }
        return result;
    }

    private static int FindCoordinate(JsonArray array, int x, int y)
    {
        for (int i = 0; i < array.Length; i++)
        {
            JsonObject value = array.GetObject(i);
            if (TryGetInt(value, "X") == x && TryGetInt(value, "Y") == y)
                return i;
        }
        return -1;
    }

    private int FindSlotIndex(JsonObject slot)
    {
        if (_slots is null)
            return -1;
        for (int i = 0; i < _slots.Length; i++)
            if (ReferenceEquals(_slots.GetObject(i), slot))
                return i;
        return -1;
    }

    private void ReindexSlots()
    {
        if (_slots is null)
            return;
        foreach (InventoryCellState state in _states)
            state.SlotArrayIndex = state.Slot is null ? -1 : FindSlotIndex(state.Slot);
    }

    private static (int Width, int Height) ResolveDimensions(JsonObject inventory, JsonArray slots)
    {
        int width = TryGetInt(inventory, "Width");
        int height = TryGetInt(inventory, "Height");
        int maxX = -1;
        int maxY = -1;
        foreach ((int x, int y) in ReadCoordinates(inventory.GetArray("ValidSlotIndices")))
        {
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }
        for (int i = 0; i < slots.Length; i++)
        {
            JsonObject? index = slots.GetObject(i).GetObject("Index");
            if (index is null)
                continue;
            maxX = Math.Max(maxX, TryGetInt(index, "X"));
            maxY = Math.Max(maxY, TryGetInt(index, "Y"));
        }
        width = width > 0 ? width : maxX >= 0 ? maxX + 1 : DefaultWidth;
        height = height > 0 ? height : maxY >= 0 ? maxY + 1 : Math.Max(1, (slots.Length + width - 1) / width);
        return (Math.Clamp(width, 1, 20), Math.Clamp(height, 1, 20));
    }

    private static int TryGetInt(JsonObject value, string key)
    {
        try { return value.GetInt(key); }
        catch { return 0; }
    }

    private static double TryGetDouble(JsonObject value, string key)
    {
        try { return value.GetDouble(key); }
        catch { return 0; }
    }

    private static bool TryReadPositiveInt(TextBox box, out int value) =>
        int.TryParse(box.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value) && value > 0;

    private static bool TryReadInt(TextBox box, out int value) =>
        int.TryParse(box.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value);

    private static bool IsFigurine(string itemId) =>
        itemId.TrimStart('^').StartsWith("BOBBLE_", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldShowClassIcon(string itemType, string itemId, string itemClass) =>
        itemClass is not ("" or "NONE") &&
        (itemType.Equals("Technology", StringComparison.OrdinalIgnoreCase) ||
         itemType.Equals("Technology Module", StringComparison.OrdinalIgnoreCase) ||
         itemType.Equals("Upgrades", StringComparison.OrdinalIgnoreCase) && !itemId.EndsWith("INV_TOKEN", StringComparison.OrdinalIgnoreCase));

    private static void ClearItemState(InventoryCellState state)
    {
        state.Slot = null;
        state.SlotArrayIndex = -1;
        state.ItemId = string.Empty;
        state.Name = string.Empty;
        state.Category = string.Empty;
        state.ItemType = string.Empty;
        state.ItemClass = string.Empty;
        state.ElementSymbol = string.Empty;
        state.Description = string.Empty;
        state.Amount = 0;
        state.MaxAmount = 0;
        state.DamageFactor = 0;
        state.Icon = null;
        state.ClassIcon = null;
        state.OriginalBinaryIdBody = null;
    }

    private void RefreshViews()
    {
        foreach (InventorySlotCell cell in _cellViews.Values)
            cell.Refresh();
    }

    private void RaiseModified() => DataModified?.Invoke(this, EventArgs.Empty);
}
