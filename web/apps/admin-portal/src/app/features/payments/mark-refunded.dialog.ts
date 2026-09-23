import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  Api,
  RefundDto,
  apiV1AdminPaymentsRefundsRefundIdMarkRefundedPost,
  fieldErrorsFor,
  toApiProblem,
} from '@upbazaar/data-access';
import { FieldErrors } from '@upbazaar/ui';
import { InrCurrencyPipe } from '@upbazaar/util';

/**
 * Records a refund as made, after it has been made in the Razorpay dashboard - or, for cash paid
 * at the door, sent to the buyer's UPI id, when the proof is the UPI transaction reference (UTR).
 *
 * Asks for Razorpay's refund id rather than a tick box, because the id is what lets anyone later
 * find the money: "marked refunded by someone" is a claim, "rfnd_Nk3..." is evidence.
 * Resolves to the updated refund, or undefined if cancelled.
 */
@Component({
  selector: 'upb-mark-refunded-dialog',
  imports: [MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, TranslocoPipe, InrCurrencyPipe, FieldErrors],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ 'payments.markRefundedTitle' | transloco }}</h2>

    <form (submit)="save($event)">
      <mat-dialog-content>
        <p>
          @if (isUpi) {
          {{ 'payments.markRefundedUpiBody' | transloco: {
            amount: (refund.amount | inr), number: refund.orderNumber, upi: refund.upiId ?? '—'
          } }}
          } @else {
          {{ 'payments.markRefundedBody' | transloco: {
            amount: (refund.amount | inr), number: refund.orderNumber, payment: refund.gatewayPaymentId ?? '—'
          } }}
          }
        </p>

        <mat-form-field class="mt-4 w-full">
          <mat-label>{{ (isUpi ? 'payments.utr' : 'payments.gatewayRefundId') | transloco }}</mat-label>
          <input matInput name="gatewayRefundId" autocomplete="off" [placeholder]="isUpi ? '412345678901' : 'rfnd_...'" required
            [value]="gatewayRefundId()" (input)="gatewayRefundId.set(value($event))" />
        </mat-form-field>
        <upb-field-errors fieldId="gatewayRefundId" [errors]="errors()" />

        <p class="mt-3 text-sm text-ink-muted">{{ 'staff.auditNote' | transloco }}</p>
      </mat-dialog-content>

      <mat-dialog-actions align="end">
        <button mat-button type="button" [mat-dialog-close]="undefined">{{ 'common.cancel' | transloco }}</button>
        <button mat-flat-button color="primary" type="submit" [disabled]="busy() || !gatewayRefundId().trim()">
          {{ 'payments.markRefunded' | transloco }}
        </button>
      </mat-dialog-actions>
    </form>
  `,
})
export class MarkRefundedDialog {
  protected readonly refund = inject<RefundDto>(MAT_DIALOG_DATA);

  /** Cash paid at the door goes back by UPI, and the proof is the bank's transaction reference. */
  protected readonly isUpi = this.refund.method === 'Upi';

  protected readonly gatewayRefundId = signal('');
  protected readonly busy = signal(false);
  protected readonly errors = signal<readonly string[]>([]);

  private readonly reference = inject<MatDialogRef<MarkRefundedDialog, RefundDto>>(MatDialogRef);
  private readonly api = inject(Api);

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected async save(event: Event): Promise<void> {
    event.preventDefault();
    this.busy.set(true);
    this.errors.set([]);

    try {
      const updated = await this.api.invoke(apiV1AdminPaymentsRefundsRefundIdMarkRefundedPost, {
        refundId: this.refund.id,
        body: { gatewayRefundId: this.gatewayRefundId().trim() },
      });

      this.reference.close(updated);
    } catch (error) {
      this.errors.set(fieldErrorsFor(toApiProblem(error), 'gatewayRefundId'));
    } finally {
      this.busy.set(false);
    }
  }
}
