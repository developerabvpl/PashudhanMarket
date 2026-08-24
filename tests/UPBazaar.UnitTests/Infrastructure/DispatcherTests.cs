using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Messaging;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Primitives;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.UnitTests.Infrastructure;

public sealed record Greet(string Name) : ICommand<string>;

public sealed record CountLetters(string Word) : IQuery<int>;

public sealed record Ping : ICommand;

public sealed record Pinged : DomainEvent;

internal sealed class GreetHandler : ICommandHandler<Greet, string>
{
    public Task<Result<string>> HandleAsync(Greet command, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success($"Namaste, {command.Name}"));
}

internal sealed class GreetValidator : AbstractValidator<Greet>
{
    public GreetValidator() => RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.");
}

internal sealed class CountLettersHandler : IQueryHandler<CountLetters, int>
{
    public Task<Result<int>> HandleAsync(CountLetters query, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(query.Word.Length));
}

internal sealed class PingHandler : ICommandHandler<Ping>
{
    public Task<Result> HandleAsync(Ping command, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success());
}

internal sealed class FirstPingedHandler : IDomainEventHandler<Pinged>
{
    public static int Calls { get; private set; }

    public static void Reset() => Calls = 0;

    public Task HandleAsync(Pinged domainEvent, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.CompletedTask;
    }
}

internal sealed class SecondPingedHandler : IDomainEventHandler<Pinged>
{
    public static int Calls { get; private set; }

    public static void Reset() => Calls = 0;

    public Task HandleAsync(Pinged domainEvent, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.CompletedTask;
    }
}

public sealed class DispatcherTests
{
    private static IDispatcher BuildDispatcher(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();

        services.AddScoped<ICommandHandler<Greet, string>, GreetHandler>();
        services.AddScoped<IQueryHandler<CountLetters, int>, CountLettersHandler>();
        services.AddScoped<ICommandHandler<Ping>, PingHandler>();

        configure?.Invoke(services);

        services.AddScoped<IDispatcher, Dispatcher>();

        return services.BuildServiceProvider().GetRequiredService<IDispatcher>();
    }

    [Fact]
    public async Task A_command_reaches_its_handler_and_returns_the_value()
    {
        var result = await BuildDispatcher().SendAsync(new Greet("Asha"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("Namaste, Asha");
    }

    [Fact]
    public async Task A_command_with_no_response_returns_plain_success()
    {
        var result = await BuildDispatcher().SendAsync(new Ping());

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_query_reaches_its_handler()
    {
        var result = await BuildDispatcher().QueryAsync(new CountLetters("saree"));

        result.Value.ShouldBe(5);
    }

    [Fact]
    public async Task A_registered_validator_runs_before_the_handler()
    {
        var dispatcher = BuildDispatcher(s => s.AddScoped<IValidator<Greet>, GreetValidator>());

        var result = await dispatcher.SendAsync(new Greet(string.Empty));

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.ValidationErrors["Name"].ShouldContain("Name is required.");
    }

    [Fact]
    public async Task A_valid_command_still_reaches_the_handler_when_validators_exist()
    {
        var dispatcher = BuildDispatcher(s => s.AddScoped<IValidator<Greet>, GreetValidator>());

        var result = await dispatcher.SendAsync(new Greet("Asha"));

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_message_with_no_handler_fails_loudly_rather_than_silently()
    {
        var dispatcher = new ServiceCollection()
            .AddScoped<IDispatcher, Dispatcher>()
            .BuildServiceProvider()
            .GetRequiredService<IDispatcher>();

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await dispatcher.SendAsync(new Greet("Asha")));

        exception.Message.ShouldContain("Greet");
    }

    [Fact]
    public async Task An_event_reaches_every_registered_handler()
    {
        FirstPingedHandler.Reset();
        SecondPingedHandler.Reset();

        var dispatcher = BuildDispatcher(s =>
        {
            s.AddScoped<IDomainEventHandler<Pinged>, FirstPingedHandler>();
            s.AddScoped<IDomainEventHandler<Pinged>, SecondPingedHandler>();
        });

        await dispatcher.PublishAsync(new Pinged());

        FirstPingedHandler.Calls.ShouldBe(1);
        SecondPingedHandler.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task An_event_with_no_handlers_is_not_an_error()
    {
        // Most events are of interest to nobody yet; that must not fail the outbox.
        await Should.NotThrowAsync(async () => await BuildDispatcher().PublishAsync(new Pinged()));
    }
}
