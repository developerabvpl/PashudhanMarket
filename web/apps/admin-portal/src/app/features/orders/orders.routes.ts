import { Route } from '@angular/router';
import { permissionGuard } from '@upbazaar/auth';
import { OrderingPermissions } from '../../core/permissions';

export const orderRoutes: Route[] = [
  {
    path: '',
    pathMatch: 'full',
    canActivate: [permissionGuard(OrderingPermissions.Read)],
    loadComponent: () => import('./order-lookup').then((m) => m.OrderLookup),
  },
];
