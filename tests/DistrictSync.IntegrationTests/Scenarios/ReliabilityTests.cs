using System.Net.Http.Json;
using System.Text.Json;
using DistrictSync.Application.Common;
using DistrictSync.Application.Tenancy;
using DistrictSync.Contracts.Api;
using DistrictSync.Contracts.Events;
using DistrictSync.Domain.Common;
using DistrictSync.Domain.Reconciliation;
using DistrictSync.Domain.Workflows;
using DistrictSync.Infrastructure.Messaging;
using DistrictSync.Infrastructure.Persistence;
using DistrictSync.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DistrictSync.IntegrationTests.Scenarios;

/// <summary>The failure-mode guarantees: duplicates, poison messages, crashed workers, cross-tenant access, lost updates.</summary>
[Collection(StackCollection.Name)]
public class ReliabilityTests(DistrictSyncStack stack)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Delivering_the_same_message_twice_causes_one_business_effect()
    {
        var client = await stack.ClientForAsync("northgate", "alice");
        var tenantId = (await client.GetFromJsonAsync<TenantDto>("/api/v1/tenants/me", Ct))!.Id;
        var sources = (await client.GetFromJsonAsync<List<SourceSystemDto>>("/api/v1/source-systems", Ct))!;

        // Create a run row directly (no outbox event) so we control delivery ourselves.
        Guid runId;
        await using (var scope = stack.WorkerServices.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetSystemForTenant(new TenantId(tenantId), "test");
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var run = ReconciliationRun.Request(new TenantId(tenantId), "Employee", sources[0].Id, sources[1].Id, null, "test", DateTimeOffset.UtcNow);
            run.ClearDomainEvents();
            db.ReconciliationRuns.Add(run);
            await db.SaveChangesAsync(Ct);
            runId = run.Id;
        }

        var publisher = stack.WorkerServices.GetRequiredService<RabbitMqPublisher>();
        var messageId = Guid.CreateVersion7();
        var evt = new ReconciliationRequestedEvent(messageId, tenantId, DateTimeOffset.UtcNow, runId, "Employee", sources[0].Id, sources[1].Id);
        var payload = JsonSerializer.Serialize(evt, JsonDefaults.Options);
        var name = EventCatalog.NameOf<ReconciliationRequestedEvent>();
        for (var i = 0; i < 3; i++)
        {
            await publisher.PublishAsync(EventCatalog.Exchange, name, messageId, name, tenantId, payload, null, 0, null, Ct);
        }

        await DistrictSyncStack.WaitUntilAsync(() => client.GetFromJsonAsync<ReconciliationRunDto>($"/api/v1/reconciliation/runs/{runId}", Ct)!, r => r!.Status is "Completed" or "Failed", what: "run");

        await using (var scope = stack.WorkerServices.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetSystem("test");
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var inbox = await DistrictSyncStack.WaitUntilAsync(
                () => db.InboxMessages.AsNoTracking().Where(m => m.MessageId == messageId).ToListAsync(Ct),
                rows => rows.Count == 1 && rows[0].IsProcessed,
                what: "inbox row");
            inbox.ShouldHaveSingleItem().Consumer.ShouldBe("worker.reconciliation");

            // Idempotency at the business level: one run, processed once, no doubled discrepancies.
            var run = await db.ReconciliationRuns.AsNoTracking().FirstAsync(r => r.Id == runId, Ct);
            var discrepancyCount = await db.Discrepancies.AsNoTracking().CountAsync(d => d.RunId == runId, Ct);
            discrepancyCount.ShouldBe((int)run.Stats.TotalDiscrepancies);
        }
    }

    [Fact]
    public async Task Poison_messages_are_parked_in_the_dead_letter_store()
    {
        var client = await stack.ClientForAsync("northgate", "alice");
        var tenantId = (await client.GetFromJsonAsync<TenantDto>("/api/v1/tenants/me", Ct))!.Id;
        var publisher = stack.WorkerServices.GetRequiredService<RabbitMqPublisher>();

        var messageId = Guid.CreateVersion7();
        var name = EventCatalog.NameOf<ReconciliationRequestedEvent>();
        await publisher.PublishAsync(EventCatalog.Exchange, name, messageId, name, tenantId, "{ this is not json", null, 0, null, Ct);

        var parked = await DistrictSyncStack.WaitUntilAsync(
            () => client.GetFromJsonAsync<PagedResult<DeadLetterDto>>("/api/v1/operations/dead-letters?pageSize=100", Ct)!,
            page => page!.Items.Any(d => d.Payload.Contains("this is not json", StringComparison.Ordinal)),
            what: "dead letter");

        var letter = parked!.Items.Single(d => d.Payload.Contains("this is not json", StringComparison.Ordinal));
        letter.Queue.ShouldBe("worker.reconciliation");
        letter.Reason.ShouldContain("Malformed payload");
    }

    [Fact]
    public async Task Job_failure_is_recorded_on_the_run_and_the_message_is_acknowledged()
    {
        var client = await stack.ClientForAsync("northgate", "alice");
        var tenantId = (await client.GetFromJsonAsync<TenantDto>("/api/v1/tenants/me", Ct))!.Id;
        var publisher = stack.WorkerServices.GetRequiredService<RabbitMqPublisher>();

        // A run whose source systems do not exist cannot be executed: the job records the failure on the run
        // (a business outcome operators can see) and acknowledges the message instead of retrying forever.
        Guid runId;
        await using (var scope = stack.WorkerServices.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetSystemForTenant(new TenantId(tenantId), "test");
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var brokenRun = ReconciliationRun.Request(new TenantId(tenantId), "Employee", Guid.NewGuid(), Guid.NewGuid(), null, "test", DateTimeOffset.UtcNow);
            brokenRun.ClearDomainEvents();
            db.ReconciliationRuns.Add(brokenRun);
            await db.SaveChangesAsync(Ct);
            runId = brokenRun.Id;
        }

        var messageId = Guid.CreateVersion7();
        var name = EventCatalog.NameOf<ReconciliationRequestedEvent>();
        var evt = new ReconciliationRequestedEvent(messageId, tenantId, DateTimeOffset.UtcNow, runId, "Employee", Guid.NewGuid(), Guid.NewGuid());
        await publisher.PublishAsync(EventCatalog.Exchange, name, messageId, name, tenantId, JsonSerializer.Serialize(evt, JsonDefaults.Options), null, 0, null, Ct);

        var run = await DistrictSyncStack.WaitUntilAsync(() => client.GetFromJsonAsync<ReconciliationRunDto>($"/api/v1/reconciliation/runs/{runId}", Ct)!, r => r!.Status == "Failed", what: "failed run");
        run!.FailureReason.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Workflow_abandoned_by_a_crashed_worker_is_picked_up_after_its_lease_expires()
    {
        var client = await stack.ClientForAsync("northgate", "alice");
        var tenantId = new TenantId((await client.GetFromJsonAsync<TenantDto>("/api/v1/tenants/me", Ct))!.Id);

        Guid caseId;
        Guid instanceId;
        await using (var scope = stack.WorkerServices.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetSystemForTenant(tenantId, "test");
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var past = DateTimeOffset.UtcNow.AddHours(-2);
            var c = await NewCaseAsync(db, tenantId, "Crash recovery case", Domain.Rules.CasePriority.Urgent, past.AddHours(-5));
            db.Cases.Add(c);
            caseId = c.Id;

            // Simulate: a worker claimed the SLA timer and died. Lease is long expired.
            var instance = WorkflowInstance.Schedule(tenantId, Application.Workflows.WorkflowTypes.CaseSla, c.Id, JsonSerializer.Serialize(new Application.Rules.CaseSlaState(c.Id, 0), JsonDefaults.Options), past, past);
            instance.Claim("dead-worker", TimeSpan.FromSeconds(1), past);
            db.WorkflowInstances.Add(instance);
            instanceId = instance.Id;
            await db.SaveChangesAsync(Ct);
        }

        var detail = await DistrictSyncStack.WaitUntilAsync(() => client.GetFromJsonAsync<CaseDetailDto>($"/api/v1/cases/{caseId}", Ct)!, d => d!.Case.State == "Escalated", what: "escalation by recovered workflow");
        detail!.Case.EscalationLevel.ShouldBe(1);
        detail.History.ShouldContain(h => h.Action == "escalate");

        await using (var scope = stack.WorkerServices.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetSystem("test");
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var instance = await db.WorkflowInstances.AsNoTracking().FirstAsync(w => w.Id == instanceId, Ct);
            instance.LockOwner.ShouldNotBe("dead-worker");
            instance.Status.ShouldBe(WorkflowStatus.Scheduled);
            instance.WakeUpAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(30));
        }
    }

    [Fact]
    public async Task Tenants_cannot_see_each_others_data()
    {
        var admin = await stack.ClientForAsync("northgate", "alice", "admin");
        var created = await admin.PostAsJsonAsync("/api/v1/tenants", new CreateTenantRequest("Eastbrook ISD", $"eastbrook-{Guid.NewGuid():N}"[..40]), Ct);
        created.EnsureSuccessStatusCode();
        var other = (await created.Content.ReadFromJsonAsync<TenantDto>(Ct))!;

        var northgate = (await admin.GetFromJsonAsync<TenantDto>("/api/v1/tenants/me", Ct))!;
        var northgateRuns = (await admin.GetFromJsonAsync<PagedResult<ReconciliationRunDto>>("/api/v1/reconciliation/runs", Ct))!;
        var northgateSources = (await admin.GetFromJsonAsync<List<SourceSystemDto>>("/api/v1/source-systems", Ct))!;
        northgateSources.ShouldNotBeEmpty();

        var otherClient = await stack.ClientForAsync(other.Slug, "bob", "admin");
        (await otherClient.GetFromJsonAsync<List<SourceSystemDto>>("/api/v1/source-systems", Ct))!.ShouldBeEmpty();
        (await otherClient.GetFromJsonAsync<PagedResult<ReconciliationRunDto>>("/api/v1/reconciliation/runs", Ct))!.TotalCount.ShouldBe(0);
        (await otherClient.GetFromJsonAsync<PagedResult<CaseDto>>("/api/v1/cases", Ct))!.TotalCount.ShouldBe(0);

        var probe = await otherClient.GetAsync($"/api/v1/source-systems/{northgateSources[0].Id}", Ct);
        probe.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);

        if (northgateRuns.Items.Count > 0)
        {
            (await otherClient.GetAsync($"/api/v1/reconciliation/runs/{northgateRuns.Items[0].Id}", Ct)).StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
        }

        // Even a direct write tagged with another tenant's id is refused by the unit of work.
        await using var scope = stack.ApiServices.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(new TenantId(other.Id), "bob", ["admin"]);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.SourceSystems.Add(Domain.Sources.SourceSystem.Create(new TenantId(northgate.Id), "Smuggled", "SMUGGLED", Domain.Sources.SourceSystemKind.Webhook, 1, "{}", DateTimeOffset.UtcNow));
        await Should.ThrowAsync<TenantIsolationViolationException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Stale_etag_is_rejected_with_412_and_fresh_etag_succeeds()
    {
        var client = await stack.ClientForAsync("northgate", "alice");
        var tenantId = new TenantId((await client.GetFromJsonAsync<TenantDto>("/api/v1/tenants/me", Ct))!.Id);

        Guid caseId;
        await using (var scope = stack.ApiServices.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetSystemForTenant(tenantId, "test");
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = await NewCaseAsync(db, tenantId, "ETag case", Domain.Rules.CasePriority.Low, DateTimeOffset.UtcNow);
            db.Cases.Add(c);
            await db.SaveChangesAsync(Ct);
            caseId = c.Id;
        }

        var detail = (await client.GetFromJsonAsync<CaseDetailDto>($"/api/v1/cases/{caseId}", Ct))!;
        var etag = detail.Case.ETag;

        using var first = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/cases/{caseId}/assign") { Content = JsonContent.Create(new AssignCaseRequest("carol")) };
        first.Headers.TryAddWithoutValidation("If-Match", etag);
        var ok = await client.SendAsync(first, Ct);
        ok.EnsureSuccessStatusCode();
        var updated = (await ok.Content.ReadFromJsonAsync<CaseDto>(Ct))!;
        updated.ETag.ShouldNotBe(etag);

        // Replaying the original ETag against an endpoint that honours If-Match must be refused.
        using var stale = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/cases/{caseId}/assign") { Content = JsonContent.Create(new AssignCaseRequest("dave")) };
        stale.Headers.TryAddWithoutValidation("If-Match", etag);
        var rejected = await client.SendAsync(stale, Ct);
        rejected.StatusCode.ShouldBe(System.Net.HttpStatusCode.PreconditionFailed);

        using var fresh = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/cases/{caseId}/assign") { Content = JsonContent.Create(new AssignCaseRequest("dave")) };
        fresh.Headers.TryAddWithoutValidation("If-Match", updated.ETag);
        (await client.SendAsync(fresh, Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Outbox_rows_are_marked_processed_after_publish()
    {
        var client = await stack.ClientForAsync("northgate", "alice");
        var health = await DistrictSyncStack.WaitUntilAsync(() => client.GetFromJsonAsync<OutboxHealthDto>("/api/v1/operations/outbox", Ct)!, h => h!.Pending == 0, timeout: TimeSpan.FromSeconds(30), what: "outbox drain");
        health!.Pending.ShouldBe(0);

        await using var scope = stack.WorkerServices.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetSystem("test");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var processed = await db.OutboxMessages.AsNoTracking().CountAsync(m => m.ProcessedAt != null, Ct);
        processed.ShouldBeGreaterThan(0);
    }

    /// <summary>A case needs a real discrepancy and run behind it (the API joins them); builds that chain for the ambient tenant.</summary>
    private static async Task<Domain.Cases.ResolutionCase> NewCaseAsync(AppDbContext db, TenantId tenantId, string title, Domain.Rules.CasePriority priority, DateTimeOffset openedAt)
    {
        var sources = await db.SourceSystems.OrderBy(s => s.Code).Take(2).ToListAsync(Ct);
        var run = ReconciliationRun.Request(tenantId, "Employee", sources[0].Id, sources[1].Id, null, "test", openedAt);
        run.ClearDomainEvents();
        var discrepancy = Discrepancy.Detect(tenantId, run.Id, "Employee", DiscrepancyType.FieldConflict, $"case-{Guid.NewGuid():N}", sources[0].Id, sources[1].Id, null, null, [new FieldDifference("salary", "1", "2", true)], openedAt);
        discrepancy.EscalateToCase(null);
        var c = Domain.Cases.ResolutionCase.Open(tenantId, discrepancy.Id, run.Id, title, priority, null, null, openedAt);
        c.ClearDomainEvents();
        db.ReconciliationRuns.Add(run);
        db.Discrepancies.Add(discrepancy);
        return c;
    }
}
