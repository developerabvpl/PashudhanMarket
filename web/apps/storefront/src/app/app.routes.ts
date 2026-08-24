import { Route } from '@angular/router';
import { ForbiddenPage, SignInPage } from '@upbazaar/auth';

export const appRoutes: Route[] = [
  { path: '', pathMatch: 'full', redirectTo: 'products' },
  {
    path: 'products',
    loadChildren: () => import('./features/products/products.routes').then((m) => m.productRoutes),
  },
  { path: 'sign-in', component: SignInPage },
  { path: 'forbidden', component: ForbiddenPage },
  { path: '**', redirectTo: 'products' },
];
