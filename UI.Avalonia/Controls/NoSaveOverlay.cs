using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using NMSE.Data;

namespace NMSE.UI.Avalonia.Controls;

/// <summary>Blocks a save-backed editor until a save session is available.</summary>
internal sealed class NoSaveOverlay : Border
{
    private readonly TextBlock _title;
    private readonly TextBlock _hint;

    public NoSaveOverlay()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Padding = new Thickness(24);

        _title = new TextBlock
        {
            FontSize = 24,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        _hint = new TextBlock
        {
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 520,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        Child = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 10,
            Children = { _title, _hint },
        };
        ActualThemeVariantChanged += (_, _) => ApplyThemeColors();
        ApplyThemeColors();
        RefreshLocalisation();
    }

    public void RefreshLocalisation()
    {
        _title.Text = UiStrings.Get("lock.title");
        _hint.Text = UiStrings.Get("lock.hint");
    }

    private void ApplyThemeColors()
    {
        bool dark = ActualThemeVariant == ThemeVariant.Dark;
        Background = new SolidColorBrush(dark
            ? Color.FromArgb(232, 28, 31, 36)
            : Color.FromArgb(238, 246, 248, 250));
        _title.Foreground = new SolidColorBrush(dark
            ? Color.Parse("#F0F3F6")
            : Color.Parse("#1F2328"));
        _hint.Foreground = new SolidColorBrush(dark
            ? Color.Parse("#B7C0CA")
            : Color.Parse("#57606A"));
    }
}
