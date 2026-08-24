import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { Api, UserDto } from '@upbazaar/data-access';
import { AuthTokenStore } from './auth-token-store';
import { anonymousOnlyGuard, authGuard, permissionGuard } from './auth.guards';

const route = {} as ActivatedRouteSnapshot;
const state = { url: '/staff' } as RouterStateSnapshot;

function profile(permissions: string[]): UserDto {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    userType: 'Staff',
    email: 'ops@upbazaar.test',
    emailVerified: true,
    mobile: null,
    mobileVerified: false,
    displayName: 'Ops',
    preferredLanguage: 'en',
    status: 'Active',
    twoFactorEnabled: false,
    roles: ['SupportAgent'],
    permissions,
    createdAtUtc: '2026-03-14T10:00:00Z',
  };
}

describe('route guards', () => {
  let invoke: ReturnType<typeof vi.fn>;
  let tokens: AuthTokenStore;
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    invoke = vi.fn();

    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: Api, useValue: { invoke } }],
    });

    tokens = TestBed.inject(AuthTokenStore);
    router = TestBed.inject(Router);
  });

  /** Signs in with a token that has plenty of life left. */
  function holdTokens(): void {
    tokens.set({
      accessToken: 'access',
      expiresInSeconds: 900,
      refreshToken: 'refresh',
      refreshTokenExpiresAtUtc: new Date(Date.now() + 86_400_000).toISOString(),
    });
  }

  describe('authGuard', () => {
    it('lets a signed-in user through once the profile has loaded', async () => {
      holdTokens();
      invoke.mockResolvedValue(profile([]));

      const result = await TestBed.runInInjectionContext(() => authGuard(route, state));

      expect(result).toBe(true);
    });

    it('sends a signed-out visitor to sign-in, remembering where they were going', async () => {
      const result = (await TestBed.runInInjectionContext(() =>
        authGuard(route, state)
      )) as UrlTree;

      expect(result).toBeInstanceOf(UrlTree);
      expect(router.serializeUrl(result)).toBe('/sign-in?returnUrl=%2Fstaff');
    });

    it('treats a failed profile load as not signed in', async () => {
      holdTokens();
      invoke.mockRejectedValue(new Error('offline'));

      const result = await TestBed.runInInjectionContext(() => authGuard(route, state));

      expect(result).toBeInstanceOf(UrlTree);
    });
  });

  describe('permissionGuard', () => {
    it('lets a user holding the permission through', async () => {
      holdTokens();
      invoke.mockResolvedValue(profile(['identity.users.read']));

      const result = await TestBed.runInInjectionContext(() =>
        permissionGuard('identity.users.read')(route, state)
      );

      expect(result).toBe(true);
    });

    it('requires every listed permission', async () => {
      holdTokens();
      invoke.mockResolvedValue(profile(['identity.users.read']));

      const result = await TestBed.runInInjectionContext(() =>
        permissionGuard('identity.users.read', 'identity.users.manage')(route, state)
      );

      expect(result).toBeInstanceOf(UrlTree);
    });

    it('sends a signed-in but unauthorised user to /forbidden, not back to sign-in', async () => {
      holdTokens();
      invoke.mockResolvedValue(profile(['orders.read']));

      const result = (await TestBed.runInInjectionContext(() =>
        permissionGuard('identity.users.manage')(route, state)
      )) as UrlTree;

      const url = router.serializeUrl(result);

      expect(url).toContain('/forbidden');
      expect(url).toContain('identity.users.manage');
    });

    it('sends a signed-out visitor to sign-in', async () => {
      const result = (await TestBed.runInInjectionContext(() =>
        permissionGuard('identity.users.manage')(route, state)
      )) as UrlTree;

      expect(router.serializeUrl(result)).toContain('/sign-in');
    });

    it('loads the profile once even when two guards run together', async () => {
      holdTokens();
      invoke.mockResolvedValue(profile(['identity.users.read']));

      await Promise.all([
        TestBed.runInInjectionContext(() => permissionGuard('identity.users.read')(route, state)),
        TestBed.runInInjectionContext(() => permissionGuard('identity.users.read')(route, state)),
      ]);

      // Two concurrent guards must share one request, not race each other.
      expect(invoke).toHaveBeenCalledTimes(1);
    });
  });

  describe('anonymousOnlyGuard', () => {
    it('lets a signed-out visitor see the sign-in page', () => {
      expect(TestBed.runInInjectionContext(() => anonymousOnlyGuard(route, state))).toBe(true);
    });

    it('redirects someone already signed in away from it', () => {
      holdTokens();

      const result = TestBed.runInInjectionContext(() => anonymousOnlyGuard(route, state));

      expect(result).toBeInstanceOf(UrlTree);
    });
  });
});
