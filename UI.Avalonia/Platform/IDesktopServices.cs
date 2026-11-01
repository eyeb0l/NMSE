using global::Avalonia.Styling;

namespace NMSE.UI.Avalonia.Platform;

/// <summary>Narrow boundary for operations supplied by the desktop host.</summary>
public interface IDesktopServices
{
    Task<string?> PickFolderAsync(string title);
    Task<IReadOnlyList<string>> PickFilesAsync(string title, bool allowMultiple, params DesktopFileType[] types);
    Task<string?> PickSaveFileAsync(string title, string suggestedName, string? defaultExtension, params DesktopFileType[] types);
    Task<bool> ConfirmAsync(string title, string message);
    Task ShowErrorAsync(string title, string message);
    Task<string?> GetClipboardTextAsync();
    Task SetClipboardTextAsync(string text);
    Task OpenUrlAsync(Uri uri);
    ThemeVariant SystemTheme { get; }
    void Shutdown();
}

public sealed record DesktopFileType(string Name, params string[] Patterns);
