using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Contracts;
using NMSE.UI.Avalonia.Services;

namespace NMSE.UI.Avalonia.Panels.ByteBeats;

public sealed record ByteBeatDesktopActions(
    Func<ByteBeatFileRequest, Task<string?>> PickOpenFileAsync,
    Func<ByteBeatFileRequest, Task<string?>> PickSaveFileAsync,
    Func<string, string, Task<bool>> ConfirmAsync,
    Func<string, string, Task> ShowInfoAsync,
    Func<string, string, Task> ShowErrorAsync);

public sealed record ByteBeatFileRequest(string Title, string SuggestedFileName, params string[] Extensions);

/// <summary>Edits the ByteBeat song library, including song import/export and library playback settings.</summary>
public sealed class ByteBeatPanel : UserControl, ISaveEditorPanel, IGoToJsonSource
{
    private readonly ByteBeatDesktopActions _desktop;
    private readonly ObservableCollection<string> _songNames = [];
    private readonly ListBox _songList;
    private readonly Grid _detailPanel;
    private readonly TextBlock _titleLabel;
    private readonly TextBlock _infoLabel;
    private readonly Button _exportButton;
    private readonly Button _importButton;
    private readonly Button _deleteButton;
    private readonly Button _goToJsonButton;
    private readonly TextBlock _detailsHeader;
    private readonly TextBlock _nameLabel;
    private readonly TextBlock _authorUsernameLabel;
    private readonly TextBlock _authorOnlineIdLabel;
    private readonly TextBlock _authorPlatformLabel;
    private readonly TextBlock _dataHeader;
    private readonly TextBlock[] _dataLabels = new TextBlock[8];
    private readonly TextBlock _libraryHeader;
    private readonly TextBox _nameField;
    private readonly TextBox _authorUsernameField;
    private readonly TextBox _authorOnlineIdField;
    private readonly TextBox _authorPlatformField;
    private readonly TextBox[] _dataFields = new TextBox[8];
    private readonly CheckBox _shuffleField = new();
    private readonly CheckBox _autoplayOnFootField = new();
    private readonly CheckBox _autoplayInShipField = new();
    private readonly CheckBox _autoplayInVehicleField = new();
    private JsonObject? _library;
    private JsonArray? _mySongs;
    private bool _loading;
    private bool _handlingSongSelection;
    private bool _updatingSongName;
    private int _previousSongIndex = -1;

    public ByteBeatPanel(ByteBeatDesktopActions desktop)
    {
        _desktop = desktop ?? throw new ArgumentNullException(nameof(desktop));
        _titleLabel = new TextBlock
        {
            FontFamily = Services.FontManager.NmsFont,
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _goToJsonButton = new Button { Content = "\U0001F4D1", Width = 32, Height = 28 };
        _goToJsonButton.Click += (_, _) => GoToJsonRequested?.Invoke(
            this, new GoToJsonRequestEventArgs("CommonStateData.ByteBeatLibrary"));
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { _titleLabel, _goToJsonButton } };
        Grid.SetColumn(_goToJsonButton, 1);

        _songList = new ListBox { ItemsSource = _songNames, MinWidth = 220 };
        _songList.SelectionChanged += OnSongSelected;
        _exportButton = new Button();
        _exportButton.Click += OnExport;
        _importButton = new Button();
        _importButton.Click += OnImport;
        _deleteButton = new Button();
        _deleteButton.Click += OnDeleteSong;
        _infoLabel = new TextBlock { TextWrapping = TextWrapping.Wrap };

        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _exportButton, _importButton, _deleteButton, _infoLabel },
        };
        var left = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            RowSpacing = 6,
            Children = { _songList, actions },
        };
        Grid.SetRow(actions, 1);

        _nameField = CreateTextField();
        _authorUsernameField = CreateTextField();
        _authorOnlineIdField = CreateTextField();
        _authorPlatformField = CreateTextField();
        _detailsHeader = CreateSectionHeader();
        _nameLabel = CreateLabel();
        _authorUsernameLabel = CreateLabel();
        _authorOnlineIdLabel = CreateLabel();
        _authorPlatformLabel = CreateLabel();
        _dataHeader = CreateSectionHeader();
        _libraryHeader = CreateSectionHeader();

        var details = new StackPanel { Spacing = 6 };
        details.Children.Add(_detailsHeader);
        AddFieldRow(details, _nameLabel, _nameField);
        AddFieldRow(details, _authorUsernameLabel, _authorUsernameField);
        AddFieldRow(details, _authorOnlineIdLabel, _authorOnlineIdField);
        AddFieldRow(details, _authorPlatformLabel, _authorPlatformField);
        details.Children.Add(_dataHeader);
        for (int index = 0; index < 8; index++)
        {
            _dataLabels[index] = CreateLabel();
            _dataFields[index] = CreateTextField();
            AddFieldRow(details, _dataLabels[index], _dataFields[index]);
        }
        details.Children.Add(_libraryHeader);
        details.Children.Add(_shuffleField);
        details.Children.Add(_autoplayOnFootField);
        details.Children.Add(_autoplayInShipField);
        details.Children.Add(_autoplayInVehicleField);

        foreach (CheckBox checkBox in new[]
                 { _shuffleField, _autoplayOnFootField, _autoplayInShipField, _autoplayInVehicleField })
            checkBox.IsCheckedChanged += (_, _) => RaiseDataModified();

        _detailPanel = new Grid
        {
            IsVisible = false,
            Children =
            {
                new ScrollViewer
                {
                    Padding = new Thickness(12, 0, 0, 0),
                    VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    Content = details,
                },
            },
        };

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("240,*"),
            ColumnSpacing = 8,
            Children = { left, _detailPanel },
        };
        Grid.SetColumn(_detailPanel, 1);

        Content = new Grid
        {
            Margin = new Thickness(10),
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 8,
            Children = { header, content },
        };
        Grid.SetRow(content, 1);
        ApplyUiLocalisation();
    }

    public event EventHandler? DataModified;
    public event EventHandler<GoToJsonRequestEventArgs>? GoToJsonRequested;

    public void LoadData(JsonObject saveData)
    {
        ArgumentNullException.ThrowIfNull(saveData);
        _loading = true;
        try
        {
            _songNames.Clear();
            _detailPanel.IsVisible = false;
            _library = saveData.GetObject("CommonStateData")?.GetObject("ByteBeatLibrary");
            _mySongs = _library?.GetArray("MySongs");
            _previousSongIndex = -1;

            if (_library is null)
            {
                _infoLabel.Text = UiStrings.Get("bytebeat.no_library");
                return;
            }
            if (_mySongs is null || _mySongs.Length == 0)
            {
                _infoLabel.Text = UiStrings.Get("bytebeat.no_songs_found");
                LoadLibrarySettings();
                return;
            }

            RefreshList(-1);
            LoadLibrarySettings();
            _songList.SelectedIndex = 0;
        }
        catch
        {
            _infoLabel.Text = UiStrings.Get("bytebeat.failed_load");
        }
        finally
        {
            _loading = false;
        }
    }

    public void SaveData(JsonObject saveData)
    {
        if (_library is null)
            return;
        SaveSongAtIndex(_songList.SelectedIndex);
        _library.Set("Shuffle", _shuffleField.IsChecked == true);
        _library.Set("AutoplayOnFoot", _autoplayOnFootField.IsChecked == true);
        _library.Set("AutoplayInShip", _autoplayInShipField.IsChecked == true);
        _library.Set("AutoplayInVehicle", _autoplayInVehicleField.IsChecked == true);
    }

    public void ApplyUiLocalisation()
    {
        _titleLabel.Text = UiStrings.Get("bytebeat.title");
        _exportButton.Content = UiStrings.Get("common.export");
        _importButton.Content = UiStrings.Get("common.import");
        _deleteButton.Content = UiStrings.Get("common.delete");
        if (_mySongs is null)
            _infoLabel.Text = UiStrings.Get("bytebeat.no_songs");
        _shuffleField.Content = UiStrings.Get("bytebeat.shuffle");
        _autoplayOnFootField.Content = UiStrings.Get("bytebeat.autoplay_foot");
        _autoplayInShipField.Content = UiStrings.Get("bytebeat.autoplay_ship");
        _autoplayInVehicleField.Content = UiStrings.Get("bytebeat.autoplay_vehicle");
        _detailsHeader.Text = UiStrings.Get("bytebeat.section_details");
        _nameLabel.Text = UiStrings.Get("bytebeat.name");
        _authorUsernameLabel.Text = UiStrings.Get("bytebeat.author_username");
        _authorOnlineIdLabel.Text = UiStrings.Get("bytebeat.author_online_id");
        _authorPlatformLabel.Text = UiStrings.Get("bytebeat.author_platform");
        _dataHeader.Text = UiStrings.Get("bytebeat.section_data");
        for (int index = 0; index < _dataLabels.Length; index++)
            _dataLabels[index].Text = string.Format(CultureInfo.CurrentCulture, UiStrings.Get("bytebeat.data_channel"), index);
        _libraryHeader.Text = UiStrings.Get("bytebeat.section_library");
        ToolTip.SetTip(_goToJsonButton, UiStrings.Get("goto_json.tooltip"));

        if (_mySongs is not null)
            RefreshList(_songList.SelectedIndex);
    }

    private TextBox CreateTextField()
    {
        var field = new TextBox();
        field.LostFocus += (_, _) => RaiseDataModified();
        field.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                RaiseDataModified();
                Focus();
                args.Handled = true;
            }
        };
        return field;
    }

    private static TextBlock CreateLabel() => new() { VerticalAlignment = VerticalAlignment.Center };
    private static TextBlock CreateSectionHeader() => new()
    {
        FontFamily = Services.FontManager.NmsFont,
        FontSize = 15,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 7, 0, 2),
    };

    private static void AddFieldRow(Panel panel, Control label, Control field)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*"), ColumnSpacing = 8 };
        row.Children.Add(label);
        row.Children.Add(field);
        Grid.SetColumn(field, 1);
        panel.Children.Add(row);
    }

    private void LoadLibrarySettings()
    {
        _shuffleField.IsChecked = ReadBool(_library!, "Shuffle");
        _autoplayOnFootField.IsChecked = ReadBool(_library!, "AutoplayOnFoot");
        _autoplayInShipField.IsChecked = ReadBool(_library!, "AutoplayInShip");
        _autoplayInVehicleField.IsChecked = ReadBool(_library!, "AutoplayInVehicle");
    }

    private static bool ReadBool(JsonObject value, string name)
    {
        try { return value.GetBool(name); }
        catch { return false; }
    }

    private void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        // Replacing a selected display name makes Avalonia temporarily update the
        // ListBox selection. Do not recursively save the song while that source
        // notification is being handled.
        if (_updatingSongName)
            return;

        _handlingSongSelection = true;
        try
        {
            LoadSelectedSong(savePrevious: true);
        }
        finally
        {
            _handlingSongSelection = false;
        }
    }

    private void LoadSelectedSong(bool savePrevious)
    {
        if (savePrevious && _previousSongIndex >= 0)
            SaveSongAtIndex(_previousSongIndex);
        JsonObject? song = SelectedSong();
        if (song is null)
        {
            _detailPanel.IsVisible = false;
            _previousSongIndex = -1;
            return;
        }

        _loading = true;
        try
        {
            _detailPanel.IsVisible = true;
            _nameField.Text = song.GetString("Name") ?? string.Empty;
            _authorUsernameField.Text = song.GetString("AuthorUsername") ?? string.Empty;
            _authorOnlineIdField.Text = song.GetString("AuthorOnlineID") ?? string.Empty;
            _authorPlatformField.Text = song.GetString("AuthorPlatform") ?? string.Empty;
            JsonArray? data = song.GetArray("Data");
            for (int index = 0; index < 8; index++)
                _dataFields[index].Text = data is not null && index < data.Length
                    ? data.GetString(index) ?? string.Empty
                    : string.Empty;
        }
        finally
        {
            _loading = false;
        }
        _previousSongIndex = _songList.SelectedIndex;
    }

    private JsonObject? SelectedSong()
    {
        int index = _songList.SelectedIndex;
        return index >= 0 && _mySongs is not null && index < _mySongs.Length
            ? _mySongs.GetObject(index)
            : null;
    }

    private void SaveSongAtIndex(int index)
    {
        if (_loading || index < 0 || _mySongs is null || index >= _mySongs.Length)
            return;
        JsonObject song = _mySongs.GetObject(index);
        song.Set("Name", _nameField.Text ?? string.Empty);
        song.Set("AuthorUsername", _authorUsernameField.Text ?? string.Empty);
        song.Set("AuthorOnlineID", _authorOnlineIdField.Text ?? string.Empty);
        song.Set("AuthorPlatform", _authorPlatformField.Text ?? string.Empty);
        JsonArray? data = song.GetArray("Data");
        if (data is not null)
        {
            for (int channel = 0; channel < 8 && channel < data.Length; channel++)
                data.Set(channel, _dataFields[channel].Text ?? string.Empty);
        }

        string name = _nameField.Text ?? string.Empty;
        string displayName = string.IsNullOrWhiteSpace(name)
            ? UiStrings.Format("bytebeat.song_format", index + 1)
            : name;
        UpdateSongName(index, displayName, song);
    }

    private void UpdateSongName(int index, string displayName, JsonObject song)
    {
        if (index < 0 || index >= _songNames.Count ||
            string.Equals(_songNames[index], displayName, StringComparison.Ordinal))
            return;

        if (_handlingSongSelection)
        {
            JsonArray? songs = _mySongs;
            Dispatcher.UIThread.Post(() =>
            {
                if (!ReferenceEquals(_mySongs, songs) || songs is null || index >= songs.Length ||
                    !ReferenceEquals(songs.GetObject(index), song))
                    return;
                SetSongName(index, displayName);
            }, DispatcherPriority.Background);
            return;
        }

        SetSongName(index, displayName);
    }

    private void SetSongName(int index, string displayName)
    {
        if (index < 0 || index >= _songNames.Count ||
            string.Equals(_songNames[index], displayName, StringComparison.Ordinal))
            return;

        _updatingSongName = true;
        try
        {
            _songNames[index] = displayName;
        }
        finally
        {
            _updatingSongName = false;
        }
    }

    private void RefreshList(int selectedIndex)
    {
        _songNames.Clear();
        if (_mySongs is null)
            return;
        for (int index = 0; index < _mySongs.Length; index++)
        {
            string name = _mySongs.GetObject(index).GetString("Name") ?? string.Empty;
            _songNames.Add(string.IsNullOrWhiteSpace(name)
                ? UiStrings.Format("bytebeat.song_format", index + 1)
                : name);
        }
        _infoLabel.Text = UiStrings.Format("bytebeat.total_songs", _mySongs.Length);
        if (selectedIndex >= 0 && selectedIndex < _songNames.Count)
            _songList.SelectedIndex = selectedIndex;
    }

    private async void OnExport(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        JsonObject? song = SelectedSong();
        if (song is null)
        {
            await _desktop.ShowInfoAsync(UiStrings.Get("common.export"), UiStrings.Get("bytebeat.no_song_selected"));
            return;
        }
        SaveSongAtIndex(_songList.SelectedIndex);
        ExportConfig config = ExportConfig.Instance;
        string fileName = ExportConfig.BuildFileName(config.ByteBeatTemplate, config.ByteBeatExt, new()
        {
            ["name"] = song.GetString("Name") ?? "song",
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
        });
        string? path = await _desktop.PickSaveFileAsync(new(
            UiStrings.Get("common.export"), fileName, config.ByteBeatExt, ".json"));
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            song.ExportToFile(path);
            await _desktop.ShowInfoAsync(UiStrings.Get("common.export"), UiStrings.Get("bytebeat.export_success"));
        }
        catch (Exception ex)
        {
            await _desktop.ShowErrorAsync(UiStrings.Get("common.error"), UiStrings.Format("common.export_failed", ex.Message));
        }
    }

    private async void OnImport(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        int index = _songList.SelectedIndex;
        if (index < 0 || _mySongs is null || index >= _mySongs.Length)
        {
            await _desktop.ShowInfoAsync(UiStrings.Get("common.import"), UiStrings.Get("bytebeat.no_song_selected"));
            return;
        }
        ExportConfig config = ExportConfig.Instance;
        string? path = await _desktop.PickOpenFileAsync(new(
            UiStrings.Get("common.import"), string.Empty, config.ByteBeatExt, ".json"));
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            JsonObject imported = JsonObject.ImportFromFile(path);
            JsonObject song = _mySongs.GetObject(index);
            foreach (string property in imported.Names())
                song.Set(property, imported.Get(property));
            _previousSongIndex = -1;
            LoadSelectedSong(savePrevious: false);
            RefreshList(index);
            RaiseDataModified();
            await _desktop.ShowInfoAsync(UiStrings.Get("common.import"), UiStrings.Get("bytebeat.import_success"));
        }
        catch (Exception ex)
        {
            await _desktop.ShowErrorAsync(UiStrings.Get("common.error"), UiStrings.Format("common.import_failed", ex.Message));
        }
    }

    private async void OnDeleteSong(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        int index = _songList.SelectedIndex;
        if (index < 0 || _mySongs is null || index >= _mySongs.Length)
        {
            await _desktop.ShowInfoAsync(UiStrings.Get("common.delete"), UiStrings.Get("bytebeat.no_song_selected"));
            return;
        }
        if (!await _desktop.ConfirmAsync(UiStrings.Get("bytebeat.delete_title"), UiStrings.Get("bytebeat.delete_confirm")))
            return;
        try
        {
            JsonObject song = _mySongs.GetObject(index);
            song.Set("Name", string.Empty);
            song.Set("AuthorUsername", string.Empty);
            song.Set("AuthorOnlineID", string.Empty);
            song.Set("AuthorPlatform", string.Empty);
            JsonArray? data = song.GetArray("Data");
            if (data is not null)
            {
                for (int channel = 0; channel < data.Length; channel++)
                    data.Set(channel, string.Empty);
            }
            _previousSongIndex = -1;
            LoadSelectedSong(savePrevious: false);
            RefreshList(index);
            RaiseDataModified();
        }
        catch (Exception ex)
        {
            await _desktop.ShowErrorAsync(UiStrings.Get("common.error"), UiStrings.Format("bytebeat.delete_failed", ex.Message));
        }
    }

    private void RaiseDataModified()
    {
        if (!_loading)
            DataModified?.Invoke(this, EventArgs.Empty);
    }
}
