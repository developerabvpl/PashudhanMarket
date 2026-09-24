import { ProductDto, RatingSummaryDto } from '@upbazaar/data-access';

/**
 * schema.org Product markup, the shape Google needs to show price and availability in a rich
 * result. Availability is derived from real stock rather than hardcoded, because publishing
 * "InStock" for something that is not is the kind of thing that gets rich results revoked.
 *
 * The rating is added in the browser, once the reviews have loaded: the page is prerendered, and
 * a rating baked in at build time would go stale. Google reads markup added by script.
 *
 * A product with no price gets no Offer node at all. Emitting `price: 0` would advertise it as
 * free in the search result, and there is no way to say "ask us" in an Offer — the absence of
 * an offer is exactly how schema.org expresses a listing that cannot be bought yet.
 */
export function productJsonLd(
  product: ProductDto,
  canonicalUrl: string,
  imageUrl?: string | null,
  rating?: RatingSummaryDto | null
): Record<string, unknown> {
  const available = (product.onHandQuantity ?? 0) - (product.reservedQuantity ?? 0) > 0;

  const node: Record<string, unknown> = {
    '@context': 'https://schema.org',
    '@type': 'Product',
    name: product.name,
    description: product.description ?? product.name,
    sku: product.sku,
    category: product.category?.name,
    url: canonicalUrl,
  };

  if (product.brand) {
    node['brand'] = { '@type': 'Brand', name: product.brand };
  }

  // Absolute, because a crawler resolves this against nothing. Omitted rather than pointed at a
  // placeholder: a drawn tile in an image result would help nobody.
  if (imageUrl) {
    node['image'] = imageUrl;
  }

  // Only from real ratings: a product nobody has rated gets no aggregateRating at all, since a
  // zero-star average would read as a terrible product rather than an unrated one. ratingCount,
  // not reviewCount, because most buyers leave stars without words.
  if (rating && rating.count > 0) {
    node['aggregateRating'] = {
      '@type': 'AggregateRating',
      ratingValue: rating.average,
      ratingCount: rating.count,
      bestRating: 5,
      worstRating: 1,
    };
  }

  if (product.price > 0) {
    node['offers'] = {
      '@type': 'Offer',
      url: canonicalUrl,
      priceCurrency: product.currency,
      price: product.price,
      availability: available
        ? 'https://schema.org/InStock'
        : 'https://schema.org/OutOfStock',
      itemCondition: 'https://schema.org/NewCondition',
    };
  }

  return node;
}

/** Breadcrumb markup so search results show Products > <name> instead of a bare URL. */
export function breadcrumbJsonLd(
  product: ProductDto,
  origin: string,
  canonicalUrl: string
): Record<string, unknown> {
  return {
    '@context': 'https://schema.org',
    '@type': 'BreadcrumbList',
    itemListElement: [
      { '@type': 'ListItem', position: 1, name: 'Products', item: `${origin}/products` },
      { '@type': 'ListItem', position: 2, name: product.name, item: canonicalUrl },
    ],
  };
}
