// .NET Aspire orchestration: one `dotnet run` (or `aspire run`) starts SQL Server, RabbitMQ, Redis,
// the API, the worker and the React dashboard, wires connection strings and OpenTelemetry, and opens
// the Aspire dashboard with traces that cross process boundaries via RabbitMQ.
var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql")
    .WithDataVolume("districtsync-sql")
    .WithLifetime(ContainerLifetime.Persistent);
var database = sql.AddDatabase("districtsync");

var rabbit = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin()
    .WithDataVolume("districtsync-rabbit")
    .WithLifetime(ContainerLifetime.Persistent);

var redis = builder.AddRedis("redis")
    .WithLifetime(ContainerLifetime.Persistent);

// API and worker share a staging directory for uploaded files (a blob store would replace this in production).
var stagingPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", ".staging"));

var worker = builder.AddProject<Projects.DistrictSync_Worker>("worker")
    .WithReference(database)
    .WithReference(rabbit)
    .WithReference(redis)
    .WaitFor(database)
    .WaitFor(rabbit)
    .WaitFor(redis)
    .WithEnvironment("Ingestion__StagingPath", stagingPath)
    .WithReplicas(1);

var api = builder.AddProject<Projects.DistrictSync_Api>("api")
    .WithReference(database)
    .WithReference(rabbit)
    .WithReference(redis)
    .WaitFor(database)
    .WaitFor(rabbit)
    .WaitFor(redis)
    .WithEnvironment("Ingestion__StagingPath", stagingPath)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");

builder.AddViteApp("web", "../DistrictSync.Web")
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();
