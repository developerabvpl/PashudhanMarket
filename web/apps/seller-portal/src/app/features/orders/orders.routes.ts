import { Route } from '@angular/router';

export const orderRoutes: Route[] = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./orders.page').then((m) => m.OrdersPage),
  },
  {
    path: ':orderId',
    loadComponent: () => import('./order.page').then((m) => m.OrderPage),
  },
];
