import { Route } from '@angular/router';
import { permissionGuard } from '@upbazaar/auth';
import { SellersPermissions } from '../../core/permissions';

export const sellerRoutes: Route[] = [
  {
    path: '',
    pathMatch: 'full',
    canActivate: [permissionGuard(SellersPermissions.Read)],
    loadComponent: () => import('./sellers.page').then((m) => m.SellersPage),
  },
];
