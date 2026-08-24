import { Route } from '@angular/router';
import { productDetailResolver, productListResolver } from './products.resolvers';

export const productRoutes: Route[] = [
  {
    path: '',
    loadComponent: () => import('./product-list').then((m) => m.ProductList),
    // The listing is filtered through the query string, so the resolver has to re-run when
    // only the query string changes.
    runGuardsAndResolvers: 'paramsOrQueryParamsChange',
    resolve: { page: productListResolver },
  },
  {
    path: ':productId',
    loadComponent: () => import('./product-detail').then((m) => m.ProductDetail),
    resolve: { product: productDetailResolver },
  },
];
