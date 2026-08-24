import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthStore } from './auth-store';

/**
 * Attaches the bearer token to same-origin API calls and signs the user out on a 401.
 *
 * A 403 is deliberately left alone: the user is who they say they are and simply lacks the
 * permission, so the screen shows the error rather than bouncing them to the login page.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthStore);
  const router = inject(Router);
  const token = auth.token();

  const authorized =
    token !== null && isApiRequest(request.url)
      ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
      : request;

  return next(authorized).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && auth.isAuthenticated()) {
        auth.signOut();
        void router.navigate(['/sign-in'], {
          queryParams: { returnUrl: router.url },
        });
      }

      return throwError(() => error);
    })
  );
};

/**
 * Only our own API gets the token. A relative URL is ours by definition; an absolute one has
 * to be checked so a third-party image or script never receives the credential.
 */
function isApiRequest(url: string): boolean {
  if (!/^https?:\/\//i.test(url)) {
    return url.startsWith('/api/') || url.startsWith('api/');
  }

  try {
    return new URL(url).pathname.startsWith('/api/');
  } catch {
    return false;
  }
}
