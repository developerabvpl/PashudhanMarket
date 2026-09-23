import { Route } from '@angular/router';
import {
  ChangePasswordPage,
  ForbiddenPage,
  ForgotPasswordPage,
  PortalSignInPage,
  TwoFactorChallengePage,
  anonymousOnlyGuard,
  authGuard,
} from '@upbazaar/auth';
import { approvedSellerGuard } from './core/seller-access';

export const appRoutes: Route[] = [
  { path: '', pathMatch: 'full', redirectTo: 'orders' },
  {
    // Anyone signed in may apply; the page itself shows where their application stands.
    path: 'apply',
    canActivate: [authGuard],
    loadComponent: () => import('./features/apply/apply.page').then((m) => m.ApplyPage),
  },
  {
    path: 'orders',
    canActivate: [authGuard, approvedSellerGuard],
    loadChildren: () => import('./features/orders/orders.routes').then((m) => m.orderRoutes),
  },
  {
    path: 'products',
    canActivate: [authGuard, approvedSellerGuard],
    loadChildren: () => import('./features/products/products.routes').then((m) => m.productRoutes),
  },
  {
    path: 'earnings',
    canActivate: [authGuard, approvedSellerGuard],
    loadComponent: () => import('./features/earnings/earnings.page').then((m) => m.EarningsPage),
  },
  {
    path: 'settings',
    canActivate: [authGuard, approvedSellerGuard],
    loadComponent: () => import('./features/settings/settings.page').then((m) => m.SettingsPage),
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
    data: { title: 'app.sellerPortal', defaultReturnUrl: '/orders', registerUrl: '/register' },
  },
  {
    path: 'register',
    canActivate: [anonymousOnlyGuard],
    loadComponent: () => import('./features/account/register.page').then((m) => m.RegisterPage),
  },
  { path: 'two-factor', component: TwoFactorChallengePage },
  { path: 'forgot-password', component: ForgotPasswordPage },
  { path: 'forbidden', component: ForbiddenPage },
  { path: '**', redirectTo: 'orders' },
];
