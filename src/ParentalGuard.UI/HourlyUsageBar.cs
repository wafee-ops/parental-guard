using System.Windows.Media;

namespace ParentalGuard.UI;

public sealed class HourlyUsageBar
{
    public required string Label { get; init; }

    public required string AccessibilityLabel { get; init; }

    public required int Seconds { get; init; }

    public required double Height { get; init; }

    public required Brush FillBrush { get; init; }

    public required string DurationText { get; init; }
}
