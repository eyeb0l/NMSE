using Avalonia.Media;
using NMSE.Models;

namespace NMSE.UI.Avalonia.Panels.Companions;

public sealed record CompanionFileRequest(
    string Title,
    string SuggestedFileName,
    params string[] Extensions);

/// <summary>Desktop operations used by the companion editor.</summary>
public sealed record CompanionDesktopActions(
    Func<CompanionFileRequest, Task<string?>> PickOpenFileAsync,
    Func<CompanionFileRequest, Task<string?>> PickSaveFileAsync,
    Func<string, string, Task<bool>> ConfirmAsync,
    Func<string, string, Task> ShowInfoAsync,
    Func<string, string, Task> ShowErrorAsync,
    Func<string, Task> OpenExternalUrlAsync,
    Func<Color, Task<Color?>> PickColourAsync,
    Func<string, IReadOnlyList<string>, Task<int?>> SelectItemAsync);

/// <summary>Describes an egg item inserted into Exosuit cargo by the companion editor.</summary>
public sealed class CompanionExosuitCargoModifiedEventArgs(
    JsonObject cargoInventory,
    int eggSlotIndex,
    int x,
    int y) : EventArgs
{
    public JsonObject CargoInventory { get; } = cargoInventory;
    public int EggSlotIndex { get; } = eggSlotIndex;
    public string EggItemId { get; } = $"^EGG{eggSlotIndex + 1}";
    public int X { get; } = x;
    public int Y { get; } = y;
}
