import { Injectable, computed, inject, signal } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { AuthService, CurrentUserStore } from '@upbazaar/auth';
import { Api, SellerDto, apiV1SellersMeGet, toApiProblem } from '@upbazaar/data-access';

/** The permission SellerOwner carries that every seller page needs; mirrors CatalogPermissions.OwnProductsWrite. */
export const SELLER_OWN_PRODUCTS = 'catalog.products.own.write';

/**
 * The caller's shop and whether they may act for it yet.
 *
 * Two things must both be true to sell: the shop is Approved, and the session carries the
 * SellerOwner permissions. Approval happens on the server while the seller's token was issued
 * before it, so after approval the token has to be refreshed - {@link refreshAccess} does that,
 * and the application page calls it the moment it sees the shop approved.
 */
@Injectable({ providedIn: 'root' })
export class SellerAccess {
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);
  private readonly user = inject(CurrentUserStore);

  private readonly sellerSignal = signal<SellerDto | null>(null);
  private readonly loadedSignal = signal(false);

  readonly seller = this.sellerSignal.asReadonly();
  readonly loaded = this.loadedSignal.asReadonly();

  readonly isApproved = computed(() => this.sellerSignal()?.status === 'Approved');

  /** Approved on the server and the session already knows it. */
  readonly canSell = computed(() => this.isApproved() && this.user.has(SELLER_OWN_PRODUCTS));

  /** Loads the caller's shop; null when they have not applied. */
  async load(): Promise<SellerDto | null> {
    try {
      this.sellerSignal.set(await this.api.invoke(apiV1SellersMeGet, {}));
    } catch (error) {
      if (toApiProblem(error).status !== 404) {
        throw error;
      }

      this.sellerSignal.set(null);
    } finally {
      this.loadedSignal.set(true);
    }

    return this.sellerSignal();
  }

  /** Replaces the held shop with one the API just returned. */
  set(seller: SellerDto): void {
    this.sellerSignal.set(seller);
    this.loadedSignal.set(true);
  }

  /** Picks up permissions granted since the token was issued: a new token, then a fresh profile. */
  async refreshAccess(): Promise<void> {
    if (await this.auth.refresh()) {
      await this.user.refresh();
    }
  }

  clear(): void {
    this.sellerSignal.set(null);
    this.loadedSignal.set(false);
  }
}

/**
 * Lets an approved seller through; anyone else goes to their application, which says where it
 * stands. An approved seller whose session predates approval has it refreshed on the way in.
 */
export const approvedSellerGuard: CanActivateFn = async (): Promise<boolean | UrlTree> => {
  const access = inject(SellerAccess);
  const router = inject(Router);

  const seller = access.loaded() ? access.seller() : await access.load();

  if (seller?.status !== 'Approved') {
    return router.createUrlTree(['/apply']);
  }

  if (!access.canSell()) {
    await access.refreshAccess();
  }

  return access.canSell() ? true : router.createUrlTree(['/apply']);
};
