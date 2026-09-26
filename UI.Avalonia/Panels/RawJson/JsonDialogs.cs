using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using NMSE.Core;
using NMSE.Data;

namespace NMSE.UI.Avalonia.Panels.RawJson;

internal sealed record JsonEditResult(string? Key, string Value);

internal static class JsonDialogs
{
    internal static async Task ShowSnippetAsync(Window owner, string snippet, Action<string> apply)
    {
        var editor = new TextBox { Text = snippet, AcceptsReturn = true, AcceptsTab = true, FontFamily = FontFamily.Parse("monospace") };
        var error = new TextBlock { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
        var save = new Button { Content = UiStrings.Get("common.apply") };
        var cancel = new Button { Content = UiStrings.Get("common.cancel") };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, save } };
        var content = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("*,Auto,Auto"), RowSpacing = 8 };
        content.Children.Add(editor); content.Children.Add(error); Grid.SetRow(error, 1); content.Children.Add(buttons); Grid.SetRow(buttons, 2);
        var window = CreateWindow(UiStrings.Get("raw_json.isolate_node"), 760, 580, content);
        save.Click += (_, _) =>
        {
            try { apply(editor.Text ?? string.Empty); window.Close(); }
            catch (Exception ex) when (ex is NMSE.Models.JsonException or FormatException or ArgumentException) { error.Text = ex.Message; }
        };
        cancel.Click += (_, _) => window.Close();
        await window.ShowDialog(owner);
    }

    internal static async Task<JsonEditResult?> ShowEditAsync(
        Window owner,
        string title,
        string? key,
        string value,
        bool editKey)
    {
        var keyBox = new TextBox { Text = key ?? string.Empty, IsVisible = editKey };
        var valueBox = new TextBox { Text = value, AcceptsReturn = true, MinHeight = 70 };
        var okButton = new Button { Content = UiStrings.Get("common.ok"), MinWidth = 90 };
        var cancelButton = new Button { Content = UiStrings.Get("common.cancel"), MinWidth = 90 };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancelButton, okButton }
        };
        var content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = UiStrings.Get("raw_json.label_key"), IsVisible = editKey },
                keyBox,
                new TextBlock { Text = UiStrings.Get("raw_json.label_value") },
                valueBox,
                buttons
            }
        };
        var window = CreateWindow(title, 500, editKey ? 270 : 230, content);
        okButton.Click += (_, _) => window.Close(new JsonEditResult(editKey ? keyBox.Text : key, valueBox.Text ?? string.Empty));
        cancelButton.Click += (_, _) => window.Close(null);
        window.Opened += (_, _) =>
        {
            var focus = editKey ? keyBox : valueBox;
            focus.Focus();
            focus.SelectAll();
        };
        return await window.ShowDialog<JsonEditResult?>(owner);
    }

    internal static async Task<bool> ConfirmAsync(Window owner, string title, string message)
    {
        var yes = new Button { Content = UiStrings.Get("common.yes"), MinWidth = 90 };
        var no = new Button { Content = UiStrings.Get("common.no"), MinWidth = 90 };
        var content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { no, yes }
                }
            }
        };
        var window = CreateWindow(title, 460, 180, content);
        yes.Click += (_, _) => window.Close(true);
        no.Click += (_, _) => window.Close(false);
        return await window.ShowDialog<bool>(owner);
    }

    internal static async Task ShowMessageAsync(Window owner, string title, string message)
    {
        var close = new Button
        {
            Content = UiStrings.Get("common.close"),
            MinWidth = 90,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                close
            }
        };
        var window = CreateWindow(title, 500, 200, content);
        close.Click += (_, _) => window.Close();
        await window.ShowDialog(owner);
    }

    internal static async Task ShowDiffAsync(
        Window owner,
        IReadOnlyList<RawJsonLogic.DiffLine> lines)
    {
        var rows = lines.Select(DiffRow.From).ToArray();
        var list = new ListBox
        {
            ItemsSource = rows,
            ItemTemplate = new FuncDataTemplate<DiffRow>((row, _) =>
            {
                var prefix = new TextBlock
                {
                    Width = 112,
                    Text = row.Gutter,
                    FontFamily = new FontFamily("Menlo, Cascadia Mono, monospace")
                };
                var text = new TextBlock
                {
                    Text = row.Text,
                    FontFamily = prefix.FontFamily,
                    TextWrapping = TextWrapping.NoWrap
                };
                return new Border
                {
                    Padding = new Thickness(4, 1),
                    Background = row.Background,
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children = { prefix, text }
                    }
                };
            }, true)
        };
        var previous = new Button { Content = UiStrings.Get("raw_json.diff_prev_change") };
        var next = new Button { Content = UiStrings.Get("raw_json.diff_next_change") };
        var count = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Text = UiStrings.Format("raw_json.diff_change_count", rows.Count(row => row.IsChange))
        };
        var close = new Button { Content = UiStrings.Get("common.close"), MinWidth = 90 };
        int[] changeIndices = rows.Select((row, index) => (row, index))
            .Where(item => item.row.IsChange).Select(item => item.index).ToArray();
        int changeIndex = -1;
        void SelectChange(int direction)
        {
            if (changeIndices.Length == 0) return;
            changeIndex = (changeIndex + direction + changeIndices.Length) % changeIndices.Length;
            list.SelectedIndex = changeIndices[changeIndex];
            list.ScrollIntoView(rows[changeIndices[changeIndex]]);
            count.Text = UiStrings.Format("raw_json.diff_change_position", changeIndex + 1, changeIndices.Length);
        }
        previous.Click += (_, _) => SelectChange(-1);
        next.Click += (_, _) => SelectChange(1);

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { count, previous, next }
        };
        var root = new Grid
        {
            Margin = new Thickness(10),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 8
        };
        root.Children.Add(toolbar);
        Grid.SetRow(list, 1);
        root.Children.Add(list);
        Grid.SetRow(close, 2);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        root.Children.Add(close);

        var window = CreateWindow(UiStrings.Get("raw_json.diff_title"), 1000, 760, root);
        close.Click += (_, _) => window.Close();
        await window.ShowDialog(owner);
    }

    private static Window CreateWindow(string title, double width, double height, Control content) => new()
    {
        Title = title,
        Width = width,
        Height = height,
        MinWidth = Math.Min(width, 360),
        MinHeight = Math.Min(height, 150),
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Content = content
    };

    private sealed record DiffRow(string Gutter, string Text, IBrush Background, bool IsChange)
    {
        internal static DiffRow From(RawJsonLogic.DiffLine line)
        {
            return line.Type switch
            {
                RawJsonLogic.DiffLineType.Added => new(
                    $"      {line.NewLineNum,5} +", line.Text,
                    new SolidColorBrush(Color.FromArgb(60, 40, 160, 70)), true),
                RawJsonLogic.DiffLineType.Removed => new(
                    $"{line.OldLineNum,5}       -", line.Text,
                    new SolidColorBrush(Color.FromArgb(60, 190, 50, 50)), true),
                RawJsonLogic.DiffLineType.Header => new(
                    string.Empty, $"@@ {line.Text} @@",
                    new SolidColorBrush(Color.FromArgb(60, 70, 100, 190)), false),
                RawJsonLogic.DiffLineType.Separator => new(
                    string.Empty, "---", new SolidColorBrush(Color.FromArgb(35, 128, 128, 128)), false),
                _ => new($"{line.OldLineNum,5} {line.NewLineNum,5}  ", line.Text, Brushes.Transparent, false)
            };
        }
    }
}
