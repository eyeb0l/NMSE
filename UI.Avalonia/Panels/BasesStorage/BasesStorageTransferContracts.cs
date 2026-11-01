using NMSE.Models;

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
    string JsonPath,
    JsonObject? BasePlayerState = null);

public sealed record BasesStorageTransferContent(string DisplayName, string JsonText, JsonObject? TerrainEditData = null, bool InvalidTerrain = false);

