using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Reconciliation;

namespace DistrictSync.UnitTests.Reconciliation;

public class ReconciliationEngineTests
{
    private static KeyedRecord R(string key, string payload = "{}", string? externalId = null, DateTimeOffset? updatedAt = null) =>
        new(Guid.CreateVersion7(), key, externalId ?? key, payload, updatedAt);

    private static async IAsyncEnumerable<KeyedRecord> Stream(params KeyedRecord[] records)
    {
        foreach (var r in records)
        {
            await Task.Yield();
            yield return r;
        }
    }

    private static async Task<List<MergeOutcome>> Merge(ReconciliationEngine engine, KeyedRecord[] left, KeyedRecord[] right)
    {
        var outcomes = new List<MergeOutcome>();
        await foreach (var o in engine.MergeAsync(Stream(left), Stream(right), TestContext.Current.CancellationToken))
        {
            outcomes.Add(o);
        }

        return outcomes;
    }

    [Fact]
    public async Task Classifies_matched_conflicting_and_missing_records()
    {
        var engine = new ReconciliationEngine(FieldComparer.CompareAll);
        var outcomes = await Merge(engine,
            [R("a", """{"x":"1"}"""), R("b", """{"x":"1"}"""), R("d", """{"x":"1"}""")],
            [R("a", """{"x":"1"}"""), R("b", """{"x":"2"}"""), R("c", """{"x":"1"}""")]);

        outcomes.Select(o => (o.GetType().Name, o.MatchKey)).ShouldBe(
        [
            (nameof(MatchedOutcome), "a"),
            (nameof(ConflictOutcome), "b"),
            (nameof(MissingOutcome), "c"),
            (nameof(MissingOutcome), "d"),
        ]);

        outcomes.OfType<MissingOutcome>().Single(m => m.MatchKey == "c").MissingIn.ShouldBe(StreamSide.Left);
        outcomes.OfType<MissingOutcome>().Single(m => m.MatchKey == "d").MissingIn.ShouldBe(StreamSide.Right);
        outcomes.OfType<ConflictOutcome>().Single().Differences.Single().Field.ShouldBe("x");
        engine.LeftRead.ShouldBe(3);
        engine.RightRead.ShouldBe(3);
    }

    [Fact]
    public async Task Reports_duplicates_within_a_side_and_still_compares_the_newest()
    {
        var engine = new ReconciliationEngine(FieldComparer.CompareAll);
        var old = R("k", """{"x":"old"}""", "k-1", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var newer = R("k", """{"x":"new"}""", "k-2", new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));

        var outcomes = await Merge(engine, [old, newer], [R("k", """{"x":"new"}""")]);

        outcomes.Count.ShouldBe(2);
        var dup = outcomes[0].ShouldBeOfType<DuplicateOutcome>();
        dup.Side.ShouldBe(StreamSide.Left);
        dup.Records.Count.ShouldBe(2);
        outcomes[1].ShouldBeOfType<MatchedOutcome>().Left.ExternalId.ShouldBe("k-2");
    }

    [Fact]
    public async Task Handles_empty_sides()
    {
        var engine = new ReconciliationEngine(FieldComparer.CompareAll);
        (await Merge(engine, [], [])).ShouldBeEmpty();
        (await Merge(engine, [R("a")], [])).Single().ShouldBeOfType<MissingOutcome>().MissingIn.ShouldBe(StreamSide.Right);
        (await Merge(engine, [], [R("a")])).Single().ShouldBeOfType<MissingOutcome>().MissingIn.ShouldBe(StreamSide.Left);
    }

    [Fact]
    public async Task Uses_ordinal_ordering()
    {
        // Ordinal: "A" (65) < "a" (97) < "b"; a culture/case-insensitive sort would interleave differently.
        var engine = new ReconciliationEngine(FieldComparer.CompareAll);
        var outcomes = await Merge(engine, [R("A"), R("a"), R("b")], [R("A"), R("a"), R("b")]);
        outcomes.ShouldAllBe(o => o is MatchedOutcome);
    }

    [Fact]
    public async Task Rejects_out_of_order_input()
    {
        var engine = new ReconciliationEngine(FieldComparer.CompareAll);
        await Should.ThrowAsync<InvalidOperationException>(() => Merge(engine, [R("b"), R("a")], []));
    }

    [Fact]
    public async Task Streams_large_inputs_without_buffering()
    {
        const int n = 50_000;
        var engine = new ReconciliationEngine(FieldComparer.CompareAll);
        var left = Enumerable.Range(0, n).Select(i => R($"k{i:D7}", $$$"""{"v":"{{{i}}}"}""")).ToArray();
        var right = Enumerable.Range(0, n).Where(i => i % 1000 != 0).Select(i => R($"k{i:D7}", $$$"""{"v":"{{{(i % 777 == 0 ? -1 : i)}}}"}""")).ToArray();

        var outcomes = await Merge(engine, left, right);
        outcomes.Count(o => o is MissingOutcome).ShouldBe(n / 1000);
        outcomes.Count(o => o is ConflictOutcome).ShouldBe(Enumerable.Range(0, n).Count(i => i % 1000 != 0 && i % 777 == 0));
        outcomes.Count(o => o is MatchedOutcome).ShouldBe(n - outcomes.Count(o => o is not MatchedOutcome));
    }
}
