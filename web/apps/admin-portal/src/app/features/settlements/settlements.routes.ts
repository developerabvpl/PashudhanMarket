import { Route } from '@angular/router';
import { permissionGuard } from '@upbazaar/auth';
import { SettlementsPermissions } from '../../core/permissions';

export const settlementRoutes: Route[] = [
  { path: '', pathMatch: 'full', redirectTo: 'payouts' },
  {
    path: 'payouts',
    canActivate: [permissionGuard(SettlementsPermissions.Read)],
    loadComponent: () => import('./payouts.page').then((m) => m.PayoutsPage),
  },
  {
    // Readable by anyone who sees payouts; the page offers changes only with PolicyWrite.
    path: 'rates',
    canActivate: [permissionGuard(SettlementsPermissions.Read)],
    loadComponent: () => import('./rates.page').then((m) => m.RatesPage),
  },
];
