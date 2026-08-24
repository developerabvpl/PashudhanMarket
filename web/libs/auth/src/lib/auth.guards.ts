import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { AuthStore } from './auth-store';

/** Requires a signed-in user; sends anyone else to sign-in with a return address. */
export const authGuard: CanActivateFn = (_route, state): boolean | UrlTree => {
  const auth = inject(AuthStore);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/sign-in'], {
    queryParams: { returnUrl: state.url },
  });
};

/**
 * Requires one or more permissions. Signed out sends to sign-in; signed in but unauthorized
 * lands on /forbidden, because bouncing a legitimate user to a login form they have already
 * completed reads as a broken app.
 *
 * ```ts
 * { path: 'products/new', canActivate: [permissionGuard('catalog.products.write')], ... }
 * ```
 */
export function permissionGuard(...permissions: string[]): CanActivateFn {
  return (_route, state): boolean | UrlTree => {
    const auth = inject(AuthStore);
    const router = inject(Router);

    if (!auth.isAuthenticated()) {
      return router.createUrlTree(['/sign-in'], {
        queryParams: { returnUrl: state.url },
      });
    }

    if (auth.hasAll(permissions)) {
      return true;
    }

    return router.createUrlTree(['/forbidden'], {
      queryParams: { required: permissions.join(',') },
    });
  };
}
