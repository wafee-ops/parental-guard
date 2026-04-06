namespace ParentalGuard.UI;

public sealed class DailyUsagePoint
{
    public required string Label { get; init; }

    public required int Seconds { get; init; }

    public double Height { get; set; }

    public string DurationText { get; init; } = string.Empty;
}
