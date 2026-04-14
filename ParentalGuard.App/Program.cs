using ParentalGuard.App;

var profiles = new[]
{
    new ChildProfile("Sam", 11, MinutesUsedToday: 70, BedtimeHour: 20),
    new ChildProfile("Alex", 15, MinutesUsedToday: 135, BedtimeHour: 22)
};

var now = DateTime.Now;

Console.WriteLine("Parental Guard Status");
Console.WriteLine("---------------------");

foreach (var profile in profiles)
{
    var decision = ScreenTimePolicy.Evaluate(profile, now);
    Console.WriteLine(
        $"{profile.Name}: Allowed={decision.IsAllowed}, Remaining={decision.RemainingMinutes} minutes, Reason={decision.Reason}");
}

Console.WriteLine();
Console.Write("Press any key to exit...");
Console.ReadKey();
