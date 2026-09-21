import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { Api, OrderDto, apiV1AdminOrdersOrderIdGet, toApiProblem } from '@upbazaar/data-access';
import { PageState } from '@upbazaar/ui';
import { DateIstPipe, InrCurrencyPipe } from '@upbazaar/util';

type LookupState = 'idle' | 'loading' | 'loaded' | 'notFound' | 'error';

/**
 * Support screen: look an order up by its public id and show what the customer sees plus the
 * payment reference, which is what a refund conversation actually needs.
 */
@Component({
  selector: 'upb-order-lookup',
  imports: [TranslocoPipe, InrCurrencyPipe, DateIstPipe, PageState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-3xl px-4 py-8">
      <h1 class="text-2xl font-semibold text-ink">{{ 'admin.orderLookup' | transloco }}</h1>

      <form class="mt-4 flex gap-2" (submit)="find($event)">
        <label class="flex-1">
          <span class="upb-sr-only">{{ 'admin.orderId' | transloco }}</span>
          <input
            name="orderId"
            type="text"
            required
            class="w-full rounded-control border border-border bg-surface px-3 py-2 font-mono text-sm text-ink"
            placeholder="00000000-0000-0000-0000-000000000000"
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
      } @case ('notFound') {
      <upb-page-state state="empty" message="admin.notFound" />
      } @case ('error') {
      <upb-page-state state="error" (retry)="retry()" />
      } @case ('loaded') { @if (order(); as found) {
      <article class="upb-card mt-6 p-6">
        <header class="flex flex-wrap items-baseline justify-between gap-2">
          <h2 class="font-mono text-lg font-semibold text-ink">{{ found.number }}</h2>
          <span class="rounded-control bg-surface-sunken px-2 py-1 text-sm text-ink-muted">
            {{ 'admin.status' | transloco }}: {{ found.status }}
          </span>
        </header>

        <dl class="mt-4 grid grid-cols-2 gap-3 text-sm">
          <dt class="text-ink-muted">{{ 'admin.placedAt' | transloco }}</dt>
          <dd class="text-ink">{{ found.placedAtUtc | dateIst: 'datetime' }}</dd>

          <dt class="text-ink-muted">{{ 'admin.total' | transloco }}</dt>
          <dd class="font-semibold text-ink">{{ found.total | inr }}</dd>

          <dt class="text-ink-muted">{{ 'admin.payment' | transloco }}</dt>
          <dd class="font-mono text-xs text-ink">{{ found.paymentReference ?? '-' }}</dd>
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
      </article>
      } } }
    </section>
  `,
})
export class OrderLookup {
  private readonly api = inject(Api);
  private readonly lastQueried = signal<string | null>(null);

  protected readonly state = signal<LookupState>('idle');
  protected readonly order = signal<OrderDto | null>(null);

  protected readonly hasResult = computed(() => this.order() !== null);

  /** Every line across the sellers' parts: support reads an order as one list, not per parcel. */
  protected readonly lines = computed(() => this.order()?.parts.flatMap((part) => part.lines) ?? []);

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

  private async load(orderId: string): Promise<void> {
    this.lastQueried.set(orderId);
    this.state.set('loading');

    try {
      this.order.set(await this.api.invoke(apiV1AdminOrdersOrderIdGet, { orderId }));
      this.state.set('loaded');
    } catch (error) {
      this.order.set(null);
      // A 404 is an ordinary answer here ("no such order"), not a failure to report.
      this.state.set(toApiProblem(error).status === 404 ? 'notFound' : 'error');
    }
  }
}
