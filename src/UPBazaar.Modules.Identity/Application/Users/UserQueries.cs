using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Identity.Contracts.Dtos;
using UPBazaar.Modules.Identity.Domain;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Identity.Application.Users;

/// <summary>Maps identity entities to the DTOs the API exposes.</summary>
internal static class UserMappings
{
    public static UserDto ToDto(this User user) => new(
        user.PublicId,
        user.UserType.ToString(),
        user.Email,
        user.EmailVerified,
        user.Mobile,
        user.MobileVerified,
        user.DisplayName,
        user.PreferredLanguage,
        user.Status.ToString(),
        user.TwoFactorEnabled,
        [.. user.Roles.Select(r => r.Role.Name).OrderBy(n => n, StringComparer.Ordinal)],
        [.. user.Roles
            .SelectMany(r => r.Role.Permissions)
            .Select(p => p.Permission.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)],
        user.CreatedAtUtc);

    public static UserSummaryDto ToSummary(this User user) => new(
        user.PublicId,
        user.UserType.ToString(),
        user.Email,
        user.Mobile,
        user.DisplayName,
        user.Status.ToString(),
        [.. user.Roles.Select(r => r.Role.Name).OrderBy(n => n, StringComparer.Ordinal)],
        user.CreatedAtUtc);
}

/// <summary>Returns the signed-in user's own profile.</summary>
public sealed record GetCurrentUserQuery(Guid UserId) : IQuery<UserDto>;

internal sealed class GetCurrentUserQueryHandler(UPBazaarDbContext dbContext)
    : IQueryHandler<GetCurrentUserQuery, UserDto>
{
    public async Task<Result<UserDto>> HandleAsync(
        GetCurrentUserQuery query,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Set<User>()
            .AsNoTracking()
            .Include(u => u.Roles)
            .ThenInclude(r => r.Role)
            .ThenInclude(r => r.Permissions)
            .ThenInclude(p => p.Permission)
            .FirstOrDefaultAsync(u => u.PublicId == query.UserId, cancellationToken);

        return user is null
            ? Result.Failure<UserDto>(IdentityErrors.UserNotFound)
            : user.ToDto();
    }
}

/// <summary>Lists accounts for an administrator.</summary>
public sealed record ListUsersQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    string? UserType = null) : IQuery<PagedList<UserSummaryDto>>;

internal sealed class ListUsersQueryValidator : AbstractValidator<ListUsersQuery>
{
    public ListUsersQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(256);
    }
}

internal sealed class ListUsersQueryHandler(UPBazaarDbContext dbContext)
    : IQueryHandler<ListUsersQuery, PagedList<UserSummaryDto>>
{
    public async Task<Result<PagedList<UserSummaryDto>>> HandleAsync(
        ListUsersQuery query,
        CancellationToken cancellationToken)
    {
        var users = dbContext.Set<User>()
            .AsNoTracking()
            .Include(u => u.Roles)
            .ThenInclude(r => r.Role)
            .AsQueryable();

        if (Enum.TryParse<UserType>(query.UserType, ignoreCase: true, out var userType))
        {
            users = users.Where(u => u.UserType == userType);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();

            users = users.Where(u =>
                u.DisplayName.Contains(search)
                || (u.Email != null && u.Email.Contains(search))
                || (u.Mobile != null && u.Mobile.Contains(search)));
        }

        var totalCount = await users.CountAsync(cancellationToken);

        var page = await users
            .OrderByDescending(u => u.CreatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<UserSummaryDto>(
            [.. page.Select(u => u.ToSummary())],
            query.Page,
            query.PageSize,
            totalCount);
    }
}

/// <summary>Returns one account in full, for an administrator.</summary>
public sealed record GetUserQuery(Guid UserId) : IQuery<UserDto>;

internal sealed class GetUserQueryHandler(UPBazaarDbContext dbContext)
    : IQueryHandler<GetUserQuery, UserDto>
{
    public async Task<Result<UserDto>> HandleAsync(GetUserQuery query, CancellationToken cancellationToken)
    {
        var user = await dbContext.Set<User>()
            .AsNoTracking()
            .Include(u => u.Roles)
            .ThenInclude(r => r.Role)
            .ThenInclude(r => r.Permissions)
            .ThenInclude(p => p.Permission)
            .FirstOrDefaultAsync(u => u.PublicId == query.UserId, cancellationToken);

        return user is null
            ? Result.Failure<UserDto>(IdentityErrors.UserNotFound)
            : user.ToDto();
    }
}

/// <summary>Lists the roles an administrator can assign.</summary>
public sealed record ListRolesQuery : IQuery<IReadOnlyList<RoleDto>>;

internal sealed class ListRolesQueryHandler(UPBazaarDbContext dbContext)
    : IQueryHandler<ListRolesQuery, IReadOnlyList<RoleDto>>
{
    public async Task<Result<IReadOnlyList<RoleDto>>> HandleAsync(
        ListRolesQuery query,
        CancellationToken cancellationToken)
    {
        var roles = await dbContext.Set<Role>()
            .AsNoTracking()
            .Include(r => r.Permissions)
            .ThenInclude(p => p.Permission)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<RoleDto>>(
        [
            .. roles.Select(r => new RoleDto(
                r.Name,
                r.Description,
                [.. r.Permissions.Select(p => p.Permission.Name).OrderBy(n => n, StringComparer.Ordinal)]))
        ]);
    }
}
