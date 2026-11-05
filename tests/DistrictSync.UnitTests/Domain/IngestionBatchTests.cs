using DistrictSync.Domain.Common;
using DistrictSync.Domain.Ingestion;

namespace DistrictSync.UnitTests.Domain;

public class IngestionBatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);

    private static IngestionBatch Receive(bool inline = false) =>
        IngestionBatch.Receive(TenantId.New(), Guid.NewGuid(), "Employee", "fs://x", "abc", "hr.csv", 10, "alice", Now, inline);

    [Fact]
    public void Receiving_raises_event_unless_processed_inline()
    {
        Receive().DomainEvents.OfType<IngestionBatchReceived>().ShouldHaveSingleItem();
        Receive(inline: true).DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Lifecycle_received_processing_completed()
    {
        var b = Receive();
        b.Start(Now);
        b.Status.ShouldBe(IngestionBatchStatus.Processing);
        b.Start(Now.AddMinutes(1)); // resume after crash is allowed
        b.StartedAt.ShouldBe(Now);
        b.Complete(100, 90, 5, 5, Now.AddMinutes(2));
        b.Status.ShouldBe(IngestionBatchStatus.Completed);
        b.StagingReference.ShouldBeNull();
        b.DomainEvents.OfType<IngestionBatchCompleted>().ShouldHaveSingleItem().Accepted.ShouldBe(90);
        Should.Throw<InvalidStateTransitionException>(() => b.Start(Now));
    }

    [Fact]
    public void Cannot_complete_without_starting()
    {
        Should.Throw<InvalidStateTransitionException>(() => Receive().Complete(1, 1, 0, 0, Now));
    }

    [Fact]
    public void Retry_only_from_failed_and_requires_staged_payload()
    {
        var b = Receive();
        Should.Throw<InvalidStateTransitionException>(() => b.Retry(Now));
        b.Start(Now);
        b.Fail("boom", Now);
        b.Status.ShouldBe(IngestionBatchStatus.Failed);
        b.ClearDomainEvents();
        b.Retry(Now.AddMinutes(5));
        b.Status.ShouldBe(IngestionBatchStatus.Received);
        b.FailureReason.ShouldBeNull();
        b.DomainEvents.OfType<IngestionBatchReceived>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Duplicate_marks_terminal_state_with_reference_to_original()
    {
        var b = Receive();
        var original = Guid.NewGuid();
        b.MarkDuplicateOf(original, Now);
        b.Status.ShouldBe(IngestionBatchStatus.Duplicate);
        b.FailureReason!.ShouldContain(original.ToString("D"));
    }
}
