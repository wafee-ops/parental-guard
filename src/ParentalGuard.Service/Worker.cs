using System.Diagnostics;

namespace ParentalGuard.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly DesktopActivityTracker _tracker = new();
    private readonly ActivityStore _store;
    private readonly HostsFileBlocker _hostsFileBlocker = new();
    private DateTime _lastBlockActionAt = DateTime.MinValue;
    private DateTime _lastHostsSyncAt = DateTime.MinValue;
    private DateTime _lastProcessSweepAt = DateTime.MinValue;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ParentalGuard",
            "usage.db");
        _store = new ActivityStore(appDataPath);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var sample = _tracker.Capture();
            _store.RecordSample(sample);
            EnsureRulesForSample(sample);
            var rules = _store.LoadBlockRules();
            SyncHostsFileIfNeeded(rules);
            EnforceForegroundRules(sample, rules);
            SweepBlockedProcessesIfNeeded(rules);
            await Task.Delay(1000, stoppingToken);
        }
    }

    private void EnsureRulesForSample(ActivitySample sample)
    {
        if (sample.ProcessId > 0 && sample.ProcessName != "desktop")
        {
            _store.EnsureRuleExists("app", NormalizeRuleKey("app", sample.ProcessName), sample.AppName);
        }

        if (!string.IsNullOrWhiteSpace(sample.WebsiteDomain))
        {
            _store.EnsureRuleExists("website", NormalizeRuleKey("website", sample.WebsiteDomain!), sample.WebsiteDomain!);
        }
    }

    private void SyncHostsFileIfNeeded(IReadOnlyCollection<BlockRuleRecord> rules)
    {
        if (DateTime.Now - _lastHostsSyncAt < TimeSpan.FromSeconds(5))
        {
            return;
        }

        var blockedDomains = rules
            .Where(rule => rule.TargetType == "website" && rule.IsEnabled)
            .Select(rule => NormalizeRuleKey("website", rule.TargetKey))
            .Where(rule => !string.IsNullOrWhiteSpace(rule))
            .ToList();

        try
        {
            _hostsFileBlocker.SyncBlockedSites(blockedDomains);
            _lastHostsSyncAt = DateTime.Now;
        }
        catch (Exception ex)
        {
            _lastHostsSyncAt = DateTime.Now;
            _logger.LogError(ex, "Failed to sync the hosts file for blocked websites");
        }
    }

    private void EnforceForegroundRules(ActivitySample sample, IReadOnlyCollection<BlockRuleRecord> rules)
    {
        if (sample.ProcessId <= 0 || DateTime.Now - _lastBlockActionAt < TimeSpan.FromSeconds(2))
        {
            return;
        }

        foreach (var rule in rules.Where(rule => rule.IsEnabled))
        {
            var isMatch = rule.TargetType == "website"
                ? MatchesWebsiteRule(rule.TargetKey, sample.WebsiteDomain)
                : MatchesAppRule(rule.TargetKey, sample);

            if (!isMatch)
            {
                continue;
            }

            var usageKey = rule.TargetType == "website"
                ? NormalizeRuleKey("website", sample.WebsiteDomain ?? rule.TargetKey)
                : ResolveAppUsageKey(rule.TargetKey, sample);
            var seconds = _store.GetUsageSecondsForToday(rule.TargetType, usageKey);
            if (seconds < rule.MaxMinutes * 60)
            {
                continue;
            }

            try
            {
                KillProcessById(sample.ProcessId, rule.DisplayName, rule.MaxMinutes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to block process {ProcessId}", sample.ProcessId);
            }

            break;
        }
    }

    private void SweepBlockedProcessesIfNeeded(IReadOnlyCollection<BlockRuleRecord> rules)
    {
        if (DateTime.Now - _lastProcessSweepAt < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _lastProcessSweepAt = DateTime.Now;

        foreach (var rule in rules.Where(rule => rule.IsEnabled && rule.TargetType == "app"))
        {
            var seconds = _store.GetUsageSecondsForTodayForAppRule(rule.TargetKey);
            if (seconds < rule.MaxMinutes * 60)
            {
                continue;
            }

            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.Id <= 0 || process.HasExited)
                    {
                        continue;
                    }

                    if (!MatchesAppRule(rule.TargetKey, process.ProcessName, process.MainWindowTitle))
                    {
                        continue;
                    }

                    KillProcessById(process.Id, rule.DisplayName, rule.MaxMinutes);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Skipping process sweep candidate {ProcessName}", process.ProcessName);
                }
            }
        }
    }

    private static bool MatchesWebsiteRule(string targetKey, string? websiteDomain)
    {
        if (string.IsNullOrWhiteSpace(websiteDomain))
        {
            return false;
        }

        var normalizedRule = NormalizeRuleKey("website", targetKey);
        var normalizedDomain = NormalizeRuleKey("website", websiteDomain);
        return normalizedDomain == normalizedRule || (normalizedRule == "x.com" && normalizedDomain == "twitter.com");
    }

    private static bool MatchesAppRule(string targetKey, ActivitySample sample)
    {
        return MatchesAppRule(targetKey, sample.ProcessName, sample.AppName, sample.AppSubtitle);
    }

    private static bool MatchesAppRule(string targetKey, params string[] values)
    {
        var normalizedRule = NormalizeRuleKey("app", targetKey);
        var candidates = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.ToLowerInvariant())
            .ToArray();

        return normalizedRule switch
        {
            "discord" => candidates.Any(value => value.Contains("discord")),
            "youtube" => candidates.Any(value => value.Contains("youtube")),
            "twitter" => candidates.Any(value => value.Contains("twitter") || value.Contains("x")),
            _ => candidates.Any(value => value.Contains(normalizedRule))
        };
    }

    private void KillProcessById(int processId, string displayName, int maxMinutes)
    {
        Process.GetProcessById(processId).Kill(true);
        _lastBlockActionAt = DateTime.Now;
        _logger.LogWarning("Blocked {DisplayName} after {Minutes} minutes", displayName, maxMinutes);
    }

    private static string ResolveAppUsageKey(string targetKey, ActivitySample sample)
    {
        var normalizedRule = NormalizeRuleKey("app", targetKey);
        var normalizedProcess = NormalizeRuleKey("app", sample.ProcessName);
        return normalizedRule switch
        {
            "youtube" when sample.AppName.Contains("youtube", StringComparison.OrdinalIgnoreCase) => sample.AppName,
            "twitter" when sample.AppName.Contains("twitter", StringComparison.OrdinalIgnoreCase) => sample.AppName,
            "discord" when sample.ProcessName.Contains("discord", StringComparison.OrdinalIgnoreCase) => sample.AppName,
            _ => sample.AppName == "Desktop" ? normalizedProcess : sample.AppName
        };
    }

    private static string NormalizeRuleKey(string targetType, string key)
    {
        var normalized = key.Trim().ToLowerInvariant();
        if (targetType == "website" && normalized.StartsWith("www."))
        {
            normalized = normalized[4..];
        }
        if (targetType == "app" && normalized.EndsWith(".exe"))
        {
            normalized = normalized[..^4];
        }
        return normalized;
    }
}
