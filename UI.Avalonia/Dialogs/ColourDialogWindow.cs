using System.Globalization;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Layout;
using global::Avalonia.Media;

namespace NMSE.UI.Avalonia.Dialogs;

/// <summary>Framework-native colour editor that avoids a platform-specific colour API.</summary>
public sealed class ColourDialogWindow : Window
{
    private readonly TextBox _hex;
    private readonly Border _preview;
    private readonly Button _accept;
    private Color? _selected;

    private ColourDialogWindow(Color initial)
    {
        Title = "Choose Colour";
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _hex = new TextBox { Text = Format(initial), Width = 170 };
        _preview = new Border
        {
            Width = 44,
            Height = 28,
            CornerRadius = new CornerRadius(4),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
        };
        _accept = new Button { Content = "OK", IsDefault = true, MinWidth = 88 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 88 };
        _accept.Click += (_, _) => Close(_selected);
        cancel.Click += (_, _) => Close(null);
        _hex.TextChanged += (_, _) => Validate();

        var editor = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { new TextBlock { Text = "Hex (RRGGBB or AARRGGBB):", VerticalAlignment = VerticalAlignment.Center }, _hex, _preview },
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, _accept },
        };
        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 14,
            Children = { editor, buttons },
        };
        Validate();
    }

    private void Validate()
    {
        _selected = TryParse(_hex.Text, out Color value) ? value : null;
        _accept.IsEnabled = _selected.HasValue;
        _preview.Background = new SolidColorBrush(_selected ?? Colors.Transparent);
    }

    private static string Format(Color color) => color.A == byte.MaxValue
        ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
        : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private static bool TryParse(string? text, out Color color)
    {
        string hex = (text ?? string.Empty).Trim().TrimStart('#');
        if (hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
        {
            color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            return true;
        }
        if (hex.Length == 8 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
        {
            color = Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            return true;
        }
        color = default;
        return false;
    }

    public static Task<Color?> ShowAsync(Window owner, Color initial) =>
        new ColourDialogWindow(initial).ShowDialog<Color?>(owner);
}
