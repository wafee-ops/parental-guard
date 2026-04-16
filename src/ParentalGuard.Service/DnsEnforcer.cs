using System.Diagnostics;

namespace ParentalGuard.Service;

public sealed class DnsEnforcer
{
    private readonly ILogger<DnsEnforcer> _logger;
    private readonly DnsEnforcementOptions _options;

    public DnsEnforcer(ILogger<DnsEnforcer> logger, DnsEnforcementOptions? options = null)
    {
        _logger = logger;
        _options = options ?? DnsEnforcementOptions.OpenDns;
    }

    public async Task<DnsEnforcementResult> EnforceAsync()
    {
        if (!WindowsPrivilegeChecker.IsRunningElevated())
        {
            _logger.LogError("DNS enforcement requires administrator privileges. Run the process elevated or install it as a Windows Service.");
            return new DnsEnforcementResult(0, 0, false);
        }

        var adapters = GetActiveAdapterNames();
        if (adapters.Count == 0)
        {
            _logger.LogWarning("No active network adapters found for DNS enforcement");
            return new DnsEnforcementResult(0, 0, false);
        }

        var successCount = 0;
        foreach (var adapter in adapters)
        {
            var setOk = await SetDnsForAdapterAsync(adapter);
            if (setOk) successCount++;
        }

        _logger.LogInformation("DNS enforcement applied to {Success}/{Total} adapters",
            successCount, adapters.Count);

        return new DnsEnforcementResult(successCount, adapters.Count, successCount == adapters.Count);
    }

    public async Task<bool> SetDnsForAdapterAsync(string adapterName)
    {
        var primaryOk = await RunNetshAsync(
            $"interface ip set dns \"{adapterName}\" static {_options.PrimaryDns}");
        if (!primaryOk)
        {
            _logger.LogError("Failed to set primary DNS for adapter {Adapter}", adapterName);
            return false;
        }

        if (_options.SecondaryDns is not null)
        {
            var secondaryOk = await RunNetshAsync(
                $"interface ip add dns \"{adapterName}\" {_options.SecondaryDns} index=2");
            if (!secondaryOk)
            {
                _logger.LogWarning("Failed to set secondary DNS for adapter {Adapter}", adapterName);
            }
        }

        _logger.LogDebug("Set DNS to {Primary}/{Secondary} on adapter {Adapter}",
            _options.PrimaryDns, _options.SecondaryDns ?? "none", adapterName);
        return true;
    }

    public List<string> GetActiveAdapterNames()
    {
        var adapters = new List<string>();
        if (!WindowsPrivilegeChecker.IsRunningElevated())
        {
            _logger.LogWarning("Skipping adapter enumeration because the process is not running with administrator privileges");
            return adapters;
        }

        try
        {
            var output = RunNetshSync("interface show interface");
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("Admin", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("---", StringComparison.Ordinal)
                    || trimmed.Length == 0)
                {
                    continue;
                }

                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) continue;

                var isAdminEnabled = parts[0].Equals("enabled", StringComparison.OrdinalIgnoreCase);
                var isConnected = parts[1].Equals("connected", StringComparison.OrdinalIgnoreCase);
                if (!isAdminEnabled || !isConnected) continue;

                var name = string.Join(' ', parts[3..]).Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    adapters.Add(name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate network adapters");
        }

        return adapters;
    }

    public async Task<bool> ResetDnsForAdapterAsync(string adapterName)
    {
        if (!WindowsPrivilegeChecker.IsRunningElevated())
        {
            _logger.LogWarning("Cannot reset DNS for adapter {Adapter} without administrator privileges", adapterName);
            return false;
        }

        return await RunNetshAsync($"interface ip set dns \"{adapterName}\" dhcp");
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
                _logger.LogWarning("netsh {Args} exited with code {Code}: {Output}",
                    arguments, process.ExitCode, output);
            }
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run netsh {Args}", arguments);
            return false;
        }
    }

    private static string RunNetshSync(string arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        process.Start();
        return process.StandardOutput.ReadToEnd();
    }
}

public sealed record DnsEnforcementOptions(string PrimaryDns, string? SecondaryDns)
{
    public static DnsEnforcementOptions OpenDns { get; } = new("208.67.222.222", "208.67.220.220");
    public static DnsEnforcementOptions CloudFlareFamily { get; } = new("1.1.1.3", "1.0.0.3");
    public static DnsEnforcementOptions CleanBrowsingFamily { get; } = new("185.228.168.168", "185.228.169.168");
    public static DnsEnforcementOptions OpenDnsFamilyShield { get; } = new("208.67.222.123", "208.67.220.123");
}

public sealed record DnsEnforcementResult(int AdaptersUpdated, int TotalAdapters, bool AllSucceeded);
