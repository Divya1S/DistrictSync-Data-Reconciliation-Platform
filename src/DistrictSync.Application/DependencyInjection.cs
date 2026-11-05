using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Cases;
using DistrictSync.Application.Dashboard;
using DistrictSync.Application.EventHandlers;
using DistrictSync.Application.Ingestion;
using DistrictSync.Application.Operations;
using DistrictSync.Application.Reconciliation;
using DistrictSync.Application.Rules;
using DistrictSync.Application.Rules.Dsl;
using DistrictSync.Application.Sources;
using DistrictSync.Application.Tenancy;
using DistrictSync.Application.Tenants;
using DistrictSync.Application.Workflows;
using DistrictSync.Contracts.Events;
using DistrictSync.Domain.Cases;
using DistrictSync.Domain.Common;
using DistrictSync.Domain.Ingestion;
using DistrictSync.Domain.Reconciliation;
using DistrictSync.Domain.Rules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DistrictSync.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        services.AddSingleton<RuleExpressionCache>();
        services.AddSingleton<RuleEngine>();
        services.AddScoped<IActiveRuleSetProvider, ActiveRuleSetProvider>();
        services.AddScoped<RuleActionApplier>();
        services.AddScoped<RuleSetService>();

        services.AddScoped<SourceRecordUpserter>();
        services.AddScoped<IngestionService>();
        services.AddScoped<IngestionBatchProcessor>();

        services.AddScoped<ReconciliationService>();
        services.AddScoped<ReconciliationJob>();

        services.AddScoped<CaseService>();
        services.AddScoped<WorkflowScheduler>();
        services.AddScoped<IWorkflowDefinition, CaseSlaWorkflow>();
        services.AddScoped<IWorkflowDefinition, ApplyResolutionWorkflow>();

        services.AddScoped<SourceSystemService>();
        services.AddScoped<TenantService>();
        services.AddScoped<DashboardQueryService>();
        services.AddScoped<OperationsService>();

        // Domain → outbox relays
        services.AddScoped<IDomainEventHandler<IngestionBatchReceived>, OutboxRelayHandlers>();
        services.AddScoped<IDomainEventHandler<IngestionBatchCompleted>, OutboxRelayHandlers>();
        services.AddScoped<IDomainEventHandler<IngestionBatchFailed>, OutboxRelayHandlers>();
        services.AddScoped<IDomainEventHandler<ReconciliationRequested>, OutboxRelayHandlers>();
        services.AddScoped<IDomainEventHandler<ReconciliationCompleted>, OutboxRelayHandlers>();
        services.AddScoped<IDomainEventHandler<ReconciliationFailed>, OutboxRelayHandlers>();
        services.AddScoped<IDomainEventHandler<RuleSetPublished>, OutboxRelayHandlers>();
        services.AddScoped<IDomainEventHandler<CaseOpened>, OutboxRelayHandlers>();
        services.AddScoped<IDomainEventHandler<CaseStateChanged>, OutboxRelayHandlers>();
        services.AddScoped<IDomainEventHandler<CaseApproved>, OutboxRelayHandlers>();
        services.AddScoped<IDomainEventHandler<CaseEscalated>, OutboxRelayHandlers>();

        return services;
    }

    /// <summary>Handlers that only the worker should run (the API registers its own set for live dashboard pushes).</summary>
    public static IServiceCollection AddWorkerEventHandlers(this IServiceCollection services)
    {
        services.AddScoped<IIntegrationEventHandler<IngestionBatchReceivedEvent>, IngestionBatchReceivedHandler>();
        services.AddScoped<IIntegrationEventHandler<ReconciliationRequestedEvent>, ReconciliationRequestedHandler>();
        services.AddScoped<IIntegrationEventHandler<RuleSetPublishedEvent>, RuleSetPublishedHandler>();
        return services;
    }
}
