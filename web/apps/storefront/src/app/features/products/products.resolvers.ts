import { inject } from '@angular/core';
import { ResolveFn, Router } from '@angular/router';
import {
  Api,
  CategoryDto,
  PagedListOfProductSummaryDto,
  ProductDto,
  apiCatalogProductsGet,
  catalogGetProduct,
  catalogListCategories,
} from '@upbazaar/data-access';

export const DEFAULT_PAGE_SIZE = 24;

/**
 * Loads the catalogue page before the route activates.
 *
 * A resolver rather than an in-component signal because the router awaits it during SSR: the
 * products end up in the HTML the crawler receives, instead of appearing after hydration.
 */
export const productListResolver: ResolveFn<PagedListOfProductSummaryDto> = (route) => {
  const api = inject(Api);
  const query = route.queryParamMap;

  return api.invoke(apiCatalogProductsGet, {
    Page: Number(query.get('page') ?? 1),
    PageSize: DEFAULT_PAGE_SIZE,
    Search: query.get('q') ?? undefined,
    CategoryId: query.get('category') ?? undefined,
    ActiveOnly: true,
  });
};

/**
 * Loads one product. A missing id is a 404 from the API, which becomes a redirect to the
 * listing rather than an empty detail page.
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
 * The category rail. Resolved rather than fetched in the component so the filter links are in
 * the server-rendered HTML, where a crawler can follow them.
 *
 * A failure here costs the rail, not the page: the catalogue is still perfectly usable without
 * it, so this resolves to an empty list instead of taking the route down.
 */
export const categoriesResolver: ResolveFn<CategoryDto[]> = async () => {
  const api = inject(Api);

  try {
    return await api.invoke(catalogListCategories, {});
  } catch {
    return [];
  }
};
