using System.Diagnostics;
using System.Net;
using System.Text;

namespace ParentalGuard.Service;

public sealed class FirewallWebsiteBlocker
{
    private const string RulePrefix = "ParentalGuard_Web_";
    private readonly ILogger _logger;
    private readonly Dictionary<string, HashSet<string>> _domainIps = new(StringComparer.OrdinalIgnoreCase);

    public FirewallWebsiteBlocker(ILogger logger)
    {
        _logger = logger;
    }

    public async Task SyncBlockedWebsitesAsync(IEnumerable<string> domains)
    {
        var domainList = domains
            .Select(d => d.Trim().ToLowerInvariant())
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingRules = GetExistingRuleNames();
        var desiredRuleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var domain in domainList)
        {
            var ruleName = GetRuleName(domain);
            desiredRuleNames.Add(ruleName);

            if (existingRules.Contains(ruleName)) continue;

            var ips = await ResolveDomainAsync(domain);
            if (ips.Count == 0)
            {
                _logger.LogWarning("Could not resolve IPs for {Domain}", domain);
                continue;
            }

            _domainIps[domain] = ips;
            await CreateBlockRuleAsync(ruleName, domain, ips);
        }

        foreach (var existingRule in existingRules)
        {
            if (!existingRule.StartsWith(RulePrefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!desiredRuleNames.Contains(existingRule))
            {
                await DeleteFirewallRuleAsync(existingRule);
            }
        }
    }

    public async Task RemoveAllRulesAsync()
    {
        var existingRules = GetExistingRuleNames();
        foreach (var rule in existingRules)
        {
            if (rule.StartsWith(RulePrefix, StringComparison.OrdinalIgnoreCase))
            {
                await DeleteFirewallRuleAsync(rule);
            }
        }
    }

    private async Task<HashSet<string>> ResolveDomainAsync(string domain)
    {
        var ips = new HashSet<string>();
        try
        {
            var entries = await Dns.GetHostAddressesAsync(domain);
            foreach (var ip in entries)
            {
                ips.Add(ip.ToString());
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DNS resolution failed for {Domain}", domain);
        }

        try
        {
            var wwwEntries = await Dns.GetHostAddressesAsync($"www.{domain}");
            foreach (var ip in wwwEntries)
            {
                ips.Add(ip.ToString());
            }
        }
        catch { }

        return ips;
    }

    private async Task CreateBlockRuleAsync(string ruleName, string domain, HashSet<string> ips)
    {
        var ipList = string.Join(",", ips);
        var ok = await RunNetshAsync(
            $"advfirewall firewall add rule name=\"{ruleName}\" dir=out action=block protocol=any remoteip={ipList} profile=any enable=yes description=\"Block {domain}\"");
        if (ok)
        {
            _logger.LogDebug("Created firewall rule for {Domain} blocking {Ips}", domain, ipList);
        }
    }

    private HashSet<string> GetExistingRuleNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall show rule name=all dir=out",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10000);

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("Rule Name:", StringComparison.OrdinalIgnoreCase))
                {
                    var name = trimmed["Rule Name:".Length..].Trim();
                    if (name.StartsWith(RulePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        names.Add(name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate firewall rules");
        }
        return names;
    }

    private async Task<bool> DeleteFirewallRuleAsync(string ruleName)
    {
        return await RunNetshAsync($"advfirewall firewall delete rule name=\"{ruleName}\"");
    }

    private async Task<bool> RunNetshAsync(string arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            process.Start();
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                var output = string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim();
                _logger.LogWarning("netsh {Args} exited with {Code}: {Output}", arguments, process.ExitCode, output);
            }
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run netsh {Args}", arguments);
            return false;
        }
    }

    private static string GetRuleName(string domain) => $"{RulePrefix}{domain.Replace('.', '_')}";
}
