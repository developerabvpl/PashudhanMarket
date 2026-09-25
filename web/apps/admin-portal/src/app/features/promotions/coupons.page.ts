import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe } from '@jsverse/transloco';
import { CurrentUserStore } from '@upbazaar/auth';
import {
  Api,
  CouponDto,
  apiV1AdminPromotionsCouponsCouponIdEndPost,
  apiV1AdminPromotionsCouponsGet,
  apiV1AdminPromotionsCouponsPost,
  fieldErrorsFor,
  toApiProblem,
} from '@upbazaar/data-access';
import { ToastService } from '@upbazaar/ui';
import { DateIstPipe, InrCurrencyPipe } from '@upbazaar/util';
import { PromotionsPermissions } from '../../core/permissions';

/** The last moment of a day in India, so a coupon "ending 31 October" works all that day. */
export function endOfIstDay(date: string): string {
  return new Date(`${date}T23:59:59+05:30`).toISOString();
}

/**
 * Coupon codes: the platform's, and sellers' own, newest first.
 *
 * A platform coupon is paid for by the platform - sellers are paid as if the buyer paid in full -
 * or, as a campaign, by the sellers who choose to join it. Coupons are not edited once made, only
 * ended: a buyer who read "10% off" should not find it changed. A new offer is a new code.
 */
@Component({
  selector: 'upb-coupons-page',
  imports: [TranslocoPipe, DateIstPipe, InrCurrencyPipe, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-6xl px-4 py-8">
      <h1 class="text-2xl font-semibold text-ink">{{ 'coupons.title' | transloco }}</h1>
      <p class="mt-1 text-sm text-ink-muted">{{ 'coupons.subtitle' | transloco }}</p>

      @if (canWrite()) {
      <form class="upb-card mt-6 grid gap-3 p-4 sm:grid-cols-3" (submit)="create($event)">
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'coupons.code' | transloco }}</mat-label>
          <input matInput name="code" maxlength="20" required [value]="code()" (input)="code.set(value($event))" />
        </mat-form-field>
        <mat-form-field class="sm:col-span-2" subscriptSizing="dynamic">
          <mat-label>{{ 'coupons.description' | transloco }}</mat-label>
          <input matInput name="description" maxlength="120" required [value]="description()" (input)="description.set(value($event))" />
        </mat-form-field>
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'coupons.discountType' | transloco }}</mat-label>
          <mat-select name="discountType" [value]="discountType()" (selectionChange)="discountType.set($event.value)">
            <mat-option value="Percent">{{ 'coupons.types.Percent' | transloco }}</mat-option>
            <mat-option value="Flat">{{ 'coupons.types.Flat' | transloco }}</mat-option>
            <mat-option value="FreeDelivery">{{ 'coupons.types.FreeDelivery' | transloco }}</mat-option>
          </mat-select>
        </mat-form-field>
        @if (discountType() !== 'FreeDelivery') {
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ (discountType() === 'Percent' ? 'coupons.percent' : 'coupons.rupees') | transloco }}</mat-label>
          <input matInput name="value" type="number" min="0" required [value]="amount()" (input)="amount.set(value($event))" />
        </mat-form-field>
        }
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'coupons.fundedBy' | transloco }}</mat-label>
          <mat-select name="fundedBy" [value]="fundedBy()" (selectionChange)="fundedBy.set($event.value)">
            <mat-option value="Platform">{{ 'coupons.funding.Platform' | transloco }}</mat-option>
            <mat-option value="Seller">{{ 'coupons.funding.Seller' | transloco }}</mat-option>
          </mat-select>
        </mat-form-field>
        @if (discountType() === 'Percent') {
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'coupons.maxDiscount' | transloco }}</mat-label>
          <input matInput name="maxDiscount" type="number" min="0" [value]="maxDiscount()" (input)="maxDiscount.set(value($event))" />
        </mat-form-field>
        }
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'coupons.minOrderValue' | transloco }}</mat-label>
          <input matInput name="minOrderValue" type="number" min="0" [value]="minOrderValue()" (input)="minOrderValue.set(value($event))" />
        </mat-form-field>
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'coupons.endsOn' | transloco }}</mat-label>
          <input matInput name="endsOn" type="date" [value]="endsOn()" (input)="endsOn.set(value($event))" />
        </mat-form-field>
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'coupons.totalLimit' | transloco }}</mat-label>
          <input matInput name="totalLimit" type="number" min="1" [value]="totalLimit()" (input)="totalLimit.set(value($event))" />
        </mat-form-field>
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'coupons.perBuyerLimit' | transloco }}</mat-label>
          <input matInput name="perBuyerLimit" type="number" min="1" max="100" [value]="perBuyerLimit()" (input)="perBuyerLimit.set(value($event))" />
        </mat-form-field>
        @if (fundedBy() === 'Seller') {
        <p class="text-sm text-ink-muted sm:col-span-3">{{ 'coupons.campaignHint' | transloco }}</p>
        }
        @if (errors().length > 0) {
        <ul class="text-sm text-danger sm:col-span-3" role="alert">
          @for (e of errors(); track e) { <li>{{ e | transloco }}</li> }
        </ul>
        }
        <div class="sm:col-span-3">
          <button mat-flat-button color="primary" type="submit" [disabled]="busy() || !code().trim() || !description().trim() || (discountType() !== 'FreeDelivery' && !amount())">
            {{ 'coupons.create' | transloco }}
          </button>
        </div>
      </form>
      }

      <ul class="upb-card mt-6 divide-y divide-border">
        @for (c of coupons(); track c.id) {
        <li class="flex flex-wrap items-start justify-between gap-3 p-4 text-sm">
          <div>
            <p class="font-medium text-ink">
              <span class="font-mono">{{ c.code }}</span> · {{ c.description }}
              @if (!isRunning(c)) { <span class="text-ink-muted">· {{ 'coupons.ended' | transloco }}</span> }
            </p>
            <p class="text-ink-muted">
              @switch (c.discountType) {
              @case ('Percent') { {{ 'coupons.percentOff' | transloco: { value: c.value } }} }
              @case ('Flat') { {{ 'coupons.flatOff' | transloco: { amount: (c.value | inr) } }} }
              @default { {{ 'coupons.types.FreeDelivery' | transloco }} }
              }
              @if (c.maxDiscount) { · {{ 'coupons.upTo' | transloco: { amount: (c.maxDiscount | inr) } }} }
              @if (c.minOrderValue) { · {{ 'coupons.minimum' | transloco: { amount: (c.minOrderValue | inr) } }} }
              · {{ (c.sellerId ? 'coupons.sellersOwn' : 'coupons.funding.' + c.fundedBy) | transloco }}
              @if (!c.sellerId && c.fundedBy === 'Seller') { · {{ 'coupons.joined' | transloco: { count: c.sellersJoined } }} }
            </p>
            <p class="text-ink-muted">
              {{ 'coupons.uses' | transloco: { uses: c.uses, limit: c.totalLimit ?? '∞' } }}
              · {{ 'coupons.perBuyer' | transloco: { limit: c.perBuyerLimit } }}
              @if (c.endsAtUtc) { · {{ 'coupons.until' | transloco: { date: (c.endsAtUtc | dateIst) } }} }
            </p>
          </div>
          @if (canWrite() && c.isActive) {
          <button mat-stroked-button color="warn" type="button" [disabled]="busy()" (click)="end(c)">{{ 'coupons.end' | transloco }}</button>
          }
        </li>
        } @empty {
        <li class="p-8 text-center text-ink-muted">{{ 'coupons.none' | transloco }}</li>
        }
      </ul>
    </section>
  `,
})
export class CouponsPage {
  protected readonly coupons = signal<readonly CouponDto[]>([]);
  protected readonly code = signal('');
  protected readonly description = signal('');
  protected readonly discountType = signal<'Percent' | 'Flat' | 'FreeDelivery'>('Percent');
  protected readonly amount = signal('');
  protected readonly fundedBy = signal<'Platform' | 'Seller'>('Platform');
  protected readonly maxDiscount = signal('');
  protected readonly minOrderValue = signal('');
  protected readonly endsOn = signal('');
  protected readonly totalLimit = signal('');
  protected readonly perBuyerLimit = signal('1');
  protected readonly busy = signal(false);
  protected readonly errors = signal<readonly string[]>([]);

  private readonly api = inject(Api);
  private readonly toast = inject(ToastService);
  private readonly currentUser = inject(CurrentUserStore);

  protected readonly canWrite = computed(() => this.currentUser.has(PromotionsPermissions.CampaignsWrite));

  constructor() {
    void this.load();
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected isRunning(coupon: CouponDto): boolean {
    return coupon.isActive && (!coupon.endsAtUtc || Date.parse(coupon.endsAtUtc) > Date.now());
  }

  protected async create(event: Event): Promise<void> {
    event.preventDefault();
    this.busy.set(true);
    this.errors.set([]);

    try {
      await this.api.invoke(apiV1AdminPromotionsCouponsPost, {
        body: {
          code: this.code().trim(),
          description: this.description().trim(),
          discountType: this.discountType(),
          value: this.discountType() === 'FreeDelivery' ? 0 : Number(this.amount()),
          fundedBy: this.fundedBy(),
          maxDiscount: this.discountType() === 'Percent' ? optionalNumber(this.maxDiscount()) : null,
          minOrderValue: optionalNumber(this.minOrderValue()),
          endsAtUtc: this.endsOn() ? endOfIstDay(this.endsOn()) : null,
          totalLimit: optionalNumber(this.totalLimit()),
          perBuyerLimit: optionalNumber(this.perBuyerLimit()),
        },
      });

      this.toast.success('coupons.created');
      this.code.set('');
      this.description.set('');
      this.amount.set('');
      this.maxDiscount.set('');
      this.minOrderValue.set('');
      this.endsOn.set('');
      this.totalLimit.set('');
      await this.load();
    } catch (error) {
      const problem = toApiProblem(error);
      const fields = Object.keys(problem.fieldErrors).flatMap((field) => fieldErrorsFor(problem, field));

      this.errors.set(fields.length > 0 ? fields : [problem.title]);
    } finally {
      this.busy.set(false);
    }
  }

  protected async end(coupon: CouponDto): Promise<void> {
    this.busy.set(true);

    try {
      await this.api.invoke(apiV1AdminPromotionsCouponsCouponIdEndPost, { couponId: coupon.id });
      this.toast.info('coupons.endedToast');
      await this.load();
    } catch {
      // Reported by the interceptor.
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.coupons.set(await this.api.invoke(apiV1AdminPromotionsCouponsGet, {}));
    } catch {
      // Reported by the interceptor.
    }
  }
}

function optionalNumber(text: string): number | null {
  return text.trim() === '' ? null : Number(text);
}
