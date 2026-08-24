import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { AuthTokenStore } from './auth-token-store';
import { CurrentUserStore } from './current-user-store';

/**
 * Requires a signed-in user.
 *
 * Awaits the profile load rather than trusting the token alone, so a route that renders a
 * display name or checks a permission never paints before that data exists.
 */
export const authGuard: CanActivateFn = async (_route, state): Promise<boolean | UrlTree> => {
  const tokens = inject(AuthTokenStore);
  const currentUser = inject(CurrentUserStore);
  const router = inject(Router);

  if (!tokens.isAuthenticated()) {
    return signInRedirect(router, state.url);
  }

  const user = await currentUser.ensureLoaded();

  return user !== null ? true : signInRedirect(router, state.url);
};

/**
 * Requires one or more permissions.
 *
 * Signed out goes to sign-in; signed in but unauthorised goes to /forbidden. Sending the
 * second case back to a login form they have already completed is the classic way to make a
 * permissions problem look like a broken app.
 *
 * ```ts
 * { path: 'staff', canActivate: [permissionGuard('identity.users.read')], ... }
 * ```
 */
export function permissionGuard(...permissions: string[]): CanActivateFn {
  return async (_route, state): Promise<boolean | UrlTree> => {
    const tokens = inject(AuthTokenStore);
    const currentUser = inject(CurrentUserStore);
    const router = inject(Router);

    if (!tokens.isAuthenticated()) {
      return signInRedirect(router, state.url);
    }

    const user = await currentUser.ensureLoaded();

    if (user === null) {
      return signInRedirect(router, state.url);
    }

    return currentUser.hasAll(permissions)
      ? true
      : router.createUrlTree(['/forbidden'], {
          queryParams: { required: permissions.join(',') },
        });
  };
}

/** Keeps anyone already signed in off the sign-in screen. */
export const anonymousOnlyGuard: CanActivateFn = (): boolean | UrlTree => {
  const tokens = inject(AuthTokenStore);
  const router = inject(Router);

  return tokens.isAuthenticated() ? router.createUrlTree(['/']) : true;
};

function signInRedirect(router: Router, returnUrl: string): UrlTree {
  return router.createUrlTree(['/sign-in'], { queryParams: { returnUrl } });
}
