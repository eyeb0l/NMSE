using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace NMSE.UI.Avalonia.Panels.Settlements;

internal sealed class OptionalTimestampEditor : UserControl
{
    private readonly CheckBox _enabled = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly DatePicker _date = new() { MinWidth = 145, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TimePicker _time = new() { MinWidth = 100, ClockIdentifier = "24HourClock", HorizontalAlignment = HorizontalAlignment.Stretch };

    internal OptionalTimestampEditor()
    {
        _enabled.IsCheckedChanged += (_, _) => UpdateEnabledState();
        _date.SelectedDateChanged += (_, _) => ValueChanged?.Invoke(this, EventArgs.Empty);
        _time.SelectedTimeChanged += (_, _) => ValueChanged?.Invoke(this, EventArgs.Empty);
        Content = new Grid
        {
            MinWidth = 280,
            ColumnDefinitions = new ColumnDefinitions("Auto,*,*"),
            ColumnSpacing = 5,
            Children = { _enabled, _date, _time },
        };
        Grid.SetColumn(_date, 1);
        Grid.SetColumn(_time, 2);
        Clear();
    }

    internal event EventHandler? ValueChanged;

    internal void SetUnixSeconds(long unixSeconds)
    {
        if (unixSeconds <= 0)
        {
            Clear();
            return;
        }
        DateTimeOffset local = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime();
        _date.SelectedDate = local;
        _time.SelectedTime = local.TimeOfDay;
        _enabled.IsChecked = true;
        UpdateEnabledState();
    }

    internal void SetLocalDateTime(DateTime? value)
    {
        if (!value.HasValue)
        {
            Clear();
            return;
        }
        var local = new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Local));
        _date.SelectedDate = local;
        _time.SelectedTime = local.TimeOfDay;
        _enabled.IsChecked = true;
        UpdateEnabledState();
    }

    internal long GetUnixSeconds()
    {
        if (_enabled.IsChecked != true || !_date.SelectedDate.HasValue)
            return 0;
        DateTime date = _date.SelectedDate.Value.LocalDateTime.Date;
        TimeSpan time = _time.SelectedTime ?? TimeSpan.Zero;
        return new DateTimeOffset(date + time).ToUniversalTime().ToUnixTimeSeconds();
    }

    internal DateTime? GetLocalDateTime() =>
        GetUnixSeconds() is > 0 and var seconds
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().DateTime
            : null;

    internal void Clear()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        _date.SelectedDate = now;
        _time.SelectedTime = now.TimeOfDay;
        _enabled.IsChecked = false;
        UpdateEnabledState();
    }

    private void UpdateEnabledState()
    {
        bool enabled = _enabled.IsChecked == true;
        _date.IsEnabled = enabled;
        _time.IsEnabled = enabled;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}
