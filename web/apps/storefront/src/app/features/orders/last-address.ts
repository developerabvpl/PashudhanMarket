import { DOCUMENT, Injectable, inject } from '@angular/core';
import { DeliveryAddressDto } from '@upbazaar/data-access';

const KEY_PREFIX = 'upb.lastAddress.';

/**
 * The address a buyer last checked out with in this browser, so they do not retype it.
 *
 * A convenience until the server has an address book. Kept per user id so that on a shared
 * device one family member's address is never offered to the next person who signs in.
 * Checkout is client-rendered, so storage is always the browser's.
 */
@Injectable({ providedIn: 'root' })
export class LastAddress {
  private readonly document = inject(DOCUMENT);

  read(userId: string): Partial<DeliveryAddressDto> | null {
    try {
      const stored = this.document.defaultView?.localStorage.getItem(KEY_PREFIX + userId);

      return stored ? (JSON.parse(stored) as Partial<DeliveryAddressDto>) : null;
    } catch {
      return null;
    }
  }

  write(userId: string, address: DeliveryAddressDto): void {
    try {
      this.document.defaultView?.localStorage.setItem(KEY_PREFIX + userId, JSON.stringify(address));
    } catch {
      // Blocked or full storage only means retyping next time.
    }
  }
}
