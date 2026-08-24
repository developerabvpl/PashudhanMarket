using System.Collections.Concurrent;

namespace UPBazaar.Infrastructure.ExternalServices.Storage;

public sealed record BlobReference(string Container, string Name, Uri Uri);

public interface IBlobStorage
{
    Task<BlobReference> UploadAsync(
        string container,
        string name,
        Stream content,
        string contentType,
        CancellationToken cancellationToken);

    Task<Stream?> DownloadAsync(string container, string name, CancellationToken cancellationToken);

    Task DeleteAsync(string container, string name, CancellationToken cancellationToken);
}

/// <summary>In-memory blob store for sandbox and tests.</summary>
public sealed class FakeBlobStorage : IBlobStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _blobs = new();

    public async Task<BlobReference> UploadAsync(
        string container,
        string name,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        _blobs[Key(container, name)] = buffer.ToArray();

        return new BlobReference(container, name, new Uri($"sandbox://{container}/{name}"));
    }

    public Task<Stream?> DownloadAsync(string container, string name, CancellationToken cancellationToken) =>
        Task.FromResult(_blobs.TryGetValue(Key(container, name), out var bytes)
            ? new MemoryStream(bytes) as Stream
            : null);

    public Task DeleteAsync(string container, string name, CancellationToken cancellationToken)
    {
        _blobs.TryRemove(Key(container, name), out _);
        return Task.CompletedTask;
    }

    private static string Key(string container, string name) => $"{container}/{name}";
}
