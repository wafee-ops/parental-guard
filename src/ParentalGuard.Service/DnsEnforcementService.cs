namespace ParentalGuard.Service;

public sealed class DnsEnforcementService : BackgroundService
{
    private readonly ILogger<DnsEnforcementService> _logger;
    private readonly DnsEnforcer _dnsEnforcer;
    private readonly DnsBypassPrevention _bypassPrevention;
    private readonly DnsEnforcementOptions _options;
    private DateTime _lastEnforcementAt = DateTime.MinValue;
    private readonly TimeSpan _enforcementInterval = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _initialDelay = TimeSpan.FromSeconds(10);
    private bool _bypassPreventionApplied;

    public DnsEnforcementService(
        ILogger<DnsEnforcementService> logger,
        ILoggerFactory loggerFactory,
        DnsEnforcementOptions? options = null)
    {
        _logger = logger;
        _options = options ?? DnsEnforcementOptions.OpenDns;
        _dnsEnforcer = new DnsEnforcer(loggerFactory.CreateLogger<DnsEnforcer>(), _options);
        _bypassPrevention = new DnsBypassPrevention(loggerFactory.CreateLogger<DnsBypassPrevention>(), _options);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DNS enforcement service starting with primary DNS {Primary}", _options.PrimaryDns);

        await Task.Delay(_initialDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnforceOnceAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during DNS enforcement cycle");
            }

            await Task.Delay(_enforcementInterval, stoppingToken);
        }

        _logger.LogInformation("DNS enforcement service stopping");
    }

    public async Task EnforceOnceAsync()
    {
        var dnsResult = await _dnsEnforcer.EnforceAsync();
        _logger.LogDebug("DNS enforcement result: {Updated}/{Total} adapters updated",
            dnsResult.AdaptersUpdated, dnsResult.TotalAdapters);

        if (!_bypassPreventionApplied)
        {
            var bypassResult = await _bypassPrevention.EnforceBypassPreventionAsync();
            if (bypassResult.AllSucceeded)
            {
                _bypassPreventionApplied = true;
                _logger.LogInformation("DNS bypass prevention rules applied");
            }
        }

        _lastEnforcementAt = DateTime.Now;
    }

    public async Task RestoreOriginalDnsAsync()
    {
        _logger.LogInformation("Restoring original DNS configuration");

        var adapters = _dnsEnforcer.GetActiveAdapterNames();
        foreach (var adapter in adapters)
        {
            await _dnsEnforcer.ResetDnsForAdapterAsync(adapter);
        }

        await _bypassPrevention.RemoveBypassPreventionAsync();
        _bypassPreventionApplied = false;
    }
}
