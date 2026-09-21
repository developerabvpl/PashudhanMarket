import { ChangeDetectionStrategy, Component, OnInit, inject, input, output, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { CheckoutSessionDto, OrderDto } from '@upbazaar/data-access';
import { ToastService } from '@upbazaar/ui';
import { DateIstPipe, InrCurrencyPipe } from '@upbazaar/util';
import { OrderPayment, PaymentOutcome } from './order-payment';

/**
 * "Pay now" for an order waiting for online payment, with its deadline.
 *
 * Checkout lands here with the panel set to start at once, so a buyer goes straight from placing
 * the order into the payment window. Closing the window, or a failed card, leaves the panel in
 * place: the order can be paid until its deadline, after which it is cancelled on its own.
 *
 * Emits `settled` whenever the order may have changed, so the page reloads it.
 */
@Component({
  selector: 'upb-order-pay-panel',
  imports: [TranslocoPipe, InrCurrencyPipe, DateIstPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mt-4 rounded-card border border-warning/50 bg-warning/10 p-5">
      <p class="font-semibold text-ink">
        {{ 'payments.due' | transloco: { amount: (order().total | inr: 'symbol' : 'auto') } }}
      </p>
      @if (order().paymentDueAtUtc; as due) {
      <p class="mt-1 text-sm text-ink">
        {{ 'payments.dueBy' | transloco: { time: (due | dateIst: 'time') } }}
      </p>
      }

      @if (simulating(); as session) {
      <div class="mt-4 rounded-control border border-border bg-surface p-4" role="alertdialog" aria-labelledby="simulate-title">
        <p id="simulate-title" class="text-sm font-medium text-ink">{{ 'payments.testMode' | transloco }}</p>
        <p class="mt-1 text-sm text-ink-muted">{{ 'payments.testModeNote' | transloco }}</p>
        <div class="mt-3 flex flex-wrap gap-3">
          <button
            type="button"
            class="rounded-control bg-brand-600 px-4 py-2 text-sm font-semibold text-white hover:bg-brand-700 disabled:opacity-50"
            [disabled]="busy()"
            (click)="simulate(session)"
          >
            {{ 'payments.simulate' | transloco }}
          </button>
          <button type="button" class="text-sm text-ink-muted hover:underline" (click)="simulating.set(null)">
            {{ 'common.cancel' | transloco }}
          </button>
        </div>
      </div>
      } @else {
      <button
        type="button"
        class="mt-4 rounded-control bg-brand-600 px-6 py-2.5 font-semibold text-white transition-colors hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-50"
        [disabled]="busy()"
        (click)="pay()"
      >
        {{ (busy() ? 'payments.opening' : 'payments.payNow') | transloco }}
      </button>
      }
    </div>
  `,
})
export class OrderPayPanel implements OnInit {
  readonly order = input.required<OrderDto>();

  /** Open the payment window as soon as the panel appears. */
  readonly autoStart = input(false);

  readonly settled = output<void>();

  protected readonly busy = signal(false);
  protected readonly simulating = signal<CheckoutSessionDto | null>(null);

  private readonly payment = inject(OrderPayment);
  private readonly toast = inject(ToastService);

  ngOnInit(): void {
    if (this.autoStart()) {
      void this.pay();
    }
  }

  protected async pay(): Promise<void> {
    this.busy.set(true);

    try {
      const address = this.order().deliveryAddress;
      const { outcome, session } = await this.payment.pay(this.order().id, {
        name: address.fullName,
        contact: address.mobile,
      });

      if (outcome === 'simulate') {
        this.simulating.set(session);
      } else {
        this.report(outcome);
      }
    } catch {
      // The interceptor has shown why; if the order changed meanwhile (paid, or cancelled at its
      // deadline) the reload shows that.
      this.settled.emit();
    } finally {
      this.busy.set(false);
    }
  }

  protected async simulate(session: CheckoutSessionDto): Promise<void> {
    this.busy.set(true);

    try {
      this.report(await this.payment.simulate(session));
      this.simulating.set(null);
    } catch {
      this.settled.emit();
    } finally {
      this.busy.set(false);
    }
  }

  private report(outcome: PaymentOutcome): void {
    switch (outcome) {
      case 'confirmed':
        this.toast.success('payments.paid');
        break;
      case 'processing':
        this.toast.info('payments.processing');
        break;
      case 'refund':
        this.toast.info('payments.refundDue');
        break;
      default:
        return;
    }

    this.settled.emit();
  }
}
