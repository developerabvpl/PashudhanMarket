using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPBazaar.Infrastructure.Api;
using UPBazaar.Modules.Catalog.Contracts;
using UPBazaar.Modules.Inventory.Application;
using UPBazaar.Modules.Inventory.Contracts.Dtos;
using UPBazaar.Modules.Inventory.Contracts.Permissions;
using UPBazaar.Modules.Inventory.Domain;
using UPBazaar.Modules.Sellers.Contracts;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Inventory.Api;

/// <summary>
/// A seller keeping their own stock figures true. Only a count: a seller says how many they have
/// and the ledger records the difference. Receipts and write-offs stay with staff, since a
/// write-off is a loss someone has to account for.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/seller/inventory")]
[Produces("application/json")]
[Authorize(InventoryPermissions.OwnStockWrite)]
public sealed class SellerInventoryController(
    IDispatcher dispatcher,
    ICurrentUser currentUser,
    ISellerDirectory sellers,
    IProductCatalog catalog) : ControllerBase
{
    /// <summary>Returns stock for one of the seller's products, with its recent ledger.</summary>
    [HttpGet("stock/{productId:guid}")]
    [EndpointSummary("Get my stock")]
    [ProducesResponseType<StockDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<StockDetailDto>> Get(Guid productId, CancellationToken cancellationToken) =>
        ForOwnProduct(productId, () => dispatcher.QueryAsync(new GetStockQuery(productId), cancellationToken), cancellationToken);

    /// <summary>Records how many the seller has.</summary>
    [HttpPut("stock/{productId:guid}")]
    [EndpointSummary("Record my stock count")]
    [EndpointDescription("Sets stock on hand to what was counted. It may not fall below what is held for open checkouts.")]
    [ProducesResponseType<StockLevelDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<StockLevelDto>> Count(Guid productId, CountStockRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ForOwnProduct(
            productId,
            () => dispatcher.SendAsync(new CountStockCommand(productId, request.OnHandQuantity, request.Reason), cancellationToken),
            cancellationToken);
    }

    /// <summary>Runs the action only for a product the caller's approved shop owns; any other answers as not found.</summary>
    private async Task<ActionResult<T>> ForOwnProduct<T>(
        Guid productId,
        Func<Task<Result<T>>> action,
        CancellationToken cancellationToken)
    {
        var seller = Guid.TryParse(currentUser.UserId, out var user)
            ? await sellers.GetApprovedSellerIdAsync(user, cancellationToken)
            : null;

        if (seller is null)
        {
            return Result.Failure<T>(SellerAccessErrors.NotAnApprovedSeller).ToActionResult();
        }

        var products = await catalog.GetProductsAsync([productId], cancellationToken);

        if (!products.TryGetValue(productId, out var product) || product.SellerId != seller)
        {
            return Result.Failure<T>(InventoryErrors.ProductNotFound).ToActionResult();
        }

        return (await action()).ToActionResult();
    }
}
