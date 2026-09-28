using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Sellers.Contracts.Dtos;
using UPBazaar.Modules.Sellers.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Sellers.Application;

/// <summary>The caller's own shop, whatever state its application is in.</summary>
public sealed record GetMySellerQuery(Guid OwnerUserId) : IQuery<SellerDto>;

internal sealed class GetMySellerQueryHandler(UPBazaarDbContext dbContext) : IQueryHandler<GetMySellerQuery, SellerDto>
{
    public async Task<Result<SellerDto>> HandleAsync(GetMySellerQuery query, CancellationToken cancellationToken)
    {
        var seller = await dbContext.Set<Seller>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.OwnerUserId == query.OwnerUserId, cancellationToken);

        return seller is null ? Result.Failure<SellerDto>(SellerErrors.NoApplication) : seller.ToDto();
    }
}

/// <summary>Applies to sell. One application per account.</summary>
public sealed record ApplyToSellCommand(Guid OwnerUserId, SellerApplication Application) : ICommand<SellerDto>;

internal sealed class ApplyToSellCommandValidator : AbstractValidator<ApplyToSellCommand>
{
    public ApplyToSellCommandValidator()
    {
        RuleFor(x => x.OwnerUserId).NotEmpty();
        RuleFor(x => x.Application).NotNull().SetValidator(new SellerApplicationValidator());
    }
}

internal sealed class ApplyToSellCommandHandler(UPBazaarDbContext dbContext, IClock clock)
    : ICommandHandler<ApplyToSellCommand, SellerDto>
{
    public async Task<Result<SellerDto>> HandleAsync(ApplyToSellCommand command, CancellationToken cancellationToken)
    {
        if (await dbContext.Set<Seller>().AnyAsync(s => s.OwnerUserId == command.OwnerUserId, cancellationToken))
        {
            return Result.Failure<SellerDto>(SellerErrors.AlreadyApplied);
        }

        // One shop per account: someone on another shop's team would otherwise hold both, and every
        // seller request would have two shops to choose from.
        if (await dbContext.Set<SellerMember>().AnyAsync(m => m.UserId == command.OwnerUserId, cancellationToken))
        {
            return Result.Failure<SellerDto>(SellerErrors.AlreadyInATeam);
        }

        var seller = Seller.Apply(command.OwnerUserId, command.Application, clock.UtcNow);
        dbContext.Set<Seller>().Add(seller);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two submits at once; the unique index on the owner let one through.
            return Result.Failure<SellerDto>(SellerErrors.AlreadyApplied);
        }

        return seller.ToDto();
    }
}

/// <summary>Corrects a pending or rejected application and submits it again.</summary>
public sealed record ResubmitApplicationCommand(Guid OwnerUserId, SellerApplication Application) : ICommand<SellerDto>;

internal sealed class ResubmitApplicationCommandValidator : AbstractValidator<ResubmitApplicationCommand>
{
    public ResubmitApplicationCommandValidator()
    {
        RuleFor(x => x.OwnerUserId).NotEmpty();
        RuleFor(x => x.Application).NotNull().SetValidator(new SellerApplicationValidator());
    }
}

internal sealed class ResubmitApplicationCommandHandler(UPBazaarDbContext dbContext, IClock clock)
    : ICommandHandler<ResubmitApplicationCommand, SellerDto>
{
    public async Task<Result<SellerDto>> HandleAsync(ResubmitApplicationCommand command, CancellationToken cancellationToken)
    {
        var seller = await dbContext.Set<Seller>().FirstOrDefaultAsync(s => s.OwnerUserId == command.OwnerUserId, cancellationToken);

        if (seller is null)
        {
            return Result.Failure<SellerDto>(SellerErrors.NoApplication);
        }

        var resubmitted = seller.Resubmit(command.Application, clock.UtcNow);

        if (resubmitted.IsFailure)
        {
            return Result.Failure<SellerDto>(resubmitted.Error);
        }

        return await SaveAsync(dbContext, seller, cancellationToken);
    }

    internal static async Task<Result<SellerDto>> SaveAsync(
        UPBazaarDbContext dbContext,
        Seller seller,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<SellerDto>(SellerErrors.ConcurrentChange);
        }

        return seller.ToDto();
    }
}

/// <summary>Changes how the shop appears and how to reach it. Allowed in any state.</summary>
public sealed record UpdateSellerProfileCommand(
    Guid OwnerUserId,
    string ShopName,
    string? Description,
    string ContactMobile,
    string? ContactEmail) : ICommand<SellerDto>;

internal sealed class UpdateSellerProfileCommandValidator : AbstractValidator<UpdateSellerProfileCommand>
{
    public UpdateSellerProfileCommandValidator()
    {
        RuleFor(x => x.ShopName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(2000);
        SellerProfileRules.Contact(this, x => x.ContactMobile, x => x.ContactEmail);
    }
}

internal sealed class UpdateSellerProfileCommandHandler(UPBazaarDbContext dbContext)
    : ICommandHandler<UpdateSellerProfileCommand, SellerDto>
{
    public async Task<Result<SellerDto>> HandleAsync(UpdateSellerProfileCommand command, CancellationToken cancellationToken)
    {
        var seller = await dbContext.Set<Seller>().FirstOrDefaultAsync(s => s.OwnerUserId == command.OwnerUserId, cancellationToken);

        if (seller is null)
        {
            return Result.Failure<SellerDto>(SellerErrors.NoApplication);
        }

        seller.UpdateProfile(command.ShopName, command.Description, command.ContactMobile, command.ContactEmail);

        return await ResubmitApplicationCommandHandler.SaveAsync(dbContext, seller, cancellationToken);
    }
}
