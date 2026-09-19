using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPBazaar.Infrastructure.Api;
using UPBazaar.Modules.Cart.Application;
using UPBazaar.Modules.Cart.Contracts.Dtos;
using UPBazaar.Modules.Cart.Contracts.Permissions;
using UPBazaar.Modules.Cart.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Cart.Api;

/// <summary>
/// The signed-in buyer's own cart. There is no cart id in any route: the cart is always the
/// caller's, found from their token, so no request can name somebody else's.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/cart")]
[Produces("application/json")]
public sealed class CartController(IDispatcher dispatcher, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Returns the cart.</summary>
    [HttpGet]
    [Authorize(CartPermissions.Read)]
    [EndpointSummary("Get my cart")]
    [EndpointDescription(
        "Returns the cart with current prices and stock. Each line carries a problem flag when it "
        + "cannot be bought as it stands: Unavailable, InsufficientStock or PriceChanged.")]
    [ProducesResponseType<CartDto>(StatusCodes.Status200OK)]
    public Task<ActionResult<CartDto>> Get(CancellationToken cancellationToken) =>
        AsBuyer(buyerId => dispatcher.QueryAsync(new GetCartQuery(buyerId), cancellationToken));

    /// <summary>Sets a product's quantity.</summary>
    [HttpPut("items/{productId:guid}")]
    [Authorize(CartPermissions.Write)]
    [EndpointSummary("Set quantity")]
    [EndpointDescription(
        "Sets how many of a product the cart holds; 0 removes it. Raising a quantity above what is "
        + "in stock is refused, lowering one never is.")]
    [ProducesResponseType<CartDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<CartDto>> SetItem(
        Guid productId,
        SetCartItemRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return AsBuyer(buyerId =>
            dispatcher.SendAsync(new SetCartItemCommand(buyerId, productId, request.Quantity), cancellationToken));
    }

    /// <summary>Removes a product.</summary>
    [HttpDelete("items/{productId:guid}")]
    [Authorize(CartPermissions.Write)]
    [EndpointSummary("Remove an item")]
    [EndpointDescription("Removes a product from the cart. Removing one that is not there changes nothing.")]
    [ProducesResponseType<CartDto>(StatusCodes.Status200OK)]
    public Task<ActionResult<CartDto>> RemoveItem(Guid productId, CancellationToken cancellationToken) =>
        AsBuyer(buyerId => dispatcher.SendAsync(new SetCartItemCommand(buyerId, productId, 0), cancellationToken));

    /// <summary>Empties the cart.</summary>
    [HttpDelete]
    [Authorize(CartPermissions.Write)]
    [EndpointSummary("Empty my cart")]
    [EndpointDescription("Removes every line.")]
    [ProducesResponseType<CartDto>(StatusCodes.Status200OK)]
    public Task<ActionResult<CartDto>> Clear(CancellationToken cancellationToken) =>
        AsBuyer(buyerId => dispatcher.SendAsync(new ClearCartCommand(buyerId), cancellationToken));

    /// <summary>Accepts current prices.</summary>
    [HttpPost("acknowledge-prices")]
    [Authorize(CartPermissions.Write)]
    [EndpointSummary("Accept price changes")]
    [EndpointDescription("Takes every line's current price as seen, clearing the PriceChanged flags.")]
    [ProducesResponseType<CartDto>(StatusCodes.Status200OK)]
    public Task<ActionResult<CartDto>> AcknowledgePrices(CancellationToken cancellationToken) =>
        AsBuyer(buyerId => dispatcher.SendAsync(new AcknowledgeCartPricesCommand(buyerId), cancellationToken));

    /// <summary>Merges a guest basket.</summary>
    [HttpPost("merge")]
    [Authorize(CartPermissions.Write)]
    [EndpointSummary("Merge a guest basket")]
    [EndpointDescription(
        "Adds the basket filled before signing in to the saved cart. Quantities add up and are "
        + "capped; products no longer on sale are skipped and listed in the response.")]
    [ProducesResponseType<CartMergeResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<ActionResult<CartMergeResultDto>> Merge(
        MergeCartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var lines = (request.Lines ?? []).Select(l => new MergeLine(l.ProductId, l.Quantity)).ToList();

        return AsBuyer(buyerId => dispatcher.SendAsync(new MergeCartCommand(buyerId, lines), cancellationToken));
    }

    private async Task<ActionResult<T>> AsBuyer<T>(Func<Guid, Task<Result<T>>> action)
    {
        if (!Guid.TryParse(currentUser.UserId, out var buyerId))
        {
            return Result.Failure<T>(CartErrors.NotSignedIn).ToActionResult();
        }

        return (await action(buyerId)).ToActionResult();
    }
}

/// <param name="Quantity">Units wanted, 0 to 99. 0 removes the line.</param>
public sealed record SetCartItemRequest(int Quantity);

/// <param name="Lines">
/// The guest basket's lines. Declared non-nullable on purpose: as a nullable array its schema
/// becomes <c>["null","array"]</c>, and the web client generator then misses the line type and
/// emits an import of a model it never wrote.
/// </param>
public sealed record MergeCartRequest(IReadOnlyList<MergeCartLineRequest> Lines);

/// <param name="ProductId">Catalog public id.</param>
/// <param name="Quantity">Units in the guest basket.</param>
public sealed record MergeCartLineRequest(Guid ProductId, int Quantity);
