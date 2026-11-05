using DistrictSync.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.AddDistrictSyncWorker();

var host = builder.Build();
await host.RunAsync();
