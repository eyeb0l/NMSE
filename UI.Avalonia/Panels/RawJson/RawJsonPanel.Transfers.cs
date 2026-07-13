using Avalonia.Interactivity;
using Avalonia.Media;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.UI.Avalonia.Panels.RawJson;

public partial class RawJsonPanel
{
    private async void OnExportDocument(object? sender, RoutedEventArgs e)
    {
        JsonObject? data = ActiveData;
        if (data == null) return;
        if (_textDirty && !TryCommitText(showErrors: true)) return;
        if (ExportJsonAsync == null)
        {
            await ShowErrorAsync(UiStrings.Get("common.error"),
                UiStrings.Get("common.error"));
            return;
        }

        string fileName = $"{(_showingAccount ? "account" : "save")}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        var request = new JsonTransferRequest(JsonTransferScope.Document,
            UiStrings.Get("raw_json.export_title"), fileName);
        if (await ExportJsonAsync(request, RawJsonLogic.ToDisplayString(data), CancellationToken.None))
            SetStatus(UiStrings.Format("raw_json.exported", fileName), Brushes.Green);
    }

    private async void OnImportDocument(object? sender, RoutedEventArgs e)
    {
        if (ImportJsonAsync == null)
        {
            await ShowErrorAsync(UiStrings.Get("common.error"),
                UiStrings.Get("common.error"));
            return;
        }
        var request = new JsonTransferRequest(JsonTransferScope.Document,
            UiStrings.Get("raw_json.import_title"), "save.json");
        JsonTransferContent? content = await ImportJsonAsync(request, CancellationToken.None);
        if (content == null) return;
        try
        {
            JsonObject parsed = RawJsonLogic.ParseJson(content.JsonText);
            JsonObject? target = ActiveData;
            if (target == null)
            {
                target = parsed;
                if (_showingAccount) _accountData = target;
                else _saveData = target;
            }
            else
            {
                byte[] before = JsonRootAction.Compress(RawJsonLogic.ToDisplayString(target));
                byte[] after = JsonRootAction.Compress(RawJsonLogic.ToDisplayString(parsed));
                JsonRootAction.Replace(target, parsed);
                _undoStack.Clear();
                _redoStack.Clear();
                _undoStack.Push(new JsonRootAction(target, before, after));
            }
            _textDirty = false;
            ReloadCurrentView();
            RaiseDataModified();
            SetStatus(UiStrings.Format("raw_json.imported", content.DisplayName), Brushes.Green);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            await ShowErrorAsync(UiStrings.Get("raw_json.import_error_title"),
                UiStrings.Format("raw_json.import_error", ex.Message));
        }
    }

    private async void OnExportNode(object? sender, RoutedEventArgs e)
    {
        if (!PrepareTreeMutation()) return;
        JsonTreeNode? node = SelectedNode;
        if (node is not { IsPlaceholder: false }) return;
        if (ExportJsonAsync == null)
        {
            await ShowErrorAsync(UiStrings.Get("common.error"),
                UiStrings.Get("common.error"));
            return;
        }
        string safeName = (node.Key ?? "root").Trim('[', ']');
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "node";
        var request = new JsonTransferRequest(JsonTransferScope.SelectedNode,
            UiStrings.Get("raw_json.export_node_title"), safeName + ".json");
        if (await ExportJsonAsync(request, RawJsonLogic.SerializeValue(node.Value), CancellationToken.None))
            SetStatus(UiStrings.Format("raw_json.exported_node", request.SuggestedFileName), Brushes.Green);
    }

    private async void OnImportNode(object? sender, RoutedEventArgs e)
    {
        if (!PrepareTreeMutation()) return;
        JsonTreeNode? node = SelectedNode;
        if (node is not { IsRoot: false, IsPlaceholder: false } || node.ParentContainer == null || node.Key == null) return;
        object parentContainer = node.ParentContainer;
        string nodeKey = node.Key;
        if (ImportJsonAsync == null)
        {
            await ShowErrorAsync(UiStrings.Get("common.error"),
                UiStrings.Get("common.error"));
            return;
        }
        var request = new JsonTransferRequest(JsonTransferScope.SelectedNode,
            UiStrings.Get("raw_json.import_node_title"), nodeKey.Trim('[', ']') + ".json");
        JsonTransferContent? content = await ImportJsonAsync(request, CancellationToken.None);
        if (content == null) return;

        try
        {
            object? replacement = RawJsonLogic.ParseValue(content.JsonText);
            object? oldValue = node.Value;
            JsonValueAction.SetValue(parentContainer, nodeKey, replacement);
            RecordAction(new JsonValueAction(parentContainer, nodeKey, oldValue, replacement));
            RebuildTreesPreservingPath(GetPath(node));
            SetStatus(UiStrings.Format("raw_json.imported_node", content.DisplayName), Brushes.Green);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            await ShowErrorAsync(UiStrings.Get("raw_json.import_error_title"),
                UiStrings.Format("raw_json.import_error", ex.Message));
        }
    }

    private async void OnShowDiff(object? sender, RoutedEventArgs e)
    {
        JsonObject? data = ActiveData;
        byte[]? baseline = ActiveBaseline;
        if (data == null || baseline == null) return;
        if (_textDirty && !TryCommitText(showErrors: true)) return;
        if (OwnerWindow is not { } owner) return;

        SetStatus(UiStrings.Get("raw_json.diff_computing"));
        try
        {
            List<RawJsonLogic.DiffLine> lines = await Task.Run(() =>
                RawJsonLogic.ComputeCompactDiffFromLines(
                    JsonRootAction.ReadLines(baseline),
                    RawJsonLogic.ToLines(data)));
            if (lines.Count == 0)
            {
                SetStatus(UiStrings.Get("raw_json.diff_no_changes"));
                await JsonDialogs.ShowMessageAsync(owner, UiStrings.Get("raw_json.diff_title"),
                    UiStrings.Get("raw_json.diff_no_changes"));
                return;
            }
            SetStatus(string.Empty);
            await JsonDialogs.ShowDiffAsync(owner, lines);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(UiStrings.Get("raw_json.diff_title"),
                UiStrings.Format("raw_json.diff_error", ex.Message));
        }
    }
}
