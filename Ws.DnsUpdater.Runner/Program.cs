using Ws.DnsUpdater.Runner;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options => { options.ServiceName = "WS DNS UPDATER"; });
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();