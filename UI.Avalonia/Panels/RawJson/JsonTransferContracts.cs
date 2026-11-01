namespace NMSE.UI.Avalonia.Panels.RawJson;

public enum JsonTransferScope
{
    Document,
    SelectedNode
}

/// <summary>A narrow shell request used to keep StorageProvider details out of the editor.</summary>
public sealed record JsonTransferRequest(
    JsonTransferScope Scope,
    string Title,
    string SuggestedFileName);

public sealed record JsonTransferContent(string DisplayName, string JsonText);
