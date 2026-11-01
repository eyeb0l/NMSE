using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace NMSE.UI.Avalonia.Dialogs;

/// <summary>Small, native-owner settings window for desktop preferences.</summary>
internal sealed class PreferencesWindow : Window
{
    private static readonly string[] ThemeChoices = ["System", "Light", "Dark"];
    private readonly ComboBox _themeSelector;

    public PreferencesWindow(string selectedTheme, Action<string> applyTheme)
    {
        ArgumentNullException.ThrowIfNull(applyTheme);

        Title = "NMSE Settings";
        Width = 460;
        Height = 235;
        MinWidth = 420;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _themeSelector = new ComboBox
        {
            ItemsSource = ThemeChoices,
            SelectedItem = ThemeChoices.FirstOrDefault(
                choice => string.Equals(choice, selectedTheme, StringComparison.OrdinalIgnoreCase)) ?? "System",
            MinWidth = 180,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var applyButton = new Button { Content = "Apply", IsDefault = true, MinWidth = 88 };
        var cancelButton = new Button { Content = "Cancel", IsCancel = true, MinWidth = 88 };
        applyButton.Click += (_, _) =>
        {
            applyTheme(_themeSelector.SelectedItem?.ToString() ?? "System");
            Close(true);
        };
        cancelButton.Click += (_, _) => Close(false);

        var form = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 16,
            RowSpacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = "Appearance:",
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                _themeSelector,
                new TextBlock
                {
                    Text = "System follows the current macOS, Windows, or Linux appearance automatically.",
                    Foreground = Brushes.Gray,
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        };
        Grid.SetColumn(_themeSelector, 1);
        Grid.SetRow(form.Children[2], 1);
        Grid.SetColumn(form.Children[2], 1);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancelButton, applyButton },
        };

        Content = new Grid
        {
            Margin = new Thickness(24),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children =
            {
                new TextBlock
                {
                    Text = "Appearance",
                    FontSize = 20,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 0, 0, 18),
                },
                form,
                buttons,
            },
        };
        Grid.SetRow(form, 1);
        Grid.SetRow(buttons, 2);
    }
}
