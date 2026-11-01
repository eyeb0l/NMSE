using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Layout;

namespace NMSE.UI.Avalonia.Dialogs;

/// <summary>Small reusable modal list picker used by editors with dynamic choices.</summary>
public sealed class ChoiceDialogWindow : Window
{
    private readonly ListBox _choices;

    private ChoiceDialogWindow(string title, IReadOnlyList<string> choices)
    {
        Title = title;
        CanResize = false;
        MinWidth = 360;
        MaxHeight = 640;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _choices = new ListBox
        {
            ItemsSource = choices,
            MinHeight = Math.Min(420, Math.Max(120, choices.Count * 32)),
            MinWidth = 340,
            SelectedIndex = choices.Count > 0 ? 0 : -1,
        };
        _choices.DoubleTapped += (_, _) => Accept();

        var accept = new Button { Content = "OK", IsDefault = true, MinWidth = 88 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 88 };
        accept.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close(null);

        Content = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("*,Auto"),
            RowSpacing = 12,
            Children =
            {
                _choices,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, accept },
                    [Grid.RowProperty] = 1,
                },
            },
        };
    }

    private void Accept() => Close(_choices.SelectedIndex >= 0 ? _choices.SelectedIndex : null);

    public static Task<int?> ShowAsync(Window owner, string title, IReadOnlyList<string> choices) =>
        new ChoiceDialogWindow(title, choices).ShowDialog<int?>(owner);
}
