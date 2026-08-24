import { Injectable, computed, inject, signal } from '@angular/core';
import { Api, UserDto, apiV1UsersMeGet } from '@upbazaar/data-access';
import { AuthTokenStore } from './auth-token-store';

/**
 * The signed-in user's profile and effective permissions.
 *
 * Loaded from /users/me after sign-in rather than decoded out of the access token. The token
 * does carry the permission claims, but reading them here would mean trusting a value the
 * client holds and can edit; asking the API keeps one answer, and it stays correct when an
 * administrator changes somebody's roles mid-session.
 */
@Injectable({ providedIn: 'root' })
export class CurrentUserStore {
  private readonly api = inject(Api);
  private readonly tokens = inject(AuthTokenStore);

  private readonly userSignal = signal<UserDto | null>(null);
  private readonly loadingSignal = signal(false);

  /** In-flight load, so concurrent guards share one request instead of racing. */
  private inFlight: Promise<UserDto | null> | null = null;

  readonly user = this.userSignal.asReadonly();

  readonly isLoading = this.loadingSignal.asReadonly();

  readonly isSignedIn = computed(() => this.userSignal() !== null);

  readonly displayName = computed(() => this.userSignal()?.displayName ?? null);

  readonly permissions = computed<readonly string[]>(() => this.userSignal()?.permissions ?? []);

  readonly roles = computed<readonly string[]>(() => this.userSignal()?.roles ?? []);

  readonly isStaff = computed(() => this.userSignal()?.userType === 'Staff');

  /** True when the profile carries this permission. */
  has(permission: string): boolean {
    return this.permissions().includes(permission);
  }

  /** True when the profile carries every one of these permissions. */
  hasAll(permissions: readonly string[]): boolean {
    return permissions.every((permission) => this.has(permission));
  }

  /**
   * Loads the profile if it is not already held.
   *
   * Returns null when there is no usable token or the call fails, which the guards read as
   * "not signed in" rather than treating a network blip as an authorisation decision.
   */
  async ensureLoaded(): Promise<UserDto | null> {
    const existing = this.userSignal();

    if (existing !== null) {
      return existing;
    }

    if (!this.tokens.isAuthenticated()) {
      return null;
    }

    this.inFlight ??= this.load();

    try {
      return await this.inFlight;
    } finally {
      this.inFlight = null;
    }
  }

  /** Reloads the profile, for use after a change that alters roles or the display name. */
  async refresh(): Promise<UserDto | null> {
    this.userSignal.set(null);

    return await this.ensureLoaded();
  }

  clear(): void {
    this.userSignal.set(null);
    this.inFlight = null;
  }

  private async load(): Promise<UserDto | null> {
    this.loadingSignal.set(true);

    try {
      const user = await this.api.invoke(apiV1UsersMeGet, {});
      this.userSignal.set(user);

      return user;
    } catch {
      // The interceptor has already reported the failure and, on a 401, signed the user out.
      this.userSignal.set(null);

      return null;
    } finally {
      this.loadingSignal.set(false);
    }
  }
}
