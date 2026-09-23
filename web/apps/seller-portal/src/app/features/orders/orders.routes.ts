import { Route } from '@angular/router';

export const orderRoutes: Route[] = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./orders.page').then((m) => m.OrdersPage),
  },
  {
    path: 'returns',
    loadComponent: () => import('./returns.page').then((m) => m.ReturnsPage),
  },
  {
    path: ':orderId',
    loadComponent: () => import('./order.page').then((m) => m.OrderPage),
  },
];
