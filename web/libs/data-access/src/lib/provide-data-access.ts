import { EnvironmentProviders, makeEnvironmentProviders } from '@angular/core';
import {
  HttpInterceptorFn,
  provideHttpClient,
  withFetch,
  withInterceptors,
} from '@angular/common/http';
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

  /**
   * Interceptors to run ahead of the built-in error reporter.
   *
   * This is how the auth interceptor gets in without data-access importing it. Auth sits above
   * data-access — it calls the generated client to log in and refresh — so a direct import here
   * would close a cycle. Each app passes `[authInterceptor]` from its composition root.
   */
  readonly interceptors?: readonly HttpInterceptorFn[];
}

/**
 * The one place HttpClient is configured. Components never inject HttpClient directly; they
 * go through the generated client, which means every call picks up the interceptors.
 *
 * Order matters: the supplied interceptors run first so auth can attach the token and claim the
 * 401, leaving the error interceptor to report everything else.
 */
export function provideDataAccess(options: DataAccessOptions = {}): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideApiConfiguration(normalizeRootUrl(options.rootUrl)),
    provideHttpClient(
      withFetch(),
      withInterceptors([...(options.interceptors ?? []), httpErrorInterceptor])
    ),
  ]);
}

/**
 * Strips the trailing slash so the generated client's `rootUrl + path` join stays valid for
 * both same-origin ("" + "/api/x") and absolute ("http://host" + "/api/x") configurations.
 */
export function normalizeRootUrl(rootUrl: string | undefined): string {
  return (rootUrl ?? '').replace(/\/+$/, '');
}
