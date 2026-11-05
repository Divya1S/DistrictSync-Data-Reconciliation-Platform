namespace DistrictSync.Infrastructure;

public sealed class IngestionOptions
{
    public const string Section = "Ingestion";

    /// <summary>Directory shared by API and worker where uploads are staged until processed.</summary>
    public string StagingPath { get; set; } = Path.Combine(Path.GetTempPath(), "districtsync-staging");

    public long MaxUploadBytes { get; set; } = 2L * 1024 * 1024 * 1024;
}

public sealed class MessagingOptions
{
    public const string Section = "Messaging";

    public int MaxAttempts { get; set; } = 4;

    public ushort Prefetch { get; set; } = 16;

    public ushort Concurrency { get; set; } = 4;

    public TimeSpan OutboxPollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    public int OutboxBatchSize { get; set; } = 100;

    public TimeSpan OutboxLease { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a long-running handler may go without a heartbeat before its inbox claim is considered abandoned.</summary>
    public TimeSpan LongRunningLease { get; set; } = TimeSpan.FromMinutes(2);
}

public sealed class WorkflowOptions
{
    public const string Section = "Workflows";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan Lease { get; set; } = TimeSpan.FromSeconds(30);

    public int BatchSize { get; set; } = 20;

    public int Concurrency { get; set; } = 4;
}

public sealed class WriteBackOptions
{
    public const string Section = "WriteBack";

    /// <summary>Fault injection for resilience testing: probability (0..1) that a write-back fails transiently.</summary>
    public double TransientFailureRate { get; set; }

    public TimeSpan SimulatedLatency { get; set; } = TimeSpan.FromMilliseconds(50);
}
