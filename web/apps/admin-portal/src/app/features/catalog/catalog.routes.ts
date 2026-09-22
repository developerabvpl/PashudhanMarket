import { Route } from '@angular/router';
import { permissionGuard } from '@upbazaar/auth';
import { CatalogPermissions } from '../../core/permissions';

export const catalogRoutes: Route[] = [
  { path: '', pathMatch: 'full', redirectTo: 'review' },
  {
    path: 'review',
    canActivate: [permissionGuard(CatalogPermissions.ProductsWrite)],
    loadComponent: () => import('./listing-review.page').then((m) => m.ListingReviewPage),
  },
];
