using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPBazaar.Infrastructure.Api;
using UPBazaar.Modules.Identity.Application.TwoFactor;
using UPBazaar.Modules.Identity.Application.Users;
using UPBazaar.Modules.Identity.Contracts.Dtos;
using UPBazaar.Modules.Identity.Contracts.Permissions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Identity.Api;

/// <summary>The signed-in user's own account.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/users")]
[Produces("application/json")]
public sealed class UsersController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Returns the caller's profile, roles and effective permissions.</summary>
    [HttpGet("me")]
    [Authorize]
    [EndpointSummary("Get my profile")]
    [EndpointDescription("Returns the signed-in account with its roles and effective permissions.")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserDto>> Me(CancellationToken cancellationToken)
    {
        if (HttpContext.CurrentUserId() is not { } userId)
        {
            return Unauthorized();
        }

        return (await dispatcher.QueryAsync(new GetCurrentUserQuery(userId), cancellationToken))
            .ToActionResult();
    }

    /// <summary>Begins TOTP enrolment for the signed-in staff account.</summary>
    [HttpPost("me/2fa/setup")]
    [Authorize]
    [EndpointSummary("Start two-factor setup")]
    [EndpointDescription(
        "Returns a shared key and otpauth URI for an authenticator app. Two-factor stays off "
        + "until a generated code is confirmed.")]
    [ProducesResponseType<TotpSetupDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TotpSetupDto>> SetupTwoFactor(CancellationToken cancellationToken)
    {
        if (HttpContext.CurrentUserId() is not { } userId)
        {
            return Unauthorized();
        }

        return (await dispatcher.SendAsync(new SetupTotpCommand(userId), cancellationToken))
            .ToActionResult();
    }

    /// <summary>Confirms TOTP enrolment.</summary>
    [HttpPost("me/2fa/verify")]
    [Authorize]
    [EndpointSummary("Confirm two-factor setup")]
    [EndpointDescription("Turns two-factor on once a code from the authenticator app checks out.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> ConfirmTwoFactor(
        ConfirmTotpRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (HttpContext.CurrentUserId() is not { } userId)
        {
            return Unauthorized();
        }

        return (await dispatcher.SendAsync(new ConfirmTotpCommand(userId, request.Code), cancellationToken))
            .ToActionResult();
    }
}

/// <param name="Code">Six-digit code from the authenticator app.</param>
public sealed record ConfirmTotpRequest(string Code);

/// <summary>Administration of staff accounts and their roles.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/users")]
[Produces("application/json")]
public sealed class AdminUsersController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Lists accounts.</summary>
    [HttpGet]
    [Authorize(IdentityPermissions.UsersRead)]
    [EndpointSummary("List users")]
    [EndpointDescription("Returns a page of accounts, filtered by search term and user type.")]
    [ProducesResponseType<PagedList<UserSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedList<UserSummaryDto>>> List(
        [FromQuery] ListUsersQuery query,
        CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(query, cancellationToken)).ToActionResult();

    /// <summary>Returns one account.</summary>
    [HttpGet("{userId:guid}", Name = IdentityRoutes.GetUser)]
    [Authorize(IdentityPermissions.UsersRead)]
    [EndpointSummary("Get a user")]
    [EndpointDescription("Returns one account with its roles and effective permissions.")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> Get(Guid userId, CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(new GetUserQuery(userId), cancellationToken)).ToActionResult();

    /// <summary>Creates a staff account.</summary>
    [HttpPost]
    [Authorize(IdentityPermissions.UsersManage)]
    [EndpointSummary("Create a staff user")]
    [EndpointDescription("Creates a staff account with an initial password and set of roles.")]
    [ProducesResponseType<UserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDto>> Create(
        CreateStaffUserRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new CreateStaffUserCommand(
            request.Email,
            request.Password,
            request.DisplayName,
            request.PreferredLanguage ?? "en",
            request.Roles ?? []);

        return (await dispatcher.SendAsync(command, cancellationToken))
            .ToCreatedResult(IdentityRoutes.GetUser, user => new { userId = user.Id });
    }

    /// <summary>Updates a staff account's profile and status.</summary>
    [HttpPut("{userId:guid}")]
    [Authorize(IdentityPermissions.UsersManage)]
    [EndpointSummary("Update a staff user")]
    [EndpointDescription(
        "Changes the display name, language and status. Suspending or deactivating an account "
        + "also revokes its live sessions.")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDto>> Update(
        Guid userId,
        UpdateStaffUserRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new UpdateStaffUserCommand(
            userId,
            request.DisplayName,
            request.PreferredLanguage ?? "en",
            request.Status);

        return (await dispatcher.SendAsync(command, cancellationToken)).ToActionResult();
    }

    /// <summary>Replaces the roles a user holds.</summary>
    [HttpPut("{userId:guid}/roles")]
    [Authorize(IdentityPermissions.UsersManage)]
    [EndpointSummary("Assign roles")]
    [EndpointDescription(
        "Sets the user's roles to exactly the list supplied. Nobody may change their own roles.")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDto>> AssignRoles(
        Guid userId,
        AssignRolesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new AssignRolesCommand(userId, request.Roles ?? []);

        return (await dispatcher.SendAsync(command, cancellationToken)).ToActionResult();
    }

    /// <summary>Closes an account.</summary>
    [HttpDelete("{userId:guid}")]
    [Authorize(IdentityPermissions.UsersManage)]
    [EndpointSummary("Deactivate a user")]
    [EndpointDescription(
        "Marks the account deactivated and revokes its sessions. Accounts are never deleted, "
        + "because orders and audit rows reference them.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Deactivate(Guid userId, CancellationToken cancellationToken) =>
        (await dispatcher.SendAsync(new DeactivateUserCommand(userId), cancellationToken))
        .ToActionResult();

    /// <summary>Lists assignable roles and what they grant.</summary>
    [HttpGet("/api/v{version:apiVersion}/admin/roles")]
    [Authorize(IdentityPermissions.RolesRead)]
    [EndpointSummary("List roles")]
    [EndpointDescription("Returns every role with the permissions it grants.")]
    [ProducesResponseType<IReadOnlyList<RoleDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> ListRoles(CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(new ListRolesQuery(), cancellationToken)).ToActionResult();
}

/// <param name="Email">Sign-in address.</param>
/// <param name="Password">Initial password, at least 12 characters.</param>
/// <param name="DisplayName">Name shown in admin screens.</param>
/// <param name="PreferredLanguage">"en" or "hi". Defaults to "en".</param>
/// <param name="Roles">Role names to grant.</param>
public sealed record CreateStaffUserRequest(
    string Email,
    string Password,
    string DisplayName,
    string? PreferredLanguage,
    IReadOnlyList<string>? Roles);

/// <param name="DisplayName">Name shown in admin screens.</param>
/// <param name="PreferredLanguage">"en" or "hi".</param>
/// <param name="Status">Active, Suspended or Deactivated.</param>
public sealed record UpdateStaffUserRequest(
    string DisplayName,
    string? PreferredLanguage,
    string Status);

/// <param name="Roles">The complete set of role names the user should hold.</param>
public sealed record AssignRolesRequest(IReadOnlyList<string>? Roles);

/// <summary>Named routes, so a created resource can point at its own address.</summary>
public static class IdentityRoutes
{
    public const string GetUser = "Identity_GetUser";
}
