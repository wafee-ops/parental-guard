using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace ParentalGuard.UI;

public sealed class UsageEntry : INotifyPropertyChanged
{
    private int _seconds;
    private string _subtitle;
    private double _shareOfTotal;

    public UsageEntry(string name, string category, int seconds, int limitMinutes, string subtitle, Brush accentBrush)
    {
        Name = name;
        Category = category;
        _seconds = seconds;
        LimitMinutes = limitMinutes;
        _subtitle = subtitle;
        AccentBrush = accentBrush;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public string Category { get; }

    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name[..1].ToUpperInvariant();

    public int LimitMinutes { get; }

    public Brush AccentBrush { get; }

    public int Seconds
    {
        get => _seconds;
        private set
        {
            if (_seconds == value)
            {
                return;
            }

            _seconds = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Minutes));
            OnPropertyChanged(nameof(DurationText));
            OnPropertyChanged(nameof(LimitText));
            OnPropertyChanged(nameof(UsagePercent));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public int Minutes => (int)Math.Ceiling(Seconds / 60d);

    public string Subtitle
    {
        get => _subtitle;
        set
        {
            if (_subtitle == value)
            {
                return;
            }

            _subtitle = value;
            OnPropertyChanged();
        }
    }

    public string DurationText => FormatDuration(Seconds);

    public string LimitText => $"Limit {LimitMinutes} min";

    public double ShareOfTotal
    {
        get => _shareOfTotal;
        set
        {
            if (Math.Abs(_shareOfTotal - value) < 0.0001)
            {
                return;
            }

            _shareOfTotal = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SharePercentText));
        }
    }

    public string SharePercentText => $"{ShareOfTotal * 100:0.#}%";

    public double UsagePercent => LimitMinutes <= 0 ? 0 : Math.Min(1, Seconds / (LimitMinutes * 60d));

    public string StatusText =>
        UsagePercent switch
        {
            >= 1 => "Limit reached",
            >= 0.9 => "Nearly capped",
            >= 0.75 => "Approaching limit",
            _ => "Healthy range"
        };

    public void AddSeconds(int seconds)
    {
        Seconds += seconds;
    }

    private static string FormatDuration(int seconds)
    {
        var duration = TimeSpan.FromSeconds(seconds);
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes:00}m"
            : $"{Math.Max(1, duration.Minutes)} min";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
