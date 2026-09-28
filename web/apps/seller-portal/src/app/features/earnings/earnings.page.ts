import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  Api,
  EarningDto,
  PayoutSummaryDto,
  SellerBalanceDto,
  apiV1SellerSettlementsBalanceGet,
  apiV1SellerSettlementsEarningsGet,
  apiV1SellerSettlementsPayoutsGet,
} from '@upbazaar/data-access';
import { DateIstPipe, InrCurrencyPipe } from '@upbazaar/util';

/**
 * What the seller is owed and has been paid. The balance is split the way the money actually
 * moves - waiting out the return window, held for a return, in Monday's payout, being transferred,
 * paid - so a seller asking "where is my money?" can see the answer instead of a single total.
 */
@Component({
  selector: 'upb-earnings-page',
  imports: [TranslocoPipe, DateIstPipe, InrCurrencyPipe, MatPaginatorModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-5xl space-y-6 px-4 py-8">
      <header>
        <h1 class="text-2xl font-semibold text-ink">{{ 'settlements.earningsTitle' | transloco }}</h1>
        <p class="mt-1 text-sm text-ink-muted">{{ 'settlements.earningsNote' | transloco }}</p>
      </header>

      @if (balance(); as b) {
      <dl class="grid grid-cols-2 gap-3 sm:grid-cols-5">
        @for (tile of tiles; track tile.key) {
        <div class="upb-card p-4">
          <dt class="text-xs text-ink-muted">{{ tile.label | transloco }}</dt>
          <dd class="mt-1 text-lg font-semibold text-ink">{{ b[tile.key] | inr }}</dd>
        </div>
        }
      </dl>
      }

      <div class="upb-card">
        @if (loading()) { <mat-progress-bar mode="indeterminate" /> }
        <table class="w-full text-sm">
          <thead>
            <tr class="border-b border-border text-left text-ink-muted">
              <th class="p-3 font-normal">{{ 'settlements.order' | transloco }}</th>
              <th class="p-3 font-normal">{{ 'settlements.delivered' | transloco }}</th>
              <th class="p-3 text-right font-normal">{{ 'settlements.gross' | transloco }}</th>
              <th class="p-3 text-right font-normal">{{ 'settlements.commission' | transloco }}</th>
              <th class="p-3 text-right font-normal">{{ 'settlements.youGet' | transloco }}</th>
              <th class="p-3 font-normal">{{ 'payments.status' | transloco }}</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-border">
            @for (e of earnings(); track e.id) {
            <tr>
              <td class="p-3 font-mono text-xs">
                {{ e.orderNumber }}
                @switch (e.kind) {
                @case ('Delivery') { <span class="font-sans text-ink-muted">· {{ 'settlements.kindDelivery' | transloco }}</span> }
                @case ('CourierCost') { <span class="font-sans text-ink-muted">· {{ 'settlements.courier.' + e.detail | transloco }}</span> }
                @case ('Adjustment') { <span class="font-sans text-ink-muted">· {{ 'settlements.adjustment.' + e.detail | transloco }}</span> }
                }
              </td>
              <td class="p-3">{{ e.deliveredAtUtc | dateIst }}</td>
              <td class="p-3 text-right">{{ e.grossAmount | inr }}</td>
              <td class="p-3 text-right">{{ e.commissionAmount | inr }} ({{ e.commissionPercent }}%)</td>
              <td class="p-3 text-right font-medium">{{ e.netAmount | inr }}</td>
              <td class="p-3">
                {{ 'settlements.earningStatuses.' + e.status | transloco }}
                @if (e.status === 'Accruing') {
                <span class="block text-xs text-ink-muted">{{ 'settlements.payableFrom' | transloco }} {{ e.payableFromUtc | dateIst }}</span>
                }
                @if (e.awaitingCash) {
                <span class="block text-xs text-warning">{{ 'settlements.awaitingCash' | transloco }}</span>
                }
              </td>
            </tr>
            } @empty {
            @if (!loading()) { <tr><td colspan="6" class="p-8 text-center text-ink-muted">{{ 'settlements.noEarnings' | transloco }}</td></tr> }
            }
          </tbody>
        </table>
        <mat-paginator [length]="total()" [pageSize]="pageSize" [pageIndex]="page() - 1" [hidePageSize]="true" (page)="turn($event)" />
      </div>

      @if (payouts().length > 0) {
      <div class="upb-card p-5">
        <h2 class="font-medium text-ink">{{ 'settlements.payoutsTitle' | transloco }}</h2>
        <ul class="mt-2 divide-y divide-border text-sm">
          @for (p of payouts(); track p.id) {
          <li class="flex flex-wrap items-center justify-between gap-2 py-2">
            <span>{{ p.createdAtUtc | dateIst }} · {{ 'settlements.parcels' | transloco }}: {{ p.earningCount }}</span>
            <span class="text-right">
              <span class="font-semibold">{{ p.netAmount | inr }}</span> ·
              {{ 'settlements.statuses.' + p.status | transloco }}
              @if (p.utr) { <span class="block font-mono text-xs text-ink-muted">UTR {{ p.utr }}</span> }
            </span>
          </li>
          }
        </ul>
      </div>
      }
    </section>
  `,
})
export class EarningsPage {
  protected readonly tiles: readonly { key: keyof SellerBalanceDto & `${string}Amount`; label: string }[] = [
    { key: 'accruingAmount', label: 'settlements.balanceAccruing' },
    { key: 'onHoldAmount', label: 'settlements.balanceOnHold' },
    { key: 'payableAmount', label: 'settlements.balancePayable' },
    { key: 'pendingPayoutAmount', label: 'settlements.balancePending' },
    { key: 'paidAmount', label: 'settlements.balancePaid' },
  ];

  protected readonly pageSize = 25;
  protected readonly balance = signal<SellerBalanceDto | null>(null);
  protected readonly earnings = signal<readonly EarningDto[]>([]);
  protected readonly payouts = signal<readonly PayoutSummaryDto[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly loading = signal(false);

  private readonly api = inject(Api);

  constructor() {
    void this.load();
  }

  protected turn(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    void this.loadEarnings();
  }

  private async load(): Promise<void> {
    try {
      const [balance, payouts] = await Promise.all([
        this.api.invoke(apiV1SellerSettlementsBalanceGet, {}),
        this.api.invoke(apiV1SellerSettlementsPayoutsGet, { page: 1, pageSize: 10 }),
      ]);

      this.balance.set(balance);
      this.payouts.set(payouts.items);
    } catch {
      // Reported by the interceptor.
    }

    await this.loadEarnings();
  }

  private async loadEarnings(): Promise<void> {
    this.loading.set(true);

    try {
      const result = await this.api.invoke(apiV1SellerSettlementsEarningsGet, { Page: this.page(), PageSize: this.pageSize });

      this.earnings.set(result.items);
      this.total.set(result.totalCount);
    } catch {
      // Reported by the interceptor.
    } finally {
      this.loading.set(false);
    }
  }
}
