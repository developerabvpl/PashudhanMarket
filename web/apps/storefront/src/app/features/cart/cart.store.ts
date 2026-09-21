import { Injectable, PLATFORM_ID, computed, effect, inject, signal, untracked } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import {
  Api,
  CartDto,
  ProductDto,
  apiV1CartAcknowledgePricesPost,
  apiV1CartDelete,
  apiV1CartGet,
  apiV1CartItemsProductIdDelete,
  apiV1CartItemsProductIdPut,
  apiV1CartMergePost,
} from '@upbazaar/data-access';
import { CurrentUserStore } from '@upbazaar/auth';
import { ToastService } from '@upbazaar/ui';
import { CartLine, fromCartLineDto } from './cart-line';
import { GuestBasket } from './guest-basket';

export type { CartLine, CartLineProblem } from './cart-line';

/**
 * The most of any one product a line can hold. Mirrors ShoppingCart.MaxQuantityPerLine on the
 * server, which rejects anything above it; capping here keeps a stuck key from becoming a 400.
 */
export const MAX_QUANTITY = 99;

/** The permission that lets a signed-in user keep a server-side cart. Buyers have it; staff do not. */
const CART_WRITE = 'cart.write';

type Mode = 'guest' | 'account';

/**
 * The shopping basket, in one of two modes.
 *
 * A guest's basket lives in localStorage (see GuestBasket) and never touches the API. A signed-in
 * buyer's basket lives in the Cart module, so it follows them between devices and is re-priced
 * and stock-checked on every read. The switch is driven by CurrentUserStore: on sign-in the guest
 * basket is merged into the account cart and then discarded locally; on sign-out the account
 * lines are dropped from view and the shopper is a guest again with an empty basket.
 *
 * In account mode the server is the source of truth. Every mutation sends the change and
 * replaces local state with the cart that comes back, so the page never shows a total the
 * server would not agree with. Adding does not reserve stock; that waits for checkout.
 */
@Injectable({ providedIn: 'root' })
export class CartStore {
  private readonly api = inject(Api);
  private readonly user = inject(CurrentUserStore);
  private readonly guest = inject(GuestBasket);
  private readonly toast = inject(ToastService);

  private readonly mode = signal<Mode>('guest');
  private readonly state = signal<readonly CartLine[]>(this.guest.read());

  /**
   * Bumped on every mode switch and every request, so a response is applied only if nothing
   * newer has been asked for since. Without it a slow GET from before sign-out could repaint
   * the previous user's basket, or an early PUT could overwrite a later one's result.
   */
  private generation = 0;

  private readonly loadingSignal = signal(false);

  readonly lines = this.state.asReadonly();

  /**
   * True while the account cart is first fetched or merged after sign-in. Checkout reads it so
   * an account cart still on its way is not mistaken for an empty one.
   */
  readonly isLoading = this.loadingSignal.asReadonly();

  /** True while the basket is held by the Cart API rather than this browser. */
  readonly isAccountCart = computed(() => this.mode() === 'account');

  /** Total number of items, which is what the header badge counts. */
  readonly count = computed(() => this.state().reduce((total, line) => total + line.quantity, 0));

  /** Lines with a problem are left out, exactly as the server leaves them out of its subtotal. */
  readonly subtotal = computed(() =>
    this.state()
      .filter((line) => line.problem === null)
      .reduce((total, line) => total + line.price * line.quantity, 0)
  );

  readonly isEmpty = computed(() => this.state().length === 0);

  /** Same rule as the server's CanCheckOut: something to buy and nothing standing in the way. */
  readonly canCheckOut = computed(
    () => this.state().length > 0 && this.state().every((line) => line.problem === null)
  );

  /** True when some price has moved since it was added, which the shopper must accept. */
  readonly hasPriceChanges = computed(() =>
    this.state().some((line) => line.problem === 'PriceChanged')
  );

  /** The currency of the basket; everything in this catalogue is INR. */
  readonly currency = computed(() => this.state()[0]?.currency ?? 'INR');

  constructor() {
    // The server render has no session and no storage, so it always shows an empty guest
    // basket; only the browser decides which cart it is looking at.
    if (!isPlatformBrowser(inject(PLATFORM_ID))) {
      return;
    }

    effect(() => {
      const account = this.user.isSignedIn() && this.user.has(CART_WRITE);

      untracked(() => (account ? void this.enterAccount() : this.leaveAccount()));
    });
  }

  /** How many of one product are already in the basket. */
  quantityOf(productId: string): number {
    return this.state().find((line) => line.productId === productId)?.quantity ?? 0;
  }

  /**
   * Adds a product, or tops up the line that is already there. Resolves false when nothing
   * was added.
   *
   * An unpriced product is refused rather than added at zero: the catalogue carries listings
   * whose seller has not set a price, and a basket that silently totals them as free is worse
   * than a button that does nothing.
   */
  async add(product: ProductDto, quantity = 1): Promise<boolean> {
    if (product.price <= 0 || quantity <= 0) {
      return false;
    }

    const next = Math.min(MAX_QUANTITY, this.quantityOf(product.id) + quantity);

    if (this.mode() === 'account') {
      return await this.send(() =>
        this.api.invoke(apiV1CartItemsProductIdPut, {
          productId: product.id,
          body: { quantity: next },
        })
      );
    }

    this.updateGuest((lines) =>
      lines.some((line) => line.productId === product.id)
        ? lines.map((line) => (line.productId === product.id ? { ...line, quantity: next } : line))
        : [
            ...lines,
            {
              productId: product.id,
              sku: product.sku,
              name: product.name,
              price: product.price,
              priceWhenAdded: product.price,
              currency: product.currency,
              quantity: next,
              problem: null,
            },
          ]
    );

    return true;
  }

  /** Sets an exact quantity; zero or less removes the line, which is what a stepper needs. */
  async setQuantity(productId: string, quantity: number): Promise<void> {
    if (quantity <= 0) {
      return await this.remove(productId);
    }

    const next = Math.min(MAX_QUANTITY, Math.floor(quantity));

    if (this.mode() === 'account') {
      await this.send(() =>
        this.api.invoke(apiV1CartItemsProductIdPut, { productId, body: { quantity: next } })
      );
      return;
    }

    this.updateGuest((lines) =>
      lines.map((line) => (line.productId === productId ? { ...line, quantity: next } : line))
    );
  }

  async remove(productId: string): Promise<void> {
    if (this.mode() === 'account') {
      await this.send(() => this.api.invoke(apiV1CartItemsProductIdDelete, { productId }));
      return;
    }

    this.updateGuest((lines) => lines.filter((line) => line.productId !== productId));
  }

  async clear(): Promise<void> {
    if (this.mode() === 'account') {
      await this.send(() => this.api.invoke(apiV1CartDelete, {}));
      return;
    }

    this.updateGuest(() => []);
  }

  /** Accepts every changed price as the one to pay. Only an account cart can have any. */
  async acknowledgePrices(): Promise<void> {
    if (this.mode() === 'account') {
      await this.send(() => this.api.invoke(apiV1CartAcknowledgePricesPost, {}));
    }
  }

  private async enterAccount(): Promise<void> {
    if (this.mode() === 'account') {
      return;
    }

    this.mode.set('account');
    this.loadingSignal.set(true);

    try {
      await this.loadAccountCart();
    } finally {
      this.loadingSignal.set(false);
    }
  }

  private async loadAccountCart(): Promise<void> {
    const pending = this.guest.read();

    if (pending.length === 0) {
      await this.send(() => this.api.invoke(apiV1CartGet, {}));
      return;
    }

    const generation = ++this.generation;

    try {
      const result = await this.api.invoke(apiV1CartMergePost, {
        body: {
          lines: pending.map((line) => ({ productId: line.productId, quantity: line.quantity })),
        },
      });

      // The merge has happened on the server whether or not anyone is still watching, so the
      // local copy goes regardless; keeping it would merge the same lines again next sign-in.
      this.guest.write([]);

      if (generation !== this.generation) {
        return;
      }

      this.apply(result.cart);

      if (result.skipped.length > 0) {
        this.toast.info('cart.mergeSkipped');
      }
    } catch {
      // Nothing was merged, so the guest basket is kept for the next attempt. The account cart
      // is still shown, because that is what a signed-in shopper is now editing.
      if (generation === this.generation) {
        await this.send(() => this.api.invoke(apiV1CartGet, {}));
      }
    }
  }

  private leaveAccount(): void {
    if (this.mode() === 'guest') {
      return;
    }

    this.generation++;
    this.mode.set('guest');
    this.loadingSignal.set(false);
    this.state.set([]);
  }

  /**
   * Runs one account-cart call and shows its result. On failure the cart is reloaded, since the
   * server may or may not have applied the change; the http error interceptor has already told
   * the shopper what went wrong.
   */
  private async send(call: () => Promise<CartDto>): Promise<boolean> {
    const generation = ++this.generation;

    try {
      const cart = await call();

      if (generation === this.generation) {
        this.apply(cart);
      }

      return true;
    } catch {
      if (generation === this.generation && this.mode() === 'account') {
        void this.refresh();
      }

      return false;
    }
  }

  /**
   * Fetches the account cart again, for when something other than this store changed it: an
   * order placed at checkout empties it on the server. A guest basket has nothing to fetch.
   */
  async refresh(): Promise<void> {
    if (this.mode() !== 'account') {
      return;
    }

    const generation = ++this.generation;

    try {
      const cart = await this.api.invoke(apiV1CartGet, {});

      if (generation === this.generation) {
        this.apply(cart);
      }
    } catch {
      // Already reported; the last good state stays on screen.
    }
  }

  private apply(cart: CartDto): void {
    this.state.set(cart.lines.map((line) => fromCartLineDto(line, cart.currency)));
  }

  private updateGuest(change: (lines: readonly CartLine[]) => readonly CartLine[]): void {
    const next = change(this.state());

    this.state.set(next);
    this.guest.write(next);
  }
}
