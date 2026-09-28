using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPBazaar.Infrastructure.Api;
using UPBazaar.Modules.Sellers.Application;
using UPBazaar.Modules.Sellers.Contracts.Dtos;
using UPBazaar.Modules.Sellers.Contracts.Permissions;
using UPBazaar.Modules.Sellers.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Sellers.Api;

/// <summary>
/// The caller's own shop. Any signed-in account may apply; there is no seller id in any route,
/// because the shop is always the caller's.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/sellers/me")]
[Produces("application/json")]
[Authorize]
public sealed class MySellerController(IDispatcher dispatcher, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Returns the caller's shop and application.</summary>
    [HttpGet]
    [EndpointSummary("Get my shop")]
    [EndpointDescription("The caller's shop in any state: Pending, Approved or Rejected. Not found if they have not applied.")]
    [ProducesResponseType<SellerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<SellerDto>> Get(CancellationToken cancellationToken) =>
        AsOwner(owner => dispatcher.QueryAsync(new GetMySellerQuery(owner), cancellationToken));

    /// <summary>Applies to sell.</summary>
    [HttpPost("application")]
    [EndpointSummary("Apply to sell")]
    [EndpointDescription(
        "Submits shop details, address and KYC for review. The shop can sell once staff approve it; "
        + "the owner then signs in again, or refreshes their session, to pick up seller access.")]
    [ProducesResponseType<SellerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<SellerDto>> Apply(SellerApplicationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return AsOwner(owner => dispatcher.SendAsync(new ApplyToSellCommand(owner, request.ToApplication()), cancellationToken));
    }

    /// <summary>Corrects and resubmits an application.</summary>
    [HttpPut("application")]
    [EndpointSummary("Resubmit my application")]
    [EndpointDescription("Replaces a pending or rejected application and puts it back in the review queue.")]
    [ProducesResponseType<SellerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<SellerDto>> Resubmit(SellerApplicationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return AsOwner(owner => dispatcher.SendAsync(new ResubmitApplicationCommand(owner, request.ToApplication()), cancellationToken));
    }

    /// <summary>Updates the shop's name, description and contact details.</summary>
    [HttpPut("profile")]
    [EndpointSummary("Update my shop profile")]
    [EndpointDescription("Name, description and contact details, which may change at any time. Legal and payout details may not once approved.")]
    [ProducesResponseType<SellerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<SellerDto>> UpdateProfile(SellerProfileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return AsOwner(owner => dispatcher.SendAsync(
            new UpdateSellerProfileCommand(owner, request.ShopName, request.Description, request.ContactMobile, request.ContactEmail),
            cancellationToken));
    }

    /// <summary>Which shop the caller works for, and as what.</summary>
    [HttpGet("access")]
    [EndpointSummary("Get my seller access")]
    [EndpointDescription(
        "The shop the caller owns or is on the team of, its status, and their role: Owner, Manager "
        + "or Dispatch. Not found if they have neither applied nor been added to a team.")]
    [ProducesResponseType<SellerAccessDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<SellerAccessDto>> Access(CancellationToken cancellationToken) =>
        AsOwner(user => dispatcher.QueryAsync(new GetMyAccessQuery(user), cancellationToken));

    /// <summary>Lists the shop's team.</summary>
    [HttpGet("team")]
    [Authorize(SellersPermissions.OwnManage)]
    [EndpointSummary("List my team")]
    [ProducesResponseType<IReadOnlyList<SellerMemberDto>>(StatusCodes.Status200OK)]
    public Task<ActionResult<IReadOnlyList<SellerMemberDto>>> Team(CancellationToken cancellationToken) =>
        AsOwner(owner => dispatcher.QueryAsync(new ListTeamQuery(owner), cancellationToken));

    /// <summary>Adds someone to the team.</summary>
    [HttpPost("team")]
    [Authorize(SellersPermissions.OwnManage)]
    [EndpointSummary("Add a team member")]
    [EndpointDescription(
        "By the email of the seller-portal account they registered. Manager: products, stock, orders, "
        + "dispatch, coupons and reviews. Dispatch: orders, packing and returns. Neither sees earnings, "
        + "the team or the shop's details. Their access starts when their session next refreshes.")]
    [ProducesResponseType<SellerMemberDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<SellerMemberDto>> AddMember(TeamMemberRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return AsOwner(owner => dispatcher.SendAsync(new AddTeamMemberCommand(owner, request.Email, request.Role), cancellationToken));
    }

    /// <summary>Changes a team member's role.</summary>
    [HttpPut("team/{memberId:guid}")]
    [Authorize(SellersPermissions.OwnManage)]
    [EndpointSummary("Change a team member's role")]
    [ProducesResponseType<SellerMemberDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<SellerMemberDto>> ChangeRole(Guid memberId, TeamRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return AsOwner(owner => dispatcher.SendAsync(new ChangeTeamMemberRoleCommand(owner, memberId, request.Role), cancellationToken));
    }

    /// <summary>Takes someone off the team.</summary>
    [HttpDelete("team/{memberId:guid}")]
    [Authorize(SellersPermissions.OwnManage)]
    [EndpointSummary("Remove a team member")]
    [EndpointDescription("They lose access to the shop at once.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RemoveMember(Guid memberId, CancellationToken cancellationToken) =>
        Guid.TryParse(currentUser.UserId, out var owner)
            ? (await dispatcher.SendAsync(new RemoveTeamMemberCommand(owner, memberId), cancellationToken)).ToActionResult()
            : Result.Failure(SellerErrors.NotSignedIn).ToActionResult();

    private async Task<ActionResult<T>> AsOwner<T>(Func<Guid, Task<Result<T>>> action) =>
        Guid.TryParse(currentUser.UserId, out var owner)
            ? (await action(owner)).ToActionResult()
            : Result.Failure<T>(SellerErrors.NotSignedIn).ToActionResult();
}

/// <param name="Email">The email of the seller-portal account to add.</param>
/// <param name="Role">Manager or Dispatch.</param>
public sealed record TeamMemberRequest(string Email, string Role);

/// <param name="Role">Manager or Dispatch.</param>
public sealed record TeamRoleRequest(string Role);

/// <summary>Staff reviewing and managing sellers.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/sellers")]
[Produces("application/json")]
public sealed class AdminSellersController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Lists sellers.</summary>
    [HttpGet]
    [Authorize(SellersPermissions.Read)]
    [EndpointSummary("List sellers")]
    [EndpointDescription("Filter to Pending for the review queue, oldest first. Search matches shop name, legal name or mobile.")]
    [ProducesResponseType<PagedList<SellerSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedList<SellerSummaryDto>>> List(
        [FromQuery] ListSellersRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.QueryAsync(
                new ListSellersQuery(request.Page ?? 1, request.PageSize ?? 20, request.Status, request.Search),
                cancellationToken))
            .ToActionResult();
    }

    /// <summary>Returns one seller.</summary>
    [HttpGet("{sellerId:guid}")]
    [Authorize(SellersPermissions.Read)]
    [EndpointSummary("Get a seller")]
    [ProducesResponseType<SellerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SellerDto>> Get(Guid sellerId, CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(new GetSellerQuery(sellerId), cancellationToken)).ToActionResult();

    /// <summary>Approves an application.</summary>
    [HttpPost("{sellerId:guid}/approve")]
    [Authorize(SellersPermissions.KycApprove)]
    [EndpointSummary("Approve a seller")]
    [EndpointDescription("Approves a pending application and grants its owner the SellerOwner role.")]
    [ProducesResponseType<SellerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SellerDto>> Approve(Guid sellerId, CancellationToken cancellationToken) =>
        (await dispatcher.SendAsync(new ApproveSellerCommand(sellerId), cancellationToken)).ToActionResult();

    /// <summary>Rejects an application.</summary>
    [HttpPost("{sellerId:guid}/reject")]
    [Authorize(SellersPermissions.KycApprove)]
    [EndpointSummary("Reject a seller")]
    [EndpointDescription("Rejects a pending application with a note the applicant sees. They can correct it and resubmit.")]
    [ProducesResponseType<SellerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SellerDto>> Reject(Guid sellerId, RejectSellerRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.SendAsync(new RejectSellerCommand(sellerId, request.Note), cancellationToken)).ToActionResult();
    }

    /// <summary>Links an owner to a seller that has none.</summary>
    [HttpPost("{sellerId:guid}/owner")]
    [Authorize(SellersPermissions.Write)]
    [EndpointSummary("Link a seller's owner")]
    [EndpointDescription("For a seller set up without an owner, such as the sample catalogue's. Grants SellerOwner if the seller is approved.")]
    [ProducesResponseType<SellerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SellerDto>> LinkOwner(Guid sellerId, LinkSellerOwnerRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.SendAsync(new LinkSellerOwnerCommand(sellerId, request.OwnerUserId), cancellationToken)).ToActionResult();
    }
}

/// <summary>A seller application as submitted.</summary>
public sealed record SellerApplicationRequest(
    string ShopName,
    string? Description,
    string ContactMobile,
    string? ContactEmail,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string Pincode,
    string LegalName,
    string? Gstin,
    string Pan,
    string BankAccountHolder,
    string BankAccountNumber,
    string Ifsc)
{
    internal SellerApplication ToApplication() => new(
        ShopName, Description, ContactMobile, ContactEmail, AddressLine1, AddressLine2, City, State, Pincode,
        LegalName, Gstin, Pan, BankAccountHolder, BankAccountNumber, Ifsc);
}

/// <param name="ShopName">Name buyers see.</param>
/// <param name="Description">About the shop.</param>
/// <param name="ContactMobile">Ten-digit mobile number.</param>
/// <param name="ContactEmail">Email, if any.</param>
public sealed record SellerProfileRequest(string ShopName, string? Description, string ContactMobile, string? ContactEmail);

/// <param name="Note">Why, shown to the applicant.</param>
public sealed record RejectSellerRequest(string Note);

/// <param name="OwnerUserId">Identity's id for the account that will run the shop.</param>
public sealed record LinkSellerOwnerRequest(Guid OwnerUserId);

/// <param name="Page">1-based page number. Defaults to 1.</param>
/// <param name="PageSize">Items per page, 1 to 100. Defaults to 20.</param>
/// <param name="Status">Pending, Approved or Rejected.</param>
/// <param name="Search">Shop name, legal name or mobile.</param>
public sealed record ListSellersRequest(int? Page, int? PageSize, string? Status, string? Search);
