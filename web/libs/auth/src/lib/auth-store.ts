import { Injectable, computed, inject, signal } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { PLATFORM_ID } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';

/** Claims the API puts in the access token. Permission names match the API constants. */
export interface AuthSession {
  readonly token: string;
  readonly userId: string;
  readonly userName: string;
  readonly permissions: readonly string[];
  readonly expiresAtUtc: number;
}

interface JwtPayload {
  sub?: string;
  nameid?: string;
  name?: string;
  preferred_username?: string;
  permission?: string | string[];
  exp?: number;
}

const STORAGE_KEY = 'upbazaar.session';

/**
 * Single source of truth for who is signed in.
 *
 * State is signal-based so guards, the interceptor and templates all read the same value
 * without a subscription. On the server there is no storage, so the session starts empty and
 * SSR renders the signed-out view; the browser rehydrates from storage on boot.
 */
@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly platformId = inject(PLATFORM_ID);
  private readonly document = inject(DOCUMENT);
  private readonly session = signal<AuthSession | null>(this.restore());

  readonly current = this.session.asReadonly();

  readonly token = computed(() => this.session()?.token ?? null);

  readonly userName = computed(() => this.session()?.userName ?? null);

  readonly permissions = computed<readonly string[]>(() => this.session()?.permissions ?? []);

  readonly isAuthenticated = computed(() => {
    const session = this.session();

    return session !== null && session.expiresAtUtc > Date.now();
  });

  /** True when the signed-in principal carries this permission. */
  has(permission: string): boolean {
    return this.isAuthenticated() && this.permissions().includes(permission);
  }

  /** True when the principal carries every one of these permissions. */
  hasAll(permissions: readonly string[]): boolean {
    return permissions.every((permission) => this.has(permission));
  }

  signIn(token: string): AuthSession | null {
    const session = this.decode(token);

    if (session === null) {
      return null;
    }

    this.session.set(session);
    this.persist(session);

    return session;
  }

  signOut(): void {
    this.session.set(null);
    this.persist(null);
  }

  private decode(token: string): AuthSession | null {
    const payload = decodeJwtPayload(token);

    if (payload === null) {
      return null;
    }

    const permission = payload.permission;

    return {
      token,
      userId: payload.sub ?? payload.nameid ?? '',
      userName: payload.name ?? payload.preferred_username ?? '',
      permissions: permission === undefined ? [] : [permission].flat(),
      // exp is in seconds since the epoch; everything else here works in milliseconds.
      expiresAtUtc: (payload.exp ?? 0) * 1000,
    };
  }

  private storage(): Storage | null {
    if (!isPlatformBrowser(this.platformId)) {
      return null;
    }

    try {
      return this.document.defaultView?.localStorage ?? null;
    } catch {
      // Storage can be blocked outright by privacy settings; treat that as signed out.
      return null;
    }
  }

  private restore(): AuthSession | null {
    const stored = this.storage()?.getItem(STORAGE_KEY);

    if (!stored) {
      return null;
    }

    try {
      const session = JSON.parse(stored) as AuthSession;

      return session.expiresAtUtc > Date.now() ? session : null;
    } catch {
      return null;
    }
  }

  private persist(session: AuthSession | null): void {
    const storage = this.storage();

    if (storage === null) {
      return;
    }

    if (session === null) {
      storage.removeItem(STORAGE_KEY);
      return;
    }

    storage.setItem(STORAGE_KEY, JSON.stringify(session));
  }
}

/** Reads a JWT payload without verifying it. The API is what actually enforces the token. */
export function decodeJwtPayload(token: string): JwtPayload | null {
  const segments = token.split('.');

  if (segments.length !== 3) {
    return null;
  }

  try {
    const base64 = segments[1].replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=');
    const json = decodeBase64(padded);

    return JSON.parse(json) as JwtPayload;
  } catch {
    return null;
  }
}

/**
 * Base64url to UTF-8. `atob` is a global in both browsers and Node 18+, so the same code path
 * serves the client and the SSR render; the percent-encoding dance is what turns atob's
 * latin1 output back into the UTF-8 a name claim may contain.
 */
function decodeBase64(value: string): string {
  if (typeof atob !== 'function') {
    throw new Error('No base64 decoder available in this environment.');
  }

  return decodeURIComponent(
    atob(value)
      .split('')
      .map((char) => `%${`00${char.charCodeAt(0).toString(16)}`.slice(-2)}`)
      .join('')
  );
}
