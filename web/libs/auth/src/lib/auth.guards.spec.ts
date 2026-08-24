import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { provideRouter } from '@angular/router';
import { AuthStore } from './auth-store';
import { authGuard, permissionGuard } from './auth.guards';

function makeToken(permissions: string[]): string {
  const encode = (value: object) =>
    btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');

  return `${encode({ alg: 'none' })}.${encode({
    sub: 'u',
    permission: permissions,
    exp: Math.floor(Date.now() / 1000) + 3600,
  })}.sig`;
}

const route = {} as ActivatedRouteSnapshot;
const state = { url: '/products/new' } as RouterStateSnapshot;

describe('route guards', () => {
  let auth: AuthStore;
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    auth = TestBed.inject(AuthStore);
    router = TestBed.inject(Router);
  });

  describe('authGuard', () => {
    it('lets a signed-in user through', () => {
      auth.signIn(makeToken([]));

      const result = TestBed.runInInjectionContext(() => authGuard(route, state));

      expect(result).toBe(true);
    });

    it('sends a signed-out user to sign-in, remembering where they were going', () => {
      const result = TestBed.runInInjectionContext(() => authGuard(route, state)) as UrlTree;

      expect(result).toBeInstanceOf(UrlTree);
      expect(router.serializeUrl(result)).toBe('/sign-in?returnUrl=%2Fproducts%2Fnew');
    });
  });

  describe('permissionGuard', () => {
    it('lets a user with the permission through', () => {
      auth.signIn(makeToken(['catalog.products.write']));

      const result = TestBed.runInInjectionContext(() =>
        permissionGuard('catalog.products.write')(route, state)
      );

      expect(result).toBe(true);
    });

    it('requires all of the listed permissions', () => {
      auth.signIn(makeToken(['catalog.products.write']));

      const result = TestBed.runInInjectionContext(() =>
        permissionGuard('catalog.products.write', 'catalog.stock.write')(route, state)
      );

      expect(result).toBeInstanceOf(UrlTree);
    });

    it('sends a signed-in but unauthorized user to /forbidden, not back to sign-in', () => {
      auth.signIn(makeToken(['ordering.orders.read']));

      const result = TestBed.runInInjectionContext(() =>
        permissionGuard('catalog.products.write')(route, state)
      ) as UrlTree;

      expect(router.serializeUrl(result)).toContain('/forbidden');
      expect(router.serializeUrl(result)).toContain('catalog.products.write');
    });

    it('sends a signed-out user to sign-in', () => {
      const result = TestBed.runInInjectionContext(() =>
        permissionGuard('catalog.products.write')(route, state)
      ) as UrlTree;

      expect(router.serializeUrl(result)).toContain('/sign-in');
    });
  });
});
