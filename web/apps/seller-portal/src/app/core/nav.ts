import { PortalNavEntry } from '@upbazaar/auth';
import { SellerPermissions } from './seller-access';

/**
 * The seller toolbar's pages. Team members see only the areas their role opens; the owner sees
 * all of them. Orders and returns need no permission of their own: the toolbar shows these pages
 * only to someone who may sell, which already means they may read the shop's orders.
 *
 * The daily work - orders, returns, products, earnings - stays one click away; the rest share a
 * drop-down so the toolbar fits from 1024px up. Below that the shell folds it all into one menu.
 */
export const SELLER_NAV: readonly PortalNavEntry[] = [
  { kind: 'link', label: 'sellerPortal.ordersTitle', route: '/orders', exact: true },
  { kind: 'link', label: 'nav.returns', route: '/orders/returns' },
  { kind: 'link', label: 'sellerPortal.productsTitle', route: '/products', permission: SellerPermissions.Products },
  { kind: 'link', label: 'nav.earnings', route: '/earnings', permission: SellerPermissions.Earnings },
  {
    kind: 'group',
    label: 'nav.shop',
    links: [
      { kind: 'link', label: 'nav.reviews', route: '/reviews', permission: SellerPermissions.Reviews },
      { kind: 'link', label: 'nav.coupons', route: '/coupons', permission: SellerPermissions.Coupons },
      { kind: 'link', label: 'nav.team', route: '/team', permission: SellerPermissions.Manage },
      { kind: 'link', label: 'sellerPortal.settingsTitle', route: '/settings', permission: SellerPermissions.Manage },
    ],
  },
];
