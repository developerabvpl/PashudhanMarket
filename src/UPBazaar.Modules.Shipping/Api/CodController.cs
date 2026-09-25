using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPBazaar.Infrastructure.Api;
using UPBazaar.Modules.Shipping.Application;
using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Contracts.Permissions;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Shipping.Api;

/// <summary>
/// Cash on delivery: what the courier collected at buyers' doors, what it has paid over, and what
/// it still owes. Sellers are paid for a cash-on-delivery parcel only once its cash is in.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/shipping/cod")]
[Produces("application/json")]
[Authorize(ShippingPermissions.CodRead)]
public sealed class CodController(IDispatcher dispatcher, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>A remittance report is a few hundred kilobytes; a megabyte leaves room.</summary>
    private const long UploadLimit = 2 * 1024 * 1024;

    /// <summary>Totals of what is owed.</summary>
    [HttpGet("summary")]
    [EndpointSummary("Cash on delivery owed")]
    [ProducesResponseType<CodSummaryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CodSummaryDto>> Summary(CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(new GetCodSummaryQuery(), cancellationToken)).ToActionResult();

    /// <summary>Lists delivered cash-on-delivery parcels.</summary>
    [HttpGet("receivables")]
    [EndpointSummary("List cash-on-delivery parcels")]
    [EndpointDescription(
        "Oldest delivery first. Filter: Owed (not fully paid over; the default), Overdue, Short, "
        + "Over (paid more than collected) or All.")]
    [ProducesResponseType<PagedList<CodReceivableDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedList<CodReceivableDto>>> Receivables(
        [FromQuery] string? filter,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(new ListCodReceivablesQuery(filter, page ?? 1, pageSize ?? 25), cancellationToken)).ToActionResult();

    /// <summary>Writes off what the courier has not paid for a parcel.</summary>
    [HttpPost("receivables/{receivableId:guid}/write-off")]
    [Authorize(ShippingPermissions.CodWrite)]
    [EndpointSummary("Write off a parcel's cash")]
    [EndpointDescription(
        "For cash the courier will not pay over, after raising it with the courier. Counts as "
        + "received, so the seller is paid. The note says why.")]
    [ProducesResponseType<CodReceivableDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CodReceivableDto>> WriteOff(Guid receivableId, CodWriteOffRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.SendAsync(new WriteOffCodReceivableCommand(receivableId, request.Note, currentUser.UserName), cancellationToken))
            .ToActionResult();
    }

    /// <summary>Lists uploaded remittances.</summary>
    [HttpGet("remittances")]
    [EndpointSummary("List remittances")]
    [EndpointDescription("Newest upload first, without their rows.")]
    [ProducesResponseType<PagedList<CodRemittanceDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedList<CodRemittanceDto>>> Remittances(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(new ListCodRemittancesQuery(page ?? 1, pageSize ?? 25), cancellationToken)).ToActionResult();

    /// <summary>Returns one remittance with its rows.</summary>
    [HttpGet("remittances/{remittanceId:guid}")]
    [EndpointSummary("Get a remittance")]
    [ProducesResponseType<CodRemittanceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CodRemittanceDto>> Remittance(Guid remittanceId, CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(new GetCodRemittanceQuery(remittanceId), cancellationToken)).ToActionResult();

    /// <summary>Uploads the courier's remittance report for one bank transfer.</summary>
    [HttpPost("remittances")]
    [Authorize(ShippingPermissions.CodWrite)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UploadLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimit)]
    [EndpointSummary("Upload a remittance report")]
    [EndpointDescription(
        "A CSV with an AWB column and an amount column (such as \"Remitted Amount\"), for the bank "
        + "transfer named by reference, the UTR. Each row pays towards the delivered cash-on-delivery "
        + "parcel it names; rows for parcels not yet reported delivered are matched when they are.")]
    [ProducesResponseType<CodRemittanceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CodRemittanceDto>> Upload(
        [FromForm] string reference,
        [FromForm] DateOnly remittedOn,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        string content;

        using (var reader = new StreamReader(file.OpenReadStream()))
        {
            content = await reader.ReadToEndAsync(cancellationToken);
        }

        return (await dispatcher.SendAsync(
                new ImportCodRemittanceCommand(reference, remittedOn, Path.GetFileName(file.FileName), content, currentUser.UserName),
                cancellationToken))
            .ToActionResult();
    }
}

/// <param name="Note">Why the rest will not come: what the courier said, say.</param>
public sealed record CodWriteOffRequest(string Note);
