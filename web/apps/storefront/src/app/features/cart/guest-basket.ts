import { DOCUMENT, Injectable, PLATFORM_ID, inject } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { CartLine } from './cart-line';

const STORAGE_KEY = 'upb.cart';

/**
 * The guest basket's persistence: one localStorage key.
 *
 * Kept apart from CartStore because it is only half the story now. A guest's basket lives
 * here and survives a reload, but not a device change; at sign-in CartStore hands it to the
 * Cart API to merge and then empties it, so the account cart is the only copy.
 */
@Injectable({ providedIn: 'root' })
export class GuestBasket {
  private readonly platformId = inject(PLATFORM_ID);
  private readonly document = inject(DOCUMENT);

  read(): readonly CartLine[] {
    const stored = this.storage()?.getItem(STORAGE_KEY);

    if (!stored) {
      return [];
    }

    try {
      const lines = JSON.parse(stored) as unknown;

      // Anything shaped wrong is dropped rather than allowed to reach the totals, where a
      // missing price would quietly become NaN.
      return Array.isArray(lines) ? lines.filter(isStoredLine).map(toLine) : [];
    } catch {
      return [];
    }
  }

  write(lines: readonly CartLine[]): void {
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
}

type StoredLine = Pick<CartLine, 'productId' | 'sku' | 'name' | 'price' | 'currency' | 'quantity'>;

/** Baskets saved before the Cart API lack priceWhenAdded and problem, so they are filled in. */
function toLine(line: StoredLine): CartLine {
  return { ...line, priceWhenAdded: line.price, problem: null };
}

function isStoredLine(value: unknown): value is StoredLine {
  const line = value as Partial<StoredLine> | null;

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
