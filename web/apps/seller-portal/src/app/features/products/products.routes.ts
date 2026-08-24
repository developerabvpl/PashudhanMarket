import { Route } from '@angular/router';
import { permissionGuard } from '@upbazaar/auth';

/** Permission names mirror the API's CatalogPermissions constants. */
export const CATALOG_PRODUCTS_WRITE = 'catalog.products.write';

export const productRoutes: Route[] = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./product-create').then((m) => m.ProductCreate),
    canActivate: [permissionGuard(CATALOG_PRODUCTS_WRITE)],
  },
];
