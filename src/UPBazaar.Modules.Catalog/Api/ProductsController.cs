using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPBazaar.Infrastructure.Api;
using UPBazaar.Modules.Catalog.Application.Products;
using UPBazaar.Modules.Catalog.Application.Stock;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Catalog.Contracts.Permissions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Catalog.Api;

[ApiController]
[Route("api/catalog/products")]
[Produces("application/json")]
public sealed class ProductsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [Authorize(CatalogPermissions.ProductsRead)]
    [EndpointSummary("List products")]
    [EndpointDescription("Returns a page of products, filtered by search term and category.")]
    [ProducesResponseType<PagedList<ProductSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedList<ProductSummaryDto>>> List(
        [FromQuery] ListProductsQuery query,
        CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(query, cancellationToken)).ToActionResult();

    [HttpGet("{productId:guid}", Name = CatalogRoutes.GetProduct)]
    [Authorize(CatalogPermissions.ProductsRead)]
    [EndpointSummary("Get a product")]
    [EndpointDescription("Returns one product with its category and stock position.")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> Get(Guid productId, CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(new GetProductQuery(productId), cancellationToken)).ToActionResult();

    [HttpPost]
    [Authorize(CatalogPermissions.ProductsWrite)]
    [EndpointSummary("Create a product")]
    [EndpointDescription("Creates a draft product with its initial stock. Publish it to make it sellable.")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductDto>> Create(
        CreateProductCommand command,
        CancellationToken cancellationToken) =>
        (await dispatcher.SendAsync(command, cancellationToken))
        .ToCreatedResult(CatalogRoutes.GetProduct, product => new { productId = product.Id });

    [HttpPost("{productId:guid}/publish")]
    [Authorize(CatalogPermissions.ProductsWrite)]
    [EndpointSummary("Publish a product")]
    [EndpointDescription("Moves a draft product to Active so it can be purchased.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Publish(Guid productId, CancellationToken cancellationToken) =>
        (await dispatcher.SendAsync(new PublishProductCommand(productId), cancellationToken))
        .ToActionResult();

    [HttpPut("{productId:guid}/stock")]
    [Authorize(CatalogPermissions.StockWrite)]
    [EndpointSummary("Adjust stock")]
    [EndpointDescription("Sets the absolute on-hand quantity from a stock take. Reserved units are protected.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> AdjustStock(
        Guid productId,
        AdjustStockRequest request,
        CancellationToken cancellationToken) =>
        (await dispatcher.SendAsync(new AdjustStockCommand(productId, request.OnHand), cancellationToken))
        .ToActionResult();
}

/// <param name="OnHand">Absolute counted quantity, not a delta.</param>
public sealed record AdjustStockRequest(int OnHand);

public static class CatalogRoutes
{
    public const string GetProduct = "Catalog_GetProduct";
}
