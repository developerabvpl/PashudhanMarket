import { ResolveFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import {
  CategoryDto,
  PagedListOfProductSummaryDto,
  ProductDto,
} from '@upbazaar/data-access';
import { getProduct, listCategories, listProducts } from './catalog.source';

export const DEFAULT_PAGE_SIZE = 24;

/**
 * Loads the catalogue page before the route activates.
 *
 * A resolver rather than an in-component signal because the router awaits it while prerendering:
 * the products end up in the HTML the crawler receives, instead of appearing after hydration.
 *
 * It reads the bundled catalogue rather than calling the API. See catalog.source.ts for why, and
 * for what changes the day the Catalog module ships.
 */
export const productListResolver: ResolveFn<PagedListOfProductSummaryDto> = (route) => {
  const query = route.queryParamMap;

  return listProducts({
    page: Number(query.get('page') ?? 1),
    pageSize: DEFAULT_PAGE_SIZE,
    search: query.get('q') ?? undefined,
    categoryId: query.get('category') ?? undefined,
    activeOnly: true,
  });
};

/**
 * Loads one product. An id that names nothing becomes a redirect to the listing rather than an
 * empty detail page.
 */
export const productDetailResolver: ResolveFn<ProductDto | null> = (route) => {
  const router = inject(Router);
  const productId = route.paramMap.get('productId');

  if (productId === null) {
    void router.navigate(['/products']);
    return null;
  }

  const product = getProduct(productId);

  if (product === null) {
    void router.navigate(['/products']);
    return null;
  }

  return product;
};

/**
 * The category rail. Resolved rather than fetched in the component so the filter links are in the
 * prerendered HTML, where a crawler can follow them.
 */
export const categoriesResolver: ResolveFn<CategoryDto[]> = () => listCategories();
