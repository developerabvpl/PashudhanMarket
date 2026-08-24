import { Route } from '@angular/router';

export const accountRoutes: Route[] = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./account.page').then((m) => m.AccountPage),
  },
];
