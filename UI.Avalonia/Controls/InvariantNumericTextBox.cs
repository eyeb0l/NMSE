using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using NMSE.Core.Utilities;

namespace NMSE.UI.Avalonia.Controls;

/// <summary>
/// Full-precision double editor with spinner, keyboard and wheel interaction.
/// Unlike <see cref="NumericUpDown"/>, values never pass through <see cref="decimal"/>
/// unless a spinner step can use decimal arithmetic to preserve the displayed digits.
/// </summary>
public sealed class InvariantNumericTextBox : UserControl
{
    public static readonly StyledProperty<double?> NumericValueProperty =
        AvaloniaProperty.Register<InvariantNumericTextBox, double?>(
            nameof(NumericValue), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<InvariantNumericTextBox, string>(
            nameof(Text), string.Empty, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> IncrementProperty =
        AvaloniaProperty.Register<InvariantNumericTextBox, double>(
            nameof(Increment), 1d, validate: value => double.IsFinite(value) && value > 0);

    public static readonly StyledProperty<double?> MinimumProperty =
        AvaloniaProperty.Register<InvariantNumericTextBox, double?>(nameof(Minimum));

    public static readonly StyledProperty<double?> MaximumProperty =
        AvaloniaProperty.Register<InvariantNumericTextBox, double?>(nameof(Maximum));

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<InvariantNumericTextBox, bool>(nameof(IsReadOnly));

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<InvariantNumericTextBox, string?>(nameof(Watermark));

    public static readonly StyledProperty<TextAlignment> TextAlignmentProperty =
        AvaloniaProperty.Register<InvariantNumericTextBox, TextAlignment>(nameof(TextAlignment));

    private readonly TextBox _textBox;
    private readonly RepeatButton _upButton;
    private readonly RepeatButton _downButton;
    private double? _committedValue;
    private bool _syncingText;
    private bool _syncingUserValue;

    public InvariantNumericTextBox()
    {
        _textBox = new TextBox
        {
            MinWidth = 40,
            MinHeight = 34,
            Padding = new Thickness(6, 4),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _textBox.TextChanged += OnEditorTextChanged;
        _textBox.LostFocus += (_, _) => TryCommit();
        _textBox.KeyDown += OnEditorKeyDown;
        _textBox.PointerWheelChanged += OnEditorPointerWheelChanged;

        _upButton = CreateSpinnerButton("▴", 1);
        _downButton = CreateSpinnerButton("▾", -1);

        var spinner = new Grid
        {
            Width = 28,
            MinHeight = 34,
            RowDefinitions = new RowDefinitions("*,*"),
            Children = { _upButton, _downButton },
        };
        Grid.SetRow(_downButton, 1);

        var layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { _textBox, spinner },
        };
        Grid.SetColumn(spinner, 1);
        Content = layout;

        Focusable = true;
        GotFocus += (_, _) => _textBox.Focus();
        SynchronizeEditorProperties();
    }

    public double? NumericValue
    {
        get => GetValue(NumericValueProperty);
        set => SetValue(NumericValueProperty, value);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    public double Increment
    {
        get => GetValue(IncrementProperty);
        set => SetValue(IncrementProperty, value);
    }

    public double? Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double? Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    public TextAlignment TextAlignment
    {
        get => GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    public string DisplayText => _textBox.Text ?? string.Empty;
    public bool UserModified { get; private set; }

    /// <summary>Raised only for committed user edits and spinner/wheel/keyboard steps.</summary>
    public event EventHandler? NumericValueChanged;

    public void SetValueWithText(double value, string displayText)
    {
        _committedValue = value;
        UserModified = false;
        SetCurrentValue(NumericValueProperty, value);
        SetEditorText(displayText ?? string.Empty);
    }

    /// <summary>Commits valid text and normalises locale-specific input for JSON safety.</summary>
    public bool TryCommit()
    {
        string input = DisplayText.Trim();
        if (input.Length == 0)
        {
            bool changed = _committedValue.HasValue;
            CommitUserValue(null, string.Empty, changed);
            return false;
        }

        if (!NumericParseHelper.TryParseDouble(input, out double parsed))
            return false;

        const NumberStyles styles = NumberStyles.Float | NumberStyles.AllowThousands;
        bool alreadyInvariant = double.TryParse(input, styles, CultureInfo.InvariantCulture, out _);
        string display = alreadyInvariant ? input : NumericParseHelper.FormatDouble(parsed);
        CommitUserValue(parsed, display, !_committedValue.Equals(parsed));
        return true;
    }

    public void Step(int direction)
    {
        if (direction == 0 || IsReadOnly || !IsEnabled)
            return;

        TryCommit();
        string currentText = DisplayText.Trim();
        if (decimal.TryParse(currentText, NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out decimal decimalValue))
        {
            try
            {
                decimal nextDecimal = decimalValue + (direction * (decimal)Increment);
                double next = Clamp((double)nextDecimal);
                string display = next.Equals((double)nextDecimal)
                    ? nextDecimal.ToString(CultureInfo.InvariantCulture)
                    : NumericParseHelper.FormatDouble(next);
                CommitUserValue(next, display, !_committedValue.Equals(next));
                return;
            }
            catch (OverflowException)
            {
                // Extreme doubles cannot be represented by decimal; use double below.
            }
        }

        double fallback = Clamp((_committedValue ?? 0d) + (direction * Increment));
        CommitUserValue(fallback, NumericParseHelper.FormatDouble(fallback), !_committedValue.Equals(fallback));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == NumericValueProperty && !_syncingUserValue)
        {
            _committedValue = change.GetNewValue<double?>();
            UserModified = false;
            SetEditorText(_committedValue.HasValue
                ? NumericParseHelper.FormatDouble(_committedValue.Value)
                : string.Empty);
        }
        else if (change.Property == TextProperty && !_syncingText)
        {
            SetEditorText(change.GetNewValue<string>() ?? string.Empty, updateTextProperty: false);
        }
        else if (change.Property == IsReadOnlyProperty ||
                 change.Property == WatermarkProperty ||
                 change.Property == TextAlignmentProperty ||
                 change.Property == IsEnabledProperty)
        {
            SynchronizeEditorProperties();
        }
    }

    private RepeatButton CreateSpinnerButton(string content, int direction)
    {
        var arrow = new TextBlock
        {
            Text = content,
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            LineHeight = 13,
            Height = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        var button = new RepeatButton
        {
            Content = arrow,
            MinWidth = 28,
            MinHeight = 17,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(1),
            Delay = 400,
            Interval = 75,
        };
        button.Click += (_, _) => Step(direction);
        return button;
    }

    private void OnEditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_syncingText)
            return;

        _syncingText = true;
        SetCurrentValue(TextProperty, _textBox.Text ?? string.Empty);
        _syncingText = false;
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
                Step(1);
                e.Handled = true;
                break;
            case Key.Down:
                Step(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                TryCommit();
                e.Handled = true;
                break;
        }
    }

    private void OnEditorPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0)
            return;

        Step(e.Delta.Y > 0 ? 1 : -1);
        e.Handled = true;
    }

    private void CommitUserValue(double? value, string displayText, bool changed)
    {
        _committedValue = value;
        _syncingUserValue = true;
        SetCurrentValue(NumericValueProperty, value);
        _syncingUserValue = false;
        SetEditorText(displayText);

        if (!changed)
            return;

        UserModified = true;
        NumericValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetEditorText(string value, bool updateTextProperty = true)
    {
        _syncingText = true;
        _textBox.Text = value;
        if (updateTextProperty)
            SetCurrentValue(TextProperty, value);
        _syncingText = false;
    }

    private double Clamp(double value)
    {
        if (Minimum.HasValue && value < Minimum.Value)
            value = Minimum.Value;
        if (Maximum.HasValue && value > Maximum.Value)
            value = Maximum.Value;
        return value;
    }

    private void SynchronizeEditorProperties()
    {
        _textBox.IsReadOnly = IsReadOnly;
        _textBox.PlaceholderText = Watermark;
        _textBox.TextAlignment = TextAlignment;
        _textBox.IsEnabled = IsEnabled;
        _upButton.IsEnabled = IsEnabled && !IsReadOnly;
        _downButton.IsEnabled = IsEnabled && !IsReadOnly;
    }
}
