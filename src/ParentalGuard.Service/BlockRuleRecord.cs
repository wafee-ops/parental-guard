namespace ParentalGuard.Service;

public sealed record BlockRuleRecord(
    string TargetType,
    string TargetKey,
    string DisplayName,
    bool IsEnabled,
    int MaxMinutes);
