using System.ComponentModel;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.UI.Avalonia.Panels.RawJson;

public partial class RawJsonPanel
{
    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateBreadcrumb(SelectedNode);
        UpdateCommandState();
    }

    private void OnSplitTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        JsonTreeNode? node = SplitTree.SelectedItem as JsonTreeNode;
        UpdateBreadcrumb(node);
        UpdateCommandState();
        if (node?.Key == null) return;

        string text = SplitTextEditor.Text ?? string.Empty;
        string needle = node.Key.StartsWith("[", StringComparison.Ordinal) ? node.Key : $"\"{node.Key}\"";
        int index = text.IndexOf(needle, StringComparison.Ordinal);
        if (index < 0) return;
        SplitTextEditor.SelectionStart = index;
        SplitTextEditor.SelectionEnd = Math.Min(text.Length, index + needle.Length);
        int line = 0;
        for (int i = 0; i < index; i++) if (text[i] == '\n') line++;
        SplitTextEditor.ScrollToLine(line);
    }

    private void UpdateBreadcrumb(JsonTreeNode? node)
    {
        BreadcrumbPanel.Children.Clear();
        if (node == null) return;
        var path = new List<JsonTreeNode>();
        for (JsonTreeNode? current = node; current != null; current = current.ParentNode)
            path.Add(current);
        path.Reverse();

        for (int i = 0; i < path.Count; i++)
        {
            if (i > 0)
                BreadcrumbPanel.Children.Add(new TextBlock
                {
                    Text = "›",
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.Gray
                });
            JsonTreeNode target = path[i];
            var button = new Button
            {
                Content = target.Key ?? "Root",
                Padding = new Thickness(5, 1),
                MinHeight = 24
            };
            button.Click += (_, _) =>
            {
                if (_viewMode == ViewMode.Split) SplitTree.SelectedItem = target;
                else Tree.SelectedItem = target;
            };
            BreadcrumbPanel.Children.Add(button);
        }
    }

    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
    {
        JsonTreeNode? node = SelectedNode;
        EditMenuItem.IsVisible = node is { IsContainer: false, IsPlaceholder: false };
        AddPropertyMenuItem.IsVisible = node?.Value is JsonObject;
        AddArrayItemMenuItem.IsVisible = node?.Value is JsonArray;
        DeleteMenuItem.IsVisible = node is { IsRoot: false, IsPlaceholder: false };
        ImportNodeMenuItem.IsVisible = node is { IsRoot: false, IsPlaceholder: false };
        ExportNodeMenuItem.IsVisible = node is { IsPlaceholder: false };
        CopyKeyMenuItem.IsVisible = node?.Key != null;
        CopyValueMenuItem.IsVisible = node is { IsPlaceholder: false };
        CopyPathMenuItem.IsVisible = node is { IsRoot: false, IsPlaceholder: false };
        SplitEditMenuItem.IsVisible = EditMenuItem.IsVisible;
        SplitAddPropertyMenuItem.IsVisible = AddPropertyMenuItem.IsVisible;
        SplitAddArrayItemMenuItem.IsVisible = AddArrayItemMenuItem.IsVisible;
        SplitDeleteMenuItem.IsVisible = DeleteMenuItem.IsVisible;
        SplitImportNodeMenuItem.IsVisible = ImportNodeMenuItem.IsVisible;
        SplitExportNodeMenuItem.IsVisible = ExportNodeMenuItem.IsVisible;
        SplitCopyKeyMenuItem.IsVisible = CopyKeyMenuItem.IsVisible;
        SplitCopyValueMenuItem.IsVisible = CopyValueMenuItem.IsVisible;
        SplitCopyPathMenuItem.IsVisible = CopyPathMenuItem.IsVisible;
    }

    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (SelectedNode is { IsContainer: false, IsPlaceholder: false })
            OnEditNode(sender, e);
    }

    private async void OnEditNode(object? sender, RoutedEventArgs e)
    {
        if (!PrepareTreeMutation()) return;
        JsonTreeNode? node = SelectedNode;
        if (node is not { IsContainer: false, IsPlaceholder: false } ||
            node.ParentContainer == null || node.Key == null || OwnerWindow is not { } owner) return;

        JsonEditResult? result = await JsonDialogs.ShowEditAsync(
            owner,
            UiStrings.Format("raw_json.edit_title", node.Key),
            node.Key,
            RawJsonLogic.FormatValueForEdit(node.Value),
            editKey: false);
        if (result == null) return;

        object? parsed = RawJsonLogic.ParseInputValue(result.Value.Trim(), node.Value);
        object? oldValue = node.Value;
        JsonValueAction.SetValue(node.ParentContainer, node.Key, parsed);
        RecordAction(new JsonValueAction(node.ParentContainer, node.Key, oldValue, parsed));
        RebuildTreesPreservingPath(GetPath(node));
        SetStatus(UiStrings.Get("raw_json.value_modified"), Brushes.DarkOrange);
    }

    private async void OnAddProperty(object? sender, RoutedEventArgs e)
    {
        if (!PrepareTreeMutation()) return;
        JsonTreeNode? node = SelectedNode;
        if (node?.Value is not JsonObject obj || OwnerWindow is not { } owner) return;
        JsonEditResult? result = await JsonDialogs.ShowEditAsync(
            owner, UiStrings.Get("raw_json.add_property_title"), string.Empty, string.Empty, editKey: true);
        string key = result?.Key?.Trim() ?? string.Empty;
        if (result == null || key.Length == 0) return;
        if (obj.Contains(key))
        {
            await ShowErrorAsync(UiStrings.Get("raw_json.duplicate_key_title"),
                UiStrings.Format("raw_json.duplicate_key_msg", key));
            return;
        }

        object? value = RawJsonLogic.ParseInputValue(result.Value.Trim());
        obj.Add(key, value);
        RecordAction(new JsonAddAction(obj, key, value));
        RebuildTreesPreservingPath([..GetPath(node), key]);
        SetStatus(UiStrings.Format("raw_json.added_property", key), Brushes.Green);
    }

    private async void OnAddArrayItem(object? sender, RoutedEventArgs e)
    {
        if (!PrepareTreeMutation()) return;
        JsonTreeNode? node = SelectedNode;
        if (node?.Value is not JsonArray array || OwnerWindow is not { } owner) return;
        JsonEditResult? result = await JsonDialogs.ShowEditAsync(
            owner, UiStrings.Get("raw_json.add_array_item_title"), null, string.Empty, editKey: false);
        if (result == null) return;

        object? value = RawJsonLogic.ParseInputValue(result.Value.Trim());
        int index = array.Length;
        array.Add(value);
        RecordAction(new JsonAddAction(array, $"[{index}]", value));
        RebuildTreesPreservingPath([..GetPath(node), $"[{index}]"]);
        SetStatus(UiStrings.Format("raw_json.added_array_item", index), Brushes.Green);
    }

    private async void OnDeleteNode(object? sender, RoutedEventArgs e)
    {
        if (!PrepareTreeMutation()) return;
        JsonTreeNode? node = SelectedNode;
        if (node is not { IsRoot: false, IsPlaceholder: false } || node.ParentContainer == null || node.Key == null) return;
        if (!await ConfirmOperationAsync(UiStrings.Get("raw_json.confirm_delete_title"),
                UiStrings.Format("raw_json.confirm_delete_msg", node.Key))) return;

        object? oldValue = node.Value;
        if (node.ParentContainer is JsonObject obj)
            obj.Remove(node.Key);
        else if (node.ParentContainer is JsonArray array)
            array.RemoveAt(JsonValueAction.ParseIndex(node.Key));
        RecordAction(new JsonDeleteAction(node.ParentContainer, node.Key, oldValue));
        string[] parentPath = GetPath(node.ParentNode);
        RebuildTreesPreservingPath(parentPath);
        SetStatus(UiStrings.Format("raw_json.deleted", node.Key), Brushes.DarkOrange);
    }

    private void OnUndo(object? sender, RoutedEventArgs e)
    {
        if (_viewMode != ViewMode.Tree)
        {
            ActiveEditor.Undo();
            return;
        }
        if (_undoStack.Count == 0) return;
        IJsonUndoAction action = _undoStack.Pop();
        action.Undo();
        _redoStack.Push(action);
        RebuildTreesPreservingPath([]);
        RaiseDataModified();
        SetStatus(UiStrings.Get("raw_json.undone"), Brushes.DarkOrange);
    }

    private void OnRedo(object? sender, RoutedEventArgs e)
    {
        if (_viewMode != ViewMode.Tree)
        {
            ActiveEditor.Redo();
            return;
        }
        if (_redoStack.Count == 0) return;
        IJsonUndoAction action = _redoStack.Pop();
        action.Redo();
        _undoStack.Push(action);
        RebuildTreesPreservingPath([]);
        RaiseDataModified();
        SetStatus(UiStrings.Get("raw_json.redone"), Brushes.DarkOrange);
    }

    private void MoveSelected(int delta)
    {
        if (!PrepareTreeMutation()) return;
        JsonTreeNode? node = SelectedNode;
        JsonTreeNode? parentNode = node?.ParentNode;
        if (node?.ParentContainer == null || parentNode == null) return;
        parentNode.EnsureChildrenLoaded();
        int from = parentNode.Children.IndexOf(node);
        int to = from + delta;
        if (from < 0 || to < 0 || to >= parentNode.Children.Count) return;
        MoveWithinParent(node.ParentContainer, from, to);
        RecordAction(new JsonReorderAction(node.ParentContainer, from, to));
        string[] path = GetPath(node);
        if (node.ParentContainer is JsonArray && path.Length > 0) path[^1] = $"[{to}]";
        RebuildTreesPreservingPath(path);
        SetStatus(UiStrings.Format("raw_json.reordered", from, to), Brushes.DarkOrange);
    }

    private static void MoveWithinParent(object parent, int from, int to)
    {
        if (parent is JsonObject obj)
            obj.Reorder(from, to);
        else if (parent is JsonArray array)
        {
            object? value = array.Get(from);
            array.RemoveAt(from);
            array.Insert(to, value);
        }
    }

    private async void OnExpandAll(object? sender, RoutedEventArgs e)
    {
        if (_expandCancellation != null)
        {
            _expandCancellation.Cancel();
            return;
        }
        if (!await ConfirmOperationAsync(UiStrings.Get("raw_json.expand_title"),
                UiStrings.Get("raw_json.expand_all_warning"))) return;

        var cancellation = _expandCancellation = new CancellationTokenSource();
        ExpandButton.Content = UiStrings.Get("raw_json.stop");
        int count = 0;
        try
        {
            JsonTreeNode? root = (_viewMode == ViewMode.Split ? _splitTreeRoots : _treeRoots).FirstOrDefault();
            if (root == null) return;
            var stack = new Stack<JsonTreeNode>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                JsonTreeNode node = stack.Pop();
                node.EnsureChildrenLoaded();
                node.IsExpanded = true;
                count++;
                for (int i = node.Children.Count - 1; i >= 0; i--)
                    if (node.Children[i].IsContainer) stack.Push(node.Children[i]);
                if (count % 200 == 0)
                {
                    SetStatus(UiStrings.Format("raw_json.expanding_count", count));
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                }
            }
            SetStatus(UiStrings.Format("raw_json.expanded_nodes", count));
        }
        catch (OperationCanceledException)
        {
            SetStatus(UiStrings.Format("raw_json.stopped_at", count));
        }
        finally
        {
            cancellation.Dispose();
            _expandCancellation = null;
            ExpandButton.Content = UiStrings.Get("raw_json.expand_all");
        }
    }

    private void OnCollapseAll(object? sender, RoutedEventArgs e)
    {
        IEnumerable<JsonTreeNode> roots = _viewMode == ViewMode.Split ? _splitTreeRoots : _treeRoots;
        foreach (JsonTreeNode root in roots)
        {
            Collapse(root);
            root.IsExpanded = true;
        }
    }

    private static void Collapse(JsonTreeNode node)
    {
        node.IsExpanded = false;
        foreach (JsonTreeNode child in node.Children.Where(child => !child.IsPlaceholder)) Collapse(child);
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) OnDeleteNode(sender, e);
        else if (e.Key == Key.F2) OnEditNode(sender, e);
        else if (e.Key == Key.F3 && e.KeyModifiers.HasFlag(KeyModifiers.Shift)) FindPrevious();
        else if (e.Key == Key.F3) FindNext();
        else if (e.KeyModifiers.HasFlag(PlatformCommandModifier) && e.Key == Key.C) _ = CopyValueAsync();
        else if (e.KeyModifiers.HasFlag(PlatformCommandModifier) && e.Key == Key.F) SearchBox.Focus();
        else if (e.KeyModifiers.HasFlag(PlatformCommandModifier) && e.Key == Key.Z &&
                 e.KeyModifiers.HasFlag(KeyModifiers.Shift)) OnRedo(sender, e);
        else if (e.KeyModifiers.HasFlag(PlatformCommandModifier) && e.Key == Key.Z) OnUndo(sender, e);
        else if (e.KeyModifiers.HasFlag(PlatformCommandModifier) && e.Key is Key.Y) OnRedo(sender, e);
        else if (e.KeyModifiers.HasFlag(PlatformCommandModifier) && e.Key == Key.Up) MoveSelected(-1);
        else if (e.KeyModifiers.HasFlag(PlatformCommandModifier) && e.Key == Key.Down) MoveSelected(1);
        else return;
        e.Handled = true;
    }

    private async void OnCopyKey(object? sender, RoutedEventArgs e)
    {
        if (SelectedNode?.Key is { } key) await SetClipboardTextAsync(key);
    }

    private async void OnCopyValue(object? sender, RoutedEventArgs e) => await CopyValueAsync();

    private async Task CopyValueAsync()
    {
        if (!PrepareTreeMutation()) return;
        if (SelectedNode is not { IsPlaceholder: false } node) return;
        await SetClipboardTextAsync(RawJsonLogic.SerializeValue(node.Value));
    }

    private async void OnCopyPath(object? sender, RoutedEventArgs e)
    {
        if (SelectedNode is { } node) await SetClipboardTextAsync(FormatPath(GetPath(node)));
    }

    private async Task SetClipboardTextAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
        else
            SetStatus(UiStrings.Get("common.error"), Brushes.OrangeRed);
    }

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Tree).Properties.IsLeftButtonPressed) return;
        _dragCandidate = FindNode(e.Source);
        if (_dragCandidate?.IsRoot != false) _dragCandidate = null;
        _dragStart = e.GetPosition(Tree);
        _pointerDragActive = false;
        if (_dragCandidate != null) e.Pointer.Capture(Tree);
    }

    private void OnTreePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragCandidate == null || !e.GetCurrentPoint(Tree).Properties.IsLeftButtonPressed) return;
        Point point = e.GetPosition(Tree);
        if (Math.Abs(point.X - _dragStart.X) + Math.Abs(point.Y - _dragStart.Y) > 8)
            _pointerDragActive = true;
    }

    private void OnTreePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        JsonTreeNode? dragged = _dragCandidate;
        _dragCandidate = null;
        if (!_pointerDragActive || dragged == null) return;
        _pointerDragActive = false;
        JsonTreeNode? target = FindNode(Tree.InputHitTest(e.GetPosition(Tree)));
        JsonTreeNode? parentNode = dragged.ParentNode;
        if (target == null || ReferenceEquals(target, dragged) || !ReferenceEquals(target.ParentNode, parentNode) ||
            parentNode == null || dragged.ParentContainer == null) return;
        int from = parentNode.Children.IndexOf(dragged);
        int to = parentNode.Children.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return;
        MoveWithinParent(dragged.ParentContainer, from, to);
        RecordAction(new JsonReorderAction(dragged.ParentContainer, from, to));
        string[] path = GetPath(dragged);
        if (dragged.ParentContainer is JsonArray && path.Length > 0) path[^1] = $"[{to}]";
        RebuildTreesPreservingPath(path);
        SetStatus(UiStrings.Format("raw_json.reordered", from, to), Brushes.DarkOrange);
    }

    private static JsonTreeNode? FindNode(object? source)
    {
        for (var element = source as StyledElement; element != null; element = element.Parent)
            if (element.DataContext is JsonTreeNode node && !node.IsPlaceholder) return node;
        return null;
    }

    private void RebuildTreesPreservingPath(string[] path)
    {
        JsonObject? data = ActiveData;
        if (data == null) return;
        BuildTree(_treeRoots, data);
        BuildTree(_splitTreeRoots, data);
        if (_viewMode == ViewMode.Split)
        {
            string display = RawJsonLogic.ToDisplayString(data);
            _suppressTextEvents = true;
            try
            {
                SplitTextEditor.Text = display;
                SplitSyntaxPreview.JsonText = display;
                _textDirty = false;
            }
            finally
            {
                _suppressTextEvents = false;
            }
        }
        if (path.Length > 0)
        {
            ObservableCollection<JsonTreeNode> roots = _viewMode == ViewMode.Split ? _splitTreeRoots : _treeRoots;
            JsonTreeNode? target = FindPath(roots.FirstOrDefault(), path);
            if (_viewMode == ViewMode.Split) SplitTree.SelectedItem = target;
            else Tree.SelectedItem = target;
            UpdateBreadcrumb(target);
        }
        UpdateCommandState();
    }

    private bool PrepareTreeMutation()
    {
        if (_viewMode != ViewMode.Split || !_textDirty) return true;
        string[] path = GetPath(SelectedNode);
        if (!TryCommitText(showErrors: true)) return false;
        JsonObject? data = ActiveData;
        if (data == null) return false;
        BuildTree(_splitTreeRoots, data);
        JsonTreeNode? selected = FindPath(_splitTreeRoots.FirstOrDefault(), path);
        SplitTree.SelectedItem = selected;
        UpdateBreadcrumb(selected);
        return true;
    }

    private static JsonTreeNode? FindPath(JsonTreeNode? root, IEnumerable<string> path)
    {
        JsonTreeNode? current = root;
        foreach (string segment in path)
        {
            if (current == null) return null;
            current.IsExpanded = true;
            current.EnsureChildrenLoaded();
            current = current.Children.FirstOrDefault(child => child.Key == segment);
        }
        return current;
    }

    private static string[] GetPath(JsonTreeNode? node)
    {
        var segments = new List<string>();
        for (JsonTreeNode? current = node; current?.ParentNode != null; current = current.ParentNode)
            if (current.Key != null) segments.Add(current.Key);
        segments.Reverse();
        return [.. segments];
    }

    private static string FormatPath(IEnumerable<string> path) => string.Concat(path.Select((segment, index) =>
        segment.StartsWith("[", StringComparison.Ordinal) ? segment : (index == 0 ? segment : "." + segment)));
}
