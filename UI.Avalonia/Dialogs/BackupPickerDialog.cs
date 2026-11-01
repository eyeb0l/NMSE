using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using NMSE.Data;

namespace NMSE.UI.Avalonia.Dialogs;

/// <summary>Modal picker for choosing an exact backup ZIP to restore.</summary>
internal sealed class BackupPickerDialog : Window
{
    private sealed record BackupChoice(
        string Path,
        string Backup,
        string Created,
        string Size,
        string Location);

    private readonly DataGrid _backups;
    private readonly Button _acceptButton;

    private BackupPickerDialog(IReadOnlyList<string> zipPaths)
    {
        Title = UiStrings.Get("dialog.restore_picker_title");
        Width = 780;
        Height = 440;
        MinWidth = 620;
        MinHeight = 320;
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        List<BackupChoice> choices = zipPaths.Select(CreateChoice).ToList();
        _backups = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserResizeColumns = true,
            CanUserSortColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            IsReadOnly = true,
            ItemsSource = choices,
            SelectedIndex = choices.Count > 0 ? 0 : -1,
            SelectionMode = DataGridSelectionMode.Single,
        };
        _backups.Columns.Add(CreateTextColumn(
            "dialog.restore_col_backup",
            nameof(BackupChoice.Backup),
            new DataGridLength(2, DataGridLengthUnitType.Star)));
        _backups.Columns.Add(CreateTextColumn(
            "dialog.restore_col_created",
            nameof(BackupChoice.Created),
            new DataGridLength(155)));
        _backups.Columns.Add(CreateTextColumn(
            "dialog.restore_col_size",
            nameof(BackupChoice.Size),
            new DataGridLength(90)));
        _backups.Columns.Add(CreateTextColumn(
            "dialog.restore_col_location",
            nameof(BackupChoice.Location),
            new DataGridLength(2, DataGridLengthUnitType.Star)));
        _backups.SelectionChanged += (_, _) => UpdateAcceptState();
        _backups.DoubleTapped += (_, _) => Accept();

        _acceptButton = new Button
        {
            Content = UiStrings.Get("common.ok"),
            IsDefault = true,
            IsEnabled = choices.Count > 0,
            MinWidth = 92,
        };
        _acceptButton.Click += (_, _) => Accept();

        var cancelButton = new Button
        {
            Content = UiStrings.Get("common.cancel"),
            IsCancel = true,
            MinWidth = 92,
        };
        cancelButton.Click += (_, _) => Close(null);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancelButton, _acceptButton },
        };

        Content = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = UiStrings.Get("dialog.restore_picker_hint"),
                    TextWrapping = TextWrapping.Wrap,
                },
                _backups,
                buttons,
            },
        };
        Grid.SetRow(_backups, 1);
        Grid.SetRow(buttons, 2);
    }

    public static Task<string?> ShowAsync(Window owner, IReadOnlyList<string> zipPaths) =>
        new BackupPickerDialog(zipPaths).ShowDialog<string?>(owner);

    private static DataGridTextColumn CreateTextColumn(
        string headerKey,
        string property,
        DataGridLength width) =>
        new()
        {
            Header = UiStrings.Get(headerKey),
            Binding = new ReflectionBinding(property),
            Width = width,
        };

    private static BackupChoice CreateChoice(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return new BackupChoice(
                path,
                file.Name,
                file.CreationTime.ToString("g", CultureInfo.CurrentCulture),
                FormatSize(file.Length),
                file.DirectoryName ?? string.Empty);
        }
        catch
        {
            return new BackupChoice(
                path,
                System.IO.Path.GetFileName(path),
                string.Empty,
                string.Empty,
                System.IO.Path.GetDirectoryName(path) ?? string.Empty);
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }

    private void UpdateAcceptState() =>
        _acceptButton.IsEnabled = _backups.SelectedItem is BackupChoice;

    private void Accept()
    {
        if (_backups.SelectedItem is BackupChoice choice)
            Close(choice.Path);
    }
}
