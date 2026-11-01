namespace NMSE.UI.Avalonia.Panels.Catalogue;

public enum CatalogueTransferKind
{
    Technologies,
    Products,
    Specials,
    Words,
    Glyphs,
    Locations,
    Fish,
    Recipes
}

public sealed record CatalogueTransferRequest(
    CatalogueTransferKind Kind,
    string Title,
    string SuggestedFileName,
    IReadOnlyList<string> Extensions,
    string JsonPath);

public sealed record CatalogueTransferContent(string DisplayName, string JsonText);
