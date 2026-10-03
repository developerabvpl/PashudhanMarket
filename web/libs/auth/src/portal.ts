// Material-based pages for the seller and admin portals, apart from the main entry point so the
// portals can load them lazily: every visitor downloads the portal shell, but only someone signing
// in needs Angular forms and Material form fields. The storefront never imports these.
export * from './lib/portal/portal-sign-in.page';
export * from './lib/portal/two-factor-challenge.page';
export * from './lib/portal/password-pages';
export * from './lib/portal/account-menu';
export * from './lib/portal/nav-menus';
export * from './lib/portal/portal-forbidden.page';
