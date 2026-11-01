using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using NMSE.Data;
using NMSE.UI.Avalonia.Controls;

namespace NMSE.UI.Avalonia.Inventory;

internal sealed class InventorySlotCell : Border
{
    private static readonly IBrush ActiveBackground = new SolidColorBrush(Color.Parse("#252B33"));
    private static readonly IBrush EmptyBackground = new SolidColorBrush(Color.Parse("#1C2026"));
    private static readonly IBrush DisabledBackground = new SolidColorBrush(Color.Parse("#14171B"));
    private static readonly IBrush SelectedBrush = new SolidColorBrush(Color.Parse("#55B7FF"));
    private static readonly IBrush NormalBorder = new SolidColorBrush(Color.Parse("#69727E"));
    private static readonly IBrush SuperchargedBrush = new SolidColorBrush(Color.Parse("#F6B73C"));

    private readonly Image _icon = new()
    {
        Width = 48,
        Height = 48,
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Image _classIcon = new()
    {
        Width = 20,
        Height = 20,
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
    };
    private readonly TextBlock _classText = new()
    {
        FontSize = 11,
        FontWeight = FontWeight.Bold,
        Foreground = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
    };
    private readonly TextBlock _element = new()
    {
        FontSize = 11,
        FontWeight = FontWeight.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        Foreground = Brushes.LightSkyBlue,
        Margin = new Thickness(1),
    };
    private readonly TextBlock _flags = new()
    {
        FontSize = 12,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(1),
    };
    private readonly MarqueeTextBlock _name = new()
    {
        FontSize = 11.5,
        FontWeight = FontWeight.SemiBold,
        Foreground = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Center,
        Height = 18,
        TextAlignment = TextAlignment.Center,
        PauseSeconds = 1.25,
    };
    private readonly TextBlock _amount = new()
    {
        FontSize = 9.5,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = 1,
        Foreground = Brushes.Gainsboro,
    };
    private readonly InventoryLevelMeter _meter = new();

    private bool _selected;

    public InventorySlotCell(InventoryCellState state)
    {
        State = state;
        Width = 84;
        Height = 120;
        CornerRadius = new CornerRadius(5);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(5);
        Focusable = true;
        ClipToBounds = true;
        UseLayoutRounding = true;

        var iconLayer = new Grid { Height = 54, ClipToBounds = true };
        iconLayer.Children.Add(_icon);
        iconLayer.Children.Add(_element);
        iconLayer.Children.Add(_classIcon);
        iconLayer.Children.Add(_classText);
        iconLayer.Children.Add(_flags);

        var content = new Grid
        {
            RowDefinitions = new RowDefinitions("54,18,16,6"),
            RowSpacing = 2,
            ClipToBounds = true,
            Children = { iconLayer, _name, _amount, _meter },
        };
        Grid.SetRow(_name, 1);
        Grid.SetRow(_amount, 2);
        Grid.SetRow(_meter, 3);
        Child = content;

        PointerEntered += (_, _) => Opacity = State.IsActive ? 0.9 : 0.7;
        PointerExited += (_, _) => Opacity = State.IsActive ? 1 : 0.55;
        Refresh();
    }

    public InventoryCellState State { get; }

    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;
            _selected = value;
            RefreshBorder();
        }
    }

    public void Refresh()
    {
        _icon.Source = State.Icon;
        _classIcon.Source = State.ClassIcon;
        _classIcon.IsVisible = State.ClassIcon is not null;
        _classText.Text = State.ClassIcon is null ? State.ItemClass : string.Empty;
        _classText.IsVisible = State.ClassIcon is null && State.ItemClass is not ("" or "NONE");
        _element.Text = State.ElementSymbol;
        _name.Text = State.HasItem ? State.Name : State.IsActive ? UiStrings.Get("inventory.empty_slot") : UiStrings.Get("inventory.disabled_slot");

        string damage = State.DamageFactor > 0 ? $" · ⚠{State.DamageFactor:P0}" : string.Empty;
        _amount.Text = State.HasItem ? $"{State.Amount:N0}/{State.MaxAmount:N0}{damage}" : $"({State.X}, {State.Y})";
        _flags.Text = string.Concat(State.IsSupercharged ? "⚡" : string.Empty, State.IsPinned ? "  🔒" : string.Empty);
        _flags.Foreground = State.IsSupercharged ? Brushes.Gold : Brushes.Gainsboro;

        double fraction = State.MaxAmount == 0 ? 0 : Math.Clamp((double)State.Amount / State.MaxAmount, 0, 1);
        _meter.Fraction = fraction;
        _meter.IsVisible = State.HasItem && (State.MaxAmount > 0 || State.DamageFactor > 0);
        _meter.Fill = State.DamageFactor > 0 ? Brushes.IndianRed : State.IsChargeable ? Brushes.DeepSkyBlue : Brushes.MediumSeaGreen;

        Background = !State.IsActive ? DisabledBackground : State.HasItem ? ActiveBackground : EmptyBackground;
        Opacity = State.IsActive ? 1 : 0.55;
        ToolTip.SetTip(this, BuildToolTip());
        RefreshBorder();
    }

    private string BuildToolTip()
    {
        if (!State.HasItem)
            return $"({State.X}, {State.Y})";
        string details = $"{State.Name}\n{State.ItemId}\n{State.ItemType} · {State.Category}";
        if (!string.IsNullOrWhiteSpace(State.Description))
            details += $"\n\n{State.Description}";
        return details;
    }

    private void RefreshBorder()
    {
        if (_selected)
        {
            BorderBrush = SelectedBrush;
            BorderThickness = new Thickness(2);
        }
        else if (State.HasAdjacency)
        {
            BorderBrush = new SolidColorBrush(State.AdjacencyColor);
            BorderThickness = new Thickness(2);
        }
        else if (State.IsSupercharged)
        {
            BorderBrush = SuperchargedBrush;
            BorderThickness = new Thickness(2);
        }
        else
        {
            BorderBrush = NormalBorder;
            BorderThickness = new Thickness(1);
        }
    }
}

/// <summary>A compact clipped meter whose fill cannot escape the inventory cell template.</summary>
internal sealed class InventoryLevelMeter : Control
{
    private static readonly IBrush Track = new SolidColorBrush(Color.Parse("#11151A"));
    private double _fraction;
    private IBrush _fill = Brushes.MediumSeaGreen;

    public InventoryLevelMeter()
    {
        Height = 6;
        MinHeight = 6;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Center;
        ClipToBounds = true;
    }

    public double Fraction
    {
        get => _fraction;
        set
        {
            double next = Math.Clamp(value, 0, 1);
            if (Math.Abs(_fraction - next) < double.Epsilon) return;
            _fraction = next;
            InvalidateVisual();
        }
    }

    public IBrush Fill
    {
        get => _fill;
        set
        {
            if (ReferenceEquals(_fill, value)) return;
            _fill = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Track, bounds);
        if (_fraction > 0)
            context.FillRectangle(_fill, new Rect(0, 0, Bounds.Width * _fraction, Bounds.Height));
    }
}
