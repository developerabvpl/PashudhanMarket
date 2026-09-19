using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPBazaar.Infrastructure.Api;
using UPBazaar.Modules.Catalog.Application.Categories;
using UPBazaar.Modules.Catalog.Application.Products;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Catalog.Contracts.Permissions;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Catalog.Api;

/// <summary>
/// The catalogue as a shopper sees it. Anonymous, because browsing before signing in is the
/// whole point of a shop window.
///
/// A caller holding <see cref="CatalogPermissions.ProductsRead"/> sees drafts and archived
/// listings through the same endpoints; everyone else sees only what is published.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/catalog")]
[Produces("application/json")]
[AllowAnonymous]
public sealed class CatalogController(IDispatcher dispatcher, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Lists products.</summary>
    [HttpGet("products")]
    [EndpointSummary("List products")]
    [EndpointDescription(
        "Returns a page of products, filtered by search term, category and seller. Searching "
        + "matches name, SKU, brand and category name. Status filtering applies only to callers "
        + "who may see unpublished listings.")]
    [ProducesResponseType<PagedList<ProductSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedList<ProductSummaryDto>>> ListProducts(
        [FromQuery] ListProductsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = new ListProductsQuery(
            request.Page ?? 1,
            request.PageSize ?? 24,
            request.Search,
            request.CategoryId,
            request.Status,
            request.SellerId,
            IncludeUnpublished: CanSeeUnpublished());

        return (await dispatcher.QueryAsync(query, cancellationToken)).ToActionResult();
    }

    /// <summary>Returns one product.</summary>
    [HttpGet("products/{productId:guid}", Name = CatalogRoutes.GetProduct)]
    [EndpointSummary("Get a product")]
    [EndpointDescription("Returns one product with its category. Unpublished products answer 404 to shoppers.")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> GetProduct(Guid productId, CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(new GetProductQuery(productId, CanSeeUnpublished()), cancellationToken))
        .ToActionResult();

    /// <summary>Lists categories.</summary>
    [HttpGet("categories")]
    [EndpointSummary("List categories")]
    [EndpointDescription("Returns every category, sorted by name.")]
    [ProducesResponseType<IReadOnlyList<CategoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> ListCategories(CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(new ListCategoriesQuery(), cancellationToken)).ToActionResult();

    private bool CanSeeUnpublished() =>
        currentUser.IsAuthenticated && currentUser.HasPermission(CatalogPermissions.ProductsRead);
}

/// <param name="Page">1-based page number. Defaults to 1.</param>
/// <param name="PageSize">Items per page, 1 to 100. Defaults to 24.</param>
/// <param name="Search">Matches name, SKU, brand and category name.</param>
/// <param name="CategoryId">A category; a top-level one includes its children.</param>
/// <param name="Status">Draft, Active or Archived. Ignored for shoppers.</param>
/// <param name="SellerId">Only this seller's listings.</param>
public sealed record ListProductsRequest(
    int? Page,
    int? PageSize,
    string? Search,
    Guid? CategoryId,
    string? Status,
    Guid? SellerId);

/// <summary>Changes to the catalogue. Staff only until seller self-service exists.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/catalog")]
[Produces("application/json")]
public sealed class AdminCatalogController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Creates a draft product.</summary>
    [HttpPost("products")]
    [Authorize(CatalogPermissions.ProductsWrite)]
    [EndpointSummary("Create a product")]
    [EndpointDescription("Creates a listing in Draft. It stays invisible to shoppers until published.")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductDto>> CreateProduct(
        CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new CreateProductCommand(
            request.Sku,
            request.Name,
            request.Brand,
            request.Description,
            request.Price,
            request.SellerId,
            request.CategoryId,
            request.OnHandQuantity ?? 0);

        return (await dispatcher.SendAsync(command, cancellationToken))
            .ToCreatedResult(CatalogRoutes.GetProduct, product => new { productId = product.Id });
    }

    /// <summary>Updates a product.</summary>
    [HttpPut("products/{productId:guid}")]
    [Authorize(CatalogPermissions.ProductsWrite)]
    [EndpointSummary("Update a product")]
    [EndpointDescription(
        "Changes name, brand, description, price and category. Changing the price of a published "
        + "listing raises ProductPriceChanged.")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductDto>> UpdateProduct(
        Guid productId,
        UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new UpdateProductCommand(
            productId,
            request.Name,
            request.Brand,
            request.Description,
            request.Price,
            request.CategoryId);

        return (await dispatcher.SendAsync(command, cancellationToken)).ToActionResult();
    }

    /// <summary>Publishes a draft.</summary>
    [HttpPost("products/{productId:guid}/publish")]
    [Authorize(CatalogPermissions.ProductsWrite)]
    [EndpointSummary("Publish a product")]
    [EndpointDescription("Makes a draft visible to shoppers. Publishing an active product changes nothing.")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductDto>> PublishProduct(Guid productId, CancellationToken cancellationToken) =>
        (await dispatcher.SendAsync(new PublishProductCommand(productId), cancellationToken)).ToActionResult();

    /// <summary>Archives a product.</summary>
    [HttpDelete("products/{productId:guid}")]
    [Authorize(CatalogPermissions.ProductsWrite)]
    [EndpointSummary("Archive a product")]
    [EndpointDescription(
        "Withdraws the listing for good. Products are never deleted, because orders and reviews "
        + "reference them.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> ArchiveProduct(Guid productId, CancellationToken cancellationToken) =>
        (await dispatcher.SendAsync(new ArchiveProductCommand(productId), cancellationToken)).ToActionResult();

    /// <summary>Sets stock on hand.</summary>
    [HttpPut("products/{productId:guid}/stock")]
    [Authorize(CatalogPermissions.ProductsWrite)]
    [EndpointSummary("Set stock")]
    [EndpointDescription("Sets the physical count on hand. It may not fall below what is reserved.")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductDto>> SetStock(
        Guid productId,
        SetStockRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.SendAsync(
                new SetProductStockCommand(productId, request.OnHandQuantity), cancellationToken))
            .ToActionResult();
    }

    /// <summary>Creates a category.</summary>
    [HttpPost("categories")]
    [Authorize(CatalogPermissions.CategoriesWrite)]
    [EndpointSummary("Create a category")]
    [EndpointDescription("Creates a category, optionally under a parent. The slug is derived from the name.")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryDto>> CreateCategory(
        CategoryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.SendAsync(
                new CreateCategoryCommand(request.Name, request.ParentId), cancellationToken))
            .ToActionResult();
    }

    /// <summary>Renames or moves a category.</summary>
    [HttpPut("categories/{categoryId:guid}")]
    [Authorize(CatalogPermissions.CategoriesWrite)]
    [EndpointSummary("Update a category")]
    [EndpointDescription("Renames a category or moves it under another. A category cannot be moved beneath itself.")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryDto>> UpdateCategory(
        Guid categoryId,
        CategoryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.SendAsync(
                new UpdateCategoryCommand(categoryId, request.Name, request.ParentId), cancellationToken))
            .ToActionResult();
    }
}

/// <param name="Sku">Unique stock-keeping unit. Letters, digits and hyphens; stored upper-case.</param>
/// <param name="Name">Listing title.</param>
/// <param name="Brand">Brand, if any.</param>
/// <param name="Description">Long description, if any.</param>
/// <param name="Price">Price in rupees, at most two decimal places.</param>
/// <param name="SellerId">Public id of the selling account.</param>
/// <param name="CategoryId">Category to list under.</param>
/// <param name="OnHandQuantity">Opening stock. Defaults to 0.</param>
public sealed record CreateProductRequest(
    string Sku,
    string Name,
    string? Brand,
    string? Description,
    decimal Price,
    Guid SellerId,
    Guid CategoryId,
    int? OnHandQuantity);

/// <param name="Name">Listing title.</param>
/// <param name="Brand">Brand, if any.</param>
/// <param name="Description">Long description, if any.</param>
/// <param name="Price">Price in rupees, at most two decimal places.</param>
/// <param name="CategoryId">Category to list under.</param>
public sealed record UpdateProductRequest(
    string Name,
    string? Brand,
    string? Description,
    decimal Price,
    Guid CategoryId);

/// <param name="OnHandQuantity">Physical count on hand.</param>
public sealed record SetStockRequest(int OnHandQuantity);

/// <param name="Name">Display name. The slug is derived from it.</param>
/// <param name="ParentId">Parent category, or null for the top level.</param>
public sealed record CategoryRequest(string Name, Guid? ParentId);

/// <summary>Named routes, so a created resource can point at its own address.</summary>
public static class CatalogRoutes
{
    public const string GetProduct = "Catalog_GetProduct";
}
