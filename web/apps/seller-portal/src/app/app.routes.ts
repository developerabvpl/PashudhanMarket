import { Route } from '@angular/router';
import {
  ChangePasswordPage,
  ForbiddenPage,
  ForgotPasswordPage,
  PortalSignInPage,
  TwoFactorChallengePage,
  authGuard,
} from '@upbazaar/auth';

export const appRoutes: Route[] = [
  { path: '', pathMatch: 'full', redirectTo: 'products' },
  {
    path: 'products',
    canActivate: [authGuard],
    loadChildren: () => import('./features/products/products.routes').then((m) => m.productRoutes),
  },
  {
    path: 'change-password',
    canActivate: [authGuard],
    component: ChangePasswordPage,
  },
  {
    path: 'sign-in',
    component: PortalSignInPage,
    // The page names itself, so one shared component serves both portals.
    data: { title: 'app.sellerPortal', defaultReturnUrl: '/products' },
  },
  { path: 'two-factor', component: TwoFactorChallengePage },
  { path: 'forgot-password', component: ForgotPasswordPage },
  { path: 'forbidden', component: ForbiddenPage },
  { path: '**', redirectTo: 'products' },
];
