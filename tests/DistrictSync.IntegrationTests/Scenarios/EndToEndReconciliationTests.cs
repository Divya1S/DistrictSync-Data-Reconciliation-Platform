using System.Net;
using System.Net.Http.Json;
using System.Text;
using DistrictSync.Contracts.Api;
using DistrictSync.IntegrationTests.Fixtures;

namespace DistrictSync.IntegrationTests.Scenarios;

[Collection(StackCollection.Name)]
public class EndToEndReconciliationTests(DistrictSyncStack stack)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Csv_upload_flows_through_outbox_worker_and_rules_into_cases()
    {
        var client = await stack.ClientForAsync("northgate", "alice");
        var sources = (await client.GetFromJsonAsync<List<SourceSystemDto>>("/api/v1/source-systems", Ct))!;
        var hr = sources.Single(s => s.Code == "HR");
        var payroll = sources.Single(s => s.Code == "PAYROLL");

        var hrBatch = await UploadAsync(client, hr.Id, "hr_employees.csv");
        var payrollBatch = await UploadAsync(client, payroll.Id, "payroll_employees.csv");

        var hrDone = await DistrictSyncStack.WaitUntilAsync(() => client.GetFromJsonAsync<IngestionBatchDto>($"/api/v1/ingestion/batches/{hrBatch.Id}", Ct)!, b => b!.Status is "Completed" or "Failed", what: "HR batch");
        var payrollDone = await DistrictSyncStack.WaitUntilAsync(() => client.GetFromJsonAsync<IngestionBatchDto>($"/api/v1/ingestion/batches/{payrollBatch.Id}", Ct)!, b => b!.Status is "Completed" or "Failed", what: "payroll batch");

        hrDone!.Status.ShouldBe("Completed", hrDone.FailureReason);
        hrDone.TotalRecords.ShouldBe(8);
        hrDone.AcceptedRecords.ShouldBe(8);
        payrollDone!.Status.ShouldBe("Completed", payrollDone.FailureReason);
        payrollDone.TotalRecords.ShouldBe(8);

        var runResponse = await client.PostAsJsonAsync("/api/v1/reconciliation/runs", new CreateReconciliationRunRequest("Employee", hr.Id, payroll.Id, null), Ct);
        runResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var run = (await runResponse.Content.ReadFromJsonAsync<ReconciliationRunDto>(Ct))!;

        var finished = await DistrictSyncStack.WaitUntilAsync(() => client.GetFromJsonAsync<ReconciliationRunDto>($"/api/v1/reconciliation/runs/{run.Id}", Ct)!, r => r!.Status is "Completed" or "Failed", what: "reconciliation run");
        finished!.Status.ShouldBe("Completed", finished.FailureReason);
        finished.RuleSetVersion.ShouldBe(1);

        // Fixture data: ava matches after normalisation; liam = salary conflict (case); noah = title+phone conflict (auto-resolve, newest);
        // emma = terminated, missing in payroll (ignored); olivia = new hire missing in payroll (urgent case); sofia = duplicate in payroll (case);
        // ghost = missing in HR (default → case).
        finished.Stats.LeftRecords.ShouldBe(8);
        finished.Stats.RightRecords.ShouldBe(8);
        finished.Stats.Matched.ShouldBe(4);
        finished.Stats.FieldConflicts.ShouldBe(2);
        finished.Stats.MissingInRight.ShouldBe(2);
        finished.Stats.MissingInLeft.ShouldBe(1);
        finished.Stats.Duplicates.ShouldBe(1);
        finished.Stats.Ignored.ShouldBe(1);
        finished.Stats.AutoResolved.ShouldBe(1);
        finished.Stats.CasesOpened.ShouldBe(4);

        var cases = (await client.GetFromJsonAsync<PagedResult<CaseDto>>("/api/v1/cases?pageSize=50", Ct))!;
        cases.Items.ShouldContain(c => c.Priority == "Urgent" && c.Title.Contains("olivia", StringComparison.OrdinalIgnoreCase));
        cases.Items.ShouldContain(c => c.Priority == "High" && c.Title.Contains("liam", StringComparison.OrdinalIgnoreCase));

        // The auto-resolution workflow applies the write-back and resolves the discrepancy.
        var discrepancies = await DistrictSyncStack.WaitUntilAsync(
            () => client.GetFromJsonAsync<PagedResult<DiscrepancyDto>>($"/api/v1/reconciliation/runs/{run.Id}/discrepancies?pageSize=50", Ct)!,
            d => d!.Items.Any(x => x.Status == "AutoResolved" && x.MatchKey == "noah.garcia@northgate.edu"),
            what: "auto-resolved discrepancy");
        discrepancies!.Items.Single(x => x.MatchKey == "emma.nguyen@northgate.edu").Status.ShouldBe("Ignored");

        var workflows = await DistrictSyncStack.WaitUntilAsync(
            () => client.GetFromJsonAsync<PagedResult<WorkflowInstanceDto>>("/api/v1/operations/workflows?status=Completed&pageSize=50", Ct)!,
            w => w!.Items.Any(x => x.WorkflowType == "apply-resolution"),
            what: "apply-resolution workflow completion");
        workflows!.Items.ShouldContain(w => w.WorkflowType == "apply-resolution" && w.Status == "Completed");

        var summary = (await client.GetFromJsonAsync<DashboardSummaryDto>("/api/v1/dashboard/summary", Ct))!;
        summary.CasesOpen.ShouldBeGreaterThanOrEqualTo(4);
        summary.ReconciliationCompletedToday.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Identical_upload_is_detected_as_duplicate_and_not_reprocessed()
    {
        var client = await stack.ClientForAsync("northgate", "alice");
        var sources = (await client.GetFromJsonAsync<List<SourceSystemDto>>("/api/v1/source-systems", Ct))!;
        var directory = sources.Single(s => s.Code == "DIRECTORY");

        // Unique content, loaded into a source the end-to-end scenario does not use: scenarios share one stack and
        // snapshot loads soft-delete rows they no longer contain, so sharing a source would make results order-dependent.
        var unique = $"E{Guid.NewGuid():N}"[..12];
        var csv = Encoding.UTF8.GetBytes(
            "employee_id,work_email,first_name,last_name,department,job_title,annual_salary,phone,status,hire_date,last_modified\n" +
            $"{unique},{unique.ToLowerInvariant()}@northgate.edu,Uni,Que,IT,Analyst,50000,555-010-0000,Active,2020-01-01,2026-10-01T09:00:00Z\n");

        var first = await UploadBytesAsync(client, directory.Id, "unique.csv", csv);
        var done = await DistrictSyncStack.WaitUntilAsync(() => client.GetFromJsonAsync<IngestionBatchDto>($"/api/v1/ingestion/batches/{first.Id}", Ct)!, b => b!.Status is "Completed" or "Failed", what: "first upload");
        done!.Status.ShouldBe("Completed", done.FailureReason);

        var second = await UploadBytesAsync(client, directory.Id, "unique.csv", csv);
        second.Status.ShouldBe("Duplicate");
        second.FailureReason!.ShouldContain(first.Id.ToString("D"));
    }

    private static async Task<IngestionBatchDto> UploadAsync(HttpClient client, Guid sourceId, string fileName) =>
        await UploadBytesAsync(client, sourceId, fileName, await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", fileName), Ct));

    private static async Task<IngestionBatchDto> UploadBytesAsync(HttpClient client, Guid sourceId, string fileName, byte[] bytes)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv") } }, "file", fileName);
        var response = await client.PostAsync($"/api/v1/ingestion/batches/csv?sourceSystemId={sourceId}&entityType=Employee", content, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<IngestionBatchDto>(Ct))!;
    }
}
