import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { Api, OrderSummaryDto, apiV1OrdersGet } from '@upbazaar/data-access';
import { PageState } from '@upbazaar/ui';
import { DateIstPipe, InrCurrencyPipe } from '@upbazaar/util';
import { SeoService } from '../../core/seo.service';
import { orderAmount } from './order-amount';
import { orderBadge } from './order-labels';

const PAGE_SIZE = 20;

/**
 * The buyer's orders, newest first. "Show more" appends the next page rather than paging, since
 * a buyer looking for last month's order scrolls; they do not jump to page 4.
 */
@Component({
  selector: 'upb-orders-page',
  imports: [RouterLink, TranslocoPipe, InrCurrencyPipe, DateIstPipe, PageState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-4xl px-4 py-8 sm:py-12">
      <h1 class="text-2xl font-bold tracking-tight text-ink sm:text-3xl">
        {{ 'orders.title' | transloco }}
      </h1>

      <div class="mt-6">
        @if (failed() && orders().length === 0) {
        <upb-page-state state="error" (retry)="load(1)" />
        } @else if (loaded() && orders().length === 0) {
        <upb-page-state state="empty" message="orders.none" />
        } @else if (!loaded()) {
        <upb-page-state state="loading" />
        } @else {
        <ul class="divide-y divide-border rounded-card border border-border bg-surface">
          @for (order of orders(); track order.id) {
          <li>
            <a
              class="flex flex-wrap items-center justify-between gap-3 p-4 transition-colors hover:bg-surface-sunken"
              [routerLink]="['/orders', order.id]"
            >
              <div class="min-w-0">
                <p class="font-medium text-ink">{{ order.number }}</p>
                <p class="mt-0.5 text-sm text-ink-muted">
                  {{ order.placedAtUtc | dateIst }} ·
                  {{ (order.itemCount === 1 ? 'cart.itemCount.one' : 'cart.itemCount.other') | transloco: { count: order.itemCount } }}
                </p>
              </div>
              <div class="flex items-center gap-3">
                @let status = badge(order.status, order.partStatuses, order.partReturns);
                <span class="rounded-full px-2.5 py-0.5 text-xs font-medium" [class]="status.tone">
                  {{ status.key | transloco }}
                </span>
                @let money = amount(order);
                <span class="text-right">
                  <span class="block font-semibold text-ink">{{ money.amount | inr: 'symbol' : 'auto' }}</span>
                  @if (money.refund !== null) {
                  <span class="block text-xs font-medium text-success">{{ 'orders.refundOf' | transloco: { amount: (money.refund | inr: 'symbol' : 'auto') } }}</span>
                  }
                </span>
              </div>
            </a>
          </li>
          }
        </ul>

        @if (hasMore()) {
        <div class="mt-4 text-center">
          <button
            type="button"
            class="rounded-control border border-border px-4 py-2 text-sm text-ink transition-colors hover:bg-surface-sunken disabled:opacity-50"
            [disabled]="busy()"
            (click)="load(page() + 1)"
          >
            {{ 'orders.showMore' | transloco }}
          </button>
        </div>
        } }
      </div>
    </section>
  `,
})
export class OrdersPage {
  protected readonly orders = signal<readonly OrderSummaryDto[]>([]);
  protected readonly page = signal(0);
  protected readonly hasMore = signal(false);
  protected readonly loaded = signal(false);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);
  /**
   * How far each order has got, as on the order's own page: Shipped once a parcel has left its
   * seller, and "Delivered - partly returned" or Returned once the buyer has sent some of it back.
   */
  protected readonly badge = orderBadge;
  /** What was paid, with the refund beneath, once anything paid online is going back. */
  protected readonly amount = orderAmount;

  private readonly api = inject(Api);

  constructor() {
    inject(SeoService).apply({
      title: 'My orders',
      description: 'Orders you have placed on UP Bazaar.',
      canonicalPath: '/orders',
      noIndex: true,
    });

    void this.load(1);
  }

  protected async load(page: number): Promise<void> {
    this.busy.set(true);
    this.failed.set(false);

    try {
      const result = await this.api.invoke(apiV1OrdersGet, { page, pageSize: PAGE_SIZE });

      this.orders.update((current) => (page === 1 ? result.items : [...current, ...result.items]));
      this.page.set(page);
      this.hasMore.set(result.hasNextPage ?? false);
      this.loaded.set(true);
    } catch {
      this.failed.set(true);
    } finally {
      this.busy.set(false);
    }
  }
}
