using System.Runtime.CompilerServices;
using DistrictSync.Application.Abstractions;
using DistrictSync.Domain.Reconciliation;

namespace DistrictSync.Application.Reconciliation;

public enum StreamSide
{
    Left = 1,
    Right = 2,
}

public abstract record MergeOutcome(string MatchKey);

public sealed record MatchedOutcome(string MatchKey, KeyedRecord Left, KeyedRecord Right) : MergeOutcome(MatchKey);

public sealed record ConflictOutcome(string MatchKey, KeyedRecord Left, KeyedRecord Right, IReadOnlyList<FieldDifference> Differences) : MergeOutcome(MatchKey);

public sealed record MissingOutcome(string MatchKey, StreamSide MissingIn, KeyedRecord Present) : MergeOutcome(MatchKey);

public sealed record DuplicateOutcome(string MatchKey, StreamSide Side, IReadOnlyList<KeyedRecord> Records) : MergeOutcome(MatchKey);

/// <summary>
/// Streaming sort-merge join over two match-key-ordered record streams. Reads each side exactly once,
/// holds at most one key-group per side in memory, and never materialises either data set. Both streams
/// must be ordered by ordinal (binary) comparison of MatchKey, which the SQL schema guarantees with a
/// binary collation on that column.
/// </summary>
public sealed class ReconciliationEngine(FieldComparer comparer)
{
    public long LeftRead { get; private set; }
    public long RightRead { get; private set; }

    public async IAsyncEnumerable<MergeOutcome> MergeAsync(IAsyncEnumerable<KeyedRecord> left, IAsyncEnumerable<KeyedRecord> right, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var leftGroups = GroupByKey(left, StreamSide.Left, ct).GetAsyncEnumerator(ct);
        var rightGroups = GroupByKey(right, StreamSide.Right, ct).GetAsyncEnumerator(ct);
        try
        {
            var hasLeft = await leftGroups.MoveNextAsync();
            var hasRight = await rightGroups.MoveNextAsync();

            while (hasLeft || hasRight)
            {
                var l = hasLeft ? leftGroups.Current : null;
                var r = hasRight ? rightGroups.Current : null;
                var cmp = (l, r) switch
                {
                    (null, _) => 1,
                    (_, null) => -1,
                    _ => string.CompareOrdinal(l.Key, r.Key),
                };

                if (cmp == 0)
                {
                    foreach (var dup in Duplicates(l!))
                    {
                        yield return dup;
                    }

                    foreach (var dup in Duplicates(r!))
                    {
                        yield return dup;
                    }

                    var differences = comparer.Compare(l!.Primary.PayloadJson, r!.Primary.PayloadJson);
                    yield return differences.Count == 0
                        ? new MatchedOutcome(l.Key, l.Primary, r.Primary)
                        : new ConflictOutcome(l.Key, l.Primary, r.Primary, differences);

                    hasLeft = await leftGroups.MoveNextAsync();
                    hasRight = await rightGroups.MoveNextAsync();
                }
                else if (cmp < 0)
                {
                    foreach (var dup in Duplicates(l!))
                    {
                        yield return dup;
                    }

                    yield return new MissingOutcome(l!.Key, StreamSide.Right, l.Primary);
                    hasLeft = await leftGroups.MoveNextAsync();
                }
                else
                {
                    foreach (var dup in Duplicates(r!))
                    {
                        yield return dup;
                    }

                    yield return new MissingOutcome(r!.Key, StreamSide.Left, r.Primary);
                    hasRight = await rightGroups.MoveNextAsync();
                }
            }
        }
        finally
        {
            await leftGroups.DisposeAsync();
            await rightGroups.DisposeAsync();
        }
    }

    private static IEnumerable<MergeOutcome> Duplicates(KeyGroup group)
    {
        if (group.Records.Count > 1)
        {
            yield return new DuplicateOutcome(group.Key, group.Side, group.Records);
        }
    }

    /// <summary>Collapses consecutive records sharing a key. Also validates ordering: an out-of-order stream is a bug, not a data issue.</summary>
    private async IAsyncEnumerable<KeyGroup> GroupByKey(IAsyncEnumerable<KeyedRecord> source, StreamSide side, [EnumeratorCancellation] CancellationToken ct)
    {
        string? currentKey = null;
        var buffer = new List<KeyedRecord>(1);

        await foreach (var record in source.WithCancellation(ct))
        {
            Count(side);
            if (currentKey is not null && string.CompareOrdinal(record.MatchKey, currentKey) < 0)
            {
                throw new InvalidOperationException($"{side} stream is not ordered by match key: '{record.MatchKey}' after '{currentKey}'.");
            }

            if (currentKey is not null && !string.Equals(record.MatchKey, currentKey, StringComparison.Ordinal))
            {
                yield return new KeyGroup(currentKey, side, buffer);
                buffer = new List<KeyedRecord>(1);
            }

            currentKey = record.MatchKey;
            buffer.Add(record);
        }

        if (currentKey is not null)
        {
            yield return new KeyGroup(currentKey, side, buffer);
        }
    }

    private void Count(StreamSide side)
    {
        if (side == StreamSide.Left)
        {
            LeftRead++;
        }
        else
        {
            RightRead++;
        }
    }

    private sealed record KeyGroup(string Key, StreamSide Side, IReadOnlyList<KeyedRecord> Records)
    {
        /// <summary>When a source has duplicates, the most recently updated record represents it in the comparison.</summary>
        public KeyedRecord Primary => Records.Count == 1 ? Records[0] : Records.OrderByDescending(r => r.SourceUpdatedAt ?? DateTimeOffset.MinValue).ThenBy(r => r.Id).First();
    }
}
