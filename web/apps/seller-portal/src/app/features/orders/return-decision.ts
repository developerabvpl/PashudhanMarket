import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoPipe } from '@jsverse/transloco';
import { Api, SellerOrderDto, apiV1SellerOrdersOrderIdPartsPartIdReturnDecisionPost } from '@upbazaar/data-access';
import { ToastService } from '@upbazaar/ui';
import { DateIstPipe } from '@upbazaar/util';

/**
 * A buyer asking to send this parcel back, and the seller's answer. Accepting books a courier to
 * collect it; refusing needs a reason, because the buyer reads it and a bare "no" only sends them
 * to support.
 */
@Component({
  selector: 'upb-return-decision',
  // A custom element is inline by default, and the page's spacing between cards skips inline boxes.
  host: { class: 'block' },
  imports: [TranslocoPipe, DateIstPipe, MatButtonModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (order().returnRequest; as request) {
    <div class="rounded-card border border-warning/50 bg-warning/10 p-5 text-sm">
      <h2 class="font-medium text-ink">{{ 'returns.requestTitle' | transloco }}</h2>
      <p class="mt-1 text-ink">
        {{ 'orders.return.reasons.' + request.reason | transloco }} ·
        {{ 'returns.requestedOn' | transloco: { date: (request.requestedAtUtc | dateIst: 'datetime') } }}
      </p>
      @if (request.comment) {
      <p class="mt-1 text-ink">{{ 'returns.buyerSays' | transloco: { comment: request.comment } }}</p>
      }
      <div class="mt-4 flex flex-wrap items-start gap-3">
        <button mat-flat-button color="primary" type="button" [disabled]="busy()" (click)="decide(true)">
          {{ 'returns.approve' | transloco }}
        </button>
        <mat-form-field class="min-w-64 flex-1" subscriptSizing="dynamic">
          <mat-label>{{ 'returns.rejectNote' | transloco }}</mat-label>
          <input matInput name="note" maxlength="500" [value]="note()" (input)="note.set(value($event))" />
        </mat-form-field>
        <button mat-stroked-button color="warn" type="button" [disabled]="busy() || !note().trim()" (click)="decide(false)">
          {{ 'returns.reject' | transloco }}
        </button>
      </div>
    </div>
    }
  `,
})
export class ReturnDecision {
  readonly order = input.required<SellerOrderDto>();
  readonly decided = output<SellerOrderDto>();

  protected readonly note = signal('');
  protected readonly busy = signal(false);

  private readonly api = inject(Api);
  private readonly toast = inject(ToastService);

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected async decide(approve: boolean): Promise<void> {
    this.busy.set(true);

    try {
      const updated = await this.api.invoke(apiV1SellerOrdersOrderIdPartsPartIdReturnDecisionPost, {
        orderId: this.order().orderId,
        partId: this.order().partId,
        body: { approve, note: approve ? null : this.note().trim() },
      });

      this.toast.success(approve ? 'returns.approvedToast' : 'returns.rejectedToast');
      this.decided.emit(updated);
    } catch {
      // Reported by the interceptor.
    } finally {
      this.busy.set(false);
    }
  }
}
