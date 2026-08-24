using System.Collections.Concurrent;
using System.Reflection;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Primitives;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Infrastructure.Messaging;

/// <summary>
/// The in-process bus. Resolves the single handler for a message, runs any registered
/// FluentValidation validators first, and returns the handler's <see cref="Result"/>.
///
/// Validating here rather than in each handler means a handler can assume a well-formed
/// request, and a controller never has to call a validator itself.
/// </summary>
public sealed class Dispatcher(IServiceProvider provider) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, MethodInfo> HandleMethods = new();

    /// <inheritdoc />
    public async Task<Result> SendAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = await ValidateAsync(command, cancellationToken);

        if (validation is not null)
        {
            return Result.ValidationFailure(validation);
        }

        var handlerType = typeof(ICommandHandler<>).MakeGenericType(command.GetType());

        return await InvokeAsync<Result>(Resolve(handlerType, command.GetType()), handlerType, command, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<TResponse>> SendAsync<TResponse>(
        ICommand<TResponse> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = await ValidateAsync(command, cancellationToken);

        if (validation is not null)
        {
            return Result.ValidationFailure<TResponse>(validation);
        }

        var handlerType = typeof(ICommandHandler<,>).MakeGenericType(command.GetType(), typeof(TResponse));

        return await InvokeAsync<Result<TResponse>>(
            Resolve(handlerType, command.GetType()),
            handlerType,
            command,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<TResponse>> QueryAsync<TResponse>(
        IQuery<TResponse> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = await ValidateAsync(query, cancellationToken);

        if (validation is not null)
        {
            return Result.ValidationFailure<TResponse>(validation);
        }

        var handlerType = typeof(IQueryHandler<,>).MakeGenericType(query.GetType(), typeof(TResponse));

        return await InvokeAsync<Result<TResponse>>(
            Resolve(handlerType, query.GetType()),
            handlerType,
            query,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
        var handlers = provider.GetServices(handlerType).OfType<object>().ToList();

        if (handlers.Count == 0)
        {
            return;
        }

        var method = HandleMethod(handlerType);

        foreach (var handler in handlers)
        {
            await (Task)method.Invoke(handler, [domainEvent, cancellationToken])!;
        }
    }

    private object Resolve(Type handlerType, Type messageType) =>
        provider.GetService(handlerType)
        ?? throw new InvalidOperationException(
            $"No handler registered for '{messageType.Name}'. Expected an implementation of "
            + $"'{handlerType.Name}' in the owning module's application layer.");

    private static MethodInfo HandleMethod(Type handlerType) =>
        HandleMethods.GetOrAdd(handlerType, static type => type.GetMethod("HandleAsync")!);

    private static async Task<TResult> InvokeAsync<TResult>(
        object handler,
        Type handlerType,
        object message,
        CancellationToken cancellationToken)
    {
        var method = HandleMethod(handlerType);

        return await (Task<TResult>)method.Invoke(handler, [message, cancellationToken])!;
    }

    /// <summary>
    /// Runs every validator registered for this message type. Returns null when the message is
    /// valid, so the caller can distinguish "no validators" from "validators all passed"
    /// without caring which it was.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string[]>?> ValidateAsync(
        object message,
        CancellationToken cancellationToken)
    {
        var validatorType = typeof(IValidator<>).MakeGenericType(message.GetType());
        var validators = provider.GetServices(validatorType).OfType<IValidator>().ToList();

        if (validators.Count == 0)
        {
            return null;
        }

        var context = new ValidationContext<object>(message);
        var failures = new List<ValidationFailure>();

        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(context, cancellationToken);
            failures.AddRange(result.Errors);
        }

        return failures.Count == 0
            ? null
            : failures
                .GroupBy(f => f.PropertyName, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray(), StringComparer.Ordinal);
    }
}
