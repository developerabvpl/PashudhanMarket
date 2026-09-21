import { Route } from '@angular/router';
import { permissionGuard } from '@upbazaar/auth';
import { PaymentsPermissions } from '../../core/permissions';

export const paymentRoutes: Route[] = [
  {
    path: '',
    pathMatch: 'full',
    canActivate: [permissionGuard(PaymentsPermissions.Read)],
    loadComponent: () => import('./payments.page').then((m) => m.PaymentsPage),
  },
];
