/**
 * Permission names this app depends on.
 *
 * Mirrors IdentityPermissions in the API's Contracts project. The names travel as plain
 * strings in the token, so this is a small, deliberate duplication rather than a shared
 * package; keeping it in one file means a rename is one search away.
 */
export const IdentityPermissions = {
  UsersRead: 'identity.users.read',
  UsersManage: 'identity.users.manage',
  RolesRead: 'identity.roles.read',
  RolesWrite: 'identity.roles.write',
} as const;

/** Permission names the ordering screens depend on. */
export const OrderingPermissions = {
  Read: 'orders.read',
  Write: 'orders.write',
} as const;

/** Permission names the payments screens depend on. */
export const PaymentsPermissions = {
  Read: 'payments.read',
  RefundsWrite: 'payments.refunds.write',
} as const;

/** Permission names the shipping screens depend on. */
export const ShippingPermissions = {
  ShipmentsRead: 'shipping.shipments.read',
  ShipmentsWrite: 'shipping.shipments.write',
} as const;

/** Permission names the seller review screens depend on. */
export const SellersPermissions = {
  Read: 'sellers.read',
  KycApprove: 'sellers.kyc.approve',
} as const;

/** Permission names the catalogue screens depend on. */
export const CatalogPermissions = {
  ProductsRead: 'catalog.products.read',
  ProductsWrite: 'catalog.products.write',
  CategoriesWrite: 'catalog.categories.write',
} as const;

/** Permission names the stock panel on the product screen depends on. */
export const InventoryPermissions = {
  StockRead: 'inventory.stock.read',
  StockWrite: 'inventory.stock.write',
} as const;
