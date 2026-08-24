using UPBazaar.SharedKernel.Results;

namespace UPBazaar.SharedKernel.Messaging;

/// <summary>
/// In-process message bus. Controllers depend on this and nothing else from the
/// Application layer, which is what keeps them thin.
/// </summary>
public interface IDispatcher
{
    Task<Result> SendAsync(ICommand command, CancellationToken cancellationToken = default);

    Task<Result<TResponse>> SendAsync<TResponse>(
        ICommand<TResponse> command,
        CancellationToken cancellationToken = default);

    Task<Result<TResponse>> QueryAsync<TResponse>(
        IQuery<TResponse> query,
        CancellationToken cancellationToken = default);
}
