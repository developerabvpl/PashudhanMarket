import {
  EnvironmentInjector,
  EnvironmentProviders,
  inject,
  makeEnvironmentProviders,
  provideAppInitializer,
  runInInjectionContext,
} from '@angular/core';

/**
 * Material's paginator in the portal's language, for a portal's app config.
 *
 * The stock MatPaginatorIntl is hard-coded English - "Items per page", "1 – 2 of 2" - so a
 * Hindi page kept an English footer under every table. translatePaginator
 * (portal/paginator-labels.ts) gives the one shared instance Transloco's words.
 *
 * It is fetched rather than imported. The obvious `{ provide: MatPaginatorIntl, useClass: ... }`
 * needs @angular/material/paginator at start-up, and that drags the paginator's select, form
 * field and overlay into every visitor's first download - about 300 kB, past the portals'
 * budget - when only the list pages, themselves lazy, show a paginator. Those pages load the
 * same chunk, so by the time a paginator draws the labels are usually in; if not, the
 * paginator redraws when they arrive.
 *
 * This half is in the main @upbazaar/auth entry, not @upbazaar/auth/portal: importing anything
 * from the portal entry at start-up would bring its sign-in pages with it (another 225 kB). The
 * Material half sits under lib/portal with the portals' other Material pieces, and not in
 * @upbazaar/ui, which has no Material dependency and serves the storefront too.
 */
export function providePortalPaginatorIntl(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideAppInitializer(() => {
      const injector = inject(EnvironmentInjector);

      // Not awaited: the first screen should not wait on a chunk it may not need.
      void import('./portal/paginator-labels')
        .then(({ translatePaginator }) => runInInjectionContext(injector, translatePaginator))
        // Offline or a failed deploy: the paginator stays in English, which is still usable.
        .catch(() => undefined);
    }),
  ]);
}
