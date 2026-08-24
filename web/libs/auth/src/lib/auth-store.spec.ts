import { TestBed } from '@angular/core/testing';
import { AuthStore, decodeJwtPayload } from './auth-store';

/** Builds an unsigned JWT. Only the payload matters here; the API verifies signatures. */
function makeToken(payload: Record<string, unknown>): string {
  const encode = (value: object) => {
    // btoa is latin1-only, so UTF-8 encode first or a Devanagari name throws here.
    const bytes = new TextEncoder().encode(JSON.stringify(value));
    const binary = Array.from(bytes, (byte) => String.fromCharCode(byte)).join('');

    return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  };

  return `${encode({ alg: 'none', typ: 'JWT' })}.${encode(payload)}.signature`;
}

const inAnHour = Math.floor(Date.now() / 1000) + 3600;
const anHourAgo = Math.floor(Date.now() / 1000) - 3600;

describe('AuthStore', () => {
  let store: AuthStore;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({});
    store = TestBed.inject(AuthStore);
  });

  it('starts signed out', () => {
    expect(store.isAuthenticated()).toBe(false);
    expect(store.permissions()).toEqual([]);
  });

  it('reads identity and permissions out of the token', () => {
    store.signIn(
      makeToken({
        sub: 'seller-1',
        name: 'Asha Devi',
        permission: ['catalog.products.write', 'catalog.products.read'],
        exp: inAnHour,
      })
    );

    expect(store.isAuthenticated()).toBe(true);
    expect(store.userName()).toBe('Asha Devi');
    expect(store.has('catalog.products.write')).toBe(true);
    expect(store.has('payments.refunds.write')).toBe(false);
  });

  it('accepts a single permission claim as well as an array', () => {
    store.signIn(makeToken({ sub: 'u', permission: 'ordering.orders.read', exp: inAnHour }));

    expect(store.permissions()).toEqual(['ordering.orders.read']);
  });

  it('treats an expired token as signed out', () => {
    store.signIn(makeToken({ sub: 'u', permission: 'x', exp: anHourAgo }));

    expect(store.isAuthenticated()).toBe(false);
    expect(store.has('x')).toBe(false);
  });

  it('requires every permission for hasAll', () => {
    store.signIn(makeToken({ sub: 'u', permission: ['a', 'b'], exp: inAnHour }));

    expect(store.hasAll(['a', 'b'])).toBe(true);
    expect(store.hasAll(['a', 'c'])).toBe(false);
  });

  it('rejects a token it cannot read', () => {
    expect(store.signIn('this-is-not-a-jwt')).toBeNull();
    expect(store.isAuthenticated()).toBe(false);
  });

  it('survives a reload by restoring from storage', () => {
    store.signIn(makeToken({ sub: 'u', name: 'Asha', permission: ['a'], exp: inAnHour }));

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});
    const restored = TestBed.inject(AuthStore);

    expect(restored.isAuthenticated()).toBe(true);
    expect(restored.has('a')).toBe(true);
  });

  it('does not restore an expired session from storage', () => {
    store.signIn(makeToken({ sub: 'u', permission: ['a'], exp: inAnHour }));
    // Simulate the clock moving past expiry by rewriting what storage holds.
    localStorage.setItem(
      'upbazaar.session',
      JSON.stringify({ token: 't', userId: 'u', userName: '', permissions: ['a'], expiresAtUtc: 1 })
    );

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});

    expect(TestBed.inject(AuthStore).isAuthenticated()).toBe(false);
  });

  it('clears storage on sign out', () => {
    store.signIn(makeToken({ sub: 'u', permission: ['a'], exp: inAnHour }));
    store.signOut();

    expect(store.isAuthenticated()).toBe(false);
    expect(localStorage.getItem('upbazaar.session')).toBeNull();
  });

  it('decodes UTF-8 claims such as a Devanagari name', () => {
    const payload = decodeJwtPayload(makeToken({ name: 'आशा देवी', exp: inAnHour }));

    expect(payload?.name).toBe('आशा देवी');
  });
});
