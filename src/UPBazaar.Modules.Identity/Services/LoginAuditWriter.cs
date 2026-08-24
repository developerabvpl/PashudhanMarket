using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Identity.Domain;
using UPBazaar.SharedKernel.Abstractions;

namespace UPBazaar.Modules.Identity.Services;

/// <summary>
/// Adds rows to <c>identity.LoginAudit</c>.
///
/// A thin helper rather than a repository: every handler needs the same three ambient values
/// (clock, correlation id, request origin) and forgetting one of them is how audit trails end
/// up half useful. Rows are added to the change tracker, so they commit with the surrounding
/// transaction and a rolled-back sign-in leaves no trace of having succeeded.
/// </summary>
public sealed class LoginAuditWriter(
    UPBazaarDbContext dbContext,
    IClock clock,
    ICorrelationContext correlation)
{
    public void RecordSuccess(long userId, LoginMethod method, RequestOrigin origin) =>
        dbContext.Set<LoginAudit>().Add(LoginAudit.Success(
            userId,
            method,
            clock.UtcNow,
            origin.IpAddress,
            origin.UserAgent,
            correlation.CorrelationId));

    public void RecordFailure(
        long? userId,
        string? attemptedIdentifier,
        LoginMethod method,
        LoginFailureReason reason,
        RequestOrigin origin) =>
        dbContext.Set<LoginAudit>().Add(LoginAudit.Failure(
            userId,
            attemptedIdentifier,
            method,
            reason,
            clock.UtcNow,
            origin.IpAddress,
            origin.UserAgent,
            correlation.CorrelationId));
}
