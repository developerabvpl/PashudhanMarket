import { Injectable, computed, inject, signal } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { AuthService, CurrentUserStore } from '@upbazaar/auth';
import { Api, SellerAccessDto, SellerDto, apiV1SellersMeAccessGet, apiV1SellersMeGet, toApiProblem } from '@upbazaar/data-access';

/**
 * The permissions the seller portal's areas depend on, mirroring the API's. Every role - owner,
 * manager, dispatch - has {@link SellerPermissions.Orders}; the rest decide which areas show.
 */
export const SellerPermissions = {
  Orders: 'orders.seller.read',
  Products: 'catalog.products.own.write',
  Earnings: 'settlements.own.read',
  Reviews: 'reviews.seller.reply',
  Coupons: 'promotions.own.write',
  Manage: 'sellers.own.manage',
} as const;

/**
 * The caller's shop, their place in it, and whether they may act for it yet.
 *
 * Someone works for a shop as its owner or as a member of its team. Two things must both be true
 * to work: the shop is Approved, and the session carries the seller permissions. Those arrive on
 * the server - at approval, or when the owner adds someone - after the session's token was issued,
 * so the token has to be refreshed: {@link refreshAccess} does that, and the guard calls it.
 *
 * The owner's full shop record - application, KYC - is theirs alone, and is loaded only for them
 * and for someone who has not applied yet.
 */
@Injectable({ providedIn: 'root' })
export class SellerAccess {
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);
  private readonly user = inject(CurrentUserStore);

  private readonly accessSignal = signal<SellerAccessDto | null>(null);
  private readonly sellerSignal = signal<SellerDto | null>(null);
  private readonly loadedSignal = signal(false);

  /** Which shop, and as what: Owner, Manager or Dispatch. Null for someone with no shop. */
  readonly access = this.accessSignal.asReadonly();

  /** The owner's shop in full; null for team members and for someone who has not applied. */
  readonly seller = this.sellerSignal.asReadonly();
  readonly loaded = this.loadedSignal.asReadonly();

  readonly role = computed(() => this.accessSignal()?.role ?? null);
  readonly isOwner = computed(() => this.role() === 'Owner');
  readonly isApproved = computed(() => this.accessSignal()?.status === 'Approved');

  /** Approved on the server and the session already knows it. */
  readonly canSell = computed(() => this.isApproved() && this.user.has(SellerPermissions.Orders));

  /** Loads the caller's place in a shop, and the owner's shop record; returns the latter. */
  async load(): Promise<SellerDto | null> {
    try {
      this.accessSignal.set(await this.orNullIfMissing(() => this.api.invoke(apiV1SellersMeAccessGet, {})));
      this.sellerSignal.set(
        this.role() === null || this.role() === 'Owner'
          ? await this.orNullIfMissing(() => this.api.invoke(apiV1SellersMeGet, {}))
          : null
      );
    } finally {
      this.loadedSignal.set(true);
    }

    return this.sellerSignal();
  }

  /** Replaces the held shop with one the API just returned to its owner. */
  set(seller: SellerDto): void {
    this.sellerSignal.set(seller);
    this.accessSignal.set({ sellerId: seller.id, shopName: seller.shopName, status: seller.status, role: 'Owner' });
    this.loadedSignal.set(true);
  }

  /** Picks up permissions granted since the token was issued: a new token, then a fresh profile. */
  async refreshAccess(): Promise<void> {
    if (await this.auth.refresh()) {
      await this.user.refresh();
    }
  }

  clear(): void {
    this.accessSignal.set(null);
    this.sellerSignal.set(null);
    this.loadedSignal.set(false);
  }

  private async orNullIfMissing<T>(fetch: () => Promise<T>): Promise<T | null> {
    try {
      return await fetch();
    } catch (error) {
      if (toApiProblem(error).status !== 404) {
        throw error;
      }

      return null;
    }
  }
}

/**
 * Lets someone working for an approved shop through. An owner whose shop is not approved goes to
 * their application, which says where it stands; a session that predates approval, or being
 * added to a team, is refreshed on the way in.
 */
export const approvedSellerGuard: CanActivateFn = async (): Promise<boolean | UrlTree> => {
  const access = inject(SellerAccess);
  const router = inject(Router);
  const user = inject(CurrentUserStore);

  if (!access.loaded()) {
    await access.load();
  }

  if (!access.isApproved()) {
    return router.createUrlTree([access.role() === null || access.isOwner() ? '/apply' : '/forbidden']);
  }

  // An owner from before teams existed lacks the permission to manage one until refreshed.
  if (!access.canSell() || (access.isOwner() && !user.has(SellerPermissions.Manage))) {
    await access.refreshAccess();
  }

  return access.canSell() ? true : router.createUrlTree(['/apply']);
};
