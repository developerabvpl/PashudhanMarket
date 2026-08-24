import { EnvironmentProviders, Provider, makeEnvironmentProviders } from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { authInterceptor } from '@upbazaar/auth';
import { provideApiConfiguration } from './api/api-configuration';
import { httpErrorInterceptor } from './http-error.interceptor';

export interface DataAccessOptions {
  /**
   * Where the API lives. Leave empty when a dev-server proxy or reverse proxy puts the API on
   * the same origin, which is what keeps the bearer token same-origin.
   *
   * Any trailing slash is stripped: the generated client concatenates this with paths that
   * already begin with "/", and "/" + "/api/..." would produce the protocol-relative
   * "//api/..." — a request to a host literally called "api".
   */
  readonly rootUrl?: string;
}

/**
 * The one place HttpClient is configured. Components never inject HttpClient directly; they
 * go through the generated client, which means every call picks up both interceptors.
 *
 * Order matters: the auth interceptor runs first so it can attach the token and claim the
 * 401, leaving the error interceptor to report everything else.
 */
export function provideDataAccess(options: DataAccessOptions = {}): EnvironmentProviders {
  const providers: Provider[] = [provideApiConfiguration(normalizeRootUrl(options.rootUrl))];

  return makeEnvironmentProviders([
    ...providers,
    provideHttpClient(withFetch(), withInterceptors([authInterceptor, httpErrorInterceptor])),
  ]);
}

/**
 * Strips the trailing slash so the generated client's `rootUrl + path` join stays valid for
 * both same-origin ("" + "/api/x") and absolute ("http://host" + "/api/x") configurations.
 */
export function normalizeRootUrl(rootUrl: string | undefined): string {
  return (rootUrl ?? '').replace(/\/+$/, '');
}
