using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.UI.Avalonia.Panels.Catalogue;

public sealed class CatalogueItemRow
{
    public Bitmap? Icon { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Id { get; init; }
}

public sealed class CatalogueWordRow : INotifyPropertyChanged
{
    private readonly Action<CatalogueWordRow, int, bool>? _changed;
    private bool _gek;
    private bool _vykeen;
    private bool _korvax;
    private bool _atlas;
    private bool _autophage;

    public CatalogueWordRow(WordEntry word, bool[] known, Action<CatalogueWordRow, int, bool>? changed)
    {
        Word = word;
        Text = word.Text;
        Id = word.Id;
        _gek = known[0];
        _vykeen = known[1];
        _korvax = known[2];
        _atlas = known[3];
        _autophage = known[4];
        _changed = changed;
    }

    public WordEntry Word { get; }
    public string Text { get; }
    public string Id { get; }
    public bool HasGek => Word.HasRace(0);
    public bool HasVykeen => Word.HasRace(1);
    public bool HasKorvax => Word.HasRace(2);
    public bool HasAtlas => Word.HasRace(4);
    public bool HasAutophage => Word.HasRace(8);

    public bool Gek { get => _gek; set => Set(ref _gek, value, 0); }
    public bool Vykeen { get => _vykeen; set => Set(ref _vykeen, value, 1); }
    public bool Korvax { get => _korvax; set => Set(ref _korvax, value, 2); }
    public bool Atlas { get => _atlas; set => Set(ref _atlas, value, 4); }
    public bool Autophage { get => _autophage; set => Set(ref _autophage, value, 8); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetKnown(int race, bool value, bool notifyBackingData)
    {
        switch (race)
        {
            case 0: Set(ref _gek, value, race, notifyBackingData); break;
            case 1: Set(ref _vykeen, value, race, notifyBackingData); break;
            case 2: Set(ref _korvax, value, race, notifyBackingData); break;
            case 4: Set(ref _atlas, value, race, notifyBackingData); break;
            case 8: Set(ref _autophage, value, race, notifyBackingData); break;
        }
    }

    public bool HasRace(int race) => Word.HasRace(race);

    private void Set(ref bool field, bool value, int race, bool notifyBackingData = true, [CallerMemberName] string? propertyName = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (notifyBackingData) _changed?.Invoke(this, race, value);
    }
}

public sealed class CatalogueLocationRow
{
    public int Index { get; set; }
    public string Name { get; init; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string RawType { get; init; } = string.Empty;
    public string Galaxy { get; init; } = string.Empty;
    public int RealityIndex { get; init; }
    public string GalaxyCore { get; init; } = string.Empty;
    public string PortalCode { get; init; } = string.Empty;
    public string PortalCodeDecimal { get; init; } = string.Empty;
    public string SignalBooster { get; init; } = string.Empty;
}

public sealed class CatalogueFishRow : INotifyPropertyChanged
{
    private string _count = "0";
    private string _largestCatch = "0";

    public Bitmap? Icon { get; init; }
    public required string ProductId { get; init; }
    public required string Name { get; init; }
    public int SlotIndex { get; init; }
    public string OriginalLargestCatch { get; set; } = "0";

    public string Count
    {
        get => _count;
        set
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return;
            Set(ref _count, value);
        }
    }

    public string LargestCatch
    {
        get => _largestCatch;
        set
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return;
            Set(ref _largestCatch, value);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void LoadValues(string count, string largestCatch)
    {
        _count = count;
        _largestCatch = largestCatch;
        OriginalLargestCatch = largestCatch;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LargestCatch)));
    }

    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class CatalogueRecipeRow
{
    public Bitmap? Icon { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public Bitmap? ResultIcon { get; init; }
    public required string Result { get; init; }
    public required string Ingredients { get; init; }
    public required string Id { get; init; }
    public Recipe? Recipe { get; init; }
}

public sealed class RecipeBrowserRow
{
    public required string Ingredients { get; init; }
    public string Arrow => "▶";
    public required string Result { get; init; }
    public int Time { get; init; }
    public required string Type { get; init; }
    public required Recipe Recipe { get; init; }
}
