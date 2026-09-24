import { Route } from '@angular/router';
import { permissionGuard } from '@upbazaar/auth';
import { ReviewsPermissions } from '../../core/permissions';

export const reviewRoutes: Route[] = [
  {
    // Readable by anyone who may see reviews; the page offers decisions only with Moderate.
    path: '',
    canActivate: [permissionGuard(ReviewsPermissions.Read)],
    loadComponent: () => import('./reviews.page').then((m) => m.ReviewsPage),
  },
];
