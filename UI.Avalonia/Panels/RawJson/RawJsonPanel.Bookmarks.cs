using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using NMSE.Config;
using NMSE.Core;
using NMSE.Data;

namespace NMSE.UI.Avalonia.Panels.RawJson;

public partial class RawJsonPanel
{
    private List<RawJsonBookmark> _bookmarks = [];
    private readonly List<RawJsonBookmark> _unsavedSaveBookmarks = [];
    private readonly List<RawJsonBookmark> _unsavedAccountBookmarks = [];
    private bool _refreshingBookmarks;

    private string? BookmarkScope
    {
        get
        {
            string? path = _showingAccount ? _accountFilePath : _saveFilePath;
            return string.IsNullOrWhiteSpace(path) ? null
                : (_showingAccount ? "account-" : "save-") + AppConfig.BuildSaveScopeKey(path);
        }
    }

    private void RefreshBookmarks()
    {
        if (BookmarkSelector == null) return;
        _refreshingBookmarks = true;
        try
        {
            _bookmarks = BookmarkScope is { } scope ? AppConfig.Instance.GetRawJsonBookmarks(scope)
                : RawJsonBookmarksLogic.CloneAll(_showingAccount ? _unsavedAccountBookmarks : _unsavedSaveBookmarks);
            BookmarkSelector.ItemsSource = _bookmarks.ToArray();
            BookmarkSelector.SelectedIndex = -1;
            BookmarkSelector.PlaceholderText = UiStrings.Get("raw_json.bookmark_empty");
        }
        finally { _refreshingBookmarks = false; }
        UpdateCommandState();
    }

    private void PersistBookmarks()
    {
        if (BookmarkScope is { } scope)
        {
            AppConfig.Instance.SetRawJsonBookmarks(scope, _bookmarks);
            AppConfig.Instance.Save();
        }
        else
        {
            var unsaved = _showingAccount ? _unsavedAccountBookmarks : _unsavedSaveBookmarks;
            unsaved.Clear(); unsaved.AddRange(RawJsonBookmarksLogic.CloneAll(_bookmarks));
        }
        RefreshBookmarks();
    }

    private async void OnAddBookmark(object? sender, RoutedEventArgs e)
    {
        if (SelectedNode is not { IsRoot: false, IsPlaceholder: false } node || OwnerWindow is not { } owner) return;
        string[] path = GetPath(node);
        var result = await JsonDialogs.ShowEditAsync(owner, UiStrings.Get("raw_json.bookmark_add_title"), null, FormatPath(path), false);
        if (result == null) return;
        string name = result.Value.Trim();
        if (!RawJsonBookmarksLogic.IsNameAvailable(name, _bookmarks))
        {
            await ShowErrorAsync(UiStrings.Get("raw_json.bookmark_add_title"), string.IsNullOrEmpty(name)
                ? UiStrings.Get("raw_json.bookmark_name_empty") : UiStrings.Format("raw_json.bookmark_name_duplicate", name));
            return;
        }
        _bookmarks.Add(new RawJsonBookmark { Name = name, Path = path, Isolated = _viewMode == ViewMode.Split && _isolateNode });
        PersistBookmarks(); SetStatus(UiStrings.Format("raw_json.bookmark_added", name));
    }

    private async void OnBookmarkSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingBookmarks || BookmarkSelector.SelectedItem is not RawJsonBookmark bookmark) return;
        await NavigateToBookmarkAsync(bookmark);
    }

    private async Task NavigateToBookmarkAsync(RawJsonBookmark bookmark)
    {
        if (ActiveData == null || _textDirty && !TryCommitText(showErrors: true)) return;
        _isolateNode = bookmark.Isolated;
        _suppressIsolateToggle = true;
        IsolateCheck.IsChecked = _isolateNode;
        _suppressIsolateToggle = false;
        SetMode(bookmark.Isolated ? ViewMode.Split : ViewMode.Tree);
        var roots = bookmark.Isolated ? _splitTreeRoots : _treeRoots;
        var target = FindPath(roots.FirstOrDefault(), bookmark.Path);
        if (target == null)
        {
            await ShowErrorAsync(UiStrings.Get("raw_json.bookmarks"), UiStrings.Get("raw_json.bookmark_not_found"));
            return;
        }
        if (bookmark.Isolated) { SplitTree.SelectedItem = target; ShowIsolatedNode(target); }
        else Tree.SelectedItem = target;
        UpdateBreadcrumb(target); UpdateCommandState();
    }

    private async void OnManageBookmarks(object? sender, RoutedEventArgs e)
    {
        if (OwnerWindow is not { } owner) return;
        // Work on a copy so closing the manager leaves the saved list intact.
        var working = RawJsonBookmarksLogic.CloneAll(_bookmarks);
        var list = new ListBox { ItemsSource = working.ToArray() };
        var name = new TextBox();
        var error = new TextBlock();
        var rename = new Button { Content = UiStrings.Get("raw_json.bookmark_rename") };
        var delete = new Button { Content = UiStrings.Get("common.delete") };
        var up = new Button { Content = "↑" }; var down = new Button { Content = "↓" };
        var save = new Button { Content = UiStrings.Get("common.apply") };
        var cancel = new Button { Content = UiStrings.Get("common.cancel") };
        var window = new Window { Title = UiStrings.Get("raw_json.bookmark_manage_title"), Width = 560, Height = 520, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var controls = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = UiStrings.Get("raw_json.bookmark_name_label") }, name, error,
            new WrapPanel { Children = { rename, delete, up, down, save, cancel } } } };
        var root = new Grid { Margin = new Thickness(12), RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 8, Children = { list, controls } };
        Grid.SetRow(controls, 1); window.Content = root;
        void Refresh(RawJsonBookmark? selected) { list.ItemsSource = working.ToArray(); list.SelectedItem = selected; }
        list.SelectionChanged += (_, _) => name.Text = (list.SelectedItem as RawJsonBookmark)?.Name ?? "";
        rename.Click += (_, _) =>
        {
            if (list.SelectedItem is not RawJsonBookmark selected) return;
            string value = name.Text?.Trim() ?? "";
            if (!RawJsonBookmarksLogic.IsNameAvailable(value, working.Where(b => !ReferenceEquals(b, selected))))
            { error.Text = value.Length == 0 ? UiStrings.Get("raw_json.bookmark_name_empty") : UiStrings.Format("raw_json.bookmark_name_duplicate", value); return; }
            selected.Name = value; error.Text = ""; Refresh(selected);
        };
        delete.Click += async (_, _) =>
        {
            if (list.SelectedItem is not RawJsonBookmark selected) return;
            if (!await JsonDialogs.ConfirmAsync(window, UiStrings.Get("common.delete"), UiStrings.Format("raw_json.bookmark_delete_confirm", selected.Name))) return;
            working.Remove(selected); Refresh(working.FirstOrDefault());
        };
        void Move(int delta)
        {
            if (list.SelectedItem is not RawJsonBookmark selected) return;
            int index = working.IndexOf(selected), target = index + delta;
            if (target < 0 || target >= working.Count) return;
            working.RemoveAt(index); working.Insert(target, selected); Refresh(selected);
        }
        up.Click += (_, _) => Move(-1); down.Click += (_, _) => Move(1);
        save.Click += (_, _) => { _bookmarks = working; PersistBookmarks(); window.Close(); };
        cancel.Click += (_, _) => window.Close();
        await window.ShowDialog(owner);
    }
}
