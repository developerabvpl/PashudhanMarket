using UPBazaar.Modules.Identity.Contracts.Dtos;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Identity.Contracts;

/// <summary>
/// Read surface other modules use instead of querying identity tables. Orders needs a buyer's
/// display name; Sellers needs to confirm an account exists. Neither gets to see a password
/// hash or a refresh token.
/// </summary>
public interface IUserDirectory
{
    Task<Result<UserSummaryDto>> GetUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<UserSummaryDto>>> GetUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken);

    /// <summary>The account registered with this email address, compared as sign-in compares it.</summary>
    Task<Result<UserSummaryDto>> FindByEmailAsync(string email, CancellationToken cancellationToken);
}
