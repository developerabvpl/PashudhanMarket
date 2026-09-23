using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Catalog.Domain;

/// <summary>Every failure this module can return.</summary>
public static class CatalogErrors
{
    public static readonly Error NotADraft = Error.Conflict(
        "catalog.product.not_a_draft",
        "Only a draft can be edited or submitted for review. Ask a moderator to change a live listing.");

    public static readonly Error NotInReview = Error.Conflict(
        "catalog.product.not_in_review",
        "This listing is not waiting for review.");

    public static readonly Error ProductNotFound = Error.NotFound(
        "catalog.product.not_found",
        "The product does not exist.");

    public static readonly Error CategoryNotFound = Error.NotFound(
        "catalog.category.not_found",
        "The category does not exist.");

    public static readonly Error SkuTaken = Error.Conflict(
        "catalog.product.sku_taken",
        "Another product already uses this SKU.");

    public static readonly Error CategorySlugTaken = Error.Conflict(
        "catalog.category.slug_taken",
        "Another category already uses this name.");

    public static readonly Error CategoryCycle = Error.Conflict(
        "catalog.category.cycle",
        "A category cannot sit underneath itself.");

    public static readonly Error CategoryInUse = Error.Conflict(
        "catalog.category.in_use",
        "This category still has products or sub-categories. Move them elsewhere first.");

    public static readonly Error ProductArchived = Error.Conflict(
        "catalog.product.archived",
        "An archived product cannot be changed. Create a new listing instead.");
}
