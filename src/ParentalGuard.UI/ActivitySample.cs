namespace ParentalGuard.UI;

public sealed record ActivitySample(
    DateTime ObservedAt,
    int ProcessId,
    string ProcessName,
    string AppName,
    string AppCategory,
    string AppSubtitle,
    string? WebsiteDomain,
    string? WebsiteCategory,
    string? WebsiteSubtitle);
