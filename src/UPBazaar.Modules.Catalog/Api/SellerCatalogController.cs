using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPBazaar.Infrastructure.Api;
using UPBazaar.Modules.Catalog.Application.Products;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Catalog.Contracts.Permissions;
using UPBazaar.Modules.Sellers.Contracts;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Catalog.Api;

/// <summary>
/// A seller's own listings. The seller is found from the caller's token; no route takes a seller
/// id, so no request can reach another seller's products.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/seller/catalog")]
[Produces("application/json")]
[Authorize(CatalogPermissions.OwnProductsWrite)]
public sealed class SellerCatalogController(IDispatcher dispatcher, ICurrentUser currentUser, ISellerDirectory sellers)
    : ControllerBase
{
    /// <summary>Lists the seller's products.</summary>
    [HttpGet("products")]
    [EndpointSummary("List my products")]
    [EndpointDescription("Every listing in any status: Draft, InReview, Active or Archived. Search matches name, SKU and brand.")]
    [ProducesResponseType<PagedList<ProductSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<ActionResult<PagedList<ProductSummaryDto>>> List(
        [FromQuery] SellerProductListRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return AsSeller(seller => dispatcher.QueryAsync(
            new ListProductsQuery(request.Page ?? 1, request.PageSize ?? 25, request.Search, null, request.Status, seller, IncludeUnpublished: true),
            cancellationToken));
    }

    /// <summary>Returns one of the seller's products.</summary>
    [HttpGet("products/{productId:guid}")]
    [EndpointSummary("Get my product")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<ProductDto>> Get(Guid productId, CancellationToken cancellationToken) =>
        AsSeller(seller => dispatcher.QueryAsync(new GetSellerProductQuery(seller, productId), cancellationToken));

    /// <summary>Creates a draft listing.</summary>
    [HttpPost("products")]
    [EndpointSummary("Create a product")]
    [EndpointDescription("Creates a Draft under the caller's shop. Submit it for review to have a moderator publish it.")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<ProductDto>> Create(SellerCreateProductRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return AsSeller(seller => dispatcher.SendAsync(
            new CreateProductCommand(
                request.Sku,
                request.Name,
                request.Brand,
                request.Description,
                request.Price,
                seller,
                request.CategoryId,
                request.OnHandQuantity ?? 0),
            cancellationToken));
    }

    /// <summary>Edits a draft.</summary>
    [HttpPut("products/{productId:guid}")]
    [EndpointSummary("Edit my draft")]
    [EndpointDescription("Name, brand, description, price and category of a Draft. A live listing's wording goes through a moderator.")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<ProductDto>> Update(Guid productId, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return AsSeller(seller => dispatcher.SendAsync(
            new UpdateSellerDraftCommand(seller, productId, request.Name, request.Brand, request.Description, request.Price, request.CategoryId),
            cancellationToken));
    }

    /// <summary>Changes a product's price.</summary>
    [HttpPut("products/{productId:guid}/price")]
    [EndpointSummary("Change my price")]
    [EndpointDescription("Allowed on a live listing too. Buyers with it in their cart see the change flagged before checkout.")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<ProductDto>> Reprice(Guid productId, SellerPriceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return AsSeller(seller => dispatcher.SendAsync(new RepriceSellerProductCommand(seller, productId, request.Price), cancellationToken));
    }

    /// <summary>Puts a product on sale.</summary>
    [HttpPut("products/{productId:guid}/sale")]
    [EndpointSummary("Put my product on sale")]
    [EndpointDescription(
        "A lower price between two moments, replacing any sale already set. You are paid on the sale "
        + "price. Buyers with it in their cart see the price change flagged when it starts and ends.")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<ProductDto>> SetSale(Guid productId, SellerSaleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return AsSeller(seller => dispatcher.SendAsync(
            new SetProductSaleCommand(seller, productId, request.SalePrice, request.StartsAtUtc, request.EndsAtUtc),
            cancellationToken));
    }

    /// <summary>Ends a product's sale.</summary>
    [HttpDelete("products/{productId:guid}/sale")]
    [EndpointSummary("End my sale")]
    [EndpointDescription("Ends a running sale now, or calls off one still to come.")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<ProductDto>> EndSale(Guid productId, CancellationToken cancellationToken) =>
        AsSeller(seller => dispatcher.SendAsync(new EndProductSaleCommand(productId, seller), cancellationToken));

    /// <summary>Sets how a product ships.</summary>
    [HttpPut("products/{productId:guid}/package")]
    [EndpointSummary("Set my product's package")]
    [EndpointDescription("One unit's packed weight (grams) and box size (centimetres). All four values, or all null to clear.")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<ProductDto>> SetPackage(Guid productId, ProductPackageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return AsSeller(async seller =>
        {
            var owned = await dispatcher.QueryAsync(new GetSellerProductQuery(seller, productId), cancellationToken);

            return owned.IsFailure
                ? owned
                : await dispatcher.SendAsync(
                    new SetProductPackageCommand(productId, request.WeightGrams, request.LengthCm, request.BreadthCm, request.HeightCm),
                    cancellationToken);
        });
    }

    /// <summary>Submits a draft for review.</summary>
    [HttpPost("products/{productId:guid}/submit")]
    [EndpointSummary("Submit for review")]
    [EndpointDescription("Moves a Draft to InReview. A moderator publishes it, or sends it back with a note.")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<ProductDto>> Submit(Guid productId, CancellationToken cancellationToken) =>
        AsSeller(seller => dispatcher.SendAsync(new SubmitProductForReviewCommand(seller, productId), cancellationToken));

    private async Task<ActionResult<T>> AsSeller<T>(Func<Guid, Task<Result<T>>> action)
    {
        var seller = Guid.TryParse(currentUser.UserId, out var user)
            ? await sellers.GetApprovedSellerIdAsync(user, HttpContext.RequestAborted)
            : null;

        return seller is { } id
            ? (await action(id)).ToActionResult()
            : Result.Failure<T>(SellerAccessErrors.NotAnApprovedSeller).ToActionResult();
    }
}

/// <param name="Page">1-based page number. Defaults to 1.</param>
/// <param name="PageSize">Items per page, 1 to 100. Defaults to 25.</param>
/// <param name="Search">Matches name, SKU and brand.</param>
/// <param name="Status">Draft, InReview, Active or Archived.</param>
public sealed record SellerProductListRequest(int? Page, int? PageSize, string? Search, string? Status);

/// <param name="Sku">Stock-keeping unit, unique across the catalogue.</param>
/// <param name="Name">Listing title.</param>
/// <param name="Brand">Brand, if any.</param>
/// <param name="Description">Long description, if any.</param>
/// <param name="Price">Price in rupees.</param>
/// <param name="CategoryId">Category to list under.</param>
/// <param name="OnHandQuantity">Opening stock. Defaults to 0.</param>
public sealed record SellerCreateProductRequest(
    string Sku,
    string Name,
    string? Brand,
    string? Description,
    decimal Price,
    Guid CategoryId,
    int? OnHandQuantity);

/// <param name="Price">New price in rupees.</param>
public sealed record SellerPriceRequest(decimal Price);

/// <param name="SalePrice">The price while the sale runs, in rupees; below the regular price.</param>
/// <param name="EndsAtUtc">When the regular price comes back.</param>
/// <param name="StartsAtUtc">When the sale starts; now if left out.</param>
public sealed record SellerSaleRequest(decimal SalePrice, DateTime EndsAtUtc, DateTime? StartsAtUtc = null);
