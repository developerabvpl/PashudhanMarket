namespace UPBazaar.Modules.Reviews.Photos;

/// <summary>
/// Keeps photos as files in one folder, for Development and Testing. Fine for one machine; a
/// deployed site with more than one server, or disks that are replaced, needs a shared store.
/// </summary>
public sealed class LocalDiskPhotoStore : IPhotoStore
{
    private readonly string _folder;

    public LocalDiskPhotoStore(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        _folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(_folder);
    }

    public bool IsEnabled => true;

    public async Task SaveAsync(string key, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        await using var file = new FileStream(PathFor(key), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(file, cancellationToken);
    }

    public Task<Stream?> OpenAsync(string key, CancellationToken cancellationToken)
    {
        var path = PathFor(key);

        return Task.FromResult<Stream?>(
            File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true) : null);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        File.Delete(PathFor(key));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Keys are generated here, never taken from a request, but they are still checked: a key
    /// that could climb out of the folder would turn a photo read into a read of any file.
    /// </summary>
    private string PathFor(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 64 || !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-')
            || key.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Not a photo key.", nameof(key));
        }

        return Path.Combine(_folder, key);
    }
}
