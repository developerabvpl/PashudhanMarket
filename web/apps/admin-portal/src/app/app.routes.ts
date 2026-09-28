import { Route } from '@angular/router';
import {
  ForbiddenPage,
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
    path: 'sellers',
    canActivate: [authGuard],
    loadChildren: () => import('./features/sellers/sellers.routes').then((m) => m.sellerRoutes),
  },
  {
    path: 'catalog',
    canActivate: [authGuard],
    loadChildren: () => import('./features/catalog/catalog.routes').then((m) => m.catalogRoutes),
  },
  {
    path: 'settlements',
    canActivate: [authGuard],
    loadChildren: () => import('./features/settlements/settlements.routes').then((m) => m.settlementRoutes),
  },
  {
    path: 'reviews',
    canActivate: [authGuard],
    loadChildren: () => import('./features/reviews/reviews.routes').then((m) => m.reviewRoutes),
  },
  {
    path: 'promotions',
    canActivate: [authGuard],
    loadChildren: () => import('./features/promotions/promotions.routes').then((m) => m.promotionRoutes),
  },
  {
    path: 'change-password',
    canActivate: [authGuard],
    loadComponent: () => import('@upbazaar/auth/portal').then((m) => m.ChangePasswordPage),
  },
  {
    path: 'sign-in',
    loadComponent: () => import('@upbazaar/auth/portal').then((m) => m.PortalSignInPage),
    data: { title: 'app.adminPortal', defaultReturnUrl: '/staff' },
  },
  { path: 'two-factor', loadComponent: () => import('@upbazaar/auth/portal').then((m) => m.TwoFactorChallengePage) },
  { path: 'forgot-password', loadComponent: () => import('@upbazaar/auth/portal').then((m) => m.ForgotPasswordPage) },
  { path: 'forbidden', component: ForbiddenPage },
  { path: '**', redirectTo: 'staff' },
];
