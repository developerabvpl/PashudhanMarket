import { PortalNavEntry } from '@upbazaar/auth';
import {
  CatalogPermissions,
  IdentityPermissions,
  OrderingPermissions,
  PaymentsPermissions,
  PromotionsPermissions,
  ReviewsPermissions,
  SellersPermissions,
  SettlementsPermissions,
  ShippingPermissions,
} from './permissions';

/**
 * The admin toolbar's pages, each with the permission its route guard asks for.
 *
 * Thirteen pages side by side do not fit a laptop screen, so related ones share a drop-down: six
 * entries fit from 1024px up, and below that the shell folds them all into one menu.
 */
export const ADMIN_NAV: readonly PortalNavEntry[] = [
  { kind: 'link', label: 'nav.staffUsers', route: '/staff', permission: IdentityPermissions.UsersRead },
  {
    kind: 'group',
    label: 'nav.orders',
    links: [
      { kind: 'link', label: 'nav.orders', route: '/orders', permission: OrderingPermissions.Read, exact: true },
      { kind: 'link', label: 'nav.returns', route: '/orders/returns', permission: OrderingPermissions.Read },
    ],
  },
  {
    kind: 'group',
    label: 'nav.money',
    links: [
      { kind: 'link', label: 'nav.payments', route: '/payments', permission: PaymentsPermissions.Read },
      { kind: 'link', label: 'nav.payouts', route: '/settlements/payouts', permission: SettlementsPermissions.Read },
      { kind: 'link', label: 'nav.settlementRates', route: '/settlements/rates', permission: SettlementsPermissions.Read },
    ],
  },
  {
    kind: 'group',
    label: 'nav.shipping',
    links: [
      // Named for the page it opens. It read "Shipments", but there is no list of shipments: each
      // order's are on the order, and /shipping is where couriers collect from.
      { kind: 'link', label: 'nav.pickupLocations', route: '/shipping', permission: ShippingPermissions.ShipmentsRead, exact: true },
      { kind: 'link', label: 'nav.cod', route: '/shipping/cod', permission: ShippingPermissions.CodRead },
    ],
  },
  { kind: 'link', label: 'nav.sellers', route: '/sellers', permission: SellersPermissions.Read },
  {
    kind: 'group',
    label: 'nav.catalogue',
    links: [
      { kind: 'link', label: 'nav.products', route: '/catalog/products', permission: CatalogPermissions.ProductsRead },
      { kind: 'link', label: 'nav.categories', route: '/catalog/categories', permission: CatalogPermissions.CategoriesWrite },
      { kind: 'link', label: 'nav.listingReview', route: '/catalog/review', permission: CatalogPermissions.ProductsWrite },
      { kind: 'link', label: 'nav.buyerReviews', route: '/reviews', permission: ReviewsPermissions.Read },
      { kind: 'link', label: 'nav.coupons', route: '/promotions/coupons', permission: PromotionsPermissions.CampaignsRead },
    ],
  },
];
