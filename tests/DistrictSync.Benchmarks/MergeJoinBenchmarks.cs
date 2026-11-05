using BenchmarkDotNet.Attributes;
using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Reconciliation;
using DistrictSync.Domain.Reconciliation;

namespace DistrictSync.Benchmarks;

/// <summary>
/// Throughput and allocation profile of the streaming merge-join over in-memory ordered streams. This
/// isolates the algorithm from I/O; the SQL reader adds roughly one round-trip per 5,000 rows on top.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class MergeJoinBenchmarks
{
    private KeyedRecord[] _left = [];
    private KeyedRecord[] _right = [];
    private FieldComparer _comparer = FieldComparer.CompareAll;

    [Params(100_000, 1_000_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(42);
        _left = new KeyedRecord[Rows];
        var right = new List<KeyedRecord>(Rows);
        for (var i = 0; i < Rows; i++)
        {
            var key = $"employee{i:D8}@northgate.edu";
            var salary = 40_000 + (i % 50_000);
            _left[i] = new KeyedRecord(Guid.CreateVersion7(), key, $"E{i}", $$"""{"firstName":"First{{i}}","lastName":"Last{{i}}","department":"Dept{{i % 40}}","title":"Title{{i % 12}}","salary":"{{salary}}","phone":"555010{{i % 10000:D4}}"}""", null);

            var roll = rng.NextDouble();
            if (roll < 0.01)
            {
                continue; // missing in right
            }

            var conflict = roll < 0.03;
            right.Add(new KeyedRecord(Guid.CreateVersion7(), key, $"P{i}", $$"""{"firstName":"First{{i}}","lastName":"Last{{i}}","department":"Dept{{i % 40}}","title":"Title{{i % 12}}","salary":"{{(conflict ? salary + 500 : salary)}}","phone":"555010{{i % 10000:D4}}"}""", null));
        }

        _right = right.ToArray();
        _comparer = new FieldComparer(
        [
            new ComparedField("firstName", FieldNormalizer.WhitespaceCollapse, false),
            new ComparedField("lastName", FieldNormalizer.WhitespaceCollapse, false),
            new ComparedField("department", FieldNormalizer.CaseInsensitive, false),
            new ComparedField("title", FieldNormalizer.CaseInsensitive, false),
            new ComparedField("salary", FieldNormalizer.Decimal, true),
            new ComparedField("phone", FieldNormalizer.PhoneDigits, false),
        ]);
    }

    [Benchmark(Baseline = true)]
    public async Task<int> MergeWithProfile()
    {
        var engine = new ReconciliationEngine(_comparer);
        var count = 0;
        await foreach (var outcome in engine.MergeAsync(AsStream(_left), AsStream(_right)))
        {
            if (outcome is not MatchedOutcome)
            {
                count++;
            }
        }

        return count;
    }

    [Benchmark]
    public async Task<int> MergeCompareAllFields()
    {
        var engine = new ReconciliationEngine(FieldComparer.CompareAll);
        var count = 0;
        await foreach (var outcome in engine.MergeAsync(AsStream(_left), AsStream(_right)))
        {
            if (outcome is not MatchedOutcome)
            {
                count++;
            }
        }

        return count;
    }

    private static async IAsyncEnumerable<KeyedRecord> AsStream(KeyedRecord[] records)
    {
        foreach (var r in records)
        {
            yield return r;
        }

        await Task.CompletedTask;
    }
}
