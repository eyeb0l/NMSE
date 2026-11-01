using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using NMSE.UI.Avalonia.Services;

namespace NMSE.UI.Avalonia.Controls;

/// <summary>Renders a portal code with the bundled NMS glyph font.</summary>
public sealed class GlyphRenderer : Control
{
    public static readonly StyledProperty<string> PortalCodeProperty =
        AvaloniaProperty.Register<GlyphRenderer, string>(nameof(PortalCode), string.Empty);

    public static readonly StyledProperty<double> GlyphSizeProperty =
        AvaloniaProperty.Register<GlyphRenderer, double>(nameof(GlyphSize), 22d, validate: value => value > 0);

    public static readonly StyledProperty<double> GlyphSpacingProperty =
        AvaloniaProperty.Register<GlyphRenderer, double>(nameof(GlyphSpacing), 0d, validate: value => value >= 0);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<GlyphRenderer, IBrush?>(nameof(Foreground), Brushes.White);

    public static readonly StyledProperty<IBrush?> GlyphBackgroundProperty =
        AvaloniaProperty.Register<GlyphRenderer, IBrush?>(nameof(GlyphBackground), new SolidColorBrush(Color.FromRgb(60, 60, 60)));

    static GlyphRenderer()
    {
        AffectsMeasure<GlyphRenderer>(PortalCodeProperty, GlyphSizeProperty, GlyphSpacingProperty);
        AffectsRender<GlyphRenderer>(
            PortalCodeProperty,
            GlyphSizeProperty,
            GlyphSpacingProperty,
            ForegroundProperty,
            GlyphBackgroundProperty);
    }

    public string PortalCode
    {
        get => GetValue(PortalCodeProperty);
        set => SetValue(PortalCodeProperty, value);
    }

    public double GlyphSize
    {
        get => GetValue(GlyphSizeProperty);
        set => SetValue(GlyphSizeProperty, value);
    }

    public double GlyphSpacing
    {
        get => GetValue(GlyphSpacingProperty);
        set => SetValue(GlyphSpacingProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public IBrush? GlyphBackground
    {
        get => GetValue(GlyphBackgroundProperty);
        set => SetValue(GlyphBackgroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int count = GetGlyphCharacters().Length;
        double width = count == 0 ? 0 : (count * GlyphSize) + ((count - 1) * GlyphSpacing);
        return new Size(width, count == 0 ? 0 : GlyphSize);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        string glyphs = GetGlyphCharacters();
        if (glyphs.Length == 0)
            return;

        IBrush foreground = Foreground ?? Brushes.White;
        Typeface typeface = Services.FontManager.CreateGlyphTypeface();
        // The glyph font has generous vertical metrics. A slightly larger face with
        // metric-aware centring is clearer than inheriting the surrounding black text
        // brush on the deliberately dark glyph cells.
        double fontSize = GlyphSize * 0.8;
        double x = 0;

        foreach (char character in glyphs)
        {
            var bounds = new Rect(x, 0, GlyphSize, GlyphSize);
            if (GlyphBackground is { } background)
                context.FillRectangle(background, bounds);

            var text = new FormattedText(
                character.ToString(),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                foreground);

            context.DrawText(text, new Point(
                x + Math.Max(0, (GlyphSize - text.Width) / 2),
                Math.Max(0, (GlyphSize - text.Height) / 2)));
            x += GlyphSize + GlyphSpacing;
        }
    }

    private string GetGlyphCharacters()
    {
        if (string.IsNullOrWhiteSpace(PortalCode))
            return string.Empty;

        return new string(PortalCode
            .Where(Uri.IsHexDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }
}
