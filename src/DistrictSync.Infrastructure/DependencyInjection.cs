using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Ingestion;
using DistrictSync.Application.Operations;
using DistrictSync.Infrastructure.Messaging;
using DistrictSync.Infrastructure.Persistence;
using DistrictSync.Infrastructure.Seeding;
using DistrictSync.Infrastructure.Sources;
using DistrictSync.Infrastructure.Staging;
using DistrictSync.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DistrictSync.Infrastructure;

public static class DependencyInjection
{
    public const string SqlConnectionName = "districtsync";
    public const string RabbitConnectionName = "rabbitmq";
    public const string RedisConnectionName = "redis";

    /// <summary>Everything a host needs to talk to SQL Server, RabbitMQ and Redis, plus the persistence unit of work.</summary>
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.Section));
        services.Configure<IngestionOptions>(configuration.GetSection(IngestionOptions.Section));
        services.Configure<MessagingOptions>(configuration.GetSection(MessagingOptions.Section));
        services.Configure<WorkflowOptions>(configuration.GetSection(WorkflowOptions.Section));
        services.Configure<WriteBackOptions>(configuration.GetSection(WriteBackOptions.Section));

        var connectionString = configuration.GetConnectionString(SqlConnectionName)
            ?? throw new InvalidOperationException($"Connection string '{SqlConnectionName}' is not configured.");

        // Scoped factory: contexts created from it see the scope's TenantContext, so tenant filters work in
        // background jobs exactly as they do in web requests.
        services.AddDbContextFactory<AppDbContext>((sp, options) =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
                sql.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema);
                sql.CommandTimeout(120);
            });
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll);
            if (sp.GetRequiredService<IHostEnvironment>().IsDevelopment())
            {
                options.EnableDetailedErrors();
            }
        }, ServiceLifetime.Scoped);
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<DomainEventDispatcher>();
        services.AddScoped<OutboxWriter>();
        services.AddScoped<IOutbox>(sp => sp.GetRequiredService<OutboxWriter>());
        services.AddScoped<ISourceRecordStreamReader, SqlSourceRecordStreamReader>();
        services.AddScoped<IWorkflowStore, SqlWorkflowStore>();
        services.AddSingleton<DatabaseInitializer>();
        services.AddScoped<DemoDataSeeder>();

        builder.AddRabbitMQClient(RabbitConnectionName);
        services.AddSingleton<RabbitMqPublisher>();
        services.AddSingleton<IEphemeralEventPublisher>(sp => sp.GetRequiredService<RabbitMqPublisher>());
        services.AddSingleton<MessagePipeline>();
        services.AddScoped<IDeadLetterReplayer, DeadLetterReplayer>();

        builder.AddRedisDistributedCache(RedisConnectionName);
        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new Microsoft.Extensions.Caching.Hybrid.HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(10),
                LocalCacheExpiration = TimeSpan.FromMinutes(1),
            };
        });

        services.AddSingleton<IStagingStore, FileSystemStagingStore>();
        services.AddHttpClient(RestPullRecordSource.ClientName, client => client.Timeout = TimeSpan.FromSeconds(60));
        services.AddScoped<CsvRecordSource>();
        services.AddScoped<RestPullRecordSource>();
        services.AddScoped<IRawRecordSourceResolver, RawRecordSourceResolver>();
        services.AddScoped<IWriteBackConnector, SimulatedWriteBackConnector>();
        services.AddSingleton<ITenantWorkLimiter, TenantWorkLimiter>();

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("sql", tags: ["ready"]);

        return builder;
    }

    /// <summary>Subscribes this process to queues. Each call adds a subscription; a single consumer host serves them all.</summary>
    public static IServiceCollection AddMessageConsumer(this IServiceCollection services, string queueName, params string[] routingKeys)
    {
        services.AddSingleton(new QueueSubscription(queueName, routingKeys));
        services.AddHostedService<RabbitMqConsumerHost>();
        return services;
    }

    public static IServiceCollection AddOutboxDispatcher(this IServiceCollection services) => services.AddHostedService<OutboxDispatcher>();

    public static IServiceCollection AddDeadLetterConsumer(this IServiceCollection services) => services.AddHostedService<DeadLetterConsumer>();

    public static IServiceCollection AddWorkflowRunner(this IServiceCollection services) => services.AddHostedService<WorkflowRunner>();
}
