using Avalonia.Media;

namespace NMSE.UI.Avalonia.Services;

/// <summary>Provides the application-local NMS text and portal-glyph fonts.</summary>
public static class FontManager
{
    private const string NmsFamilyName = "NMS GeoSans";
    private const string GlyphFamilyName = "NMS Glyphs";

    private static readonly Lazy<FontFamily> NmsFontLazy = new(
        () => CreateFontFamily("NMSGeoSans_Kerned.ttf", NmsFamilyName),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<FontFamily> GlyphFontLazy = new(
        () => CreateFontFamily("NMS_Glyphs_Mono.ttf", GlyphFamilyName),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static FontFamily NmsFont => NmsFontLazy.Value;
    public static FontFamily GlyphFont => GlyphFontLazy.Value;

    public static Typeface CreateNmsTypeface(FontWeight? weight = null, FontStyle style = FontStyle.Normal) =>
        new(NmsFont, style, weight ?? FontWeight.Normal);

    public static Typeface CreateGlyphTypeface() => new(GlyphFont);

    private static FontFamily CreateFontFamily(string fileName, string familyName)
    {
        string assemblyName = typeof(FontManager).Assembly.GetName().Name ?? "NMSE";
        return new FontFamily($"avares://{assemblyName}/Resources/app/{fileName}#{familyName}");
    }
}
