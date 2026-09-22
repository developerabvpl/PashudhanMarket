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
  { path: '', pathMatch: 'full', redirectTo: 'staff' },
  {
    path: 'staff',
    canActivate: [authGuard],
    loadChildren: () => import('./features/staff/staff.routes').then((m) => m.staffRoutes),
  },
  {
    path: 'orders',
    canActivate: [authGuard],
    loadChildren: () => import('./features/orders/orders.routes').then((m) => m.orderRoutes),
  },
  {
    path: 'payments',
    canActivate: [authGuard],
    loadChildren: () => import('./features/payments/payments.routes').then((m) => m.paymentRoutes),
  },
  {
    path: 'shipping',
    canActivate: [authGuard],
    loadChildren: () => import('./features/shipping/shipping.routes').then((m) => m.shippingRoutes),
  },
  {
    path: 'change-password',
    canActivate: [authGuard],
    component: ChangePasswordPage,
  },
  {
    path: 'sign-in',
    component: PortalSignInPage,
    data: { title: 'app.adminPortal', defaultReturnUrl: '/staff' },
  },
  { path: 'two-factor', component: TwoFactorChallengePage },
  { path: 'forgot-password', component: ForgotPasswordPage },
  { path: 'forbidden', component: ForbiddenPage },
  { path: '**', redirectTo: 'staff' },
];
