using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace NMSE.UI.Avalonia.Controls;

/// <summary>
/// Displays ordinary text until constrained below its desired width, then scrolls
/// continuously with a configurable pause and gap between repetitions.
/// </summary>
public sealed class MarqueeTextBlock : Control
{
    private const double TickIntervalMilliseconds = 16;

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<MarqueeTextBlock, string>(nameof(Text), string.Empty);

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextBlock.FontFamilyProperty.AddOwner<MarqueeTextBlock>();

    public static readonly StyledProperty<double> FontSizeProperty =
        TextBlock.FontSizeProperty.AddOwner<MarqueeTextBlock>();

    public static readonly StyledProperty<FontStyle> FontStyleProperty =
        TextBlock.FontStyleProperty.AddOwner<MarqueeTextBlock>();

    public static readonly StyledProperty<FontWeight> FontWeightProperty =
        TextBlock.FontWeightProperty.AddOwner<MarqueeTextBlock>();

    public static readonly StyledProperty<FontStretch> FontStretchProperty =
        TextBlock.FontStretchProperty.AddOwner<MarqueeTextBlock>();

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextBlock.ForegroundProperty.AddOwner<MarqueeTextBlock>();

    public static readonly StyledProperty<TextAlignment> TextAlignmentProperty =
        TextBlock.TextAlignmentProperty.AddOwner<MarqueeTextBlock>();

    public static readonly StyledProperty<double> ScrollSpeedProperty =
        AvaloniaProperty.Register<MarqueeTextBlock, double>(nameof(ScrollSpeed), 30d, validate: value => value > 0);

    public static readonly StyledProperty<double> PauseSecondsProperty =
        AvaloniaProperty.Register<MarqueeTextBlock, double>(nameof(PauseSeconds), 1.5d, validate: value => value >= 0);

    public static readonly StyledProperty<double> RepetitionGapProperty =
        AvaloniaProperty.Register<MarqueeTextBlock, double>(nameof(RepetitionGap), 40d, validate: value => value >= 0);

    private readonly Stopwatch _clock = new();
    private DispatcherTimer? _timer;
    private FormattedText? _formattedText;
    private double _offset;
    private double _pauseRemaining;
    private TimeSpan _lastTick;
    private bool _isAttached;

    static MarqueeTextBlock()
    {
        AffectsMeasure<MarqueeTextBlock>(
            TextProperty,
            FontFamilyProperty,
            FontSizeProperty,
            FontStyleProperty,
            FontWeightProperty,
            FontStretchProperty,
            TextAlignmentProperty);
        AffectsRender<MarqueeTextBlock>(
            TextProperty,
            FontFamilyProperty,
            FontSizeProperty,
            FontStyleProperty,
            FontWeightProperty,
            FontStretchProperty,
            ForegroundProperty,
            TextAlignmentProperty,
            RepetitionGapProperty);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontStyle FontStyle
    {
        get => GetValue(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public FontStretch FontStretch
    {
        get => GetValue(FontStretchProperty);
        set => SetValue(FontStretchProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public TextAlignment TextAlignment
    {
        get => GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    public double ScrollSpeed
    {
        get => GetValue(ScrollSpeedProperty);
        set => SetValue(ScrollSpeedProperty, value);
    }

    public double PauseSeconds
    {
        get => GetValue(PauseSecondsProperty);
        set => SetValue(PauseSecondsProperty, value);
    }

    public double RepetitionGap
    {
        get => GetValue(RepetitionGapProperty);
        set => SetValue(RepetitionGapProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty ||
            change.Property == FontFamilyProperty ||
            change.Property == FontSizeProperty ||
            change.Property == FontStyleProperty ||
            change.Property == FontWeightProperty ||
            change.Property == FontStretchProperty ||
            change.Property == ForegroundProperty ||
            change.Property == TextAlignmentProperty)
        {
            _formattedText = null;
            ResetAnimation();
        }
        else if (change.Property == PauseSecondsProperty || change.Property == RepetitionGapProperty)
        {
            ResetAnimation();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureFormattedText();
        if (_formattedText is null)
            return default;

        double width = double.IsInfinity(availableSize.Width)
            ? _formattedText.Width
            : Math.Min(_formattedText.Width, availableSize.Width);
        return new Size(width, _formattedText.Height);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ResetAnimation();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        UpdateTimerState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        StopTimer();
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        EnsureFormattedText();
        if (_formattedText is null || Bounds.Width <= 0)
            return;

        using (context.PushClip(new Rect(Bounds.Size)))
        {
            if (!NeedsScrolling)
            {
                double x = TextAlignment switch
                {
                    TextAlignment.Center => Math.Max(0, (Bounds.Width - _formattedText.Width) / 2),
                    TextAlignment.Right => Math.Max(0, Bounds.Width - _formattedText.Width),
                    _ => 0,
                };
                context.DrawText(_formattedText, new Point(x, 0));
                return;
            }

            double cycle = _formattedText.Width + RepetitionGap;
            for (double x = -_offset; x < Bounds.Width; x += cycle)
                context.DrawText(_formattedText, new Point(x, 0));
        }
    }

    private bool NeedsScrolling
    {
        get
        {
            EnsureFormattedText();
            return _formattedText is not null && Bounds.Width > 0 && _formattedText.Width > Bounds.Width;
        }
    }

    private void EnsureFormattedText()
    {
        _formattedText ??= new FormattedText(
            Text ?? string.Empty,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
            FontSize,
            Foreground ?? Brushes.Black);
    }

    private void ResetAnimation()
    {
        _offset = 0;
        _pauseRemaining = PauseSeconds;
        _lastTick = TimeSpan.Zero;
        InvalidateVisual();
        UpdateTimerState();
    }

    private void UpdateTimerState()
    {
        if (_isAttached && NeedsScrolling)
            StartTimer();
        else
            StopTimer();
    }

    private void StartTimer()
    {
        if (_timer is not null)
            return;

        _clock.Restart();
        _lastTick = _clock.Elapsed;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickIntervalMilliseconds) };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private void StopTimer()
    {
        if (_timer is null)
            return;

        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _timer = null;
        _clock.Stop();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!NeedsScrolling)
        {
            StopTimer();
            return;
        }

        TimeSpan now = _clock.Elapsed;
        double elapsed = Math.Min(0.1, Math.Max(0, (now - _lastTick).TotalSeconds));
        _lastTick = now;

        if (_pauseRemaining > 0)
        {
            _pauseRemaining = Math.Max(0, _pauseRemaining - elapsed);
            return;
        }

        _offset += ScrollSpeed * elapsed;
        double cycle = (_formattedText?.Width ?? 0) + RepetitionGap;
        if (cycle > 0 && _offset >= cycle)
        {
            _offset %= cycle;
            _pauseRemaining = PauseSeconds;
        }

        InvalidateVisual();
    }
}
