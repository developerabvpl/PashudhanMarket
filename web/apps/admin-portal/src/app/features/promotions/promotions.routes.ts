import { Route } from '@angular/router';
import { permissionGuard } from '@upbazaar/auth';
import { PromotionsPermissions } from '../../core/permissions';

export const promotionRoutes: Route[] = [
  { path: '', pathMatch: 'full', redirectTo: 'coupons' },
  {
    // Readable by anyone who may see coupons; the page offers changes only with CampaignsWrite.
    path: 'coupons',
    canActivate: [permissionGuard(PromotionsPermissions.CampaignsRead)],
    loadComponent: () => import('./coupons.page').then((m) => m.CouponsPage),
  },
];
