import { ResolveFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import {
  Api,
  CategoryDto,
  PagedListOfProductSummaryDto,
  ProductDto,
  apiV1CatalogCategoriesGet,
  apiV1CatalogProductsGet,
  catalogGetProduct,
} from '@upbazaar/data-access';

export const DEFAULT_PAGE_SIZE = 24;

/**
 * Loads the catalogue page before the route activates.
 *
 * A resolver rather than an in-component signal because the router awaits it while prerendering:
 * the products end up in the HTML the crawler receives, instead of appearing after hydration.
 * It runs again in the browser on load, and because catalogue responses are kept out of the
 * hydration transfer cache (app.config.ts), that second run fetches live prices and stock rather
 * than replaying the ones captured at build time.
 */
export const productListResolver: ResolveFn<PagedListOfProductSummaryDto> = (route) => {
  const api = inject(Api);
  const query = route.queryParamMap;

  return api.invoke(apiV1CatalogProductsGet, {
    Page: pageNumber(query.get('page')),
    PageSize: DEFAULT_PAGE_SIZE,
    Search: query.get('q') ?? undefined,
    CategoryId: query.get('category') ?? undefined,
  });
};

/**
 * Loads one product. A missing or unpublished id is a 404 from the API, which becomes a redirect
 * to the listing rather than an empty detail page. That also covers a product archived since the
 * last build: its prerendered page still exists, but the live fetch sends the shopper onwards.
 */
export const productDetailResolver: ResolveFn<ProductDto | null> = async (route) => {
  const api = inject(Api);
  const router = inject(Router);
  const productId = route.paramMap.get('productId');

  if (productId === null) {
    void router.navigate(['/products']);
    return null;
  }

  try {
    return await api.invoke(catalogGetProduct, { productId });
  } catch {
    void router.navigate(['/products']);
    return null;
  }
};

/**
 * The category rail. Resolved rather than fetched in the component so the filter links are in the
 * prerendered HTML, where a crawler can follow them.
 *
 * A failure here costs the rail, not the page: the catalogue is still perfectly usable without
 * it, so this resolves to an empty list instead of taking the route down.
 */
export const categoriesResolver: ResolveFn<CategoryDto[]> = async () => {
  const api = inject(Api);

  try {
    return await api.invoke(apiV1CatalogCategoriesGet, {});
  } catch {
    return [];
  }
};

/** A hand-edited ?page=abc or ?page=-3 means the first page, not a 400 from the API. */
function pageNumber(raw: string | null): number {
  const page = Number(raw ?? 1);

  return Number.isInteger(page) && page > 0 ? page : 1;
}
