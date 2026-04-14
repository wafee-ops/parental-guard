using System.Diagnostics;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.Models;

namespace ParentalGuard.Service;


public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly DesktopActivityTracker _tracker = new();
    private readonly ActivityStore _store;
    private readonly HostsFileBlocker _hostsFileBlocker = new();
    private readonly FirewallWebsiteBlocker _firewallBlocker;
    private readonly ProxyServer _proxyServer = new();
    private DateTime _lastBlockActionAt = DateTime.MinValue;
    private DateTime _lastHostsSyncAt = DateTime.MinValue;
    private DateTime _lastFirewallSyncAt = DateTime.MinValue;
    private DateTime _lastProcessSweepAt = DateTime.MinValue;

    private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ParentalGuard.UI", "ParentalGuard.Service", "ParentalGuard.App", "dotnet",
        "explorer", "svchost", "csrss", "lsass", "services", "winlogon", "wininit",
        "dwm", "taskmgr", "SearchHost", "RuntimeBroker", "ShellExperienceHost",
        "StartMenuExperienceHost", "SearchIndexer", "sihost", "taskhostw", "ctfmon",
        "conhost", "fontdrvhost", "smss", "Registry", "WUDFHost", "WmiPrvSE",
        "MemCompression", "System", "SystemSettings", "SecurityHealthService",
        "SecurityHealthSystray", "ApplicationFrameHost", "SearchApp", "TextInputHost",
        "Microsoft.Windows.ShellExperienceHost", "desktop", " idle"
    };

    private static int SelfProcessId => Environment.ProcessId;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ParentalGuard",
            "usage.db");
        _store = new ActivityStore(appDataPath);
        _firewallBlocker = new FirewallWebsiteBlocker(logger);

        SetupProxyServer();
    }

    private void SetupProxyServer()
    {
        _proxyServer.BeforeRequest += async (sender, e) =>
        {
            var rules = _store.LoadBlockRules();
            var host = e.HttpClient.Request.RequestUri.Host.ToLowerInvariant();

            foreach (var rule in rules.Where(r => r.IsEnabled && r.TargetType == "website"))
            {
                if (host.Contains(rule.TargetKey, StringComparison.OrdinalIgnoreCase))
                {
                    var usageKey = NormalizeRuleKey("website", host);
                    var seconds = _store.GetUsageSecondsForToday("website", usageKey);

                    if (seconds >= rule.MaxMinutes * 60)
                    {
                        e.GenericResponse(
                            "Website blocked by ParentalGuard due to time limit.",
                            System.Net.HttpStatusCode.Forbidden);
                        return;
                    }
                }
            }
        };

        var endpoint = new ExplicitProxyEndPoint(System.Net.IPAddress.Any, 8080);
        _proxyServer.AddEndPoint(endpoint);
        _proxyServer.Start();
        _logger.LogInformation("Proxy server started on port 8080");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var sample = _tracker.Capture();
            _store.RecordSample(sample);
            var rules = _store.LoadBlockRules();
            SyncHostsFileIfNeeded(rules);
            await SyncFirewallIfNeededAsync(rules);
            EnforceForegroundRules(sample, rules);
            SweepBlockedProcessesIfNeeded(rules);
            await Task.Delay(1000, stoppingToken);
        }
    }

    private void SyncHostsFileIfNeeded(IReadOnlyCollection<BlockRuleRecord> rules)
    {
        if (DateTime.Now - _lastHostsSyncAt < TimeSpan.FromSeconds(5))
        {
            return;
        }

        var blockedDomains = rules
            .Where(rule => rule.TargetType == "website" && rule.ListType == "blocked")
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

    private async Task SyncFirewallIfNeededAsync(IReadOnlyCollection<BlockRuleRecord> rules)
    {
        if (DateTime.Now - _lastFirewallSyncAt < TimeSpan.FromMinutes(2))
        {
            return;
        }

        var blockedDomains = rules
            .Where(rule => rule.TargetType == "website" && rule.ListType == "blocked")
            .Select(rule => NormalizeRuleKey("website", rule.TargetKey))
            .Where(rule => !string.IsNullOrWhiteSpace(rule))
            .ToList();

        try
        {
            await _firewallBlocker.SyncBlockedWebsitesAsync(blockedDomains);
            _lastFirewallSyncAt = DateTime.Now;
        }
        catch (Exception ex)
        {
            _lastFirewallSyncAt = DateTime.Now;
            _logger.LogError(ex, "Failed to sync firewall rules for blocked websites");
        }
    }

    private void EnforceForegroundRules(ActivitySample sample, IReadOnlyCollection<BlockRuleRecord> rules)
    {
        if (ProtectedProcesses.Contains(sample.ProcessName)) return;
        if (sample.ProcessId <= 0 || DateTime.Now - _lastBlockActionAt < TimeSpan.FromSeconds(2))
        {
            return;
        }

        var matchedRule = rules.FirstOrDefault(r =>
            r.TargetType == "app" && MatchesAppRule(r.TargetKey, sample));

        if (matchedRule is null)
        {
            if (!IsBrowserProcess(sample.ProcessName)) return;

            var matchedWebsite = rules.FirstOrDefault(r =>
                r.TargetType == "website" &&
                MatchesWebsiteRule(r.TargetKey, sample.WebsiteDomain));

            if (matchedWebsite is null)
            {
                return;
            }

            if (matchedWebsite.ListType == "allowed")
            {
                var usageKey = NormalizeRuleKey("website", sample.WebsiteDomain ?? matchedWebsite.TargetKey);
                var seconds = _store.GetUsageSecondsForToday("website", usageKey);
                if (seconds < matchedWebsite.MaxMinutes * 60) return;
            }

            try
            {
                KillProcessById(sample.ProcessId, matchedWebsite.DisplayName, matchedWebsite.MaxMinutes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to block process {ProcessId}", sample.ProcessId);
            }
            return;
        }

        if (matchedRule.ListType == "allowed")
        {
            var usageKey = ResolveAppUsageKey(matchedRule.TargetKey, sample);
            var seconds = _store.GetUsageSecondsForToday("app", usageKey);
            if (seconds < matchedRule.MaxMinutes * 60) return;
        }

        try
        {
            KillProcessById(sample.ProcessId, matchedRule.DisplayName, matchedRule.MaxMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to block process {ProcessId}", sample.ProcessId);
        }
    }

    private static bool IsBrowserProcess(string processName) =>
        processName is "chrome" or "msedge" or "firefox" or "brave" or "opera" or "vivaldi" or "arc";

    private void SweepBlockedProcessesIfNeeded(IReadOnlyCollection<BlockRuleRecord> rules)
    {
        if (DateTime.Now - _lastProcessSweepAt < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _lastProcessSweepAt = DateTime.Now;
        var appRules = rules.Where(r => r.TargetType == "app").ToList();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id <= 0 || process.HasExited) continue;
                if (ProtectedProcesses.Contains(process.ProcessName)) continue;

                var matchedRule = appRules.FirstOrDefault(r =>
                    MatchesAppRule(r.TargetKey, process.ProcessName, process.MainWindowTitle));

                if (matchedRule is null) continue;

                if (matchedRule.ListType == "allowed")
                {
                    var seconds = _store.GetUsageSecondsForTodayForAppRule(matchedRule.TargetKey);
                    if (seconds < matchedRule.MaxMinutes * 60) continue;
                }

                KillProcessById(process.Id, matchedRule.DisplayName, matchedRule.MaxMinutes);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Skipping process sweep candidate {ProcessName}", process.ProcessName);
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
        if (processId == SelfProcessId) return;
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill();
            _lastBlockActionAt = DateTime.Now;
            _logger.LogWarning("Blocked {DisplayName} after {Minutes} minutes (PID: {ProcessId})", displayName, maxMinutes, processId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to kill process {ProcessId}", processId);
        }
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
