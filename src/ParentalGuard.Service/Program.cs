using ParentalGuard.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Services.AddSingleton(DnsEnforcementOptions.OpenDns);
builder.Services.AddHostedService<DnsEnforcementService>();

var host = builder.Build();
host.Run();
