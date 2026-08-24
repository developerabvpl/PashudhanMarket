import { Route } from '@angular/router';
import { permissionGuard } from '@upbazaar/auth';

/** Permission names mirror the API's OrderingPermissions constants. */
export const ORDERING_ORDERS_READ = 'ordering.orders.read';

export const orderRoutes: Route[] = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./order-lookup').then((m) => m.OrderLookup),
    canActivate: [permissionGuard(ORDERING_ORDERS_READ)],
  },
];
