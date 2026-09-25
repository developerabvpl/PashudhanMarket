import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoPipe } from '@jsverse/transloco';
import { HasPermissionDirective } from '@upbazaar/auth';
import {
  Api,
  PayoutDto,
  apiV1AdminSettlementsPayoutsPayoutIdMarkPaidPost,
  fieldErrorsFor,
  toApiProblem,
} from '@upbazaar/data-access';
import { FieldErrors } from '@upbazaar/ui';
import { DateIstPipe, InrCurrencyPipe } from '@upbazaar/util';
import { SettlementsPermissions } from '../../core/permissions';

/**
 * One payout in full: where the money goes, how it adds up, and the parcels it covers. For a
 * payout still to pay, the form to record the transfer's UTR once it has been sent.
 *
 * The account is shown whole here, and only here, because this is where finance copy it into
 * their bank. Resolves to the updated payout if it was recorded as paid.
 */
@Component({
  selector: 'upb-payout-dialog',
  imports: [
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    TranslocoPipe,
    DateIstPipe,
    InrCurrencyPipe,
    FieldErrors,
    HasPermissionDirective,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ payout.shopName }} · {{ payout.netAmount | inr }}</h2>

    <mat-dialog-content class="space-y-4 text-sm">
      <dl class="grid grid-cols-[10rem_1fr] gap-x-3 gap-y-1">
        <dt class="text-ink-muted">{{ 'settlements.accountHolder' | transloco }}</dt><dd>{{ payout.accountHolder }}</dd>
        <dt class="text-ink-muted">{{ 'settlements.accountNumber' | transloco }}</dt><dd class="font-mono">{{ payout.accountNumber }}</dd>
        <dt class="text-ink-muted">{{ 'settlements.ifsc' | transloco }}</dt><dd class="font-mono">{{ payout.ifsc }}</dd>
      </dl>

      <dl class="grid grid-cols-[10rem_1fr] gap-x-3 gap-y-1 border-t border-border pt-3">
        <dt class="text-ink-muted">{{ 'settlements.gross' | transloco }}</dt><dd>{{ payout.grossAmount | inr }}</dd>
        <dt class="text-ink-muted">{{ 'settlements.commission' | transloco }}</dt><dd>− {{ payout.commissionAmount | inr }}</dd>
        <dt class="text-ink-muted">{{ 'settlements.tcs' | transloco }}</dt><dd>− {{ payout.tcsAmount | inr }}</dd>
        <dt class="text-ink-muted">{{ 'settlements.tds' | transloco }}</dt><dd>− {{ payout.tdsAmount | inr }}</dd>
        @if (payout.courierCostAmount !== 0) {
        <dt class="text-ink-muted">{{ 'settlements.courierCosts' | transloco }}</dt><dd>− {{ payout.courierCostAmount | inr }}</dd>
        }
        <dt class="font-medium text-ink">{{ 'settlements.net' | transloco }}</dt><dd class="font-semibold">{{ payout.netAmount | inr }}</dd>
      </dl>

      <table class="w-full border-t border-border">
        <thead>
          <tr class="text-left text-ink-muted">
            <th class="py-1 font-normal">{{ 'settlements.order' | transloco }}</th>
            <th class="py-1 font-normal">{{ 'settlements.delivered' | transloco }}</th>
            <th class="py-1 text-right font-normal">{{ 'settlements.gross' | transloco }}</th>
            <th class="py-1 text-right font-normal">{{ 'settlements.net' | transloco }}</th>
          </tr>
        </thead>
        <tbody>
          @for (e of payout.earnings; track e.id) {
          <tr>
            <td class="py-1 font-mono text-xs">
              {{ e.orderNumber }}
              @switch (e.kind) {
              @case ('Delivery') { <span class="font-sans text-ink-muted">· {{ 'settlements.kindDelivery' | transloco }}</span> }
              @case ('CourierCost') { <span class="font-sans text-ink-muted">· {{ 'settlements.courier.' + e.detail | transloco }}</span> }
              }
            </td>
            <td class="py-1">{{ e.deliveredAtUtc | dateIst }}</td>
            <td class="py-1 text-right">{{ e.grossAmount | inr }}</td>
            <td class="py-1 text-right">{{ e.netAmount | inr }}</td>
          </tr>
          }
        </tbody>
      </table>

      @if (payout.status === 'Paid') {
      <p class="border-t border-border pt-3">
        {{ 'settlements.paidWith' | transloco: { date: (payout.paidAtUtc | dateIst: 'datetime'), utr: payout.utr } }}
      </p>
      } @else {
      <form *hasPermission="payoutsApprove" id="mark-paid" class="border-t border-border pt-3" (submit)="save($event)">
        <p>
          {{ 'settlements.markPaidBody' | transloco: {
            amount: (payout.netAmount | inr), holder: payout.accountHolder, account: payout.accountNumber, ifsc: payout.ifsc
          } }}
        </p>
        <mat-form-field class="mt-3 w-full">
          <mat-label>{{ 'settlements.utr' | transloco }}</mat-label>
          <input matInput name="utr" autocomplete="off" maxlength="32" required [value]="utr()" (input)="utr.set(value($event))" />
        </mat-form-field>
        <upb-field-errors fieldId="utr" [errors]="errors()" />
        <p class="mt-2 text-ink-muted">{{ 'staff.auditNote' | transloco }}</p>
      </form>
      }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button type="button" [mat-dialog-close]="undefined">{{ 'common.close' | transloco }}</button>
      @if (payout.status === 'Pending') {
      <button *hasPermission="payoutsApprove" mat-flat-button color="primary" type="submit" form="mark-paid"
        [disabled]="busy() || !utr().trim()">
        {{ 'settlements.markPaid' | transloco }}
      </button>
      }
    </mat-dialog-actions>
  `,
})
export class PayoutDialog {
  protected readonly payout = inject<PayoutDto>(MAT_DIALOG_DATA);
  protected readonly payoutsApprove = SettlementsPermissions.PayoutsApprove;

  protected readonly utr = signal('');
  protected readonly busy = signal(false);
  protected readonly errors = signal<readonly string[]>([]);

  private readonly reference = inject<MatDialogRef<PayoutDialog, PayoutDto>>(MatDialogRef);
  private readonly api = inject(Api);

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected async save(event: Event): Promise<void> {
    event.preventDefault();
    this.busy.set(true);
    this.errors.set([]);

    try {
      const updated = await this.api.invoke(apiV1AdminSettlementsPayoutsPayoutIdMarkPaidPost, {
        payoutId: this.payout.id,
        body: { utr: this.utr().trim() },
      });

      this.reference.close(updated);
    } catch (error) {
      const problem = toApiProblem(error);
      const fieldErrors = fieldErrorsFor(problem, 'utr');

      this.errors.set(fieldErrors.length > 0 ? fieldErrors : [problem.title]);
    } finally {
      this.busy.set(false);
    }
  }
}
