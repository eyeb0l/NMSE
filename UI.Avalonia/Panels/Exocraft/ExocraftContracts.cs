using NMSE.Models;

namespace NMSE.UI.Avalonia.Panels.Exocraft;

public sealed record ExocraftFileRequest(string Title, string SuggestedFileName, params string[] Extensions);

public sealed record ExocraftDesktopActions(
    Func<ExocraftFileRequest, Task<string?>> PickOpenFileAsync,
    Func<ExocraftFileRequest, Task<string?>> PickSaveFileAsync,
    Func<string, string, Task<bool>> ConfirmAsync,
    Func<string, string, Task> ShowInfoAsync,
    Func<string, string, Task> ShowErrorAsync);

public sealed class ExocraftInventoryModifiedEventArgs(
    int vehicleIndex,
    string vehicleType,
    JsonObject inventory,
    bool isTechnology) : EventArgs
{
    public int VehicleIndex { get; } = vehicleIndex;
    public string VehicleType { get; } = vehicleType;
    public JsonObject Inventory { get; } = inventory;
    public bool IsTechnology { get; } = isTechnology;
}
