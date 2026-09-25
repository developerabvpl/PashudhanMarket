import { ChangeDetectionStrategy, Component, inject, output, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { Api, CouponPreviewDto, apiV1PromotionsCouponsPreviewPost, toApiProblem } from '@upbazaar/data-access';
import { InrCurrencyPipe } from '@upbazaar/util';

/**
 * Where a buyer enters a coupon code at checkout. Applying it asks the API what it takes off the
 * basket as it stands, and says why not when it takes nothing; placing the order prices it again.
 *
 * It sits inside the checkout form, so Enter in the code box applies the code rather than placing
 * the order.
 */
@Component({
  selector: 'upb-coupon-field',
  imports: [TranslocoPipe, InrCurrencyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mt-4 border-t border-border pt-4 text-sm">
      @if (coupon(); as c) {
      <div class="flex items-start justify-between gap-3" role="status">
        <p class="text-ink">
          <span class="font-semibold">{{ c.code }}</span> · {{ c.description }}<br />
          <span class="text-success">{{ 'checkout.coupon.saves' | transloco: { amount: (c.discount + c.deliveryDiscount | inr: 'symbol' : 'auto') } }}</span>
        </p>
        <button type="button" class="shrink-0 text-ink-muted hover:underline" (click)="remove()">
          {{ 'checkout.coupon.remove' | transloco }}
        </button>
      </div>
      } @else {
      <label class="block font-medium text-ink" for="coupon-code">{{ 'checkout.coupon.label' | transloco }}</label>
      <div class="mt-1 flex gap-2">
        <input id="coupon-code" autocomplete="off" maxlength="20"
          class="min-w-0 flex-1 rounded-control border border-border bg-surface px-3 py-2 uppercase text-ink aria-[invalid=true]:border-danger"
          [value]="code()" [attr.aria-invalid]="!!error()" aria-describedby="coupon-code-error"
          (input)="code.set(value($event))" (keydown.enter)="enter($event)" />
        <button type="button" [disabled]="busy() || !code().trim()"
          class="shrink-0 rounded-control border border-border px-4 py-2 font-medium text-ink transition-colors hover:bg-surface-sunken disabled:opacity-50"
          (click)="apply()">
          {{ 'checkout.coupon.apply' | transloco }}
        </button>
      </div>
      @if (error(); as e) {
      <p id="coupon-code-error" class="mt-1 text-danger" role="alert">{{ e | transloco }}</p>
      }
      }
    </div>
  `,
})
export class CouponField {
  /** The coupon as priced, or null once removed. */
  readonly applied = output<CouponPreviewDto | null>();

  protected readonly code = signal('');
  protected readonly coupon = signal<CouponPreviewDto | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);

  private readonly api = inject(Api);

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected enter(event: Event): void {
    event.preventDefault();
    void this.apply();
  }

  protected async apply(): Promise<void> {
    const code = this.code().trim();

    if (!code) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      const coupon = await this.api.invoke(apiV1PromotionsCouponsPreviewPost, { body: { code } });

      this.coupon.set(coupon);
      this.applied.emit(coupon);
    } catch (error) {
      this.error.set(toApiProblem(error).title);
    } finally {
      this.busy.set(false);
    }
  }

  protected remove(): void {
    this.coupon.set(null);
    this.code.set('');
    this.applied.emit(null);
  }
}
