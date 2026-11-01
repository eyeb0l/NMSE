namespace NMSE.UI.Avalonia.Panels.Fleet;

public enum FleetTransferKind
{
    Freighter,
    FreighterCargo,
    FreighterTechnology,
    Frigate,
    SquadronPilot
}

public sealed record FleetTransferRequest(
    FleetTransferKind Kind,
    string Title,
    string SuggestedFileName,
    IReadOnlyList<string> Extensions);

public sealed record FleetTransferContent(string DisplayName, string JsonText);

