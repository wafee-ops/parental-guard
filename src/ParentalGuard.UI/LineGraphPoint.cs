namespace ParentalGuard.UI;

public sealed class LineGraphPoint
{
    public required string Label { get; init; }

    public required string DurationText { get; init; }

    public double X { get; init; }

    public double Y { get; init; }
}
