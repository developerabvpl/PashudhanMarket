import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  Api,
  CouponDto,
  apiV1SellerPromotionsCampaignsCouponIdJoinedPut,
  apiV1SellerPromotionsCampaignsGet,
  apiV1SellerPromotionsCouponsCouponIdEndPost,
  apiV1SellerPromotionsCouponsGet,
  apiV1SellerPromotionsCouponsPost,
  fieldErrorsFor,
  toApiProblem,
} from '@upbazaar/data-access';
import { ToastService } from '@upbazaar/ui';
import { DateIstPipe, InrCurrencyPipe, endOfIstDay } from '@upbazaar/util';

/**
 * The seller's own coupons, and the platform's campaigns they can join.
 *
 * Both come out of the seller's pay: a coupon's discount is taken off the price they are paid on,
 * and so is a campaign's while they are in it. That is said on the page, so nobody joins a
 * campaign thinking the platform pays for it.
 */
@Component({
  selector: 'upb-seller-coupons-page',
  imports: [TranslocoPipe, DateIstPipe, InrCurrencyPipe, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-5xl space-y-6 px-4 py-8">
      <div>
        <h1 class="text-2xl font-semibold text-ink">{{ 'coupons.sellerTitle' | transloco }}</h1>
        <p class="mt-1 text-sm text-ink-muted">{{ 'coupons.sellerSubtitle' | transloco }}</p>
      </div>

      <form class="upb-card grid gap-3 p-4 sm:grid-cols-3" (submit)="create($event)">
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
        <p class="self-center text-sm text-ink-muted">{{ 'coupons.youPay' | transloco }}</p>
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

      <ul class="upb-card divide-y divide-border">
        @for (c of coupons(); track c.id) {
        <li class="flex flex-wrap items-start justify-between gap-3 p-4 text-sm">
          <div>
            <p class="font-medium text-ink"><span class="font-mono">{{ c.code }}</span> · {{ c.description }}
              @if (!c.isActive) { <span class="text-ink-muted">· {{ 'coupons.ended' | transloco }}</span> }
            </p>
            <p class="text-ink-muted">
              @switch (c.discountType) {
              @case ('Percent') { {{ 'coupons.percentOff' | transloco: { value: c.value } }} }
              @case ('Flat') { {{ 'coupons.flatOff' | transloco: { amount: (c.value | inr) } }} }
              @default { {{ 'coupons.types.FreeDelivery' | transloco }} }
              }
              · {{ 'coupons.uses' | transloco: { uses: c.uses, limit: c.totalLimit ?? '∞' } }}
              @if (c.endsAtUtc) { · {{ 'coupons.until' | transloco: { date: (c.endsAtUtc | dateIst) } }} }
            </p>
          </div>
          @if (c.isActive) {
          <button mat-stroked-button color="warn" type="button" [disabled]="busy()" (click)="end(c)">{{ 'coupons.end' | transloco }}</button>
          }
        </li>
        } @empty {
        <li class="p-6 text-center text-ink-muted">{{ 'coupons.noneOwn' | transloco }}</li>
        }
      </ul>

      <div>
        <h2 class="text-lg font-semibold text-ink">{{ 'coupons.campaignsTitle' | transloco }}</h2>
        <p class="mt-1 text-sm text-ink-muted">{{ 'coupons.campaignsSubtitle' | transloco }}</p>
      </div>
      <ul class="upb-card divide-y divide-border">
        @for (c of campaigns(); track c.id) {
        <li class="flex flex-wrap items-center justify-between gap-3 p-4 text-sm">
          <div>
            <p class="font-medium text-ink"><span class="font-mono">{{ c.code }}</span> · {{ c.description }}</p>
            <p class="text-ink-muted">
              @switch (c.discountType) {
              @case ('Percent') { {{ 'coupons.percentOff' | transloco: { value: c.value } }} }
              @case ('Flat') { {{ 'coupons.flatOff' | transloco: { amount: (c.value | inr) } }} }
              @default { {{ 'coupons.types.FreeDelivery' | transloco }} }
              }
              @if (c.maxDiscount) { · {{ 'coupons.upTo' | transloco: { amount: (c.maxDiscount | inr) } }} }
              @if (c.endsAtUtc) { · {{ 'coupons.until' | transloco: { date: (c.endsAtUtc | dateIst) } }} }
            </p>
          </div>
          @if (c.joined) {
          <button mat-stroked-button type="button" [disabled]="busy()" (click)="setJoined(c, false)">{{ 'coupons.leave' | transloco }}</button>
          } @else {
          <button mat-flat-button color="primary" type="button" [disabled]="busy()" (click)="setJoined(c, true)">{{ 'coupons.join' | transloco }}</button>
          }
        </li>
        } @empty {
        <li class="p-6 text-center text-ink-muted">{{ 'coupons.noCampaigns' | transloco }}</li>
        }
      </ul>
    </section>
  `,
})
export class CouponsPage {
  protected readonly coupons = signal<readonly CouponDto[]>([]);
  protected readonly campaigns = signal<readonly CouponDto[]>([]);
  protected readonly code = signal('');
  protected readonly description = signal('');
  protected readonly discountType = signal<'Percent' | 'Flat' | 'FreeDelivery'>('Percent');
  protected readonly amount = signal('');
  protected readonly minOrderValue = signal('');
  protected readonly endsOn = signal('');
  protected readonly totalLimit = signal('');
  protected readonly busy = signal(false);
  protected readonly errors = signal<readonly string[]>([]);

  private readonly api = inject(Api);
  private readonly toast = inject(ToastService);

  constructor() {
    void this.load();
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected async create(event: Event): Promise<void> {
    event.preventDefault();
    this.busy.set(true);
    this.errors.set([]);

    try {
      await this.api.invoke(apiV1SellerPromotionsCouponsPost, {
        body: {
          code: this.code().trim(),
          description: this.description().trim(),
          discountType: this.discountType(),
          value: this.discountType() === 'FreeDelivery' ? 0 : Number(this.amount()),
          minOrderValue: optionalNumber(this.minOrderValue()),
          endsAtUtc: this.endsOn() ? endOfIstDay(this.endsOn()) : null,
          totalLimit: optionalNumber(this.totalLimit()),
        },
      });

      this.toast.success('coupons.created');
      this.code.set('');
      this.description.set('');
      this.amount.set('');
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
    await this.act(async () => {
      await this.api.invoke(apiV1SellerPromotionsCouponsCouponIdEndPost, { couponId: coupon.id });
      this.toast.info('coupons.endedToast');
    });
  }

  protected async setJoined(campaign: CouponDto, joined: boolean): Promise<void> {
    await this.act(async () => {
      await this.api.invoke(apiV1SellerPromotionsCampaignsCouponIdJoinedPut, { couponId: campaign.id, body: { joined } });
      this.toast.success(joined ? 'coupons.joinedToast' : 'coupons.leftToast');
    });
  }

  private async act(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);

    try {
      await work();
      await this.load();
    } catch {
      // Reported by the interceptor.
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      const [coupons, campaigns] = await Promise.all([
        this.api.invoke(apiV1SellerPromotionsCouponsGet, {}),
        this.api.invoke(apiV1SellerPromotionsCampaignsGet, {}),
      ]);

      this.coupons.set(coupons);
      this.campaigns.set(campaigns);
    } catch {
      // Reported by the interceptor.
    }
  }
}

function optionalNumber(text: string): number | null {
  return text.trim() === '' ? null : Number(text);
}
