import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoPipe } from '@jsverse/transloco';
import { Api, SellerOrderSummaryDto, apiV1SellerOrdersGet } from '@upbazaar/data-access';
import { DateIstPipe, InrCurrencyPipe } from '@upbazaar/util';

/**
 * The seller's order queue. Opens on what to pack, oldest first, because that is the work; the
 * other filters are for looking something up.
 */
@Component({
  selector: 'upb-seller-orders-page',
  imports: [RouterLink, TranslocoPipe, InrCurrencyPipe, DateIstPipe, MatButtonToggleModule, MatPaginatorModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-5xl px-4 py-8">
      <h1 class="text-2xl font-semibold text-ink">{{ 'sellerPortal.ordersTitle' | transloco }}</h1>

      <mat-button-toggle-group class="mt-4" [value]="status()" (change)="filter($event.value)">
        @for (s of statuses; track s) {
        <mat-button-toggle [value]="s">{{ 'orders.partStatus.' + s | transloco }}</mat-button-toggle>
        }
      </mat-button-toggle-group>

      <div class="upb-card mt-4">
        @if (loading()) { <mat-progress-bar mode="indeterminate" /> }
        <ul class="divide-y divide-border">
          @for (order of orders(); track order.partId) {
          <li>
            <a class="flex flex-wrap items-center justify-between gap-3 p-4 hover:bg-surface-sunken" [routerLink]="['/orders', order.orderId]">
              <div>
                <p class="font-mono font-medium text-ink">{{ order.orderNumber }}</p>
                <p class="text-sm text-ink-muted">
                  {{ order.placedAtUtc | dateIst: 'datetime' }} · {{ order.city }} ·
                  {{ 'cart.itemCount' | transloco: { count: order.itemCount } }}
                </p>
                @if (order.returnRequestStatus === 'Requested') {
                <p class="mt-1 inline-block rounded-full bg-warning/15 px-2 py-0.5 text-xs font-medium text-ink">{{ 'returns.requestTitle' | transloco }}</p>
                }
              </div>
              <div class="text-right text-sm">
                <p class="font-semibold text-ink">{{ order.subtotal | inr }}</p>
                @if (order.codAmount > 0) {
                <p class="text-ink-muted">{{ 'sellerPortal.codCollect' | transloco: { amount: (order.codAmount | inr) } }}</p>
                }
              </div>
            </a>
          </li>
          } @empty {
          @if (!loading()) { <li class="p-8 text-center text-ink-muted">{{ 'sellerPortal.noOrders' | transloco }}</li> }
          }
        </ul>
        <mat-paginator [length]="total()" [pageSize]="pageSize" [pageIndex]="page() - 1" [hidePageSize]="true" (page)="turn($event)" />
      </div>
    </section>
  `,
})
export class OrdersPage {
  protected readonly statuses = ['Confirmed', 'Packed', 'Shipped', 'Delivered', 'Returned', 'Cancelled'] as const;
  protected readonly pageSize = 25;
  protected readonly orders = signal<readonly SellerOrderSummaryDto[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly status = signal<string>('Confirmed');
  protected readonly loading = signal(false);

  private readonly api = inject(Api);

  constructor() {
    void this.load();
  }

  protected filter(status: string): void {
    this.status.set(status);
    this.page.set(1);
    void this.load();
  }

  protected turn(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    void this.load();
  }

  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      const result = await this.api.invoke(apiV1SellerOrdersGet, { Page: this.page(), PageSize: this.pageSize, Status: this.status() });

      this.orders.set(result.items);
      this.total.set(result.totalCount);
    } catch {
      // Reported by the interceptor.
    } finally {
      this.loading.set(false);
    }
  }
}
