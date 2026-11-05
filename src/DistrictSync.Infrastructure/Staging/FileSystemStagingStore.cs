using DistrictSync.Application.Abstractions;
using DistrictSync.Domain.Common;
using Microsoft.Extensions.Options;

namespace DistrictSync.Infrastructure.Staging;

/// <summary>
/// Stages uploads on a directory shared by API and worker. Swap for a blob-storage implementation in
/// production by implementing <see cref="IStagingStore"/>; references are opaque to the rest of the system.
/// </summary>
public sealed class FileSystemStagingStore(IOptions<IngestionOptions> options) : IStagingStore
{
    private const string Scheme = "fs://";

    public async Task<string> StoreAsync(TenantId tenantId, Guid batchId, Stream content, CancellationToken ct)
    {
        var relative = Path.Combine(tenantId.ToString(), $"{batchId:N}.bin");
        var full = Path.Combine(options.Value.StagingPath, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var file = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
        await content.CopyToAsync(file, ct);
        return Scheme + relative.Replace('\\', '/');
    }

    public Task<Stream> OpenReadAsync(string reference, CancellationToken ct)
    {
        var full = Resolve(reference);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"Staged payload '{reference}' is missing.", full);
        }

        return Task.FromResult<Stream>(new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan));
    }

    public Task DeleteAsync(string reference, CancellationToken ct)
    {
        var full = Resolve(reference);
        if (File.Exists(full))
        {
            File.Delete(full);
        }

        return Task.CompletedTask;
    }

    private string Resolve(string reference)
    {
        if (!reference.StartsWith(Scheme, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Unsupported staging reference '{reference}'.", nameof(reference));
        }

        var root = Path.GetFullPath(options.Value.StagingPath);
        var full = Path.GetFullPath(Path.Combine(root, reference[Scheme.Length..]));
        if (!full.StartsWith(root, StringComparison.Ordinal))
        {
            throw new ArgumentException("Staging reference escapes the staging root.", nameof(reference));
        }

        return full;
    }
}
