import { Route } from '@angular/router';
import { permissionGuard } from '@upbazaar/auth';
import { CatalogPermissions } from '../../core/permissions';

export const catalogRoutes: Route[] = [
  { path: '', pathMatch: 'full', redirectTo: 'products' },
  {
    path: 'products',
    canActivate: [permissionGuard(CatalogPermissions.ProductsRead)],
    loadComponent: () => import('./products.page').then((m) => m.ProductsPage),
  },
  {
    // Creating needs write; viewing an existing one needs only read, and the page hides what
    // the caller cannot change.
    path: 'products/new',
    canActivate: [permissionGuard(CatalogPermissions.ProductsWrite)],
    loadComponent: () => import('./product-edit.page').then((m) => m.ProductEditPage),
    data: { productId: 'new' },
  },
  {
    path: 'products/:productId',
    canActivate: [permissionGuard(CatalogPermissions.ProductsRead)],
    loadComponent: () => import('./product-edit.page').then((m) => m.ProductEditPage),
  },
  {
    path: 'categories',
    canActivate: [permissionGuard(CatalogPermissions.CategoriesWrite)],
    loadComponent: () => import('./categories.page').then((m) => m.CategoriesPage),
  },
  {
    path: 'review',
    canActivate: [permissionGuard(CatalogPermissions.ProductsWrite)],
    loadComponent: () => import('./listing-review.page').then((m) => m.ListingReviewPage),
  },
];
