using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Controls;

namespace NMSE.UI.Avalonia.Panels.BasesStorage;

public sealed partial class BasesEditorPanel
{
    private readonly TabItem _infoTab = new();
    private readonly TabItem _objectsTab = new();
    private readonly List<BaseObjectItem> _allObjects = [];
    private readonly ObservableCollection<string> _freighterRooms = [];
    private readonly Dictionary<string, TextBlock> _advancedLabels = [];
    private readonly Dictionary<string, TextBlock> _objectLabels = [];

    private readonly InvariantNumericTextBox _originalBaseVersion = new() { Minimum = 0 };
    private readonly InvariantNumericTextBox _baseUserData = new();
    private readonly InvariantNumericTextBox _lastUpdateTimestamp = new();
    private readonly TextBox _rid = new();
    private readonly TextBox _autoPower = new();
    private readonly InvariantNumericTextBox[] _screenshotAt = NewVector();
    private readonly InvariantNumericTextBox[] _screenshotPos = NewVector();
    private readonly CheckBox _isReported = new();
    private readonly CheckBox _isFeatured = new();

    private readonly TextBox _baseType = ReadOnlyTextBox();
    private readonly TextBox _gameMode = ReadOnlyTextBox();
    private readonly TextBox _difficulty = ReadOnlyTextBox();
    private readonly TextBox _platformToken = ReadOnlyTextBox();
    private readonly TextBox _ownerLid = ReadOnlyTextBox();
    private readonly TextBox _ownerUid = ReadOnlyTextBox();
    private readonly TextBox _ownerUsn = ReadOnlyTextBox();
    private readonly TextBox _ownerPtk = ReadOnlyTextBox();
    private readonly TextBox _ownerTs = ReadOnlyTextBox();
    private readonly TextBox _lastEditedById = ReadOnlyTextBox();
    private readonly TextBox _lastEditedByUsername = ReadOnlyTextBox();

    private readonly TextBox _objectSearchBox = new();
    private readonly Button _objectSearchClear = new() { Content = "✕" };
    private readonly StackPanel _freighterRoomsPanel = new() { IsVisible = false, Spacing = 4 };
    private readonly TextBlock _freighterRoomsHeader = new() { FontSize = 16, FontWeight = FontWeight.SemiBold };
    private readonly ListBox _freighterRoomList = new() { MinHeight = 110, MaxHeight = 180 };
    private readonly TextBlock _objectsHeader = new() { FontSize = 16, FontWeight = FontWeight.SemiBold };

    private readonly InvariantNumericTextBox _objectTimestamp = new();
    private readonly TextBox _objectId = new();
    private readonly InvariantNumericTextBox _objectUserData = new();
    private readonly InvariantNumericTextBox[] _objectPosition = NewVector();
    private readonly InvariantNumericTextBox[] _objectUp = NewVector();
    private readonly InvariantNumericTextBox[] _objectAt = NewVector();
    private BaseObjectItem? _editingObject;

    private static InvariantNumericTextBox[] NewVector()
        => Enumerable.Range(0, 3).Select(_ => new InvariantNumericTextBox()).ToArray();

    private static TextBox ReadOnlyTextBox() => new() { IsReadOnly = true };

    private void AppendAdvancedBaseMetadataRows(Grid form, ref int row)
    {
        AddAdvancedRow(form, ref row, "base.obj_original_base_version", _originalBaseVersion);
        AddAdvancedRow(form, ref row, "base.obj_user_data", _baseUserData);
        AddAdvancedRow(form, ref row, "base.obj_rid", _rid);
        AddAdvancedRow(form, ref row, "base.obj_last_update_timestamp", _lastUpdateTimestamp);
        AddAdvancedRow(form, ref row, "base.obj_auto_power", _autoPower);
        AddAdvancedRow(form, ref row, "base.obj_screenshot_at", VectorEditor(_screenshotAt));
        AddAdvancedRow(form, ref row, "base.obj_screenshot_pos", VectorEditor(_screenshotPos));
        AddAdvancedRow(form, ref row, "base.obj_is_reported", _isReported);
        AddAdvancedRow(form, ref row, "base.obj_is_featured", _isFeatured);
        AddAdvancedRow(form, ref row, "base.obj_base_type", _baseType);
        AddAdvancedRow(form, ref row, "base.obj_game_mode", _gameMode);
        AddAdvancedRow(form, ref row, "base.obj_difficulty", _difficulty);
        AddAdvancedRow(form, ref row, "base.obj_platform_token", _platformToken);
        AddAdvancedRow(form, ref row, "base.obj_owner_lid", _ownerLid);
        AddAdvancedRow(form, ref row, "base.obj_owner_uid", _ownerUid);
        AddAdvancedRow(form, ref row, "base.obj_owner_usn", _ownerUsn);
        AddAdvancedRow(form, ref row, "base.obj_owner_ptk", _ownerPtk);
        AddAdvancedRow(form, ref row, "base.obj_owner_ts", _ownerTs);
        AddAdvancedRow(form, ref row, "base.obj_last_edited_by_id", _lastEditedById);
        AddAdvancedRow(form, ref row, "base.obj_last_edited_by_username", _lastEditedByUsername);

        foreach (Control field in new Control[] { _originalBaseVersion, _baseUserData, _lastUpdateTimestamp, _rid, _autoPower }
                     .Concat(_screenshotAt).Concat(_screenshotPos))
            field.LostFocus += (_, _) => CommitAdvancedFields();
        _isReported.IsCheckedChanged += (_, _) => CommitAdvancedFields();
        _isFeatured.IsCheckedChanged += (_, _) => CommitAdvancedFields();
    }

    private void AddAdvancedRow(Grid form, ref int row, string key, Control field)
    {
        _advancedLabels[key] = BaseUi.AddRow(form, row++, key, field);
    }

    private Control BuildObjectsEditor()
    {
        _objectList.MinHeight = 180;
        _objectSearchBox.TextChanged += (_, _) => ApplyObjectFilter();
        _objectSearchClear.Click += (_, _) => _objectSearchBox.Text = string.Empty;
        _objectList.SelectionChanged += (_, _) =>
        {
            CommitSelectedObject(force: true, refreshList: false);
            _editingObject = _objectList.SelectedItem as BaseObjectItem;
            LoadSelectedObject();
            UpdateEnabled();
        };

        _freighterRoomList.ItemsSource = _freighterRooms;
        _freighterRoomsPanel.Children.Add(_freighterRoomsHeader);
        _freighterRoomsPanel.Children.Add(_freighterRoomList);

        var search = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 5 };
        search.Children.Add(_objectSearchBox);
        Grid.SetColumn(_objectSearchClear, 1);
        search.Children.Add(_objectSearchClear);

        var details = BaseUi.FormGrid();
        int row = 0;
        AddObjectRow(details, ref row, "base.obj_detail_timestamp", _objectTimestamp);
        AddObjectRow(details, ref row, "base.obj_detail_object_id", _objectId);
        AddObjectRow(details, ref row, "base.obj_detail_user_data", _objectUserData);
        AddObjectRow(details, ref row, "base.obj_detail_position", VectorEditor(_objectPosition));
        AddObjectRow(details, ref row, "base.obj_detail_up", VectorEditor(_objectUp));
        AddObjectRow(details, ref row, "base.obj_detail_at", VectorEditor(_objectAt));
        foreach (Control field in new Control[] { _objectTimestamp, _objectId, _objectUserData }
                     .Concat(_objectPosition).Concat(_objectUp).Concat(_objectAt))
            field.LostFocus += (_, _) => CommitSelectedObject();

        var list = new StackPanel { Spacing = 5, Children = { search, _objectList } };
        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,3*"), ColumnSpacing = 12 };
        split.Children.Add(list);
        Grid.SetColumn(details, 1);
        split.Children.Add(details);

        return new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(8), Spacing = 8,
                Children = { _objectsHeader, _freighterRoomsPanel, split }
            }
        };
    }

    private void AddObjectRow(Grid form, ref int row, string key, Control field)
    {
        _objectLabels[key] = BaseUi.AddRow(form, row++, key, field);
    }

    private void LoadAdvancedFields(BaseListItem item)
    {
        JsonObject value = item.Data;
        LoadNumber(value, "OriginalBaseVersion", _originalBaseVersion);
        LoadNumber(value, "UserData", _baseUserData);
        LoadNumber(value, "LastUpdateTimestamp", _lastUpdateTimestamp);
        _rid.Text = value.GetString("RID") ?? string.Empty;
        _autoPower.Text = value.GetObject("AutoPowerSetting")?.GetString("BaseAutoPowerSetting") ?? string.Empty;
        LoadVector(value.GetArray("ScreenshotAt"), _screenshotAt, emptyAsZero: false);
        LoadVector(value.GetArray("ScreenshotPos"), _screenshotPos, emptyAsZero: false);
        _isReported.IsChecked = TryBool(value, "IsReported");
        _isFeatured.IsChecked = TryBool(value, "IsFeatured");

        _baseType.Text = value.GetString("BaseType.PersistentBaseTypes") ?? value.GetString("BaseType") ?? string.Empty;
        _gameMode.Text = value.GetString("GameMode.PresetGameMode") ?? string.Empty;
        _difficulty.Text = value.GetString("Difficulty.DifficultyPreset.DifficultyPresetType") ?? string.Empty;
        _platformToken.Text = value.GetString("PlatformToken") ?? string.Empty;
        _ownerLid.Text = value.GetString("Owner.LID") ?? string.Empty;
        _ownerUid.Text = value.GetString("Owner.UID") ?? string.Empty;
        _ownerUsn.Text = value.GetString("Owner.USN") ?? string.Empty;
        _ownerPtk.Text = value.GetString("Owner.PTK") ?? string.Empty;
        _ownerTs.Text = Scalar(value.GetValue("Owner.TS"));
        _lastEditedById.Text = value.GetString("LastEditedById") ?? string.Empty;
        _lastEditedByUsername.Text = value.GetString("LastEditedByUsername") ?? string.Empty;

        _freighterRoomsPanel.IsVisible = item.IsFreighterBase;
        _freighterRooms.Clear();
        if (item.IsFreighterBase)
            foreach (string room in FreighterLogic.DetectFreighterRooms(value)) _freighterRooms.Add(room);

        _allObjects.Clear();
        if (value.GetArray("Objects") is { } objects)
        {
            for (int i = 0; i < objects.Length; i++)
            {
                try
                {
                    JsonObject obj = objects.GetObject(i);
                    _allObjects.Add(new BaseObjectItem(obj.GetString("ObjectID") ?? string.Empty, obj, i));
                }
                catch { }
            }
        }
        _objectSearchBox.Text = string.Empty;
        ApplyObjectFilter(_allObjects.FirstOrDefault(value => value.ObjectId != "^BASE_FLAG") ?? _allObjects.FirstOrDefault());
    }

    private void ClearAdvancedFields()
    {
        _originalBaseVersion.NumericValue = _baseUserData.NumericValue = _lastUpdateTimestamp.NumericValue = null;
        _rid.Text = _autoPower.Text = string.Empty;
        ClearVector(_screenshotAt); ClearVector(_screenshotPos);
        _isReported.IsChecked = _isFeatured.IsChecked = false;
        foreach (TextBox field in new[] { _baseType, _gameMode, _difficulty, _platformToken, _ownerLid, _ownerUid, _ownerUsn, _ownerPtk, _ownerTs, _lastEditedById, _lastEditedByUsername }) field.Text = string.Empty;
        _freighterRooms.Clear(); _freighterRoomsPanel.IsVisible = false;
        _allObjects.Clear(); _objects.Clear(); _editingObject = null; ClearObjectFields();
    }

    private void CommitAdvancedFields(bool force = false)
    {
        if ((_loading && !force) || SelectedBase is not { } item) return;
        JsonObject value = item.Data;
        CommitNumber(value, "OriginalBaseVersion", _originalBaseVersion);
        CommitNumber(value, "UserData", _baseUserData);
        CommitNumber(value, "LastUpdateTimestamp", _lastUpdateTimestamp);
        if (value.Contains("RID") || !string.IsNullOrEmpty(_rid.Text))
            value.Set("RID", _rid.Text ?? string.Empty);
        JsonObject? autoPower = value.GetObject("AutoPowerSetting");
        if (autoPower == null && !string.IsNullOrEmpty(_autoPower.Text))
        {
            autoPower = new JsonObject();
            value.Set("AutoPowerSetting", autoPower);
        }
        if (autoPower != null)
            autoPower.Set("BaseAutoPowerSetting", _autoPower.Text ?? string.Empty);
        CommitVector(value, "ScreenshotAt", _screenshotAt);
        CommitVector(value, "ScreenshotPos", _screenshotPos);
        if (value.Contains("IsReported") || _isReported.IsChecked == true)
            value.Set("IsReported", _isReported.IsChecked == true);
        if (value.Contains("IsFeatured") || _isFeatured.IsChecked == true)
            value.Set("IsFeatured", _isFeatured.IsChecked == true);
        if (!force) RaiseModified();
    }

    private void ApplyObjectFilter(BaseObjectItem? preferred = null)
    {
        string filter = _objectSearchBox.Text?.Trim() ?? string.Empty;
        BaseObjectItem? selected = preferred ?? _objectList.SelectedItem as BaseObjectItem;
        _objects.Clear();
        foreach (BaseObjectItem item in _allObjects)
            if (filter.Length == 0 || item.ObjectId.Contains(filter, StringComparison.OrdinalIgnoreCase)) _objects.Add(item);
        _objectList.SelectedItem = selected != null && _objects.Contains(selected) ? selected : _objects.FirstOrDefault();
        if (_objectList.SelectedItem == null) { _editingObject = null; ClearObjectFields(); }
        UpdateEnabled();
    }

    private void LoadSelectedObject()
    {
        bool old = _loading; _loading = true;
        try
        {
            if (_editingObject == null) { ClearObjectFields(); return; }
            JsonObject value = _editingObject.Data;
            LoadNumber(value, "Timestamp", _objectTimestamp);
            _objectId.Text = value.GetString("ObjectID") ?? string.Empty;
            LoadNumber(value, "UserData", _objectUserData);
            LoadVector(value.GetArray("Position"), _objectPosition, emptyAsZero: false);
            LoadVector(value.GetArray("Up"), _objectUp, emptyAsZero: false);
            LoadVector(value.GetArray("At"), _objectAt, emptyAsZero: false);
        }
        finally { _loading = old; }
    }

    private void ClearObjectFields()
    {
        _objectTimestamp.NumericValue = _objectUserData.NumericValue = null; _objectId.Text = string.Empty;
        ClearVector(_objectPosition); ClearVector(_objectUp); ClearVector(_objectAt);
    }

    private void CommitSelectedObject(bool force = false, bool refreshList = true)
    {
        if ((_loading && !force) || _editingObject == null) return;
        JsonObject value = _editingObject.Data;
        CommitNumber(value, "Timestamp", _objectTimestamp);
        CommitNumber(value, "UserData", _objectUserData);
        string objectId = _objectId.Text?.Trim() ?? string.Empty;
        if (objectId.Length > 0) { value.Set("ObjectID", objectId); _editingObject.ObjectId = objectId; }
        CommitVector(value, "Position", _objectPosition);
        CommitVector(value, "Up", _objectUp);
        CommitVector(value, "At", _objectAt);
        if (!force)
        {
            if (refreshList) ApplyObjectFilter(_editingObject);
            RaiseModified();
        }
    }

    private void ApplyAdvancedLocalisation()
    {
        _infoTab.Header = UiStrings.Get("base.info_tab"); _objectsTab.Header = UiStrings.Get("base.objects_tab");
        _objectsHeader.Text = UiStrings.Get("base.obj_heading");
        _freighterRoomsHeader.Text = UiStrings.Get("base.freighter_rooms_header");
        _objectSearchBox.PlaceholderText = UiStrings.Get("base.objects_filter");
        foreach ((string key, TextBlock label) in _advancedLabels) label.Text = UiStrings.Get(key);
        foreach ((string key, TextBlock label) in _objectLabels) label.Text = UiStrings.Get(key);
    }

    private static void LoadNumber(JsonObject value, string key, InvariantNumericTextBox field)
    {
        try { field.SetValueWithText(value.GetDouble(key), value.GetDoubleText(key)); }
        catch { field.NumericValue = null; }
    }

    private static void CommitNumber(JsonObject value, string key, InvariantNumericTextBox field)
    {
        if (!field.TryCommit() || field.NumericValue is not { } number) return;
        string text = field.DisplayText.Trim();
        try { if (text == value.GetDoubleText(key)) return; }
        catch { }
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer)) RawNumberGuard.SetLong(value, key, integer);
        else value.Set(key, number);
    }

    private static void LoadVector(JsonArray? values, InvariantNumericTextBox[] fields, bool emptyAsZero)
    {
        for (int i = 0; i < fields.Length; i++)
        {
            try
            {
                if (values != null && i < values.Length) fields[i].SetValueWithText(values.GetDouble(i), values.GetDoubleText(i));
                else fields[i].NumericValue = emptyAsZero ? 0 : null;
            }
            catch { fields[i].NumericValue = emptyAsZero ? 0 : null; }
        }
    }

    private static void CommitVector(JsonObject value, string key, InvariantNumericTextBox[] fields)
    {
        if (fields.All(field => string.IsNullOrWhiteSpace(field.DisplayText))) return;
        JsonArray? values = value.GetArray(key);
        if (values == null) { values = new JsonArray(); value.Set(key, values); }
        for (int i = 0; i < fields.Length; i++)
        {
            if (!fields[i].TryCommit() || fields[i].NumericValue is not { } number) continue;
            while (values.Length <= i) values.Add(0d);
            try { if (fields[i].DisplayText.Trim() == values.GetDoubleText(i)) continue; }
            catch { }
            values.Set(i, number);
        }
    }

    private static void ClearVector(IEnumerable<InvariantNumericTextBox> fields)
    {
        foreach (InvariantNumericTextBox field in fields) field.NumericValue = null;
    }
}
