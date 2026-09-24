namespace UPBazaar.Modules.Reviews.Photos;

/// <summary>
/// Where review photos are kept.
///
/// Local disk in Development and Testing; elsewhere nothing, until a cloud store is chosen and
/// implemented here. Without one, <see cref="IsEnabled"/> is false and reviews carry stars and
/// words only - the feature switches off rather than failing at the first upload.
/// </summary>
public interface IPhotoStore
{
    /// <summary>False when no store is configured: photos cannot be added.</summary>
    bool IsEnabled { get; }

    Task SaveAsync(string key, Stream content, CancellationToken cancellationToken);

    /// <summary>Opens a photo for reading, or returns null if there is no such file.</summary>
    Task<Stream?> OpenAsync(string key, CancellationToken cancellationToken);

    /// <summary>Removes a photo. Removing one that is already gone is not an error.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

/// <summary>No store configured: photos are switched off.</summary>
public sealed class UnconfiguredPhotoStore : IPhotoStore
{
    public bool IsEnabled => false;

    public Task SaveAsync(string key, Stream content, CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "No photo store is configured. Implement IPhotoStore against a cloud store and register "
            + "it in the Reviews module to accept review photos outside Development.");

    public Task<Stream?> OpenAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult<Stream?>(null);

    public Task DeleteAsync(string key, CancellationToken cancellationToken) => Task.CompletedTask;
}
