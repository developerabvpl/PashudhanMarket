import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  Api,
  ApiProblem,
  OrderDto,
  OrderPartDto,
  ShipmentDto,
  apiV1OrdersOrderIdPartsPartIdReturnPost,
  fieldErrorsFor,
  toApiProblem,
} from '@upbazaar/data-access';
import { FieldErrors, ToastService } from '@upbazaar/ui';
import { DateIstPipe, InrCurrencyPipe } from '@upbazaar/util';

/** The reasons a buyer can give, in the order the form lists them. */
export const RETURN_REASONS = ['Damaged', 'WrongItem', 'NotAsDescribed', 'QualityIssue', 'NoLongerNeeded', 'Other'] as const;

/**
 * A delivered parcel's return: the button and form to ask for one while the window is open, and
 * afterwards where the return stands.
 *
 * The whole parcel goes back, never some of it, and the refund falls due only once it is with the
 * seller - both said up front, so a buyer knows what they are asking for. A cash-on-delivery buyer
 * gives a UPI id, since there is no online payment to reverse.
 */
@Component({
  selector: 'upb-return-panel',
  imports: [TranslocoPipe, DateIstPipe, InrCurrencyPipe, FieldErrors],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (part().returnRequest; as request) {
    <div class="border-t border-border px-4 py-3 text-sm text-ink" role="status">
      @switch (request.status) {
      @case ('Requested') {
      <p>{{ 'orders.return.requested' | transloco: { date: (request.requestedAtUtc | dateIst) } }}</p>
      }
      @case ('Rejected') {
      <p>{{ 'orders.return.rejected' | transloco: { note: request.decisionNote ?? '' } }}</p>
      }
      @default {
      @if (part().status === 'Returned') {
      <p>
        @if (request.refundUpiId) {
        {{ 'orders.return.refundUpi' | transloco: { amount: (part().subtotal | inr: 'symbol' : 'auto'), upi: request.refundUpiId } }}
        } @else {
        {{ 'orders.return.refundOnline' | transloco: { amount: (part().subtotal | inr: 'symbol' : 'auto') } }}
        }
      </p>
      } @else {
      <p>{{ 'orders.return.approved' | transloco }}</p>
      @if (pickup()?.awb; as awb) {
      <p class="mt-1 text-ink-muted">
        {{ 'orders.return.pickupWith' | transloco: { courier: pickup()?.courierName ?? '—', awb: awb } }}
        @if (pickup()?.trackingUrl; as url) {
        · <a class="font-medium text-accent-600 hover:underline" [href]="url" target="_blank" rel="noopener">{{ 'orders.track' | transloco }}</a>
        }
      </p>
      }
      }
      }
      }
    </div>
    } @else if (canAsk()) {
    <div class="border-t border-border px-4 py-3 text-sm">
      @if (!open()) {
      <div class="flex flex-wrap items-center justify-between gap-2">
        <p class="text-ink-muted">{{ 'orders.return.until' | transloco: { date: (part().returnableUntilUtc | dateIst) } }}</p>
        <button type="button" class="font-medium text-accent-600 hover:underline" (click)="open.set(true)">
          {{ 'orders.return.start' | transloco }}
        </button>
      </div>
      } @else {
      <form class="space-y-3" novalidate (submit)="submit($event)">
        <p class="text-ink-muted">{{ 'orders.return.wholeParcel' | transloco: { amount: (part().subtotal | inr: 'symbol' : 'auto') } }}</p>

        <div>
          <label class="block font-medium text-ink" [for]="id('reason')">{{ 'orders.return.reasonLabel' | transloco }}</label>
          <select [id]="id('reason')" [class]="inputClass" (change)="reason.set(value($event))">
            @for (r of reasons; track r) {
            <option [value]="r" [selected]="reason() === r">{{ 'orders.return.reasons.' + r | transloco }}</option>
            }
          </select>
        </div>

        <div>
          <label class="block font-medium text-ink" [for]="id('comment')">
            {{ 'orders.return.commentLabel' | transloco }}
            @if (reason() !== 'Other') { <span class="font-normal text-ink-muted">({{ 'common.optional' | transloco }})</span> }
          </label>
          <textarea [id]="id('comment')" rows="3" maxlength="500" [class]="inputClass"
            [attr.aria-invalid]="errors('comment').length > 0" [attr.aria-describedby]="id('comment') + '-errors'" [value]="comment()" (input)="comment.set(value($event))"></textarea>
          <upb-field-errors [fieldId]="id('comment')" [errors]="errors('comment')" />
        </div>

        @if (isCod()) {
        <div>
          <label class="block font-medium text-ink" [for]="id('upi')">{{ 'orders.return.upiLabel' | transloco }}</label>
          <input [id]="id('upi')" autocomplete="off" inputmode="email" maxlength="64" placeholder="name@okicici" [class]="inputClass"
            [attr.aria-invalid]="errors('refundUpiId').length > 0" [attr.aria-describedby]="id('upi') + '-errors'" [value]="upiId()" (input)="upiId.set(value($event))" />
          <p class="mt-1 text-xs text-ink-muted">{{ 'orders.return.upiHint' | transloco }}</p>
          <upb-field-errors [fieldId]="id('upi')" [errors]="errors('refundUpiId')" />
        </div>
        }

        <upb-field-errors [fieldId]="id('form')" [errors]="generalErrors()" />

        <div class="flex flex-wrap items-center gap-3">
          <button type="submit" [disabled]="busy()"
            class="rounded-control bg-brand-600 px-4 py-2 font-semibold text-white transition-colors hover:bg-brand-700 disabled:opacity-50">
            {{ 'orders.return.submit' | transloco }}
          </button>
          <button type="button" class="text-ink-muted hover:underline" (click)="open.set(false)">
            {{ 'orders.return.cancel' | transloco }}
          </button>
        </div>
      </form>
      }
    </div>
    }
  `,
})
export class ReturnPanel {
  readonly order = input.required<OrderDto>();
  readonly part = input.required<OrderPartDto>();

  /** The courier booked to collect the return, once there is one. */
  readonly pickup = input<ShipmentDto | undefined>(undefined);

  /** The order as it is after asking, so the page can show it without reloading. */
  readonly requested = output<OrderDto>();

  protected readonly reasons = RETURN_REASONS;
  protected readonly inputClass =
    'mt-1 w-full rounded-control border border-border bg-surface px-3 py-2 text-ink aria-[invalid=true]:border-danger';

  protected readonly open = signal(false);
  protected readonly reason = signal<string>('Damaged');
  protected readonly comment = signal('');
  protected readonly upiId = signal('');
  protected readonly busy = signal(false);
  private readonly problem = signal<ApiProblem | null>(null);
  private readonly commentMissing = signal(false);

  protected readonly isCod = computed(() => this.order().paymentMethod === 'CashOnDelivery');

  /** Delivered, not yet asked about, and still inside its window. The API checks all three again. */
  protected readonly canAsk = computed(() => {
    const part = this.part();
    const until = part.returnableUntilUtc;

    return part.status === 'Delivered' && !part.returnRequest && !!until && Date.parse(until) > Date.now();
  });

  /** Messages that name no field of this form, such as "give a UPI id". */
  protected readonly generalErrors = computed(() => {
    const problem = this.problem();

    return problem && Object.keys(problem.fieldErrors).length === 0 ? [problem.title] : [];
  });

  private readonly api = inject(Api);
  private readonly toast = inject(ToastService);

  protected id(name: string): string {
    return `return-${name}-${this.part().id}`;
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected errors(field: string): readonly string[] {
    if (field === 'comment' && this.commentMissing()) {
      return ['orders.return.commentRequired'];
    }

    return fieldErrorsFor(this.problem(), field);
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.problem.set(null);
    this.commentMissing.set(this.reason() === 'Other' && !this.comment().trim());

    if (this.commentMissing()) {
      return;
    }

    this.busy.set(true);

    try {
      const order = await this.api.invoke(apiV1OrdersOrderIdPartsPartIdReturnPost, {
        orderId: this.order().id,
        partId: this.part().id,
        body: {
          reason: this.reason(),
          comment: this.comment().trim() || null,
          refundUpiId: this.isCod() ? this.upiId().trim() || null : null,
        },
      });

      this.toast.success('orders.return.sent');
      this.open.set(false);
      this.requested.emit(order);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
