import { Route } from '@angular/router';
import { ForbiddenPage, SignInPage, authGuard } from '@upbazaar/auth';

export const appRoutes: Route[] = [
  { path: '', pathMatch: 'full', redirectTo: 'orders' },
  {
    path: 'orders',
    canActivate: [authGuard],
    loadChildren: () => import('./features/orders/orders.routes').then((m) => m.orderRoutes),
  },
  { path: 'sign-in', component: SignInPage },
  { path: 'forbidden', component: ForbiddenPage },
  { path: '**', redirectTo: 'orders' },
];
