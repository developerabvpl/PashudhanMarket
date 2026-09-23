import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoPipe } from '@jsverse/transloco';
import { Api, ReturnRequestSummaryDto, apiV1SellerOrdersReturnsGet } from '@upbazaar/data-access';
import { DateIstPipe, InrCurrencyPipe } from '@upbazaar/util';

/**
 * Buyers asking to send the seller's parcels back. Opens on the ones waiting for an answer,
 * oldest first: each is a buyer waiting. Opening one goes to the order, where it is decided.
 */
@Component({
  selector: 'upb-seller-returns-page',
  imports: [RouterLink, TranslocoPipe, InrCurrencyPipe, DateIstPipe, MatButtonToggleModule, MatPaginatorModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-5xl px-4 py-8">
      <h1 class="text-2xl font-semibold text-ink">{{ 'returns.title' | transloco }}</h1>
      <p class="mt-1 text-sm text-ink-muted">{{ 'returns.subtitle' | transloco }}</p>

      <mat-button-toggle-group class="mt-4" [value]="status()" (change)="filter($event.value)">
        @for (s of statuses; track s) {
        <mat-button-toggle [value]="s">{{ 'returns.statuses.' + s | transloco }}</mat-button-toggle>
        }
      </mat-button-toggle-group>

      <div class="upb-card mt-4">
        @if (loading()) { <mat-progress-bar mode="indeterminate" /> }
        <ul class="divide-y divide-border">
          @for (r of requests(); track r.partId) {
          <li>
            <a class="flex flex-wrap items-center justify-between gap-3 p-4 hover:bg-surface-sunken" [routerLink]="['/orders', r.orderId]">
              <div>
                <p class="font-mono font-medium text-ink">{{ r.orderNumber }}</p>
                <p class="text-sm text-ink-muted">
                  {{ 'orders.return.reasons.' + r.reason | transloco }} ·
                  {{ 'returns.requestedOn' | transloco: { date: (r.requestedAtUtc | dateIst: 'datetime') } }}
                </p>
                @if (r.comment) { <p class="mt-1 text-sm text-ink">{{ r.comment }}</p> }
              </div>
              <p class="font-semibold text-ink">{{ r.subtotal | inr }}</p>
            </a>
          </li>
          } @empty {
          @if (!loading()) { <li class="p-8 text-center text-ink-muted">{{ 'returns.none' | transloco }}</li> }
          }
        </ul>
        <mat-paginator [length]="total()" [pageSize]="pageSize" [pageIndex]="page() - 1" [hidePageSize]="true" (page)="turn($event)" />
      </div>
    </section>
  `,
})
export class ReturnsPage {
  protected readonly statuses = ['Requested', 'Approved', 'Rejected'] as const;
  protected readonly pageSize = 25;
  protected readonly requests = signal<readonly ReturnRequestSummaryDto[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly status = signal<string>('Requested');
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
      const result = await this.api.invoke(apiV1SellerOrdersReturnsGet, { Page: this.page(), PageSize: this.pageSize, Status: this.status() });

      this.requests.set(result.items);
      this.total.set(result.totalCount);
    } catch {
      // Reported by the interceptor.
    } finally {
      this.loading.set(false);
    }
  }
}
