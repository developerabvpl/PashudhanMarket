using UPBazaar.SharedKernel.Primitives;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.SharedKernel.Messaging;

/// <summary>
/// In-process message bus. Controllers depend on this and nothing else from a module's
/// application layer, which is what keeps them thin.
///
/// Deliberately hand-rolled rather than taking a MediatR dependency: the surface needed here
/// is three methods, and the implementation lives in Infrastructure.
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

    /// <summary>
    /// Invokes every handler registered for this event. Used by the outbox processor; module
    /// code raises events on aggregates rather than publishing them directly.
    /// </summary>
    Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}
