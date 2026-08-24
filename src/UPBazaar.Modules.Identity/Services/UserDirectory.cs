using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Identity.Application.Users;
using UPBazaar.Modules.Identity.Contracts;
using UPBazaar.Modules.Identity.Contracts.Dtos;
using UPBazaar.Modules.Identity.Domain;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Identity.Services;

/// <summary>
/// The identity side of every cross-module conversation.
///
/// Returns summaries only: another module has no business seeing a password hash, a TOTP
/// secret or a refresh token, and this is the type system saying so.
/// </summary>
internal sealed class UserDirectory(UPBazaarDbContext dbContext) : IUserDirectory
{
    public async Task<Result<UserSummaryDto>> GetUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Set<User>()
            .AsNoTracking()
            .Include(u => u.Roles)
            .ThenInclude(r => r.Role)
            .FirstOrDefaultAsync(u => u.PublicId == userId, cancellationToken);

        return user is null
            ? Result.Failure<UserSummaryDto>(IdentityErrors.UserNotFound)
            : user.ToSummary();
    }

    public async Task<Result<IReadOnlyList<UserSummaryDto>>> GetUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        var users = await dbContext.Set<User>()
            .AsNoTracking()
            .Include(u => u.Roles)
            .ThenInclude(r => r.Role)
            .Where(u => userIds.Contains(u.PublicId))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<UserSummaryDto>>([.. users.Select(u => u.ToSummary())]);
    }
}
