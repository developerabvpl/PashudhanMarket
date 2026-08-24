using System.Collections.Concurrent;
using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Infrastructure.Messaging;

/// <summary>
/// Resolves the single handler for a message and runs any registered FluentValidation
/// validators first, so handlers can assume a well-formed request and controllers never
/// have to call the validator themselves.
/// </summary>
public sealed class Dispatcher(IServiceProvider provider) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, MethodInfo> HandleMethods = new();

    public async Task<Result> SendAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = await ValidateAsync(command, cancellationToken);

        if (validation is not null)
        {
            return Result.ValidationFailure(validation);
        }

        var handlerType = typeof(ICommandHandler<>).MakeGenericType(command.GetType());
        var handler = Resolve(handlerType, command.GetType());

        return await Invoke<Result>(handler, handlerType, command, cancellationToken);
    }

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
        var handler = Resolve(handlerType, command.GetType());

        return await Invoke<Result<TResponse>>(handler, handlerType, command, cancellationToken);
    }

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
        var handler = Resolve(handlerType, query.GetType());

        return await Invoke<Result<TResponse>>(handler, handlerType, query, cancellationToken);
    }

    private object Resolve(Type handlerType, Type messageType) =>
        provider.GetService(handlerType)
        ?? throw new InvalidOperationException(
            $"No handler registered for '{messageType.Name}'. Expected an implementation of "
            + $"'{handlerType.Name}' in the owning module's Application layer.");

    private static async Task<TResult> Invoke<TResult>(
        object handler,
        Type handlerType,
        object message,
        CancellationToken cancellationToken)
    {
        var method = HandleMethods.GetOrAdd(
            handlerType,
            static type => type.GetMethod(nameof(ICommandHandler<ICommand>.HandleAsync))!);

        return await (Task<TResult>)method.Invoke(handler, [message, cancellationToken])!;
    }

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
        var failures = new List<FluentValidation.Results.ValidationFailure>();

        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(context, cancellationToken);
            failures.AddRange(result.Errors);
        }

        return failures.Count == 0
            ? null
            : failures
                .GroupBy(f => f.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray());
    }
}
