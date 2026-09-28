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

/// <summary>Which shop the caller works for, as owner or team member, and in what role.</summary>
public sealed record GetMyAccessQuery(Guid UserId) : IQuery<SellerAccessDto>;

internal sealed class GetMyAccessQueryHandler(UPBazaarDbContext dbContext) : IQueryHandler<GetMyAccessQuery, SellerAccessDto>
{
    public async Task<Result<SellerAccessDto>> HandleAsync(GetMyAccessQuery query, CancellationToken cancellationToken)
    {
        var owned = await dbContext.Set<Seller>()
            .AsNoTracking()
            .Where(s => s.OwnerUserId == query.UserId)
            .Select(s => new SellerAccessDto(s.PublicId, s.ShopName, s.Status.ToString(), "Owner"))
            .FirstOrDefaultAsync(cancellationToken);

        if (owned is not null)
        {
            return owned;
        }

        var member = await (
                from m in dbContext.Set<SellerMember>().AsNoTracking()
                join s in dbContext.Set<Seller>().AsNoTracking() on m.SellerId equals s.Id
                where m.UserId == query.UserId
                select new { s.PublicId, s.ShopName, s.Status, m.Role })
            .FirstOrDefaultAsync(cancellationToken);

        return member is null
            ? Result.Failure<SellerAccessDto>(SellerErrors.NotFound)
            : new SellerAccessDto(member.PublicId, member.ShopName, member.Status.ToString(), member.Role.ToString());
    }
}

/// <summary>The owner's team, oldest first.</summary>
public sealed record ListTeamQuery(Guid OwnerUserId) : IQuery<IReadOnlyList<SellerMemberDto>>;

internal sealed class ListTeamQueryHandler(UPBazaarDbContext dbContext, IUserDirectory users)
    : IQueryHandler<ListTeamQuery, IReadOnlyList<SellerMemberDto>>
{
    public async Task<Result<IReadOnlyList<SellerMemberDto>>> HandleAsync(ListTeamQuery query, CancellationToken cancellationToken)
    {
        var seller = await Team.OwnedAsync(dbContext, query.OwnerUserId, cancellationToken);

        if (seller is null)
        {
            return Result.Failure<IReadOnlyList<SellerMemberDto>>(SellerErrors.NotFound);
        }

        var members = await dbContext.Set<SellerMember>()
            .AsNoTracking()
            .Where(m => m.SellerId == seller.Id)
            .OrderBy(m => m.AddedAtUtc)
            .ToListAsync(cancellationToken);

        var people = await users.GetUsersAsync([.. members.Select(m => m.UserId)], cancellationToken);
        var byId = people.IsSuccess ? people.Value.ToDictionary(u => u.Id) : [];

        return Result.Success<IReadOnlyList<SellerMemberDto>>([.. members.Select(m => m.ToDto(byId.GetValueOrDefault(m.UserId)))]);
    }
}

/// <summary>
/// The owner adds someone to their team by the email of the seller-portal account they
/// registered. They get the role's access the next time their session refreshes.
/// </summary>
/// <param name="OwnerUserId">The owner; always the caller.</param>
/// <param name="Email">The account's email.</param>
/// <param name="Role">Manager or Dispatch.</param>
public sealed record AddTeamMemberCommand(Guid OwnerUserId, string Email, string Role) : ICommand<SellerMemberDto>;

internal sealed class AddTeamMemberCommandValidator : AbstractValidator<AddTeamMemberCommand>
{
    public AddTeamMemberCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Role)
            .Must(r => Enum.TryParse<SellerMemberRole>(r, ignoreCase: true, out _))
            .WithMessage("Role must be Manager or Dispatch.");
    }
}

internal sealed class AddTeamMemberCommandHandler(UPBazaarDbContext dbContext, IUserDirectory users, IUserRoles roles, IClock clock)
    : ICommandHandler<AddTeamMemberCommand, SellerMemberDto>
{
    public async Task<Result<SellerMemberDto>> HandleAsync(AddTeamMemberCommand command, CancellationToken cancellationToken)
    {
        var seller = await Team.OwnedAsync(dbContext, command.OwnerUserId, cancellationToken);

        if (seller is null)
        {
            return Result.Failure<SellerMemberDto>(SellerErrors.NotFound);
        }

        if (seller.Status != SellerStatus.Approved)
        {
            return Result.Failure<SellerMemberDto>(SellerErrors.NotApprovedShop);
        }

        // An account registered on the seller portal - an email account. Platform staff are not
        // someone a shop can hire through this screen.
        var user = await users.FindByEmailAsync(command.Email, cancellationToken);

        if (user.IsFailure || user.Value.UserType == "Staff" || user.Value.Status != "Active")
        {
            return Result.Failure<SellerMemberDto>(SellerErrors.NoSellerAccount);
        }

        if (user.Value.Id == command.OwnerUserId)
        {
            return Result.Failure<SellerMemberDto>(SellerErrors.NotYourself);
        }

        if (await dbContext.Set<Seller>().AnyAsync(s => s.OwnerUserId == user.Value.Id, cancellationToken)
            || await dbContext.Set<SellerMember>().AnyAsync(m => m.UserId == user.Value.Id, cancellationToken))
        {
            return Result.Failure<SellerMemberDto>(SellerErrors.AlreadyInATeam);
        }

        if (await dbContext.Set<SellerMember>().CountAsync(m => m.SellerId == seller.Id, cancellationToken) >= Team.MaxMembers)
        {
            return Result.Failure<SellerMemberDto>(SellerErrors.TeamFull);
        }

        var role = Enum.Parse<SellerMemberRole>(command.Role, ignoreCase: true);
        var member = SellerMember.Add(seller.Id, user.Value.Id, role, command.OwnerUserId.ToString(), clock.UtcNow);
        dbContext.Set<SellerMember>().Add(member);

        var granted = await roles.StageGrantAsync(user.Value.Id, SellerMember.IdentityRole(role), $"seller-team:{seller.PublicId}", cancellationToken);

        if (granted.IsFailure)
        {
            return Result.Failure<SellerMemberDto>(granted.Error);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Added to another shop at the same moment; the unique index let one through.
            return Result.Failure<SellerMemberDto>(SellerErrors.AlreadyInATeam);
        }

        return member.ToDto(user.Value);
    }
}

/// <summary>The owner changes what a team member may do.</summary>
public sealed record ChangeTeamMemberRoleCommand(Guid OwnerUserId, Guid MemberId, string Role) : ICommand<SellerMemberDto>;

internal sealed class ChangeTeamMemberRoleCommandValidator : AbstractValidator<ChangeTeamMemberRoleCommand>
{
    public ChangeTeamMemberRoleCommandValidator() =>
        RuleFor(x => x.Role)
            .Must(r => Enum.TryParse<SellerMemberRole>(r, ignoreCase: true, out _))
            .WithMessage("Role must be Manager or Dispatch.");
}

internal sealed class ChangeTeamMemberRoleCommandHandler(UPBazaarDbContext dbContext, IUserDirectory users, IUserRoles roles)
    : ICommandHandler<ChangeTeamMemberRoleCommand, SellerMemberDto>
{
    public async Task<Result<SellerMemberDto>> HandleAsync(ChangeTeamMemberRoleCommand command, CancellationToken cancellationToken)
    {
        var (seller, member) = await Team.MemberAsync(dbContext, command.OwnerUserId, command.MemberId, cancellationToken);

        if (seller is null || member is null)
        {
            return Result.Failure<SellerMemberDto>(SellerErrors.MemberNotFound);
        }

        var role = Enum.Parse<SellerMemberRole>(command.Role, ignoreCase: true);

        if (role != member.Role)
        {
            var revoked = await roles.StageRevokeAsync(member.UserId, SellerMember.IdentityRole(member.Role), cancellationToken);
            var granted = revoked.IsSuccess
                ? await roles.StageGrantAsync(member.UserId, SellerMember.IdentityRole(role), $"seller-team:{seller.PublicId}", cancellationToken)
                : revoked;

            if (granted.IsFailure)
            {
                return Result.Failure<SellerMemberDto>(granted.Error);
            }

            member.ChangeRole(role);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var user = await users.GetUserAsync(member.UserId, cancellationToken);

        return member.ToDto(user.IsSuccess ? user.Value : null);
    }
}

/// <summary>
/// The owner takes someone off the team. They lose the shop at once - every seller request looks
/// the shop up afresh - and the role with it.
/// </summary>
public sealed record RemoveTeamMemberCommand(Guid OwnerUserId, Guid MemberId) : ICommand;

internal sealed class RemoveTeamMemberCommandHandler(UPBazaarDbContext dbContext, IUserRoles roles)
    : ICommandHandler<RemoveTeamMemberCommand>
{
    public async Task<Result> HandleAsync(RemoveTeamMemberCommand command, CancellationToken cancellationToken)
    {
        var (seller, member) = await Team.MemberAsync(dbContext, command.OwnerUserId, command.MemberId, cancellationToken);

        if (seller is null || member is null)
        {
            return Result.Failure(SellerErrors.MemberNotFound);
        }

        var revoked = await roles.StageRevokeAsync(member.UserId, SellerMember.IdentityRole(member.Role), cancellationToken);

        if (revoked.IsFailure)
        {
            return revoked;
        }

        dbContext.Set<SellerMember>().Remove(member);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Finding the owner's shop and the members on it.</summary>
internal static class Team
{
    /// <summary>More than a small business needs; a check on a shop handing out access wholesale.</summary>
    public const int MaxMembers = 20;

    public static Task<Seller?> OwnedAsync(UPBazaarDbContext dbContext, Guid ownerUserId, CancellationToken cancellationToken) =>
        dbContext.Set<Seller>().FirstOrDefaultAsync(s => s.OwnerUserId == ownerUserId, cancellationToken);

    /// <summary>The owner's shop and one of its members; either null when there is no such pair.</summary>
    public static async Task<(Seller? Seller, SellerMember? Member)> MemberAsync(
        UPBazaarDbContext dbContext,
        Guid ownerUserId,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var seller = await OwnedAsync(dbContext, ownerUserId, cancellationToken);

        if (seller is null)
        {
            return (null, null);
        }

        var member = await dbContext.Set<SellerMember>()
            .FirstOrDefaultAsync(m => m.PublicId == memberId && m.SellerId == seller.Id, cancellationToken);

        return (seller, member);
    }

    public static SellerMemberDto ToDto(this SellerMember member, Identity.Contracts.Dtos.UserSummaryDto? user) => new(
        member.PublicId,
        member.UserId,
        user?.DisplayName ?? string.Empty,
        user?.Email,
        member.Role.ToString(),
        member.AddedAtUtc);
}
