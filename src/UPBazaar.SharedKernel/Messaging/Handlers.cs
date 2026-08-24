using UPBazaar.SharedKernel.Primitives;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.SharedKernel.Messaging;

/// <summary>Handles a command. Exactly one handler per command type.</summary>
/// <typeparam name="TCommand">Command handled.</typeparam>
public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    Task<Result> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

/// <summary>Handles a command that produces a value.</summary>
/// <typeparam name="TCommand">Command handled.</typeparam>
/// <typeparam name="TResponse">Value produced on success.</typeparam>
public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

/// <summary>Handles a query.</summary>
/// <typeparam name="TQuery">Query handled.</typeparam>
/// <typeparam name="TResponse">Value produced on success.</typeparam>
public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Handles a domain event pulled off the outbox. Unlike commands, an event may have any
/// number of handlers, and they may live in modules other than the one that raised it.
/// </summary>
/// <typeparam name="TEvent">Event handled.</typeparam>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
