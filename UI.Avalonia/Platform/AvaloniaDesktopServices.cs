using System.Diagnostics;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.ApplicationLifetimes;
using global::Avalonia.Input.Platform;
using global::Avalonia.Platform.Storage;
using global::Avalonia.Styling;
using NMSE.UI.Avalonia.Dialogs;

namespace NMSE.UI.Avalonia.Platform;

public sealed class AvaloniaDesktopServices(Window owner) : IDesktopServices
{
    public ThemeVariant SystemTheme => owner.ActualThemeVariant;

    public async Task<string?> PickFolderAsync(string title)
    {
        IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<IReadOnlyList<string>> PickFilesAsync(
        string title,
        bool allowMultiple,
        params DesktopFileType[] types)
    {
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = allowMultiple,
                FileTypeFilter = ToAvaloniaTypes(types),
            });
        return files.Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();
    }

    public async Task<string?> PickSaveFileAsync(
        string title,
        string suggestedName,
        string? defaultExtension,
        params DesktopFileType[] types)
    {
        IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedName,
                DefaultExtension = defaultExtension,
                FileTypeChoices = ToAvaloniaTypes(types),
            });
        return file?.TryGetLocalPath();
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = new MessageDialogWindow(title, message, showCancel: true);
        return await dialog.ShowDialog<bool>(owner);
    }

    public async Task ShowErrorAsync(string title, string message)
    {
        var dialog = new MessageDialogWindow(title, message, showCancel: false);
        await dialog.ShowDialog<bool>(owner);
    }

    public async Task<string?> GetClipboardTextAsync()
    {
        IClipboard? clipboard = owner.Clipboard;
        return clipboard is null ? null : await clipboard.TryGetTextAsync();
    }

    public async Task SetClipboardTextAsync(string text)
    {
        IClipboard? clipboard = owner.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(text);
    }

    public Task OpenUrlAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
        return Task.CompletedTask;
    }

    public void Shutdown()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
            owner.Close();
    }

    private static IReadOnlyList<FilePickerFileType> ToAvaloniaTypes(IEnumerable<DesktopFileType> types) =>
        types.Select(type => new FilePickerFileType(type.Name) { Patterns = type.Patterns }).ToArray();
}
