using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.Modules.Orders.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Orders.Application;

/// <summary>The buyer asks to send a delivered part back.</summary>
/// <param name="OrderId">The buyer's order.</param>
/// <param name="PartId">The part to return: a whole parcel, never some of its items.</param>
/// <param name="BuyerId">The caller; the order must be theirs.</param>
/// <param name="Reason">Damaged, WrongItem, NotAsDescribed, QualityIssue, NoLongerNeeded or Other.</param>
/// <param name="Comment">The buyer's own words. Required for Other.</param>
/// <param name="RefundUpiId">Where a cash-on-delivery refund goes. Required for cash orders, ignored otherwise.</param>
/// <param name="Items">How many of which products go back; null or empty for the whole parcel.</param>
public sealed record RequestReturnCommand(
    Guid OrderId,
    Guid PartId,
    Guid BuyerId,
    string Reason,
    string? Comment,
    string? RefundUpiId,
    IReadOnlyList<ReturnItemDto>? Items = null) : ICommand<OrderDto>;

internal sealed class RequestReturnCommandValidator : AbstractValidator<RequestReturnCommand>
{
    /// <summary>
    /// A UPI virtual payment address: a handle, "@", and the provider's name, as in asha.devi@okicici.
    /// Checks the shape only; whether the account exists is found out when the refund is paid.
    /// </summary>
    public const string UpiIdPattern = @"^[A-Za-z0-9][A-Za-z0-9._-]{1,255}@[A-Za-z][A-Za-z0-9]{1,63}$";

    public RequestReturnCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.PartId).NotEmpty();
        RuleFor(x => x.Reason)
            .Must(r => Enum.TryParse<ReturnReason>(r, ignoreCase: true, out _))
            .WithMessage("Reason must be Damaged, WrongItem, NotAsDescribed, QualityIssue, NoLongerNeeded or Other.");
        RuleFor(x => x.Comment).MaximumLength(500);
        RuleFor(x => x.Comment)
            .NotEmpty()
            .When(x => Enum.TryParse<ReturnReason>(x.Reason, ignoreCase: true, out var r) && r == ReturnReason.Other)
            .WithMessage("Say what is wrong with it.");
        RuleFor(x => x.RefundUpiId)
            .MaximumLength(64)
            .Matches(UpiIdPattern)
            .When(x => !string.IsNullOrWhiteSpace(x.RefundUpiId))
            .WithMessage("Enter a UPI id such as name@okicici.");
        RuleFor(x => x.Items)
            .Must(items => items is null || items.Select(i => i.ProductId).Distinct().Count() == items.Count)
            .WithMessage("Name each product once.");
    }
}

internal sealed class RequestReturnCommandHandler(
    UPBazaarDbContext dbContext,
    OrderTransaction transaction,
    OrderReader reader,
    IClock clock) : ICommandHandler<RequestReturnCommand, OrderDto>
{
    public Task<Result<OrderDto>> HandleAsync(RequestReturnCommand command, CancellationToken cancellationToken) =>
        transaction.RunAsync(async ct =>
        {
            var order = await reader.FindForBuyerAsync(command.OrderId, command.BuyerId, ct);

            if (order is null)
            {
                return Result.Failure<OrderDto>(OrderErrors.NotFound);
            }

            var requested = order.RequestReturn(
                command.PartId,
                Enum.Parse<ReturnReason>(command.Reason, ignoreCase: true),
                command.Comment,
                command.RefundUpiId?.Trim(),
                clock.UtcNow,
                command.Items?.ToDictionary(i => i.ProductId, i => i.Quantity));

            if (requested.IsFailure)
            {
                return Result.Failure<OrderDto>(requested.Error);
            }

            await dbContext.SaveChangesAsync(ct);

            return order.ToDto();
        }, cancellationToken);
}

/// <summary>The seller, or staff, accept or refuse a buyer's return.</summary>
/// <param name="OrderId">The order.</param>
/// <param name="PartId">The part the buyer wants to return.</param>
/// <param name="SellerId">Set when the seller decides: the part must be theirs. Null for staff.</param>
/// <param name="Approve">True to accept and have a pickup booked; false to refuse.</param>
/// <param name="Note">Why it was refused, shown to the buyer. Required when refusing.</param>
public sealed record DecideReturnCommand(Guid OrderId, Guid PartId, Guid? SellerId, bool Approve, string? Note)
    : ICommand<OrderDto>;

internal sealed class DecideReturnCommandValidator : AbstractValidator<DecideReturnCommand>
{
    public DecideReturnCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.PartId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(500);
        RuleFor(x => x.Note)
            .NotEmpty()
            .When(x => !x.Approve)
            .WithMessage("Tell the buyer why the return is refused.");
    }
}

/// <summary>
/// Approving moves the part to Returning and raises the event Shipping books the pickup from, in
/// the same save, so a return is never approved without its pickup being arranged.
/// </summary>
internal sealed class DecideReturnCommandHandler(
    UPBazaarDbContext dbContext,
    OrderTransaction transaction,
    OrderReader reader,
    ICurrentUser currentUser,
    IClock clock) : ICommandHandler<DecideReturnCommand, OrderDto>
{
    public Task<Result<OrderDto>> HandleAsync(DecideReturnCommand command, CancellationToken cancellationToken) =>
        transaction.RunAsync(async ct =>
        {
            var order = await reader.FindAsync(command.OrderId, ct);
            var part = order?.Parts.FirstOrDefault(p => p.PublicId == command.PartId);

            // A seller asking about a part that is not theirs gets the same answer as a wrong id.
            if (order is null || part is null || (command.SellerId is { } seller && part.SellerId != seller))
            {
                return Result.Failure<OrderDto>(OrderErrors.NotFound);
            }

            var decided = command.Approve
                ? order.ApproveReturn(part.PublicId, command.Note, currentUser.UserId, clock.UtcNow)
                : order.RejectReturn(part.PublicId, command.Note!, currentUser.UserId, clock.UtcNow);

            if (decided.IsFailure)
            {
                return Result.Failure<OrderDto>(decided.Error);
            }

            await dbContext.SaveChangesAsync(ct);

            return order.ToDto();
        }, cancellationToken);
}

/// <summary>
/// Return requests, for staff or one seller. Undecided ones come oldest first, since a buyer is
/// waiting on each; decided ones newest first, for looking something up.
/// </summary>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Items per page, 1 to 100.</param>
/// <param name="Status">Requested, Approved or Rejected; all when null.</param>
/// <param name="SellerId">Only this seller's parts; every seller's when null.</param>
public sealed record ListReturnRequestsQuery(int Page, int PageSize, string? Status, Guid? SellerId)
    : IQuery<PagedList<ReturnRequestSummaryDto>>;

internal sealed class ListReturnRequestsQueryValidator : AbstractValidator<ListReturnRequestsQuery>
{
    public ListReturnRequestsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Status)
            .Must(s => s is null || Enum.TryParse<ReturnRequestStatus>(s, ignoreCase: true, out _))
            .WithMessage("Status must be Requested, Approved or Rejected.");
    }
}

internal sealed class ListReturnRequestsQueryHandler(UPBazaarDbContext dbContext)
    : IQueryHandler<ListReturnRequestsQuery, PagedList<ReturnRequestSummaryDto>>
{
    public async Task<Result<PagedList<ReturnRequestSummaryDto>>> HandleAsync(
        ListReturnRequestsQuery query,
        CancellationToken cancellationToken)
    {
        var requests =
            from order in dbContext.Set<Order>().AsNoTracking()
            from part in order.Parts
            where part.ReturnRequest != null
            select new { order, part };

        if (query.SellerId is { } seller)
        {
            requests = requests.Where(x => x.part.SellerId == seller);
        }

        var filtered = Enum.TryParse<ReturnRequestStatus>(query.Status, ignoreCase: true, out var status);

        if (filtered)
        {
            requests = requests.Where(x => x.part.ReturnRequest!.Status == status);
        }

        var total = await requests.CountAsync(cancellationToken);

        var ordered = filtered && status == ReturnRequestStatus.Requested
            ? requests.OrderBy(x => x.part.ReturnRequest!.RequestedAtUtc)
            : requests.OrderByDescending(x => x.part.ReturnRequest!.RequestedAtUtc);

        var items = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new ReturnRequestSummaryDto(
                x.order.PublicId,
                x.order.Number,
                x.part.PublicId,
                x.part.SellerId,
                x.part.Status.ToString(),
                x.part.ReturnRequest!.Status.ToString(),
                x.part.ReturnRequest.Reason.ToString(),
                x.part.ReturnRequest.Comment,
                x.order.PaymentMethod.ToString(),
                x.part.ReturnRequest.RefundDue ?? x.part.Lines.Sum(l =>
                    (l.UnitPrice * l.ReturnRequestedQuantity) - Math.Round((l.Discount - l.RevokedDiscount) * l.ReturnRequestedQuantity / l.Quantity, 2)),
                x.order.Currency,
                x.part.ReturnRequest.RequestedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedList<ReturnRequestSummaryDto>(items, query.Page, query.PageSize, total);
    }
}
