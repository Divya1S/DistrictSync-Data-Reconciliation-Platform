using DistrictSync.Application;
using DistrictSync.Contracts.Events;
using DistrictSync.Infrastructure;

namespace DistrictSync.Worker;

public static class WorkerHostExtensions
{
    /// <summary>Registers everything the worker process runs: consumers, outbox dispatcher, dead-letter parking and the workflow runner.</summary>
    public static IHostApplicationBuilder AddDistrictSyncWorker(this IHostApplicationBuilder builder)
    {
        builder.AddServiceDefaults();
        builder.Services.AddApplication();
        builder.Services.AddWorkerEventHandlers();
        builder.AddInfrastructure();

        // Work queues. Each queue is a competing-consumer group; routing keys are the integration events it handles.
        builder.Services
            .AddMessageConsumer("worker.ingestion", EventCatalog.NameOf<IngestionBatchReceivedEvent>())
            .AddMessageConsumer("worker.reconciliation", EventCatalog.NameOf<ReconciliationRequestedEvent>())
            .AddMessageConsumer("worker.rules", EventCatalog.NameOf<RuleSetPublishedEvent>());

        builder.Services.AddOutboxDispatcher();
        builder.Services.AddDeadLetterConsumer();
        builder.Services.AddWorkflowRunner();
        builder.Services.AddHostedService<StartupInitializer>();
        return builder;
    }
}
