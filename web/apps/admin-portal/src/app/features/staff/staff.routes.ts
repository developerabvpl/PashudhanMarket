import { Route } from '@angular/router';
import { permissionGuard } from '@upbazaar/auth';
import { IdentityPermissions } from '../../core/permissions';

export const staffRoutes: Route[] = [
  {
    path: '',
    pathMatch: 'full',
    // Reading the directory needs users.read; the write actions inside are additionally
    // hidden by *hasPermission and rejected by the API.
    canActivate: [permissionGuard(IdentityPermissions.UsersRead)],
    loadComponent: () => import('./staff-list.page').then((m) => m.StaffListPage),
  },
];
