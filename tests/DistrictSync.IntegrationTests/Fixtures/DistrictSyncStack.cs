using System.Net.Http.Headers;
using System.Net.Http.Json;
using DistrictSync.Contracts.Api;
using DistrictSync.Worker;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.MsSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace DistrictSync.IntegrationTests.Fixtures;

/// <summary>
/// The whole stack in one process: real SQL Server, RabbitMQ and Redis in containers, the API via
/// WebApplicationFactory and the worker as a second generic host. Shared by all scenarios in the
/// collection, so containers start once per test run.
/// </summary>
public sealed class DistrictSyncStack : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-management").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7").Build();
    private readonly string _stagingPath = Path.Combine(Path.GetTempPath(), "districtsync-tests", Guid.NewGuid().ToString("N"));

    private WebApplicationFactory<DistrictSync.Api.ApiAssemblyMarker>? _api;
    private IHost? _worker;

    public HttpClient Api { get; private set; } = null!;

    public IServiceProvider WorkerServices => _worker!.Services;

    public IServiceProvider ApiServices => _api!.Services;

    public string SqlConnectionString => _sql.GetConnectionString();

    public string RabbitConnectionString => _rabbit.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_sql.StartAsync(), _rabbit.StartAsync(), _redis.StartAsync());

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:districtsync"] = _sql.GetConnectionString(),
            ["ConnectionStrings:rabbitmq"] = _rabbit.GetConnectionString(),
            ["ConnectionStrings:redis"] = _redis.GetConnectionString(),
            ["Ingestion:StagingPath"] = _stagingPath,
            ["Database:MigrateOnStartup"] = "true",
            ["Database:SeedDemoData"] = "true",
            ["Auth:EnableDevTokens"] = "true",
            ["Messaging:OutboxPollInterval"] = "00:00:00.200",
            ["Workflows:PollInterval"] = "00:00:00.500",
            ["WriteBack:TransientFailureRate"] = "0",
            ["WriteBack:SimulatedLatency"] = "00:00:00.001",
        };

        // UseSetting feeds the host configuration that a minimal-API Program.cs reads while building; a deferred
        // ConfigureAppConfiguration callback would arrive too late for AddInfrastructure().
        _api = new WebApplicationFactory<DistrictSync.Api.ApiAssemblyMarker>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            foreach (var (key, value) in settings)
            {
                b.UseSetting(key, value);
            }
        });
        Api = _api.CreateClient();

        var workerBuilder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Development", ApplicationName = "DistrictSync.Worker" });
        workerBuilder.Configuration.AddInMemoryCollection(settings);
        workerBuilder.AddDistrictSyncWorker();
        _worker = workerBuilder.Build();
        await _worker.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_worker is not null)
        {
            try
            {
                await _worker.StopAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception)
            {
                // the host may never have started; the startup exception is what matters
            }

            _worker.Dispose();
        }

        if (_api is not null)
        {
            await _api.DisposeAsync();
        }

        await Task.WhenAll(_sql.DisposeAsync().AsTask(), _rabbit.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
        if (Directory.Exists(_stagingPath))
        {
            Directory.Delete(_stagingPath, recursive: true);
        }
    }

    public async Task<HttpClient> ClientForAsync(string tenantSlug, string userId, params string[] roles)
    {
        var client = _api!.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/dev-token", new DevTokenRequest(tenantSlug, userId, roles.Length == 0 ? ["admin", "analyst", "approver"] : roles), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<DevTokenResponse>(TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }

    /// <summary>Polls until <paramref name="predicate"/> is satisfied or the timeout elapses; returns the last observed value.</summary>
    public static async Task<T> WaitUntilAsync<T>(Func<Task<T>> probe, Func<T, bool> predicate, TimeSpan? timeout = null, string? what = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(60));
        T last = await probe();
        while (!predicate(last))
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for {what ?? typeof(T).Name}. Last value: {last}");
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
            last = await probe();
        }

        return last;
    }
}

[CollectionDefinition(Name)]
public sealed class StackCollection : ICollectionFixture<DistrictSyncStack>
{
    public const string Name = "stack";
}
