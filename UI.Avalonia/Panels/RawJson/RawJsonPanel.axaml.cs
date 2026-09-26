using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Contracts;

namespace NMSE.UI.Avalonia.Panels.RawJson;

/// <summary>
/// Cross-platform raw save/account JSON editor. Tree mutations are applied directly to
/// the authoritative JsonObject; text replacements preserve the root object reference.
/// </summary>
public partial class RawJsonPanel : UserControl, ISaveEditorPanel
{
    private enum ViewMode { Tree, Text, Split }

    private readonly ObservableCollection<JsonTreeNode> _treeRoots = [];
    private readonly ObservableCollection<JsonTreeNode> _splitTreeRoots = [];
    private readonly Stack<IJsonUndoAction> _undoStack = [];
    private readonly Stack<IJsonUndoAction> _redoStack = [];
    private readonly List<string[]> _searchPaths = [];

    private JsonObject? _saveData;
    private JsonObject? _accountData;
    private JsonObject? _capturedSaveReference;
    private byte[]? _saveBaseline;
    private byte[]? _accountBaseline;
    private string? _saveFilePath;
    private string? _accountFilePath;
    private ViewMode _viewMode;
    private bool _showingAccount;
    private bool _suppressDocumentSelection;
    private bool _suppressTextEvents;
    private bool _textDirty;
    private int _searchIndex = -1;
    private string _lastSearch = string.Empty;
    private JsonTreeNode? _dragCandidate;
    private Point _dragStart;
    private bool _pointerDragActive;
    private CancellationTokenSource? _expandCancellation;

    public event EventHandler? DataModified;

    /// <summary>Shell hook that opens a JSON file and returns its text.</summary>
    public Func<JsonTransferRequest, CancellationToken, Task<JsonTransferContent?>>? ImportJsonAsync { get; set; }

    /// <summary>Shell hook that chooses a path and writes JSON text.</summary>
    public Func<JsonTransferRequest, string, CancellationToken, Task<bool>>? ExportJsonAsync { get; set; }

    /// <summary>Optional shell confirmation hook. An Avalonia modal is used when omitted.</summary>
    public Func<string, string, CancellationToken, Task<bool>>? ConfirmAsync { get; set; }

    public RawJsonPanel()
    {
        InitializeComponent();
        Tree.ItemsSource = _treeRoots;
        SplitTree.ItemsSource = _splitTreeRoots;
        ApplyUiLocalisation();
        SetMode(ViewMode.Tree, reload: false);
        UpdateDocumentSelector();
        UpdateCommandState();
    }

    public void CaptureBaseline(JsonObject saveData)
    {
        _capturedSaveReference = saveData;
        _saveBaseline = JsonRootAction.Compress(RawJsonLogic.ToDisplayString(saveData));
    }

    public void NotifyDataChanged() => SetStatus(string.Empty);

    public void LoadData(JsonObject saveData)
    {
        _saveData = saveData;
        _showingAccount = false;
        if (!ReferenceEquals(_capturedSaveReference, saveData) || _saveBaseline == null)
            CaptureBaseline(saveData);

        ResetEditHistory();
        UpdateDocumentSelector();
        ReloadCurrentView();
        SetStatus(UiStrings.Format("raw_json.loaded_keys", saveData.Size().ToString("N0", CultureInfo.CurrentCulture)));
    }

    public void SaveData(JsonObject saveData)
    {
        if (!_showingAccount && _textDirty && !TryCommitText(showErrors: false))
            throw new JsonException(UiStrings.Get("raw_json.validation_error"));

        if (_saveData != null && !ReferenceEquals(saveData, _saveData))
            JsonRootAction.Replace(saveData, _saveData);
    }

    public void SetAccountData(JsonObject? accountData, string? accountFilePath = null)
    {
        _accountData = accountData;
        _accountFilePath = accountFilePath;
        _accountBaseline = accountData == null
            ? null
            : JsonRootAction.Compress(RawJsonLogic.ToDisplayString(accountData));
        if (accountData == null) _showingAccount = false;
        UpdateDocumentSelector();
    }

    public void SetSaveFilePath(string? saveFilePath)
    {
        _saveFilePath = saveFilePath;
        UpdateDocumentSelector();
    }

    public JsonObject? GetEditedData()
    {
        if (!_showingAccount && _textDirty) TryCommitText(showErrors: false);
        return _saveData;
    }

    public JsonObject? GetEditedAccountData()
    {
        if (_showingAccount && _textDirty) TryCommitText(showErrors: false);
        return _accountData;
    }

    public void RefreshTree(JsonObject? latestSaveData = null)
    {
        if (latestSaveData != null) _saveData = latestSaveData;
        ReloadCurrentView();
    }

    public void ApplyUiLocalisation()
    {
        TitleText.Text = UiStrings.Get("raw_json.title");
        ExportButton.Content = UiStrings.Get("raw_json.export");
        ImportButton.Content = UiStrings.Get("raw_json.import");
        DiffButton.Content = UiStrings.Get("raw_json.diff");
        SearchBox.PlaceholderText = UiStrings.Get("raw_json.search_keys_values");
        SearchButton.Content = UiStrings.Get("raw_json.find");
        PreviousSearchButton.Content = UiStrings.Get("raw_json.find_prev");
        TreeModeButton.Content = UiStrings.Get("raw_json.tree_view");
        TextModeButton.Content = UiStrings.Get("raw_json.text_view");
        SplitModeButton.Content = UiStrings.Get("raw_json.split_view");
        TextSyntaxTitle.Text = UiStrings.Get("raw_json.text_view");
        SplitSyntaxTitle.Text = UiStrings.Get("raw_json.text_view");
        ExpandButton.Content = UiStrings.Get("raw_json.expand_all");
        CollapseButton.Content = UiStrings.Get("raw_json.collapse_all");
        FormatButton.Content = UiStrings.Get("raw_json.format");
        ValidateButton.Content = UiStrings.Get("raw_json.validate");
        EditMenuItem.Header = UiStrings.Get("raw_json.ctx_edit_value");
        IsolateMenuItem.Header = SplitIsolateMenuItem.Header = UiStrings.Get("raw_json.isolate_node");
        AddPropertyMenuItem.Header = UiStrings.Get("raw_json.ctx_add_property");
        AddArrayItemMenuItem.Header = UiStrings.Get("raw_json.ctx_add_array_item");
        DeleteMenuItem.Header = UiStrings.Get("raw_json.ctx_delete");
        CopyKeyMenuItem.Header = UiStrings.Get("raw_json.ctx_copy_key");
        CopyValueMenuItem.Header = UiStrings.Get("raw_json.ctx_copy_value");
        CopyPathMenuItem.Header = UiStrings.Get("raw_json.ctx_copy_path");
        ExportNodeMenuItem.Header = UiStrings.Get("raw_json.ctx_export_node");
        ImportNodeMenuItem.Header = UiStrings.Get("raw_json.ctx_import_node");
        SplitEditMenuItem.Header = EditMenuItem.Header;
        SplitAddPropertyMenuItem.Header = AddPropertyMenuItem.Header;
        SplitAddArrayItemMenuItem.Header = AddArrayItemMenuItem.Header;
        SplitDeleteMenuItem.Header = DeleteMenuItem.Header;
        SplitCopyKeyMenuItem.Header = CopyKeyMenuItem.Header;
        SplitCopyValueMenuItem.Header = CopyValueMenuItem.Header;
        SplitCopyPathMenuItem.Header = CopyPathMenuItem.Header;
        SplitExportNodeMenuItem.Header = ExportNodeMenuItem.Header;
        SplitImportNodeMenuItem.Header = ImportNodeMenuItem.Header;
        UpdateDocumentSelector();
    }

    private JsonObject? ActiveData => _showingAccount ? _accountData : _saveData;
    private byte[]? ActiveBaseline => _showingAccount ? _accountBaseline : _saveBaseline;
    private JsonTreeNode? SelectedNode => (_viewMode == ViewMode.Split ? SplitTree.SelectedItem : Tree.SelectedItem) as JsonTreeNode;
    private TextBox ActiveEditor => _viewMode == ViewMode.Split ? SplitTextEditor : TextEditor;

    private void UpdateDocumentSelector()
    {
        if (DocumentSelector == null) return;
        _suppressDocumentSelection = true;
        try
        {
            var options = new List<DocumentOption>
            {
                new(false, string.IsNullOrWhiteSpace(_saveFilePath) ? "Save File" : Path.GetFileName(_saveFilePath))
            };
            if (_accountData != null)
                options.Add(new(true, string.IsNullOrWhiteSpace(_accountFilePath) ? "accountdata.hg" : Path.GetFileName(_accountFilePath)));
            DocumentSelector.ItemsSource = options;
            DocumentSelector.SelectedIndex = _showingAccount && options.Count > 1 ? 1 : 0;
        }
        finally
        {
            _suppressDocumentSelection = false;
        }
    }

    private async void OnDocumentSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressDocumentSelection || DocumentSelector.SelectedItem is not DocumentOption option) return;
        if (option.IsAccount == _showingAccount) return;

        if (_textDirty && !TryCommitText(showErrors: false))
        {
            await ShowErrorAsync(UiStrings.Get("raw_json.validation_error"),
                UiStrings.Get("raw_json.validation_error"));
            UpdateDocumentSelector();
            return;
        }

        _showingAccount = option.IsAccount;
        ResetEditHistory();
        ReloadCurrentView();
        JsonObject? data = ActiveData;
        if (data != null)
        {
            SetStatus(_showingAccount
                ? UiStrings.Format("raw_json.edited_account", data.Size().ToString("N0", CultureInfo.CurrentCulture))
                : UiStrings.Format("raw_json.loaded_keys", data.Size().ToString("N0", CultureInfo.CurrentCulture)));
        }
    }

    private async void OnTreeMode(object? sender, RoutedEventArgs e)
    {
        if (!await CommitBeforeViewSwitchAsync()) return;
        SetMode(ViewMode.Tree);
    }

    private async void OnTextMode(object? sender, RoutedEventArgs e)
    {
        if (!await CommitBeforeViewSwitchAsync()) return;
        SetMode(ViewMode.Text);
    }

    private async void OnSplitMode(object? sender, RoutedEventArgs e)
    {
        if (!await CommitBeforeViewSwitchAsync()) return;
        SetMode(ViewMode.Split);
    }

    private async Task<bool> CommitBeforeViewSwitchAsync()
    {
        if (_viewMode == ViewMode.Tree || !_textDirty) return true;
        if (TryCommitText(showErrors: false)) return true;
        await ShowErrorAsync(UiStrings.Get("raw_json.validation_error"),
            UiStrings.Get("raw_json.validation_error"));
        return false;
    }

    private void SetMode(ViewMode mode, bool reload = true)
    {
        _viewMode = mode;
        Tree.IsVisible = mode == ViewMode.Tree;
        TextView.IsVisible = mode == ViewMode.Text;
        SplitView.IsVisible = mode == ViewMode.Split;
        TreeModeButton.IsEnabled = mode != ViewMode.Tree;
        TextModeButton.IsEnabled = mode != ViewMode.Text;
        SplitModeButton.IsEnabled = mode != ViewMode.Split;
        ExpandButton.IsVisible = mode != ViewMode.Text;
        CollapseButton.IsVisible = mode != ViewMode.Text;
        FormatButton.IsVisible = mode != ViewMode.Tree;
        ValidateButton.IsVisible = mode != ViewMode.Tree;
        if (reload) ReloadCurrentView();
        UpdateCommandState();
    }

    private void ReloadCurrentView()
    {
        JsonObject? data = ActiveData;
        _suppressTextEvents = true;
        try
        {
            if (data == null)
            {
                _treeRoots.Clear();
                _splitTreeRoots.Clear();
                TextEditor.Text = string.Empty;
                SplitTextEditor.Text = string.Empty;
                TextSyntaxPreview.JsonText = string.Empty;
                SplitSyntaxPreview.JsonText = string.Empty;
                return;
            }

            string? display = null;
            if (_viewMode == ViewMode.Tree)
                BuildTree(_treeRoots, data);
            else if (_viewMode == ViewMode.Text)
            {
                display = RawJsonLogic.ToDisplayString(data);
                TextEditor.Text = display;
                TextSyntaxPreview.JsonText = display;
            }
            else
            {
                BuildTree(_splitTreeRoots, data);
                display = RawJsonLogic.ToDisplayString(data);
                SplitTextEditor.Text = display;
                SplitSyntaxPreview.JsonText = display;
            }
            _textDirty = false;
        }
        finally
        {
            _suppressTextEvents = false;
        }
        UpdateCommandState();
    }

    private static void BuildTree(ObservableCollection<JsonTreeNode> roots, JsonObject data)
    {
        roots.Clear();
        roots.Add(JsonTreeNode.CreateRoot(data));
    }

    private bool TryCommitText(bool showErrors)
    {
        if (!_textDirty || _viewMode == ViewMode.Tree) return true;
        JsonObject? target = ActiveData;
        if (target == null) return true;
        string json = ActiveEditor.Text ?? string.Empty;
        try
        {
            JsonObject parsed = RawJsonLogic.ParseJson(json);
            string oldText = RawJsonLogic.ToDisplayString(target);
            byte[] before = JsonRootAction.Compress(oldText);
            byte[] after = JsonRootAction.Compress(RawJsonLogic.ToDisplayString(parsed));
            JsonRootAction.Replace(target, parsed);
            _undoStack.Clear();
            _redoStack.Clear();
            _undoStack.Push(new JsonRootAction(target, before, after));
            _textDirty = false;
            SetStatus(UiStrings.Get("raw_json.tree_rebuilt"));
            UpdateCommandState();
            return true;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            SetStatus(UiStrings.Format("raw_json.parse_error", ex.Message), Brushes.OrangeRed);
            if (showErrors) _ = ShowErrorAsync(UiStrings.Get("raw_json.validation_error"), ex.Message);
            return false;
        }
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e) => TextChanged(TextEditor, TextSyntaxPreview);
    private void OnSplitTextChanged(object? sender, TextChangedEventArgs e) => TextChanged(SplitTextEditor, SplitSyntaxPreview);

    private void TextChanged(TextBox editor, JsonSyntaxView preview)
    {
        if (_suppressTextEvents) return;
        _textDirty = true;
        preview.JsonText = editor.Text ?? string.Empty;
        RaiseDataModified();
        UpdateCommandState();
    }

    private void OnTextEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(PlatformCommandModifier) && e.Key == Key.S)
        {
            TryCommitText(showErrors: true);
            e.Handled = true;
        }
    }

    private static KeyModifiers PlatformCommandModifier =>
        OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    private void ResetEditHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        _textDirty = false;
        _searchPaths.Clear();
        _searchIndex = -1;
        UpdateCommandState();
    }

    private void RaiseDataModified()
    {
        DataModified?.Invoke(this, EventArgs.Empty);
        UpdateCommandState();
    }

    private void RecordAction(IJsonUndoAction action)
    {
        _undoStack.Push(action);
        _redoStack.Clear();
        RaiseDataModified();
    }

    private void UpdateCommandState()
    {
        if (DiffButton == null) return;
        bool hasDocument = ActiveData != null;
        ExportButton.IsEnabled = hasDocument;
        DiffButton.IsEnabled = hasDocument && ActiveBaseline != null;
        ImportButton.IsEnabled = true;
    }

    private void SetStatus(string message, IBrush? foreground = null)
    {
        StatusText.Text = message;
        StatusText.Foreground = foreground ?? Brushes.Gray;
    }

    private Window? OwnerWindow => TopLevel.GetTopLevel(this) as Window;

    private async Task ShowErrorAsync(string title, string message)
    {
        if (OwnerWindow is { } owner)
            await JsonDialogs.ShowMessageAsync(owner, title, message);
        else
            SetStatus(message, Brushes.OrangeRed);
    }

    private async Task<bool> ConfirmOperationAsync(string title, string message)
    {
        if (ConfirmAsync != null)
            return await ConfirmAsync(title, message, CancellationToken.None);
        return OwnerWindow is { } owner && await JsonDialogs.ConfirmAsync(owner, title, message);
    }

    private sealed record DocumentOption(bool IsAccount, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
