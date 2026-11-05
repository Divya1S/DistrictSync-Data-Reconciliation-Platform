using System.Globalization;
using System.Runtime.CompilerServices;
using CsvHelper;
using CsvHelper.Configuration;
using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Ingestion;
using DistrictSync.Domain.Ingestion;
using DistrictSync.Domain.Sources;

namespace DistrictSync.Infrastructure.Sources;

/// <summary>Streams a staged CSV row by row. Never loads the file into memory.</summary>
public sealed class CsvRecordSource(IStagingStore staging) : IRawRecordSource
{
    public async IAsyncEnumerable<RawRecord> ReadAsync(IngestionBatch batch, SourceSystem source, [EnumeratorCancellation] CancellationToken ct)
    {
        var config = SourceConfig.Parse(source.ConfigJson).Csv ?? new CsvConfig();
        await using var stream = await staging.OpenReadAsync(batch.StagingReference ?? throw new InvalidOperationException("Batch has no staged payload."), ct);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = config.Delimiter,
            HasHeaderRecord = config.HasHeader,
            BadDataFound = null,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim,
            DetectColumnCountChanges = false,
        });

        string[] headers;
        if (config.HasHeader)
        {
            if (!await csv.ReadAsync() || !csv.ReadHeader())
            {
                yield break;
            }

            headers = csv.HeaderRecord ?? [];
        }
        else
        {
            headers = [];
        }

        var line = config.HasHeader ? 1L : 0L;
        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();
            line++;
            var count = csv.Parser.Count;
            var attributes = new Dictionary<string, string?>(count, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < count; i++)
            {
                var name = i < headers.Length ? headers[i] : $"column{i + 1}";
                attributes[name] = csv.Parser[i];
            }

            yield return new RawRecord(null, attributes, null, false, line);
        }
    }
}
