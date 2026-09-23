import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialog } from '@angular/material/dialog';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTableModule } from '@angular/material/table';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { HasPermissionDirective } from '@upbazaar/auth';
import {
  Api,
  PayoutDto,
  PayoutSummaryDto,
  apiV1AdminSettlementsPayoutRunsPost,
  apiV1AdminSettlementsPayoutsGet,
  apiV1AdminSettlementsPayoutsPayoutIdGet,
} from '@upbazaar/data-access';
import { ToastService } from '@upbazaar/ui';
import { DateIstPipe, InrCurrencyPipe } from '@upbazaar/util';
import { SettlementsPermissions } from '../../core/permissions';
import { PayoutDialog } from './payout.dialog';

/**
 * Transfers owed to sellers, for finance. Opens on those still to pay, oldest first, because each
 * is a seller waiting for money. The weekly run makes them on Monday morning; "Run payouts now"
 * does the same at any time and finds nothing new if run twice.
 */
@Component({
  selector: 'upb-payouts-page',
  imports: [
    TranslocoPipe,
    DateIstPipe,
    InrCurrencyPipe,
    HasPermissionDirective,
    MatButtonModule,
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
          <h1 class="text-2xl font-semibold text-ink">{{ 'settlements.payoutsTitle' | transloco }}</h1>
          <p class="mt-1 text-sm text-ink-muted">{{ 'settlements.payoutsSubtitle' | transloco }}</p>
        </div>
        <div class="flex flex-wrap items-center gap-3">
          <button *hasPermission="payoutsApprove" mat-stroked-button type="button" [disabled]="running()" (click)="runNow()">
            {{ 'settlements.runNow' | transloco }}
          </button>
          <mat-button-toggle-group [value]="status()" (change)="filter($event.value)" [attr.aria-label]="'settlements.payoutsTitle' | transloco">
            <mat-button-toggle value="Pending">{{ 'settlements.statuses.Pending' | transloco }}</mat-button-toggle>
            <mat-button-toggle value="Paid">{{ 'settlements.statuses.Paid' | transloco }}</mat-button-toggle>
            <mat-button-toggle value="">{{ 'settlements.all' | transloco }}</mat-button-toggle>
          </mat-button-toggle-group>
        </div>
      </header>

      <div class="upb-card mt-4 overflow-x-auto">
        @if (loading()) {
        <mat-progress-bar mode="indeterminate" />
        }

        <table mat-table [dataSource]="payouts()" class="w-full">
          <ng-container matColumnDef="createdAtUtc">
            <th mat-header-cell *matHeaderCellDef>{{ 'settlements.made' | transloco }}</th>
            <td mat-cell *matCellDef="let row">{{ row.createdAtUtc | dateIst }}</td>
          </ng-container>
          <ng-container matColumnDef="seller">
            <th mat-header-cell *matHeaderCellDef>{{ 'settlements.seller' | transloco }}</th>
            <td mat-cell *matCellDef="let row">{{ row.shopName }}</td>
          </ng-container>
          <ng-container matColumnDef="parcels">
            <th mat-header-cell *matHeaderCellDef>{{ 'settlements.parcels' | transloco }}</th>
            <td mat-cell *matCellDef="let row">{{ row.earningCount }}</td>
          </ng-container>
          <ng-container matColumnDef="gross">
            <th mat-header-cell *matHeaderCellDef>{{ 'settlements.gross' | transloco }}</th>
            <td mat-cell *matCellDef="let row">{{ row.grossAmount | inr }}</td>
          </ng-container>
          <ng-container matColumnDef="net">
            <th mat-header-cell *matHeaderCellDef>{{ 'settlements.net' | transloco }}</th>
            <td mat-cell *matCellDef="let row" class="font-semibold">{{ row.netAmount | inr }}</td>
          </ng-container>
          <ng-container matColumnDef="status">
            <th mat-header-cell *matHeaderCellDef>{{ 'payments.status' | transloco }}</th>
            <td mat-cell *matCellDef="let row" class="text-sm">
              {{ 'settlements.statuses.' + row.status | transloco }}
              @if (row.utr) { <span class="block font-mono text-xs text-ink-muted">{{ row.utr }}</span> }
            </td>
          </ng-container>
          <ng-container matColumnDef="actions">
            <th mat-header-cell *matHeaderCellDef>{{ 'common.actions' | transloco }}</th>
            <td mat-cell *matCellDef="let row">
              <button mat-button type="button" (click)="open(row)">{{ 'settlements.open' | transloco }}</button>
            </td>
          </ng-container>

          <tr mat-header-row *matHeaderRowDef="columns"></tr>
          <tr mat-row *matRowDef="let row; columns: columns"></tr>
        </table>

        @if (!loading() && payouts().length === 0) {
        <p class="p-8 text-center text-ink-muted">{{ 'settlements.none' | transloco }}</p>
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
export class PayoutsPage {
  private readonly api = inject(Api);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);
  private readonly transloco = inject(TranslocoService);

  protected readonly payoutsApprove = SettlementsPermissions.PayoutsApprove;
  protected readonly columns = ['createdAtUtc', 'seller', 'parcels', 'gross', 'net', 'status', 'actions'];

  protected readonly payouts = signal<readonly PayoutSummaryDto[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(25);
  protected readonly status = signal('Pending');
  protected readonly loading = signal(false);
  protected readonly running = signal(false);

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

  protected async runNow(): Promise<void> {
    this.running.set(true);

    try {
      const result = await this.api.invoke(apiV1AdminSettlementsPayoutRunsPost, {});

      if (result.payoutsCreated > 0) {
        this.toast.success(
          this.transloco.translate('settlements.runResult', { payouts: result.payoutsCreated, earnings: result.earningsSettled })
        );
      } else {
        this.toast.info('settlements.runNothing');
      }

      await this.load();
    } catch {
      // Reported by the interceptor.
    } finally {
      this.running.set(false);
    }
  }

  protected async open(summary: PayoutSummaryDto): Promise<void> {
    let payout: PayoutDto;

    try {
      payout = await this.api.invoke(apiV1AdminSettlementsPayoutsPayoutIdGet, { payoutId: summary.id });
    } catch {
      return;
    }

    const reference = this.dialog.open<PayoutDialog, PayoutDto, PayoutDto>(PayoutDialog, { data: payout, width: '40rem' });
    const updated = await new Promise<PayoutDto | undefined>((resolve) => reference.afterClosed().subscribe((result) => resolve(result)));

    if (updated) {
      this.toast.success('settlements.recorded');

      await this.load();
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      const result = await this.api.invoke(apiV1AdminSettlementsPayoutsGet, {
        Page: this.page(),
        PageSize: this.pageSize(),
        Status: this.status() || undefined,
      });

      this.payouts.set(result.items);
      this.totalCount.set(result.totalCount);
    } catch {
      // The global interceptor has already raised a toast; leave the table as it was.
    } finally {
      this.loading.set(false);
    }
  }
}
