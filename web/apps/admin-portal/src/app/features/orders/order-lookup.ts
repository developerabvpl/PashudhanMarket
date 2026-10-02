import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  Api,
  OrderDto,
  OrderSummaryDto,
  apiV1AdminOrdersGet,
  apiV1AdminOrdersOrderIdGet,
  orderProgress,
  toApiProblem,
} from '@upbazaar/data-access';
import { PageState } from '@upbazaar/ui';
import { DateIstPipe, InrCurrencyPipe } from '@upbazaar/util';
import { OrderParcels } from './order-parcels';

type LookupState = 'idle' | 'loading' | 'loaded' | 'choose' | 'notFound' | 'error';

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Support screen: look an order up by its number - what a buyer reads out on the phone, such as
 * UPB-260929-QMRATS, or any part of it - or by its public id, and show what the customer sees plus the
 * payment reference, which is what a refund conversation actually needs.
 *
 * The money is broken down the way the buyer's own order page breaks it down - coupon and
 * free-delivery discounts included - so the parcels' full-price subtotals visibly add up to the
 * total. The total counts only what the buyer keeps; for an order paid online that is being
 * refunded, what was paid and what goes back are shown beneath it.
 *
 * The order's status is shown in the buyer's words (orders.status.*), translated like the rest,
 * so support reads "Delivered" where the API says Completed - the same word the buyer sees.
 */
@Component({
  selector: 'upb-order-lookup',
  imports: [TranslocoPipe, InrCurrencyPipe, DateIstPipe, PageState, OrderParcels],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-3xl px-4 py-8">
      <h1 class="text-2xl font-semibold text-ink">{{ 'admin.orderLookup' | transloco }}</h1>

      <form class="mt-4 flex gap-2" (submit)="find($event)">
        <label class="flex-1">
          <span class="upb-sr-only">{{ 'admin.orderNumberOrId' | transloco }}</span>
          <input
            name="orderId"
            type="text"
            required
            class="w-full rounded-control border border-border bg-surface px-3 py-2 font-mono text-sm text-ink"
            placeholder="UPB-260929-QMRATS"
            [value]="orderId() ?? ''"
          />
        </label>
        <button
          type="submit"
          class="rounded-control bg-brand-600 px-4 py-2 font-medium text-white hover:bg-brand-700"
        >
          {{ 'admin.find' | transloco }}
        </button>
      </form>

      @switch (state()) { @case ('loading') {
      <upb-page-state state="loading" />
      } @case ('choose') {
      <div class="upb-card mt-6 p-4 text-sm">
        <p class="text-ink">{{ 'admin.manyMatches' | transloco: { count: matches().length } }}</p>
        <ul class="mt-2 divide-y divide-border">
          @for (match of matches(); track match.id) {
          <li>
            <button type="button" class="flex w-full justify-between gap-3 py-2 text-left hover:bg-surface-sunken" (click)="pick(match.id)">
              <span class="font-mono">{{ match.number }}</span>
              <span class="text-ink-muted">{{ match.placedAtUtc | dateIst }} · {{ match.total | inr }}</span>
            </button>
          </li>
          }
        </ul>
      </div>
      } @case ('notFound') {
      <upb-page-state state="empty" message="admin.notFound" />
      } @case ('error') {
      <upb-page-state state="error" (retry)="retry()" />
      } @case ('loaded') { @if (order(); as found) {
      <article class="upb-card mt-6 p-6">
        <header class="flex flex-wrap items-baseline justify-between gap-2">
          <h2 class="font-mono text-lg font-semibold text-ink">{{ found.number }}</h2>
          <span class="rounded-control bg-surface-sunken px-2 py-1 text-sm text-ink-muted">
            {{ 'admin.status' | transloco }}: {{ 'orders.status.' + progress(found) | transloco }}
          </span>
        </header>

        <dl class="mt-4 grid grid-cols-2 gap-3 text-sm">
          <dt class="text-ink-muted">{{ 'admin.placedAt' | transloco }}</dt>
          <dd class="text-ink">{{ found.placedAtUtc | dateIst: 'datetime' }}</dd>

          <dt class="text-ink-muted">{{ 'admin.subtotal' | transloco }}</dt>
          <dd class="text-ink">{{ found.subtotal | inr }}</dd>

          @if (found.discount > 0) {
          <dt class="text-ink-muted">{{ 'admin.couponDiscount' | transloco: { code: found.couponCode ?? '' } }}</dt>
          <dd class="text-ink">{{ -found.discount | inr }}</dd>
          }

          <dt class="text-ink-muted">{{ 'admin.delivery' | transloco }}</dt>
          <dd class="text-ink">{{ found.shippingFee | inr }}</dd>

          @if (found.deliveryDiscount > 0) {
          <dt class="text-ink-muted">{{ 'admin.deliveryDiscount' | transloco: { code: found.couponCode ?? '' } }}</dt>
          <dd class="text-ink">{{ -found.deliveryDiscount | inr }}</dd>
          }

          <dt class="text-ink-muted">{{ 'admin.total' | transloco }}</dt>
          <dd class="font-semibold text-ink">{{ found.total | inr }}</dd>

          <dt class="text-ink-muted">{{ 'admin.payment' | transloco }}</dt>
          <dd class="text-ink">
            {{ 'orders.paymentMethod.' + found.paymentMethod | transloco }}
            @if (found.paymentReference) {
            <span class="block font-mono text-xs">{{ found.paymentReference }}</span>
            }
          </dd>

          @if (found.amountPaid !== null && found.amountPaid !== undefined && found.refundTotal > 0) {
          <dt class="text-ink-muted">{{ 'admin.paidOnline' | transloco }}</dt>
          <dd class="text-ink">{{ found.amountPaid | inr }}</dd>

          <dt class="text-ink-muted">{{ 'admin.refund' | transloco }}</dt>
          <dd class="text-ink">{{ found.refundTotal | inr }}</dd>
          }
        </dl>

        <h3 class="mt-6 font-medium text-ink">{{ 'admin.lines' | transloco }}</h3>
        <table class="mt-2 w-full text-left text-sm">
          <caption class="upb-sr-only">{{ 'admin.lines' | transloco }}</caption>
          <thead>
            <tr class="border-b border-border text-ink-muted">
              <th scope="col" class="py-2">{{ 'catalog.sku' | transloco }}</th>
              <th scope="col" class="py-2 text-right">{{ 'admin.quantity' | transloco }}</th>
              <th scope="col" class="py-2 text-right">{{ 'admin.unitPrice' | transloco }}</th>
              <th scope="col" class="py-2 text-right">{{ 'admin.lineTotal' | transloco }}</th>
            </tr>
          </thead>
          <tbody>
            @for (line of lines(); track line.productId) {
            <tr class="border-b border-border/60">
              <td class="py-2">
                <span class="font-mono text-xs">{{ line.sku }}</span>
                <span class="block text-ink-muted">{{ line.name }}</span>
              </td>
              <td class="py-2 text-right">{{ line.quantity }}</td>
              <td class="py-2 text-right">{{ line.unitPrice | inr }}</td>
              <td class="py-2 text-right font-medium">{{ line.lineTotal | inr }}</td>
            </tr>
            }
          </tbody>
        </table>

        <upb-order-parcels [order]="found" (changed)="retry()" />
      </article>
      } } }
    </section>
  `,
})
export class OrderLookup {
  /** ?orderId=..., bound by the router: other screens, such as the returns queue, link straight to an order. */
  readonly orderId = input<string | undefined>(undefined);

  private readonly api = inject(Api);
  private readonly lastQueried = signal<string | null>(null);

  protected readonly state = signal<LookupState>('idle');
  protected readonly order = signal<OrderDto | null>(null);

  /** Orders whose numbers matched what was typed, when more than one did. */
  protected readonly matches = signal<readonly OrderSummaryDto[]>([]);

  protected readonly hasResult = computed(() => this.order() !== null);

  /** Every line across the sellers' parts: support reads an order as one list, not per parcel. */
  protected readonly lines = computed(() => this.order()?.parts.flatMap((part) => part.lines) ?? []);

  constructor() {
    effect(() => {
      const orderId = this.orderId();
      untracked(() => void (orderId ? this.load(orderId) : undefined));
    });
  }

  find(event: Event): void {
    event.preventDefault();

    const form = event.target as HTMLFormElement;
    const orderId = new FormData(form).get('orderId')?.toString().trim() ?? '';

    if (orderId === '') {
      return;
    }

    void this.load(orderId);
  }

  retry(): void {
    const orderId = this.lastQueried();

    if (orderId !== null) {
      void this.load(orderId);
    }
  }

  /** Opens one of several orders whose numbers matched. */
  pick(orderId: string): void {
    void this.load(orderId);
  }

  /** The order's status in the buyer's words, Shipped or Packed once its parcels have got that far. */
  protected progress(order: OrderDto): string {
    return orderProgress(order.status, order.parts.map((p) => p.status));
  }

  /**
   * An id opens that order. Anything else is an order number, or part of one: the staff orders
   * search finds it, and one match - or one whose number is exactly what was typed - opens
   * straight away, while several are listed to choose from.
   */
  private async load(query: string): Promise<void> {
    this.lastQueried.set(query);
    this.state.set('loading');
    this.matches.set([]);

    try {
      let orderId = query;

      if (!GUID.test(query)) {
        const found = (await this.api.invoke(apiV1AdminOrdersGet, { Number: query, PageSize: 20 })).items;
        const exact = found.find((o) => o.number.toUpperCase() === query.toUpperCase());

        if (!exact && found.length !== 1) {
          this.order.set(null);
          this.matches.set(found);
          this.state.set(found.length === 0 ? 'notFound' : 'choose');
          return;
        }

        orderId = (exact ?? found[0]).id;
      }

      this.order.set(await this.api.invoke(apiV1AdminOrdersOrderIdGet, { orderId }));
      this.state.set('loaded');
    } catch (error) {
      this.order.set(null);
      // A 404 is an ordinary answer here ("no such order"), not a failure to report.
      this.state.set(toApiProblem(error).status === 404 ? 'notFound' : 'error');
    }
  }
}
