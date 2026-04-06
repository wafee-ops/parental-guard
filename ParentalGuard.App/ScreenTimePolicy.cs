namespace ParentalGuard.App;

public sealed record ChildProfile(string Name, int Age, int MinutesUsedToday, int BedtimeHour);

public sealed record ScreenTimeDecision(bool IsAllowed, int RemainingMinutes, string Reason);

public static class ScreenTimePolicy
{
    public static ScreenTimeDecision Evaluate(ChildProfile profile, DateTime currentTime)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var dailyLimit = profile.Age switch
        {
            <= 12 => 90,
            <= 15 => 120,
            _ => 150
        };

        if (currentTime.Hour >= profile.BedtimeHour)
        {
            return new ScreenTimeDecision(false, 0, "Bedtime has started.");
        }

        var remainingMinutes = Math.Max(0, dailyLimit - profile.MinutesUsedToday);
        if (remainingMinutes == 0)
        {
            return new ScreenTimeDecision(false, 0, "Daily screen-time limit reached.");
        }

        return new ScreenTimeDecision(true, remainingMinutes, "Screen time available.");
    }
}
