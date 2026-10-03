import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTableModule } from '@angular/material/table';
import { TranslocoPipe } from '@jsverse/transloco';
import { HasPermissionDirective } from '@upbazaar/auth';
import {
  Api,
  PaymentDto,
  RefundDto,
  apiV1AdminPaymentsGet,
  apiV1AdminPaymentsRefundsGet,
} from '@upbazaar/data-access';
import { ToastService } from '@upbazaar/ui';
import { DateIstPipe, InrCurrencyPipe } from '@upbazaar/util';
import { PaymentsPermissions } from '../../core/permissions';
import { MarkRefundedDialog } from './mark-refunded.dialog';
import { refundReasonKey } from './refund-reason';

type View = 'refunds' | 'payments';

/**
 * Payments and the refunds owed out of them, for support and finance.
 *
 * Opens on refunds due, oldest first, because that list is work to do: each row is a buyer
 * waiting for money. Payments is the reference view, searchable by the three things a support
 * call or a Razorpay dashboard row gives you - order number, Razorpay order id, Razorpay payment id.
 */
@Component({
  selector: 'upb-payments-page',
  imports: [
    TranslocoPipe,
    DateIstPipe,
    InrCurrencyPipe,
    HasPermissionDirective,
    MatButtonModule,
    MatButtonToggleModule,
    MatFormFieldModule,
    MatInputModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatTableModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-6xl px-4 py-8">
      <header class="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 class="text-2xl font-semibold text-ink">{{ 'payments.title' | transloco }}</h1>
          <p class="mt-1 text-sm text-ink-muted">{{ 'payments.subtitle' | transloco }}</p>
        </div>

        <mat-button-toggle-group [value]="view()" (change)="show($event.value)" [attr.aria-label]="'payments.title' | transloco">
          <mat-button-toggle value="refunds">{{ 'payments.refundsDue' | transloco }}</mat-button-toggle>
          <mat-button-toggle value="payments">{{ 'payments.allPayments' | transloco }}</mat-button-toggle>
        </mat-button-toggle-group>
      </header>

      @if (view() === 'payments') {
      <form class="mt-6 flex flex-wrap items-center gap-3" (submit)="applySearch($event)">
        <mat-form-field class="flex-1" subscriptSizing="dynamic">
          <mat-label>{{ 'payments.searchLabel' | transloco }}</mat-label>
          <input matInput name="search" type="search" [value]="search()" />
        </mat-form-field>
        <button mat-stroked-button type="submit">{{ 'common.search' | transloco }}</button>
      </form>
      }

      <div class="upb-card mt-4 overflow-x-auto">
        @if (loading()) {
        <mat-progress-bar mode="indeterminate" />
        }

        @if (view() === 'refunds') {
        <table mat-table [dataSource]="refunds()" class="w-full">
          <ng-container matColumnDef="createdAtUtc">
            <th mat-header-cell *matHeaderCellDef>{{ 'payments.owedSince' | transloco }}</th>
            <td mat-cell *matCellDef="let row">{{ row.createdAtUtc | dateIst: 'datetime' }}</td>
          </ng-container>
          <ng-container matColumnDef="orderNumber">
            <th mat-header-cell *matHeaderCellDef>{{ 'payments.order' | transloco }}</th>
            <td mat-cell *matCellDef="let row" class="font-mono text-sm">{{ row.orderNumber }}</td>
          </ng-container>
          <ng-container matColumnDef="amount">
            <th mat-header-cell *matHeaderCellDef>{{ 'payments.amount' | transloco }}</th>
            <td mat-cell *matCellDef="let row" class="font-semibold">{{ row.amount | inr }}</td>
          </ng-container>
          <ng-container matColumnDef="reason">
            <th mat-header-cell *matHeaderCellDef>{{ 'payments.reason' | transloco }}</th>
            <!-- The label is translated from the code; the sentence stored with it is the audit record, kept on hover. -->
            <td mat-cell *matCellDef="let row" class="text-sm" [attr.title]="row.reason">
              @if (reasonKey(row); as key) {
              {{ key | transloco }}
              } @else {
              {{ row.reason }}
              }
            </td>
          </ng-container>
          <ng-container matColumnDef="gatewayPaymentId">
            <th mat-header-cell *matHeaderCellDef>{{ 'payments.refundTo' | transloco }}</th>
            <!--
              Where the money goes, in words: a UPI refund names the buyer's UPI id; any other goes back
              through the payment it came from, whose gateway id is the detail finance looks up, not the answer.
            -->
            <td mat-cell *matCellDef="let row" class="text-sm">
              @if (row.method === 'Upi') {
              <span class="font-mono text-xs">{{ 'payments.upiTo' | transloco: { upi: row.upiId } }}</span>
              } @else {
              {{ 'payments.originalPayment' | transloco }}
              @if (row.gatewayPaymentId) {
              <span class="block font-mono text-xs text-ink-muted">{{ row.gatewayPaymentId }}</span>
              }
              }
            </td>
          </ng-container>
          <ng-container matColumnDef="actions">
            <th mat-header-cell *matHeaderCellDef>{{ 'common.actions' | transloco }}</th>
            <td mat-cell *matCellDef="let row">
              <button *hasPermission="refundsWrite" mat-button type="button" (click)="markRefunded(row)">
                {{ 'payments.markRefunded' | transloco }}
              </button>
            </td>
          </ng-container>

          <tr mat-header-row *matHeaderRowDef="refundColumns"></tr>
          <tr mat-row *matRowDef="let row; columns: refundColumns"></tr>
        </table>

        @if (!loading() && refunds().length === 0) {
        <p class="p-8 text-center text-ink-muted">{{ 'payments.noRefundsDue' | transloco }}</p>
        }
        } @else {
        <table mat-table [dataSource]="payments()" class="w-full">
          <ng-container matColumnDef="createdAtUtc">
            <th mat-header-cell *matHeaderCellDef>{{ 'payments.started' | transloco }}</th>
            <td mat-cell *matCellDef="let row">{{ row.createdAtUtc | dateIst: 'datetime' }}</td>
          </ng-container>
          <ng-container matColumnDef="orderNumber">
            <th mat-header-cell *matHeaderCellDef>{{ 'payments.order' | transloco }}</th>
            <td mat-cell *matCellDef="let row" class="font-mono text-sm">{{ row.orderNumber }}</td>
          </ng-container>
          <ng-container matColumnDef="amount">
            <th mat-header-cell *matHeaderCellDef>{{ 'payments.amount' | transloco }}</th>
            <td mat-cell *matCellDef="let row" class="font-semibold">{{ row.amount | inr }}</td>
          </ng-container>
          <ng-container matColumnDef="status">
            <th mat-header-cell *matHeaderCellDef>{{ 'payments.status' | transloco }}</th>
            <td mat-cell *matCellDef="let row">
              {{ 'payments.statuses.' + row.status | transloco }}
              @if (row.status === 'Paid') {
              <span class="block text-xs text-ink-muted">{{ 'payments.outcomes.' + row.orderOutcome | transloco }}</span>
              } @else if (row.lastFailure) {
              <span class="block text-xs text-danger">{{ row.lastFailure }}</span>
              }
            </td>
          </ng-container>
          <ng-container matColumnDef="gateway">
            <th mat-header-cell *matHeaderCellDef>{{ 'payments.razorpayIds' | transloco }}</th>
            <td mat-cell *matCellDef="let row" class="font-mono text-xs">
              {{ row.gatewayOrderId }}
              <span class="block">{{ row.gatewayPaymentId ?? '—' }}</span>
            </td>
          </ng-container>
          <ng-container matColumnDef="refundDue">
            <th mat-header-cell *matHeaderCellDef>{{ 'payments.refundDueColumn' | transloco }}</th>
            <td mat-cell *matCellDef="let row" [class.text-danger]="row.refundDue > 0">
              {{ row.refundDue > 0 ? (row.refundDue | inr) : '—' }}
            </td>
          </ng-container>

          <tr mat-header-row *matHeaderRowDef="paymentColumns"></tr>
          <tr mat-row *matRowDef="let row; columns: paymentColumns"></tr>
        </table>

        @if (!loading() && payments().length === 0) {
        <p class="p-8 text-center text-ink-muted">{{ 'payments.none' | transloco }}</p>
        }
        }

        <mat-paginator
          [length]="totalCount()"
          [pageSize]="pageSize()"
          [pageIndex]="page() - 1"
          [pageSizeOptions]="[10, 25, 50]"
          (page)="changePage($event)"
        />
      </div>
    </section>
  `,
})
export class PaymentsPage {
  private readonly api = inject(Api);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);

  protected readonly refundsWrite = PaymentsPermissions.RefundsWrite;
  protected readonly reasonKey = refundReasonKey;
  protected readonly refundColumns = ['createdAtUtc', 'orderNumber', 'amount', 'reason', 'gatewayPaymentId', 'actions'];
  protected readonly paymentColumns = ['createdAtUtc', 'orderNumber', 'amount', 'status', 'gateway', 'refundDue'];

  protected readonly view = signal<View>('refunds');
  protected readonly refunds = signal<readonly RefundDto[]>([]);
  protected readonly payments = signal<readonly PaymentDto[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(25);
  protected readonly search = signal('');
  protected readonly loading = signal(false);

  constructor() {
    void this.load();
  }

  protected show(view: View): void {
    this.view.set(view);
    this.page.set(1);

    void this.load();
  }

  protected applySearch(event: Event): void {
    event.preventDefault();

    this.search.set(new FormData(event.target as HTMLFormElement).get('search')?.toString().trim() ?? '');
    this.page.set(1);

    void this.load();
  }

  protected changePage(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    this.pageSize.set(event.pageSize);

    void this.load();
  }

  protected async markRefunded(refund: RefundDto): Promise<void> {
    const reference = this.dialog.open<MarkRefundedDialog, RefundDto, RefundDto>(MarkRefundedDialog, {
      data: refund,
      width: '32rem',
    });

    const updated = await new Promise<RefundDto | undefined>((resolve) =>
      reference.afterClosed().subscribe((result) => resolve(result))
    );

    if (updated) {
      this.toast.success('payments.refundRecorded');

      await this.load();
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      if (this.view() === 'refunds') {
        const result = await this.api.invoke(apiV1AdminPaymentsRefundsGet, {
          Page: this.page(),
          PageSize: this.pageSize(),
          Status: 'Due',
        });

        this.refunds.set(result.items);
        this.totalCount.set(result.totalCount);
      } else {
        const result = await this.api.invoke(apiV1AdminPaymentsGet, {
          Page: this.page(),
          PageSize: this.pageSize(),
          Search: this.search() || undefined,
        });

        this.payments.set(result.items);
        this.totalCount.set(result.totalCount);
      }
    } catch {
      // The global interceptor has already raised a toast; leave the table as it was.
    } finally {
      this.loading.set(false);
    }
  }
}
