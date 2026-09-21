import { DOCUMENT, Injectable, inject } from '@angular/core';
import { CheckoutSessionDto } from '@upbazaar/data-access';

const SCRIPT_URL = 'https://checkout.razorpay.com/v1/checkout.js';

/** What Checkout hands back after a successful payment; the API verifies all three. */
export interface RazorpaySuccess {
  readonly razorpay_payment_id: string;
  readonly razorpay_order_id: string;
  readonly razorpay_signature: string;
}

interface RazorpayInstance {
  open(): void;
  on(event: 'payment.failed', handler: (response: { error?: { description?: string } }) => void): void;
}

type RazorpayConstructor = new (options: Record<string, unknown>) => RazorpayInstance;

/** Who is paying, to save them typing it into Checkout again. */
export interface PayerDetails {
  readonly name?: string | null;
  readonly contact?: string | null;
}

/**
 * Razorpay Checkout: its script, loaded on first use, and one window per payment.
 *
 * Loaded lazily because it is only needed by buyers who pay online, and it is third-party
 * script, so it should not be on every product page. The window reports three outcomes: paid
 * (resolves with the ids to verify), closed by the buyer (resolves null), or the script could
 * not load (rejects). A declined card is not an outcome: Checkout lets the buyer try again inside
 * the same window, and Razorpay's webhook records the failure for support.
 */
@Injectable({ providedIn: 'root' })
export class RazorpayCheckout {
  private readonly document = inject(DOCUMENT);
  private loading: Promise<RazorpayConstructor> | null = null;

  async open(session: CheckoutSessionDto, payer: PayerDetails): Promise<RazorpaySuccess | null> {
    const Razorpay = await this.load();

    // Checkout counts down on its own; stop it at the order's payment deadline so a buyer is not
    // left paying for an order that has just been cancelled underneath them.
    const secondsLeft = Math.floor((new Date(session.expiresAtUtc).getTime() - Date.now()) / 1000);

    return new Promise<RazorpaySuccess | null>((resolve) => {
      const checkout = new Razorpay({
        key: session.keyId,
        order_id: session.gatewayOrderId,
        amount: session.amountInPaise,
        currency: session.currency,
        name: 'UP Bazaar',
        description: session.orderNumber,
        timeout: Math.max(60, secondsLeft),
        prefill: { name: payer.name ?? undefined, contact: payer.contact ?? undefined },
        theme: { color: '#c2410c' },
        handler: (response: RazorpaySuccess) => resolve(response),
        modal: { ondismiss: () => resolve(null), confirm_close: true },
      });

      checkout.open();
    });
  }

  private load(): Promise<RazorpayConstructor> {
    const existing = (this.document.defaultView as { Razorpay?: RazorpayConstructor } | null)?.Razorpay;

    if (existing) {
      return Promise.resolve(existing);
    }

    this.loading ??= new Promise<RazorpayConstructor>((resolve, reject) => {
      const script = this.document.createElement('script');

      script.src = SCRIPT_URL;
      script.async = true;
      script.onload = () => {
        const loaded = (this.document.defaultView as { Razorpay?: RazorpayConstructor } | null)?.Razorpay;

        if (loaded) {
          resolve(loaded);
        } else {
          reject(new Error('Razorpay Checkout did not load.'));
        }
      };
      script.onerror = () => {
        // Let the next attempt try again rather than remembering the failure for good.
        this.loading = null;
        script.remove();
        reject(new Error('Razorpay Checkout could not be reached.'));
      };

      this.document.head.appendChild(script);
    });

    return this.loading;
  }
}
