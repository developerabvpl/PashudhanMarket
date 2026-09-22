import { Route } from '@angular/router';

export const productRoutes: Route[] = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./products.page').then((m) => m.ProductsPage),
  },
  {
    path: ':productId',
    loadComponent: () => import('./product-edit.page').then((m) => m.ProductEditPage),
  },
];
