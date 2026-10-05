import { TestBed } from '@angular/core/testing';
import { AuthTokensDto } from '@upbazaar/data-access';
import { AUTH_SESSION_KEY, AuthTokenStore } from './auth-token-store';

function tokens(overrides: Partial<AuthTokensDto> = {}): AuthTokensDto {
  return {
    accessToken: 'access-token',
    expiresInSeconds: 900,
    refreshToken: 'refresh-token',
    refreshTokenExpiresAtUtc: new Date(Date.now() + 30 * 86_400_000).toISOString(),
    ...overrides,
  };
}

describe('AuthTokenStore', () => {
  let store: AuthTokenStore;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({});
    store = TestBed.inject(AuthTokenStore);
  });

  it('starts signed out', () => {
    expect(store.isAuthenticated()).toBe(false);
    expect(store.accessToken()).toBeNull();
  });

  it('picks up the pair another tab was given', () => {
    store.set(tokens());
    const other = { accessToken: 'newer-access', accessTokenExpiresAtUtc: Date.now() + 900_000, refreshToken: 'newer-refresh', refreshTokenExpiresAtUtc: Date.now() + 86_400_000 };
    localStorage.setItem('upbazaar.session', JSON.stringify(other));

    window.dispatchEvent(new StorageEvent('storage', { key: 'upbazaar.session' }));

    expect(store.refreshToken()).toBe('newer-refresh');
  });

  it('holds the pair it was given', () => {
    store.set(tokens());

    expect(store.accessToken()).toBe('access-token');
    expect(store.refreshToken()).toBe('refresh-token');
    expect(store.hasValidAccessToken()).toBe(true);
    expect(store.isAuthenticated()).toBe(true);
  });

  it('treats a nearly-expired access token as already gone', () => {
    // Inside the 30-second skew, so a request would otherwise leave with a token that dies
    // in flight.
    store.set(tokens({ expiresInSeconds: 10 }));

    expect(store.hasValidAccessToken()).toBe(false);
    // The refresh token is still good, so the session is not over.
    expect(store.canRefresh()).toBe(true);
    expect(store.isAuthenticated()).toBe(true);
  });

  it('is signed out once the refresh token has expired too', () => {
    store.set(tokens({
      expiresInSeconds: 0,
      refreshTokenExpiresAtUtc: new Date(Date.now() - 1000).toISOString(),
    }));

    expect(store.canRefresh()).toBe(false);
    expect(store.isAuthenticated()).toBe(false);
  });

  it('survives a reload', () => {
    store.set(tokens());

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});

    expect(TestBed.inject(AuthTokenStore).isAuthenticated()).toBe(true);
  });

  it('does not restore a session whose refresh token has died', () => {
    store.set(tokens({
      refreshTokenExpiresAtUtc: new Date(Date.now() - 1000).toISOString(),
    }));

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});

    expect(TestBed.inject(AuthTokenStore).isAuthenticated()).toBe(false);
  });

  it('clears storage when signed out', () => {
    store.set(tokens());
    store.clear();

    expect(store.isAuthenticated()).toBe(false);
    expect(localStorage.getItem('upbazaar.session')).toBeNull();
  });
});

describe('AuthTokenStore with a key of its own', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [{ provide: AUTH_SESSION_KEY, useValue: 'upbazaar.admin.session' }],
    });
  });

  it('does not take up the session another app on the same domain left', () => {
    // What the storefront would have stored when a buyer signed in.
    localStorage.setItem(
      'upbazaar.session',
      JSON.stringify({
        accessToken: 'buyer',
        accessTokenExpiresAtUtc: Date.now() + 900_000,
        refreshToken: 'buyer-refresh',
        refreshTokenExpiresAtUtc: Date.now() + 86_400_000,
      })
    );

    expect(TestBed.inject(AuthTokenStore).isAuthenticated()).toBe(false);
  });

  it('keeps its session under its own key and leaves the other alone on sign-out', () => {
    localStorage.setItem('upbazaar.session', 'the storefront session');
    const store = TestBed.inject(AuthTokenStore);

    store.set(tokens());
    expect(localStorage.getItem('upbazaar.admin.session')).not.toBeNull();

    store.clear();
    expect(localStorage.getItem('upbazaar.admin.session')).toBeNull();
    expect(localStorage.getItem('upbazaar.session')).toBe('the storefront session');
  });
});
