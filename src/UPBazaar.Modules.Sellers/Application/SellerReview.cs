using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Identity.Contracts;
using UPBazaar.Modules.Sellers.Contracts.Dtos;
using UPBazaar.Modules.Sellers.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Sellers.Application;

/// <summary>Sellers for staff. Pending ones oldest first, which is the review queue; otherwise newest first.</summary>
public sealed record ListSellersQuery(int Page, int PageSize, string? Status, string? Search) : IQuery<PagedList<SellerSummaryDto>>;

internal sealed class ListSellersQueryValidator : AbstractValidator<ListSellersQuery>
{
    public ListSellersQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Status)
            .Must(s => s is null || Enum.TryParse<SellerStatus>(s, ignoreCase: true, out _))
            .WithMessage("Status must be Pending, Approved or Rejected.");
    }
}

internal sealed class ListSellersQueryHandler(UPBazaarDbContext dbContext)
    : IQueryHandler<ListSellersQuery, PagedList<SellerSummaryDto>>
{
    public async Task<Result<PagedList<SellerSummaryDto>>> HandleAsync(ListSellersQuery query, CancellationToken cancellationToken)
    {
        var sellers = dbContext.Set<Seller>().AsNoTracking();
        var filtered = Enum.TryParse<SellerStatus>(query.Status, ignoreCase: true, out var status);

        if (filtered)
        {
            sellers = sellers.Where(s => s.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            sellers = sellers.Where(s => s.ShopName.Contains(search) || s.LegalName.Contains(search) || s.ContactMobile == search);
        }

        var total = await sellers.CountAsync(cancellationToken);

        var ordered = filtered && status == SellerStatus.Pending
            ? sellers.OrderBy(s => s.SubmittedAtUtc)
            : sellers.OrderByDescending(s => s.SubmittedAtUtc);

        var items = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(s => new SellerSummaryDto(s.PublicId, s.ShopName, s.Status.ToString(), s.ContactMobile, s.OwnerUserId, s.SubmittedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedList<SellerSummaryDto>(items, query.Page, query.PageSize, total);
    }
}

/// <summary>One seller in full, for review.</summary>
public sealed record GetSellerQuery(Guid SellerId) : IQuery<SellerDto>;

internal sealed class GetSellerQueryHandler(UPBazaarDbContext dbContext) : IQueryHandler<GetSellerQuery, SellerDto>
{
    public async Task<Result<SellerDto>> HandleAsync(GetSellerQuery query, CancellationToken cancellationToken)
    {
        var seller = await dbContext.Set<Seller>().AsNoTracking().FirstOrDefaultAsync(s => s.PublicId == query.SellerId, cancellationToken);

        return seller is null ? Result.Failure<SellerDto>(SellerErrors.NotFound) : seller.ToDto();
    }
}

/// <summary>
/// Approves an application and grants its owner the SellerOwner role, saved together: an approved
/// shop whose owner cannot sign in to it, or an owner with seller rights and no approved shop,
/// would each be a support call.
/// </summary>
public sealed record ApproveSellerCommand(Guid SellerId) : ICommand<SellerDto>;

internal sealed class ApproveSellerCommandHandler(
    UPBazaarDbContext dbContext,
    IUserRoles roles,
    ICurrentUser currentUser,
    IClock clock) : ICommandHandler<ApproveSellerCommand, SellerDto>
{
    public async Task<Result<SellerDto>> HandleAsync(ApproveSellerCommand command, CancellationToken cancellationToken)
    {
        var seller = await dbContext.Set<Seller>().FirstOrDefaultAsync(s => s.PublicId == command.SellerId, cancellationToken);

        if (seller is null)
        {
            return Result.Failure<SellerDto>(SellerErrors.NotFound);
        }

        var approved = seller.Approve(currentUser.UserId ?? "unknown", clock.UtcNow);

        if (approved.IsFailure)
        {
            return Result.Failure<SellerDto>(approved.Error);
        }

        if (seller.OwnerUserId is { } owner)
        {
            var granted = await roles.StageGrantAsync(owner, SellerRoles.Owner, $"seller-approval:{seller.PublicId}", cancellationToken);

            if (granted.IsFailure)
            {
                return Result.Failure<SellerDto>(granted.Error);
            }
        }

        return await ResubmitApplicationCommandHandler.SaveAsync(dbContext, seller, cancellationToken);
    }
}

/// <summary>Rejects an application, saying why. The owner can correct it and resubmit.</summary>
public sealed record RejectSellerCommand(Guid SellerId, string Note) : ICommand<SellerDto>;

internal sealed class RejectSellerCommandValidator : AbstractValidator<RejectSellerCommand>
{
    public RejectSellerCommandValidator()
    {
        RuleFor(x => x.SellerId).NotEmpty();
        RuleFor(x => x.Note).NotEmpty().MaximumLength(1000);
    }
}

internal sealed class RejectSellerCommandHandler(UPBazaarDbContext dbContext, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<RejectSellerCommand, SellerDto>
{
    public async Task<Result<SellerDto>> HandleAsync(RejectSellerCommand command, CancellationToken cancellationToken)
    {
        var seller = await dbContext.Set<Seller>().FirstOrDefaultAsync(s => s.PublicId == command.SellerId, cancellationToken);

        if (seller is null)
        {
            return Result.Failure<SellerDto>(SellerErrors.NotFound);
        }

        var rejected = seller.Reject(command.Note, currentUser.UserId ?? "unknown", clock.UtcNow);

        return rejected.IsFailure
            ? Result.Failure<SellerDto>(rejected.Error)
            : await ResubmitApplicationCommandHandler.SaveAsync(dbContext, seller, cancellationToken);
    }
}

/// <summary>
/// Gives an ownerless seller - the one the sample catalogue came with - an owner, granting them
/// SellerOwner if the seller is already approved.
/// </summary>
public sealed record LinkSellerOwnerCommand(Guid SellerId, Guid OwnerUserId) : ICommand<SellerDto>;

internal sealed class LinkSellerOwnerCommandHandler(
    UPBazaarDbContext dbContext,
    IUserDirectory users,
    IUserRoles roles) : ICommandHandler<LinkSellerOwnerCommand, SellerDto>
{
    public async Task<Result<SellerDto>> HandleAsync(LinkSellerOwnerCommand command, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(command.OwnerUserId, cancellationToken);

        if (user.IsFailure)
        {
            return Result.Failure<SellerDto>(user.Error);
        }

        if (await dbContext.Set<Seller>().AnyAsync(s => s.OwnerUserId == command.OwnerUserId, cancellationToken))
        {
            return Result.Failure<SellerDto>(SellerErrors.OwnerHasShop);
        }

        var seller = await dbContext.Set<Seller>().FirstOrDefaultAsync(s => s.PublicId == command.SellerId, cancellationToken);

        if (seller is null)
        {
            return Result.Failure<SellerDto>(SellerErrors.NotFound);
        }

        var linked = seller.LinkOwner(command.OwnerUserId);

        if (linked.IsFailure)
        {
            return Result.Failure<SellerDto>(linked.Error);
        }

        if (seller.Status == SellerStatus.Approved)
        {
            var granted = await roles.StageGrantAsync(command.OwnerUserId, SellerRoles.Owner, $"seller-owner:{seller.PublicId}", cancellationToken);

            if (granted.IsFailure)
            {
                return Result.Failure<SellerDto>(granted.Error);
            }
        }

        return await ResubmitApplicationCommandHandler.SaveAsync(dbContext, seller, cancellationToken);
    }
}

/// <summary>The Identity role a seller's owner holds. Named here, not referenced, to keep Sellers off Identity's internals.</summary>
internal static class SellerRoles
{
    public const string Owner = "SellerOwner";
}
