using FluentValidation;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Inventory.Contracts.Dtos;
using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.Modules.Orders.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Orders.Application;

/// <summary>
/// The seller, or staff, record what they found in a parcel the courier brought back.
/// </summary>
/// <param name="OrderId">The order.</param>
/// <param name="PartId">The part that came back.</param>
/// <param name="SellerId">Set when the seller is inspecting: the part must be theirs. Null for staff.</param>
/// <param name="Condition">Good or Damaged, for every line that came back; ignored for lines named in <paramref name="Lines"/>.</param>
/// <param name="Note">What was wrong, if anything.</param>
/// <param name="Lines">A condition for each product that came back, when they differ.</param>
public sealed record InspectReturnCommand(
    Guid OrderId,
    Guid PartId,
    Guid? SellerId,
    string? Condition,
    string? Note,
    IReadOnlyList<ReturnLineConditionDto>? Lines = null)
    : ICommand<OrderDto>;

internal sealed class InspectReturnCommandValidator : AbstractValidator<InspectReturnCommand>
{
    public InspectReturnCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.PartId).NotEmpty();
        RuleFor(x => x.Condition)
            .Must(c => c is null || Enum.TryParse<ReturnCondition>(c, ignoreCase: true, out _))
            .WithMessage("Condition must be Good or Damaged.");
        RuleForEach(x => x.Lines).ChildRules(line =>
            line.RuleFor(l => l.Condition)
                .Must(c => Enum.TryParse<ReturnCondition>(c, ignoreCase: true, out _))
                .WithMessage("Condition must be Good or Damaged."));
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

/// <summary>
/// Good units go back on sale, as a Returned stock movement; damaged ones do not, since their
/// stock left the shelf at confirmation and never came back to it. Both in one transaction with
/// the inspection, so a parcel cannot be inspected twice and restocked twice.
/// </summary>
internal sealed class InspectReturnCommandHandler(
    UPBazaarDbContext dbContext,
    OrderTransaction transaction,
    OrderReader reader,
    OrderStock stock,
    ICurrentUser currentUser,
    IClock clock) : ICommandHandler<InspectReturnCommand, OrderDto>
{
    public Task<Result<OrderDto>> HandleAsync(InspectReturnCommand command, CancellationToken cancellationToken) =>
        transaction.RunAsync(async ct =>
        {
            var order = await reader.FindAsync(command.OrderId, ct);
            var part = order?.Parts.FirstOrDefault(p => p.PublicId == command.PartId);

            // A seller asking about a part that is not theirs gets the same answer as a wrong id.
            if (order is null || part is null || (command.SellerId is { } seller && part.SellerId != seller))
            {
                return Result.Failure<OrderDto>(OrderErrors.NotFound);
            }

            var conditions = part.CameBack.ToDictionary(
                x => x.Line.ProductId,
                x => command.Lines?.FirstOrDefault(l => l.ProductId == x.Line.ProductId)?.Condition ?? command.Condition);

            var inspected = conditions.Values.Any(c => c is null)
                ? Result.Failure(OrderErrors.ConditionForEveryLine)
                : order.InspectReturn(
                    part.PublicId,
                    conditions.ToDictionary(c => c.Key, c => Enum.Parse<ReturnCondition>(c.Value!, ignoreCase: true)),
                    command.Note,
                    currentUser.UserId,
                    clock.UtcNow);

            if (inspected.IsFailure)
            {
                return Result.Failure<OrderDto>(inspected.Error);
            }

            var good = part.CameBack
                .Where(x => x.Line.ReturnCondition == ReturnCondition.Good)
                .Select(x => new ReservationLineDto(x.Line.ProductId, x.Quantity))
                .ToList();

            if (good.Count > 0)
            {
                var restocked = await stock.RestockAsync(order, good, ct);

                if (restocked.IsFailure)
                {
                    return Result.Failure<OrderDto>(restocked.Error);
                }

                if (transaction.WasDetached(order))
                {
                    return Result.Failure<OrderDto>(OrderErrors.ConcurrentChange);
                }
            }

            await dbContext.SaveChangesAsync(ct);

            return order.ToDto();
        }, cancellationToken);
}
