import { DOCUMENT, Injectable, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { AuthTokensDto } from '@upbazaar/data-access';

/** The token pair as it is persisted, with the access token's expiry resolved to a timestamp. */
interface StoredSession {
  readonly accessToken: string;
  readonly accessTokenExpiresAtUtc: number;
  readonly refreshToken: string;
  readonly refreshTokenExpiresAtUtc: number;
}

const STORAGE_KEY = 'upbazaar.session';

/**
 * Holds the tokens and nothing else.
 *
 * Deliberately separate from {@link CurrentUserStore}: tokens are a transport concern the
 * interceptor needs on every request, while the profile is application state loaded once.
 * Keeping them apart means the interceptor does not drag a user profile into its dependency
 * graph, and signing out is a single call that cannot leave half the state behind.
 *
 * On the server there is no storage, so a server render always starts signed out and the
 * browser rehydrates from storage on boot.
 */
@Injectable({ providedIn: 'root' })
export class AuthTokenStore {
  /**
   * Treat a token as expired slightly early, so a request does not leave with a token that
   * dies in flight.
   */
  private static readonly ExpirySkewMs = 30_000;

  private readonly platformId = inject(PLATFORM_ID);
  private readonly document = inject(DOCUMENT);
  private readonly session = signal<StoredSession | null>(this.restore());

  readonly accessToken = computed(() => this.session()?.accessToken ?? null);

  readonly refreshToken = computed(() => this.session()?.refreshToken ?? null);

  /** True while an unexpired access token is held. */
  readonly hasValidAccessToken = computed(() => {
    const session = this.session();

    return session !== null
      && session.accessTokenExpiresAtUtc - AuthTokenStore.ExpirySkewMs > Date.now();
  });

  /** True while a refresh is still possible, even if the access token has expired. */
  readonly canRefresh = computed(() => {
    const session = this.session();

    return session !== null && session.refreshTokenExpiresAtUtc > Date.now();
  });

  readonly isAuthenticated = computed(() => this.hasValidAccessToken() || this.canRefresh());

  constructor() {
    // Another tab of the same app refreshed, signed in or signed out. Refresh tokens are
    // single-use and the server treats a used one as stolen - signing every tab out - so each
    // tab must pick up the pair the other one was handed rather than spend its stale copy.
    this.document.defaultView?.addEventListener('storage', (event) => {
      if (event.key === STORAGE_KEY || event.key === null) {
        this.session.set(this.restore());
      }
    });
  }

  set(tokens: AuthTokensDto): void {
    const session: StoredSession = {
      accessToken: tokens.accessToken,
      accessTokenExpiresAtUtc: Date.now() + (tokens.expiresInSeconds * 1000),
      refreshToken: tokens.refreshToken,
      refreshTokenExpiresAtUtc: Date.parse(tokens.refreshTokenExpiresAtUtc),
    };

    this.session.set(session);
    this.persist(session);
  }

  clear(): void {
    this.session.set(null);
    this.persist(null);
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

  private restore(): StoredSession | null {
    const stored = this.storage()?.getItem(STORAGE_KEY);

    if (!stored) {
      return null;
    }

    try {
      const session = JSON.parse(stored) as StoredSession;

      // A dead refresh token is worth nothing; drop the whole session rather than keeping a
      // half-usable one around.
      return session.refreshTokenExpiresAtUtc > Date.now() ? session : null;
    } catch {
      return null;
    }
  }

  private persist(session: StoredSession | null): void {
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
