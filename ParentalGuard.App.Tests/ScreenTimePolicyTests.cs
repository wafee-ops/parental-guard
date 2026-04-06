using ParentalGuard.App;

namespace ParentalGuard.App.Tests;

public class ScreenTimePolicyTests
{
    [Fact]
    public void Evaluate_BlocksUsage_WhenBedtimeHasStarted()
    {
        var profile = new ChildProfile("Sam", 11, 20, BedtimeHour: 20);

        var decision = ScreenTimePolicy.Evaluate(profile, new DateTime(2026, 4, 5, 20, 0, 0));

        Assert.False(decision.IsAllowed);
        Assert.Equal(0, decision.RemainingMinutes);
        Assert.Equal("Bedtime has started.", decision.Reason);
    }

    [Fact]
    public void Evaluate_BlocksUsage_WhenDailyLimitIsReached()
    {
        var profile = new ChildProfile("Alex", 15, 120, BedtimeHour: 22);

        var decision = ScreenTimePolicy.Evaluate(profile, new DateTime(2026, 4, 5, 18, 0, 0));

        Assert.False(decision.IsAllowed);
        Assert.Equal(0, decision.RemainingMinutes);
        Assert.Equal("Daily screen-time limit reached.", decision.Reason);
    }

    [Fact]
    public void Evaluate_ReturnsRemainingMinutes_WhenUsageIsStillAllowed()
    {
        var profile = new ChildProfile("Riley", 9, 30, BedtimeHour: 20);

        var decision = ScreenTimePolicy.Evaluate(profile, new DateTime(2026, 4, 5, 16, 0, 0));

        Assert.True(decision.IsAllowed);
        Assert.Equal(60, decision.RemainingMinutes);
        Assert.Equal("Screen time available.", decision.Reason);
    }
}
