namespace NMSE.UI.Avalonia.Panels.BasesStorage;

public enum BasesStorageTransferKind
{
    Base,
    Chest,
    Storage
}

public sealed record BasesStorageTransferRequest(
    BasesStorageTransferKind Kind,
    string Title,
    string SuggestedFileName,
    IReadOnlyList<string> Extensions,
    string JsonPath);

public sealed record BasesStorageTransferContent(string DisplayName, string JsonText);

