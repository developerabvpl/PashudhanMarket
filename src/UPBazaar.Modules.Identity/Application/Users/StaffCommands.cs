using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Identity.Contracts.Dtos;
using UPBazaar.Modules.Identity.Domain;
using UPBazaar.Modules.Identity.Services;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Identity.Application.Users;

/// <summary>Creates a staff account with an initial password and roles.</summary>
public sealed record CreateStaffUserCommand(
    string Email,
    string Password,
    string DisplayName,
    string PreferredLanguage,
    IReadOnlyList<string> Roles) : ICommand<UserDto>;

internal sealed class CreateStaffUserCommandValidator : AbstractValidator<CreateStaffUserCommand>
{
    public CreateStaffUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(12).MaximumLength(256)
            .WithMessage("Choose a password of at least 12 characters.");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(128);
        RuleFor(x => x.PreferredLanguage).NotEmpty().Must(l => l is "en" or "hi");
        RuleFor(x => x.Roles).NotNull();
    }
}

/// <summary>
/// Creates a staff account.
///
/// The audit row this writes is produced by the persistence interceptor because
/// <see cref="User"/> implements <c>IAuditable</c> - nothing here has to remember to log it.
/// </summary>
internal sealed class CreateStaffUserCommandHandler(
    UPBazaarDbContext dbContext,
    IPasswordHasher<User> passwordHasher,
    ICurrentUser currentUser,
    IClock clock) : ICommandHandler<CreateStaffUserCommand, UserDto>
{
    public async Task<Result<UserDto>> HandleAsync(
        CreateStaffUserCommand command,
        CancellationToken cancellationToken)
    {
        var email = User.Normalize(command.Email);

        if (await dbContext.Set<User>().AnyAsync(u => u.Email == email, cancellationToken))
        {
            return Result.Failure<UserDto>(IdentityErrors.EmailAlreadyRegistered);
        }

        var roles = await ResolveRolesAsync(dbContext, command.Roles, cancellationToken);

        if (roles.IsFailure)
        {
            return Result.Failure<UserDto>(roles.Error);
        }

        var user = User.CreateWithPassword(
            UserType.Staff,
            email,
            passwordHash: string.Empty,
            command.DisplayName.Trim(),
            command.PreferredLanguage);

        user.SetPassword(passwordHasher.HashPassword(user, command.Password));

        dbContext.Set<User>().Add(user);

        // Saved before roles are attached so the join rows have a real user id.
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var role in roles.Value)
        {
            user.AssignRole(role, clock.UtcNow, currentUser.UserId);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return user.ToDto();
    }

    internal static async Task<Result<List<Role>>> ResolveRolesAsync(
        UPBazaarDbContext dbContext,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken)
    {
        if (names.Count == 0)
        {
            return Result.Success(new List<Role>());
        }

        var roles = await dbContext.Set<Role>()
            .Include(r => r.Permissions)
            .ThenInclude(p => p.Permission)
            .Where(r => names.Contains(r.Name))
            .ToListAsync(cancellationToken);

        return roles.Count == names.Distinct(StringComparer.Ordinal).Count()
            ? Result.Success(roles)
            : Result.Failure<List<Role>>(IdentityErrors.RoleNotFound);
    }
}

/// <summary>Updates a staff member's profile and status.</summary>
public sealed record UpdateStaffUserCommand(
    Guid UserId,
    string DisplayName,
    string PreferredLanguage,
    string Status) : ICommand<UserDto>;

internal sealed class UpdateStaffUserCommandValidator : AbstractValidator<UpdateStaffUserCommand>
{
    public UpdateStaffUserCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(128);
        RuleFor(x => x.PreferredLanguage).NotEmpty().Must(l => l is "en" or "hi");
        RuleFor(x => x.Status).NotEmpty()
            .Must(s => Enum.TryParse<UserStatus>(s, ignoreCase: true, out _))
            .WithMessage("Status must be Active, Suspended or Deactivated.");
    }
}

internal sealed class UpdateStaffUserCommandHandler(
    UPBazaarDbContext dbContext,
    TokenService tokenService,
    ICurrentUser currentUser) : ICommandHandler<UpdateStaffUserCommand, UserDto>
{
    public async Task<Result<UserDto>> HandleAsync(
        UpdateStaffUserCommand command,
        CancellationToken cancellationToken)
    {
        var user = await LoadAsync(dbContext, command.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<UserDto>(IdentityErrors.UserNotFound);
        }

        var status = Enum.Parse<UserStatus>(command.Status, ignoreCase: true);

        // An administrator suspending themselves would lock the platform out of its own admin
        // tools, and is nearly always a mistake rather than an intent.
        if (status != UserStatus.Active && IsSelf(user, currentUser))
        {
            return Result.Failure<UserDto>(IdentityErrors.CannotModifySelf);
        }

        user.UpdateProfile(command.DisplayName.Trim(), command.PreferredLanguage);
        user.SetStatus(status);

        if (status != UserStatus.Active)
        {
            // A suspended account must lose its live sessions, not merely its ability to sign
            // in again.
            await tokenService.RevokeAllForUserAsync(
                user.Id, RefreshTokenRevocationReason.Administrative, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return user.ToDto();
    }

    internal static Task<User?> LoadAsync(
        UPBazaarDbContext dbContext,
        Guid userId,
        CancellationToken cancellationToken) =>
        dbContext.Set<User>()
            .Include(u => u.Roles)
            .ThenInclude(r => r.Role)
            .ThenInclude(r => r.Permissions)
            .ThenInclude(p => p.Permission)
            .FirstOrDefaultAsync(u => u.PublicId == userId, cancellationToken);

    internal static bool IsSelf(User user, ICurrentUser currentUser) =>
        string.Equals(currentUser.UserId, user.PublicId.ToString(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>Replaces the set of roles a user holds.</summary>
public sealed record AssignRolesCommand(Guid UserId, IReadOnlyList<string> Roles) : ICommand<UserDto>;

internal sealed class AssignRolesCommandValidator : AbstractValidator<AssignRolesCommand>
{
    public AssignRolesCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Roles).NotNull();
    }
}

/// <summary>
/// Sets a user's roles to exactly the list given.
///
/// Replacing rather than adding makes the call idempotent and lets one screen express "these
/// are their roles now" without the caller working out a diff.
/// </summary>
internal sealed class AssignRolesCommandHandler(
    UPBazaarDbContext dbContext,
    ICurrentUser currentUser,
    IClock clock) : ICommandHandler<AssignRolesCommand, UserDto>
{
    public async Task<Result<UserDto>> HandleAsync(
        AssignRolesCommand command,
        CancellationToken cancellationToken)
    {
        var user = await UpdateStaffUserCommandHandler.LoadAsync(
            dbContext, command.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<UserDto>(IdentityErrors.UserNotFound);
        }

        // Nobody edits their own permissions: an administrator who can grant themselves
        // anything makes every other permission check advisory.
        if (UpdateStaffUserCommandHandler.IsSelf(user, currentUser))
        {
            return Result.Failure<UserDto>(IdentityErrors.CannotModifySelf);
        }

        var roles = await CreateStaffUserCommandHandler.ResolveRolesAsync(
            dbContext, command.Roles, cancellationToken);

        if (roles.IsFailure)
        {
            return Result.Failure<UserDto>(roles.Error);
        }

        user.ClearRoles();

        foreach (var role in roles.Value)
        {
            user.AssignRole(role, clock.UtcNow, currentUser.UserId);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return user.ToDto();
    }
}

/// <summary>Closes a staff account.</summary>
public sealed record DeactivateUserCommand(Guid UserId) : ICommand;

internal sealed class DeactivateUserCommandValidator : AbstractValidator<DeactivateUserCommand>
{
    public DeactivateUserCommandValidator() => RuleFor(x => x.UserId).NotEmpty();
}

/// <summary>
/// Deactivates rather than deletes.
///
/// Accounts are referenced by orders, audit rows and settlement history; removing the row
/// would either break those references or quietly rewrite the past.
/// </summary>
internal sealed class DeactivateUserCommandHandler(
    UPBazaarDbContext dbContext,
    TokenService tokenService,
    ICurrentUser currentUser) : ICommandHandler<DeactivateUserCommand>
{
    public async Task<Result> HandleAsync(
        DeactivateUserCommand command,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Set<User>()
            .FirstOrDefaultAsync(u => u.PublicId == command.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure(IdentityErrors.UserNotFound);
        }

        if (UpdateStaffUserCommandHandler.IsSelf(user, currentUser))
        {
            return Result.Failure(IdentityErrors.CannotModifySelf);
        }

        user.SetStatus(UserStatus.Deactivated);

        await tokenService.RevokeAllForUserAsync(
            user.Id, RefreshTokenRevocationReason.Administrative, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
