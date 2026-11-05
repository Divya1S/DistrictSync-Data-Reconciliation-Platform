using DistrictSync.Application.Abstractions;
using DistrictSync.Domain.Ingestion;
using DistrictSync.Domain.Sources;

namespace DistrictSync.Application.Ingestion;

/// <summary>Picks the reader for a batch: staged CSV, remote REST pull, etc. Implemented in Infrastructure.</summary>
public interface IRawRecordSourceResolver
{
    IRawRecordSource Resolve(IngestionBatch batch, SourceSystem source);
}

public static class StagingSchemes
{
    public const string RestPull = "rest://pull";
    public const string Inline = "inline://webhook";
}
