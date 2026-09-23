import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTableModule } from '@angular/material/table';
import { TranslocoPipe } from '@jsverse/transloco';
import { Api, ReturnRequestSummaryDto, apiV1AdminOrdersReturnsGet } from '@upbazaar/data-access';
import { DateIstPipe, InrCurrencyPipe } from '@upbazaar/util';

/**
 * Buyers' return requests across every seller. Opens on those still waiting for a decision,
 * oldest first, so staff can step in for a seller who has not answered. Each row opens the order,
 * where the request is decided and the return pickup followed.
 */
@Component({
  selector: 'upb-returns-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    DateIstPipe,
    InrCurrencyPipe,
    MatButtonToggleModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatTableModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-6xl px-4 py-8">
      <header class="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 class="text-2xl font-semibold text-ink">{{ 'returns.title' | transloco }}</h1>
          <p class="mt-1 text-sm text-ink-muted">{{ 'returns.subtitle' | transloco }}</p>
        </div>

        <mat-button-toggle-group [value]="status()" (change)="filter($event.value)" [attr.aria-label]="'returns.title' | transloco">
          @for (s of statuses; track s) {
          <mat-button-toggle [value]="s">{{ 'returns.statuses.' + s | transloco }}</mat-button-toggle>
          }
          <mat-button-toggle value="">{{ 'returns.all' | transloco }}</mat-button-toggle>
        </mat-button-toggle-group>
      </header>

      <div class="upb-card mt-4 overflow-x-auto">
        @if (loading()) {
        <mat-progress-bar mode="indeterminate" />
        }

        <table mat-table [dataSource]="requests()" class="w-full">
          <ng-container matColumnDef="requestedAtUtc">
            <th mat-header-cell *matHeaderCellDef>{{ 'returns.requestedAt' | transloco }}</th>
            <td mat-cell *matCellDef="let row">{{ row.requestedAtUtc | dateIst: 'datetime' }}</td>
          </ng-container>
          <ng-container matColumnDef="order">
            <th mat-header-cell *matHeaderCellDef>{{ 'returns.order' | transloco }}</th>
            <td mat-cell *matCellDef="let row">
              <a class="font-mono text-sm" routerLink="/orders" [queryParams]="{ orderId: row.orderId }">{{ row.orderNumber }}</a>
            </td>
          </ng-container>
          <ng-container matColumnDef="reason">
            <th mat-header-cell *matHeaderCellDef>{{ 'returns.reason' | transloco }}</th>
            <td mat-cell *matCellDef="let row" class="text-sm">
              {{ 'orders.return.reasons.' + row.reason | transloco }}
              @if (row.comment) { <span class="block text-ink-muted">{{ row.comment }}</span> }
            </td>
          </ng-container>
          <ng-container matColumnDef="value">
            <th mat-header-cell *matHeaderCellDef>{{ 'returns.value' | transloco }}</th>
            <td mat-cell *matCellDef="let row">
              {{ row.subtotal | inr }}
              <span class="block text-xs text-ink-muted">{{ 'orders.paymentMethod.' + row.paymentMethod | transloco }}</span>
            </td>
          </ng-container>
          <ng-container matColumnDef="status">
            <th mat-header-cell *matHeaderCellDef>{{ 'payments.status' | transloco }}</th>
            <td mat-cell *matCellDef="let row" class="text-sm">
              {{ 'returns.statuses.' + row.status | transloco }}
              <span class="block text-xs text-ink-muted">{{ 'orders.partStatus.' + row.partStatus | transloco }}</span>
            </td>
          </ng-container>

          <tr mat-header-row *matHeaderRowDef="columns"></tr>
          <tr mat-row *matRowDef="let row; columns: columns"></tr>
        </table>

        @if (!loading() && requests().length === 0) {
        <p class="p-8 text-center text-ink-muted">{{ 'returns.none' | transloco }}</p>
        }

        <mat-paginator
          [length]="totalCount()"
          [pageSize]="pageSize()"
          [pageIndex]="page() - 1"
          [pageSizeOptions]="[25, 50, 100]"
          (page)="changePage($event)"
        />
      </div>
    </section>
  `,
})
export class ReturnsPage {
  private readonly api = inject(Api);

  protected readonly statuses = ['Requested', 'Approved', 'Rejected'] as const;
  protected readonly columns = ['requestedAtUtc', 'order', 'reason', 'value', 'status'];

  protected readonly requests = signal<readonly ReturnRequestSummaryDto[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(25);
  protected readonly status = signal('Requested');
  protected readonly loading = signal(false);

  constructor() {
    void this.load();
  }

  protected filter(status: string): void {
    this.status.set(status);
    this.page.set(1);

    void this.load();
  }

  protected changePage(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    this.pageSize.set(event.pageSize);

    void this.load();
  }

  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      const result = await this.api.invoke(apiV1AdminOrdersReturnsGet, {
        Page: this.page(),
        PageSize: this.pageSize(),
        Status: this.status() || undefined,
      });

      this.requests.set(result.items);
      this.totalCount.set(result.totalCount);
    } catch {
      // The global interceptor has already raised a toast; leave the table as it was.
    } finally {
      this.loading.set(false);
    }
  }
}
