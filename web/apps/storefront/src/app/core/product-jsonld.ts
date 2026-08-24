import { ProductDto } from '@upbazaar/data-access';

/**
 * schema.org Product markup, the shape Google needs to show price and availability in a rich
 * result. Availability is derived from real stock rather than hardcoded, because publishing
 * "InStock" for something that is not is the kind of thing that gets rich results revoked.
 */
export function productJsonLd(product: ProductDto, canonicalUrl: string): Record<string, unknown> {
  const available = (product.onHandQuantity ?? 0) - (product.reservedQuantity ?? 0) > 0;

  return {
    '@context': 'https://schema.org',
    '@type': 'Product',
    name: product.name,
    description: product.description ?? product.name,
    sku: product.sku,
    category: product.category?.name,
    url: canonicalUrl,
    offers: {
      '@type': 'Offer',
      url: canonicalUrl,
      priceCurrency: product.currency,
      price: product.price,
      availability: available
        ? 'https://schema.org/InStock'
        : 'https://schema.org/OutOfStock',
      itemCondition: 'https://schema.org/NewCondition',
    },
  };
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
