import { DOCUMENT, Injectable, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { ProductDto } from '@upbazaar/data-access';

const STORAGE_KEY = 'upb.cart';

/**
 * The most of any one product a shopper can put in the basket here.
 *
 * A cap belongs on the server, and will live there once Ordering exists. Until then this stops
 * a stuck key or a fat finger turning into a four-figure quantity that nothing downstream is
 * prepared for.
 */
export const MAX_QUANTITY = 99;

/**
 * A line as it is stored.
 *
 * The price and name are copied in rather than looked up, because a basket has to survive a
 * reload without a round trip and has to show what the shopper agreed to. When the catalogue
 * moves underneath it, `reconcile` is what notices.
 */
export interface CartLine {
  readonly productId: string;
  readonly sku: string;
  readonly name: string;
  readonly price: number;
  readonly currency: string;
  readonly quantity: number;
}

/**
 * The shopping basket.
 *
 * Entirely client-side and deliberately so: the Cart and Ordering modules do not exist in the
 * API yet, so there is nowhere to send this. It persists to localStorage under one key, which
 * means a basket survives a reload and a return visit but does not follow the shopper to another
 * device. Point `sync` at the real endpoint when Cart ships; nothing else here needs to change.
 */
@Injectable({ providedIn: 'root' })
export class CartStore {
  private readonly platformId = inject(PLATFORM_ID);
  private readonly document = inject(DOCUMENT);

  private readonly state = signal<readonly CartLine[]>(this.restore());

  readonly lines = this.state.asReadonly();

  /** Total number of items, which is what the header badge counts. */
  readonly count = computed(() => this.state().reduce((total, line) => total + line.quantity, 0));

  readonly subtotal = computed(() =>
    this.state().reduce((total, line) => total + line.price * line.quantity, 0)
  );

  readonly isEmpty = computed(() => this.state().length === 0);

  /** The currency of the basket; everything in this catalogue is INR. */
  readonly currency = computed(() => this.state()[0]?.currency ?? 'INR');

  /** How many of one product are already in the basket. */
  quantityOf(productId: string): number {
    return this.state().find((line) => line.productId === productId)?.quantity ?? 0;
  }

  /**
   * Adds a product, or tops up the line that is already there.
   *
   * An unpriced product is refused rather than added at zero: the catalogue carries listings
   * whose seller has not set a price, and a basket that silently totals them as free is worse
   * than a button that does nothing.
   */
  add(product: ProductDto, quantity = 1): boolean {
    if (product.price <= 0 || quantity <= 0) {
      return false;
    }

    this.update((lines) => {
      const existing = lines.find((line) => line.productId === product.id);

      if (existing) {
        return lines.map((line) =>
          line.productId === product.id
            ? { ...line, quantity: Math.min(MAX_QUANTITY, line.quantity + quantity) }
            : line
        );
      }

      return [
        ...lines,
        {
          productId: product.id,
          sku: product.sku,
          name: product.name,
          price: product.price,
          currency: product.currency,
          quantity: Math.min(MAX_QUANTITY, quantity),
        },
      ];
    });

    return true;
  }

  /** Sets an exact quantity; zero or less removes the line, which is what a stepper needs. */
  setQuantity(productId: string, quantity: number): void {
    if (quantity <= 0) {
      this.remove(productId);
      return;
    }

    this.update((lines) =>
      lines.map((line) =>
        line.productId === productId
          ? { ...line, quantity: Math.min(MAX_QUANTITY, Math.floor(quantity)) }
          : line
      )
    );
  }

  remove(productId: string): void {
    this.update((lines) => lines.filter((line) => line.productId !== productId));
  }

  clear(): void {
    this.update(() => []);
  }

  private update(change: (lines: readonly CartLine[]) => readonly CartLine[]): void {
    const next = change(this.state());

    this.state.set(next);
    this.persist(next);
  }

  private storage(): Storage | null {
    if (!isPlatformBrowser(this.platformId)) {
      return null;
    }

    try {
      return this.document.defaultView?.localStorage ?? null;
    } catch {
      // Storage can be blocked outright by privacy settings; an in-memory basket still works.
      return null;
    }
  }

  private restore(): readonly CartLine[] {
    const stored = this.storage()?.getItem(STORAGE_KEY);

    if (!stored) {
      return [];
    }

    try {
      const lines = JSON.parse(stored) as CartLine[];

      // Anything shaped wrong is dropped rather than allowed to reach the totals, where a
      // missing price would quietly become NaN.
      return Array.isArray(lines) ? lines.filter(isCartLine) : [];
    } catch {
      return [];
    }
  }

  private persist(lines: readonly CartLine[]): void {
    const storage = this.storage();

    if (!storage) {
      return;
    }

    try {
      if (lines.length === 0) {
        storage.removeItem(STORAGE_KEY);
      } else {
        storage.setItem(STORAGE_KEY, JSON.stringify(lines));
      }
    } catch {
      // A full or blocked store must not take the page down with it.
    }
  }
}

function isCartLine(value: unknown): value is CartLine {
  const line = value as Partial<CartLine> | null;

  return (
    !!line &&
    typeof line.productId === 'string' &&
    typeof line.sku === 'string' &&
    typeof line.name === 'string' &&
    typeof line.price === 'number' &&
    Number.isFinite(line.price) &&
    typeof line.currency === 'string' &&
    typeof line.quantity === 'number' &&
    Number.isInteger(line.quantity) &&
    line.quantity > 0
  );
}
