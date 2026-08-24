import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthTokenStore } from './auth-token-store';
import { AuthService } from './auth.service';

/** Paths that must never carry a token or trigger a refresh: they are how you get one. */
const ANONYMOUS_PATHS = [
  '/api/v1/auth/login',
  '/api/v1/auth/register',
  '/api/v1/auth/request-otp',
  '/api/v1/auth/verify-otp',
  '/api/v1/auth/verify-2fa',
  '/api/v1/auth/refresh',
  '/api/v1/auth/forgot-password',
  '/api/v1/auth/reset-password',
];

/**
 * Serialises refresh attempts.
 *
 * Without this, a screen firing three requests at once on an expired token would start three
 * refreshes; the first rotates the token and the other two present one that has just been
 * retired, which the API correctly reads as theft and answers by killing the session.
 */
@Injectable({ providedIn: 'root' })
export class RefreshCoordinator {
  private inFlight: Promise<boolean> | null = null;

  async refreshOnce(auth: AuthService): Promise<boolean> {
    this.inFlight ??= auth.refresh().finally(() => {
      this.inFlight = null;
    });

    return await this.inFlight;
  }
}

/**
 * Attaches the bearer token to same-origin API calls, and on a 401 refreshes once and retries.
 *
 * A 403 is deliberately left alone: the caller is who they say they are and simply lacks the
 * permission, so the screen shows the error rather than bouncing them to a sign-in form they
 * have already completed.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const tokens = inject(AuthTokenStore);
  const auth = inject(AuthService);
  const coordinator = inject(RefreshCoordinator);
  const router = inject(Router);

  if (!isApiRequest(request.url) || isAnonymous(request.url)) {
    return next(request);
  }

  return next(withToken(request, tokens.accessToken())).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }

      if (!tokens.canRefresh()) {
        auth.signOutLocally();

        return throwError(() => error);
      }

      return from(coordinator.refreshOnce(auth)).pipe(
        switchMap((refreshed) => {
          if (!refreshed) {
            void router.navigate(['/sign-in'], { queryParams: { returnUrl: router.url } });

            return throwError(() => error);
          }

          return next(withToken(request, tokens.accessToken()));
        })
      );
    })
  );
};

function withToken(request: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token === null
    ? request
    : request.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
}

function isAnonymous(url: string): boolean {
  return ANONYMOUS_PATHS.some((path) => url.includes(path));
}

/**
 * Only our own API gets the token. A relative URL is ours by definition; an absolute one is
 * checked so a third-party image or script never receives the credential.
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
