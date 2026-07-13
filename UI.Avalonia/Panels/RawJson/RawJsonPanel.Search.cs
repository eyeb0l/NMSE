using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.UI.Avalonia.Panels.RawJson;

public partial class RawJsonPanel
{
    private void OnSearch(object? sender, RoutedEventArgs e) => Search();
    private void OnPreviousSearch(object? sender, RoutedEventArgs e) => FindPrevious();

    private void OnClearSearch(object? sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        _searchPaths.Clear();
        _searchIndex = -1;
        _lastSearch = string.Empty;
        SetStatus(string.Empty);
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Search();
            e.Handled = true;
        }
        else if (e.Key == Key.F3 && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            FindPrevious();
            e.Handled = true;
        }
        else if (e.Key == Key.F3)
        {
            FindNext();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            OnClearSearch(sender, e);
            e.Handled = true;
        }
    }

    private void Search()
    {
        string query = SearchBox.Text?.Trim() ?? string.Empty;
        if (query.Length == 0 || ActiveData == null) return;
        if (query.Equals(_lastSearch, StringComparison.OrdinalIgnoreCase) && _searchPaths.Count > 0)
        {
            FindNext();
            return;
        }

        if (_viewMode != ViewMode.Tree)
        {
            if (_textDirty && !TryCommitText(showErrors: true)) return;
            SetMode(ViewMode.Tree);
        }

        _lastSearch = query;
        _searchPaths.Clear();
        _searchIndex = -1;
        var path = new List<string>();
        SearchValue(ActiveData, query, path);
        if (_searchPaths.Count == 0)
        {
            SetStatus(UiStrings.Get("raw_json.no_matches_found"), Brushes.OrangeRed);
            return;
        }

        FindNext();
        SetStatus(UiStrings.Format("raw_json.search_found", _searchPaths.Count), Brushes.Green);
    }

    private void SearchValue(object? value, string query, List<string> path)
    {
        const int maxMatches = 500;
        if (_searchPaths.Count >= maxMatches) return;
        if (value is JsonObject obj)
        {
            foreach (string key in obj.Names())
            {
                if (_searchPaths.Count >= maxMatches) return;
                object? child = obj.Get(key);
                path.Add(key);
                bool matched = key.Contains(query, StringComparison.CurrentCultureIgnoreCase);
                if (!matched && child is not JsonObject and not JsonArray)
                    matched = JsonTreeNode.FormatValue(child).Contains(query, StringComparison.CurrentCultureIgnoreCase);
                if (matched) AddSearchPath(path);
                if (child is JsonObject or JsonArray) SearchValue(child, query, path);
                path.RemoveAt(path.Count - 1);
            }
        }
        else if (value is JsonArray array)
        {
            for (int i = 0; i < array.Length; i++)
            {
                if (_searchPaths.Count >= maxMatches) return;
                object? child = array.Get(i);
                path.Add($"[{i}]");
                if (child is JsonObject or JsonArray)
                    SearchValue(child, query, path);
                else if (JsonTreeNode.FormatValue(child).Contains(query, StringComparison.CurrentCultureIgnoreCase))
                    AddSearchPath(path);
                path.RemoveAt(path.Count - 1);
            }
        }
    }

    private void AddSearchPath(List<string> path)
    {
        if (_searchPaths.Count == 0 || !_searchPaths[^1].SequenceEqual(path))
            _searchPaths.Add([.. path]);
    }

    private void FindNext()
    {
        if (_searchPaths.Count == 0) return;
        _searchIndex = (_searchIndex + 1) % _searchPaths.Count;
        NavigateToPath(_searchPaths[_searchIndex]);
        SetStatus(UiStrings.Format("raw_json.match_position", _searchIndex + 1, _searchPaths.Count));
    }

    private void FindPrevious()
    {
        if (_searchPaths.Count == 0) return;
        _searchIndex = (_searchIndex - 1 + _searchPaths.Count) % _searchPaths.Count;
        NavigateToPath(_searchPaths[_searchIndex]);
        SetStatus(UiStrings.Format("raw_json.match_position", _searchIndex + 1, _searchPaths.Count));
    }

    /// <summary>Navigate from a dotted/bracketed Go To JSON path.</summary>
    public void NavigateToPath(string jsonPath) => NavigateToPath(ParsePath(jsonPath));

    /// <summary>Navigate from already-tokenized Go To JSON path segments.</summary>
    public void NavigateToPath(string[] pathSegments)
    {
        JsonObject? data = ActiveData;
        if (data == null || pathSegments.Length == 0) return;
        if (_viewMode != ViewMode.Tree)
        {
            if (_textDirty && !TryCommitText(showErrors: true)) return;
            SetMode(ViewMode.Tree);
        }

        pathSegments = ResolveTransformedPath(pathSegments, data);
        if (_treeRoots.FirstOrDefault() is not { } current) return;
        current.IsExpanded = true;
        foreach (string segment in pathSegments)
        {
            current.EnsureChildrenLoaded();
            JsonTreeNode? next = current.Children.FirstOrDefault(child => child.Key == segment);
            if (next == null)
                return;
            current.IsExpanded = true;
            current = next;
        }

        Tree.SelectedItem = current;
        current.IsExpanded = true;
        UpdateBreadcrumb(current);
    }

    private static string[] ParsePath(string path)
    {
        var segments = new List<string>();
        var text = new StringBuilder();
        for (int i = 0; i < path.Length; i++)
        {
            char ch = path[i];
            if (ch == '.')
            {
                if (text.Length > 0) { segments.Add(text.ToString()); text.Clear(); }
            }
            else if (ch == '[')
            {
                if (text.Length > 0) { segments.Add(text.ToString()); text.Clear(); }
                int end = path.IndexOf(']', i + 1);
                if (end < 0) { text.Append(path.AsSpan(i)); break; }
                segments.Add(path[i..(end + 1)]);
                i = end;
            }
            else text.Append(ch);
        }
        if (text.Length > 0) segments.Add(text.ToString());
        return [.. segments.Where(segment => !string.IsNullOrWhiteSpace(segment))];
    }

    private static string[] ResolveTransformedPath(string[] pathSegments, JsonObject data)
    {
        if (pathSegments.Length == 0 || data.Get(pathSegments[0]) != null) return pathSegments;
        string? prefix = FindTreePrefix(pathSegments[0], data);
        return prefix == null
            ? pathSegments
            : [.. prefix.Split('.'), .. pathSegments.Skip(1)];
    }

    private static string? FindTreePrefix(string key, JsonObject data)
    {
        string? activeContext = data.Get("ActiveContext") as string;
        if (key == "PlayerStateData")
        {
            if (data.GetValue("ExpeditionContext.PlayerStateData") != null &&
                (data.GetValue("BaseContext.PlayerStateData") == null ||
                 string.Equals(activeContext, "Season", StringComparison.Ordinal)))
                return "ExpeditionContext.PlayerStateData";
            if (data.GetValue("BaseContext.PlayerStateData") != null) return "BaseContext.PlayerStateData";
            if (data.Get("PlayerStateData") != null) return "PlayerStateData";
        }
        if (key == "SpawnStateData")
        {
            if (data.GetValue("ExpeditionContext.SpawnStateData") != null &&
                (data.GetValue("BaseContext.SpawnStateData") == null ||
                 string.Equals(activeContext, "Season", StringComparison.Ordinal)))
                return "ExpeditionContext.SpawnStateData";
            if (data.GetValue("BaseContext.SpawnStateData") != null) return "BaseContext.SpawnStateData";
            if (data.Get("SpawnStateData") != null) return "SpawnStateData";
        }
        return null;
    }

    private void OnFormat(object? sender, RoutedEventArgs e)
    {
        if (_viewMode == ViewMode.Tree) return;
        try
        {
            ActiveEditor.Text = RawJsonLogic.FormatJson(ActiveEditor.Text ?? string.Empty);
            SetStatus(UiStrings.Get("raw_json.formatted"), Brushes.Green);
        }
        catch (JsonException ex)
        {
            SetStatus(UiStrings.Format("raw_json.invalid_json", ex.Message), Brushes.OrangeRed);
        }
    }

    private void OnValidate(object? sender, RoutedEventArgs e)
    {
        if (_viewMode == ViewMode.Tree) return;
        try
        {
            RawJsonLogic.ParseJson(ActiveEditor.Text ?? string.Empty);
            SetStatus(UiStrings.Get("raw_json.json_valid"), Brushes.Green);
        }
        catch (JsonException ex)
        {
            SetStatus(UiStrings.Format("raw_json.invalid_json", ex.Message), Brushes.OrangeRed);
        }
    }
}
