using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPBazaar.Infrastructure.Api;
using UPBazaar.Modules.Inventory.Application;
using UPBazaar.Modules.Inventory.Contracts.Dtos;
using UPBazaar.Modules.Inventory.Contracts.Permissions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Inventory.Api;

/// <summary>
/// Stock as staff manage it. Shoppers never call this: they see availability on the product,
/// which Catalog fills in from here.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/inventory")]
[Produces("application/json")]
public sealed class InventoryController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Lists stock levels.</summary>
    [HttpGet("stock")]
    [Authorize(InventoryPermissions.StockRead)]
    [EndpointSummary("List stock")]
    [EndpointDescription(
        "Returns stock levels, lowest availability first. Pass maxAvailable to see only what is "
        + "running low, e.g. maxAvailable=5.")]
    [ProducesResponseType<PagedList<StockLevelDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedList<StockLevelDto>>> ListStock(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] int? maxAvailable,
        CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(new ListStockQuery(page ?? 1, pageSize ?? 25, maxAvailable), cancellationToken))
        .ToActionResult();

    /// <summary>Returns one product's stock and history.</summary>
    [HttpGet("stock/{productId:guid}")]
    [Authorize(InventoryPermissions.StockRead)]
    [EndpointSummary("Get stock for a product")]
    [EndpointDescription("Returns the product's stock level and its most recent movements, newest first.")]
    [ProducesResponseType<StockDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StockDetailDto>> GetStock(Guid productId, CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(new GetStockQuery(productId), cancellationToken)).ToActionResult();

    /// <summary>Records a delivery.</summary>
    [HttpPost("stock/{productId:guid}/receipts")]
    [Authorize(InventoryPermissions.StockWrite)]
    [EndpointSummary("Receive stock")]
    [EndpointDescription("Adds a delivery to stock on hand.")]
    [ProducesResponseType<StockLevelDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StockLevelDto>> Receive(
        Guid productId,
        ReceiveStockRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.SendAsync(
                new ReceiveStockCommand(productId, request.Quantity, request.Reason, request.Reference),
                cancellationToken))
            .ToActionResult();
    }

    /// <summary>Records a stock-take.</summary>
    [HttpPut("stock/{productId:guid}")]
    [Authorize(InventoryPermissions.StockWrite)]
    [EndpointSummary("Record a stock count")]
    [EndpointDescription(
        "Sets stock on hand to what was counted. The ledger records the difference. It may not "
        + "fall below what is reserved for open checkouts.")]
    [ProducesResponseType<StockLevelDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StockLevelDto>> Count(
        Guid productId,
        CountStockRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.SendAsync(
                new CountStockCommand(productId, request.OnHandQuantity, request.Reason),
                cancellationToken))
            .ToActionResult();
    }

    /// <summary>Writes stock off.</summary>
    [HttpPost("stock/{productId:guid}/write-offs")]
    [Authorize(InventoryPermissions.AdjustmentsApprove)]
    [EndpointSummary("Write stock off")]
    [EndpointDescription(
        "Removes lost, damaged or expired stock. Needs a reason, and a permission of its own "
        + "because it is the one movement that could hide theft.")]
    [ProducesResponseType<StockLevelDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StockLevelDto>> WriteOff(
        Guid productId,
        WriteOffStockRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.SendAsync(
                new WriteOffStockCommand(productId, request.Quantity, request.Reason ?? string.Empty),
                cancellationToken))
            .ToActionResult();
    }
}

/// <param name="Quantity">Units delivered.</param>
/// <param name="Reason">Note for the ledger, if any.</param>
/// <param name="Reference">Delivery note or invoice number, if any.</param>
public sealed record ReceiveStockRequest(int Quantity, string? Reason, string? Reference);

/// <param name="OnHandQuantity">Units counted on the shelf.</param>
/// <param name="Reason">Note for the ledger, if any.</param>
public sealed record CountStockRequest(int OnHandQuantity, string? Reason);

/// <param name="Quantity">Units lost, damaged or expired.</param>
/// <param name="Reason">Why. Required.</param>
public sealed record WriteOffStockRequest(int Quantity, string? Reason);
