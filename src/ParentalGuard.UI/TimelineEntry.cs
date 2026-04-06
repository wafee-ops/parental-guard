using System.Windows.Media;

namespace ParentalGuard.UI;

public sealed record TimelineEntry(
    string TimeRange,
    string Title,
    string Detail,
    string DurationText,
    Brush AccentBrush);
