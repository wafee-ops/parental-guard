using System.Diagnostics;

namespace ParentalGuard.Service;

public sealed class DnsBypassPrevention
{
    private const string RuleGroupPrefix = "ParentalGuard_DNS_";
    private const string BlockExternalDnsUdp = RuleGroupPrefix + "BlockExternalDnsUDP";
    private const string BlockExternalDnsTcp = RuleGroupPrefix + "BlockExternalDnsTCP";
    private const string BlockDnsOverTls = RuleGroupPrefix + "BlockDoT";
    private const string AllowEnforcedDnsUdp = RuleGroupPrefix + "AllowEnforcedDNS_UDP";
    private const string AllowEnforcedDnsTcp = RuleGroupPrefix + "AllowEnforcedDNS_TCP";
    private const string BlockDnsOverHttpsProviders = RuleGroupPrefix + "BlockDoHProviders";

    private readonly ILogger<DnsBypassPrevention> _logger;
    private readonly DnsEnforcementOptions _options;

    private static readonly string[] KnownDoHProviderIps =
    [
        "1.1.1.1", "1.0.0.1",
        "8.8.8.8", "8.8.4.4",
        "9.9.9.9",
        "149.112.112.112",
        "94.140.14.14", "94.140.15.15"
    ];

    public DnsBypassPrevention(ILogger<DnsBypassPrevention> logger, DnsEnforcementOptions? options = null)
    {
        _logger = logger;
        _options = options ?? DnsEnforcementOptions.OpenDns;
    }

    public async Task<DnsBypassResult> EnforceBypassPreventionAsync()
    {
        var results = new List<(string Rule, bool Success)>();

        results.Add(("AllowEnforcedDNS_UDP", await EnsureAllowRuleAsync(AllowEnforcedDnsUdp, "udp", _options.PrimaryDns)));
        if (_options.SecondaryDns is not null)
        {
            results.Add(("AllowEnforcedDNS_TCP", await EnsureAllowRuleAsync(AllowEnforcedDnsTcp, "tcp", _options.PrimaryDns)));
        }

        results.Add(("BlockExternalDNS_UDP", await EnsureBlockExternalDnsAsync(BlockExternalDnsUdp, "udp")));
        results.Add(("BlockExternalDNS_TCP", await EnsureBlockExternalDnsAsync(BlockExternalDnsTcp, "tcp")));
        results.Add(("BlockDoT", await EnsureBlockPortAsync(BlockDnsOverTls, "tcp", 853, "Block DNS-over-TLS (port 853)")));
        results.Add(("BlockDoHProviders", await EnsureBlockDoHProvidersAsync()));

        var failures = results.Count(r => !r.Success);
        if (failures > 0)
        {
            _logger.LogWarning("DNS bypass prevention completed with {Failures} rule failures", failures);
        }
        else
        {
            _logger.LogInformation("DNS bypass prevention rules applied successfully");
        }

        return new DnsBypassResult(results.Count - failures, results.Count, failures == 0);
    }

    public async Task<DnsBypassResult> RemoveBypassPreventionAsync()
    {
        var rules = new[]
        {
            BlockExternalDnsUdp, BlockExternalDnsTcp,
            BlockDnsOverTls, AllowEnforcedDnsUdp, AllowEnforcedDnsTcp,
            BlockDnsOverHttpsProviders
        };

        var removed = 0;
        foreach (var rule in rules)
        {
            if (await DeleteFirewallRuleAsync(rule)) removed++;
        }

        _logger.LogInformation("Removed {Removed}/{Total} DNS bypass prevention rules", removed, rules.Length);
        return new DnsBypassResult(removed, rules.Length, removed == rules.Length);
    }

    private async Task<bool> EnsureAllowRuleAsync(string ruleName, string protocol, string dnsServer)
    {
        if (await RuleExistsAsync(ruleName))
        {
            return true;
        }

        return await RunNetshAdvFirewallAsync(
            $"firewall add rule name=\"{ruleName}\" dir=out action=allow protocol={protocol} remoteip={dnsServer} remoteport=53 profile=any enable=yes");
    }

    private async Task<bool> EnsureBlockExternalDnsAsync(string ruleName, string protocol)
    {
        if (await RuleExistsAsync(ruleName))
        {
            return true;
        }

        var allowedIps = _options.SecondaryDns is not null
            ? $"{_options.PrimaryDns},{_options.SecondaryDns},127.0.0.1"
            : $"{_options.PrimaryDns},127.0.0.1";

        return await RunNetshAdvFirewallAsync(
            $"firewall add rule name=\"{ruleName}\" dir=out action=block protocol={protocol} remoteport=53 remoteip=!{allowedIps} profile=any enable=yes");
    }

    private async Task<bool> EnsureBlockPortAsync(string ruleName, string protocol, int port, string description)
    {
        if (await RuleExistsAsync(ruleName))
        {
            return true;
        }

        return await RunNetshAdvFirewallAsync(
            $"firewall add rule name=\"{ruleName}\" dir=out action=block protocol={protocol} remoteport={port} profile=any enable=yes description=\"{description}\"");
    }

    private async Task<bool> EnsureBlockDoHProvidersAsync()
    {
        if (await RuleExistsAsync(BlockDnsOverHttpsProviders))
        {
            return true;
        }

        var blockedIps = string.Join(",", KnownDoHProviderIps);
        return await RunNetshAdvFirewallAsync(
            $"firewall add rule name=\"{BlockDnsOverHttpsProviders}\" dir=out action=block protocol=any remoteip={blockedIps} profile=any enable=yes description=\"Block known DNS-over-HTTPS provider IPs\"");
    }

    private async Task<bool> RuleExistsAsync(string ruleName)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall show rule name=\"{ruleName}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output.Contains(ruleName, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check firewall rule {Rule}", ruleName);
            return false;
        }
    }

    private async Task<bool> DeleteFirewallRuleAsync(string ruleName)
    {
        return await RunNetshAdvFirewallAsync($"firewall delete rule name=\"{ruleName}\"");
    }

    private async Task<bool> RunNetshAdvFirewallAsync(string arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall {arguments}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            process.Start();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                _logger.LogWarning("netsh advfirewall {Args} exited with code {Code}: {Error}",
                    arguments, process.ExitCode, error.Trim());
            }
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run netsh advfirewall {Args}", arguments);
            return false;
        }
    }
}

public sealed record DnsBypassResult(int RulesApplied, int TotalRules, bool AllSucceeded);
