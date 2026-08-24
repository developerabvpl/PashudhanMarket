import { Injectable, inject } from '@angular/core';
import { Dialog } from '@angular/cdk/dialog';
import { AuthSheet } from './auth-sheet';

/**
 * Opens the sign-in panel as a bottom sheet.
 *
 * A sheet rather than a route for the common case: a buyer who taps "sign in" from a product
 * page should come back to that product, and a modal keeps the page underneath rather than
 * unmounting it. The full-page route still exists for deep links and for guards redirecting.
 */
@Injectable({ providedIn: 'root' })
export class AuthSheetService {
  private readonly dialog = inject(Dialog);

  /** Opens the sheet and resolves true once the visitor is signed in. */
  async open(): Promise<boolean> {
    const reference = this.dialog.open<boolean>(AuthSheet, {
      // CDK traps focus and wires escape; the panel class does the bottom-sheet placement.
      panelClass: 'upb-auth-sheet',
      backdropClass: 'upb-auth-backdrop',
      autoFocus: 'first-tabbable',
      ariaModal: true,
    });

    return (await new Promise<boolean | undefined>((resolve) => {
      reference.closed.subscribe((result) => resolve(result));
    })) === true;
  }
}
