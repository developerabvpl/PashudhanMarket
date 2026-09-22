import { Route } from '@angular/router';
import { permissionGuard } from '@upbazaar/auth';
import { ShippingPermissions } from '../../core/permissions';

export const shippingRoutes: Route[] = [
  { path: '', pathMatch: 'full', redirectTo: 'pickup-locations' },
  {
    path: 'pickup-locations',
    canActivate: [permissionGuard(ShippingPermissions.ShipmentsRead)],
    loadComponent: () => import('./pickup-locations.page').then((m) => m.PickupLocationsPage),
  },
];
