import { Route } from '@angular/router';
import { ForbiddenPage, authGuard } from '@upbazaar/auth';

export const appRoutes: Route[] = [
  { path: '', pathMatch: 'full', redirectTo: 'products' },
  {
    path: 'products',
    loadChildren: () => import('./features/products/products.routes').then((m) => m.productRoutes),
  },
  {
    // Everything under /account needs a signed-in buyer; the guard also awaits the profile,
    // so these pages never render before the data they show exists.
    path: 'account',
    canActivate: [authGuard],
    loadChildren: () => import('./features/account/account.routes').then((m) => m.accountRoutes),
  },
  {
    path: 'cart',
    loadComponent: () => import('./features/cart/cart.page').then((m) => m.CartPage),
  },
  {
    path: 'sign-in',
    loadComponent: () => import('./features/auth/sign-in.page').then((m) => m.SignInPage),
  },
  { path: 'forbidden', component: ForbiddenPage },
  { path: '**', redirectTo: 'products' },
];
