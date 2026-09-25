import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { CurrentUserStore } from '@upbazaar/auth';
import {
  Api,
  ApiProblem,
  CouponPreviewDto,
  DeliveryAddressDto,
  apiV1OrdersDeliveryStatesGet,
  apiV1OrdersPost,
  fieldErrorsFor,
  toApiProblem,
} from '@upbazaar/data-access';
import { FieldErrors } from '@upbazaar/ui';
import { InrCurrencyPipe, isMobile, isPincode, normalizeMobile } from '@upbazaar/util';
import { DeliveryCharge } from '../../core/delivery-charge';
import { SeoService } from '../../core/seo.service';
import { CartStore } from '../cart/cart.store';
import { OrderPayment } from '../payments/order-payment';
import { CouponField } from './coupon-field';
import { LastAddress } from './last-address';

type AddressField = keyof DeliveryAddressDto;

const REQUIRED: readonly AddressField[] = ['fullName', 'mobile', 'line1', 'city', 'state', 'pincode'];

const EMPTY: DeliveryAddressDto = {
  fullName: '',
  mobile: '',
  line1: '',
  line2: null,
  landmark: null,
  city: '',
  district: null,
  state: '',
  pincode: '',
};

/**
 * Checkout: where it goes, how it is paid, and one button.
 *
 * Online payment is offered only when the API says a gateway is configured; otherwise the option
 * is shown disabled, since an online order nobody can pay would hold stock for fifteen minutes and
 * then cancel itself. An online order is placed first and paid for on its own page, so a buyer
 * whose payment fails, or who closes the window, still has the order to pay for until its deadline.
 *
 * The page works from the account cart the store already holds, and places nothing unless the
 * store says the cart can be checked out; the API checks again, against live prices and stock.
 */
@Component({
  selector: 'upb-checkout-page',
  imports: [RouterLink, TranslocoPipe, InrCurrencyPipe, FieldErrors, CouponField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-5xl px-4 py-8 sm:py-12">
      <h1 class="text-2xl font-bold tracking-tight text-ink sm:text-3xl">
        {{ 'checkout.title' | transloco }}
      </h1>

      @if (cart.isLoading()) {
      <p class="mt-8 text-ink-muted" role="status">{{ 'state.loading' | transloco }}</p>
      } @else if (cart.isEmpty()) {
      <div class="mt-10 rounded-card border border-border bg-surface p-10 text-center">
        <p class="text-ink-muted">{{ 'cart.empty' | transloco }}</p>
        <a
          class="mt-5 inline-block rounded-control bg-brand-600 px-5 py-2.5 font-semibold text-white transition-colors hover:bg-brand-700"
          routerLink="/products"
        >
          {{ 'cart.browse' | transloco }}
        </a>
      </div>
      } @else {
      <form class="mt-6 grid gap-6 lg:grid-cols-[1fr_22rem]" novalidate (submit)="place($event)">
        <div class="space-y-6">
          <fieldset class="rounded-card border border-border bg-surface p-5">
            <legend class="px-1 font-semibold text-ink">{{ 'checkout.address' | transloco }}</legend>

            <div class="mt-2 grid gap-4 sm:grid-cols-2">
              <div class="sm:col-span-2">
                <label class="block text-sm font-medium text-ink" for="fullName">
                  {{ 'checkout.fullName' | transloco }}
                </label>
                <input id="fullName" autocomplete="name" [class]="inputClass" [value]="address().fullName"
                  [attr.aria-invalid]="errorsFor('fullName').length > 0"
                  (input)="set('fullName', $event)" />
                <upb-field-errors fieldId="fullName" [errors]="errorsFor('fullName')" />
              </div>

              <div>
                <label class="block text-sm font-medium text-ink" for="mobile">
                  {{ 'checkout.mobile' | transloco }}
                </label>
                <input id="mobile" type="tel" inputmode="numeric" maxlength="13" autocomplete="tel-national"
                  [class]="inputClass" [value]="address().mobile"
                  [attr.aria-invalid]="errorsFor('mobile').length > 0"
                  (input)="set('mobile', $event)" />
                <upb-field-errors fieldId="mobile" [errors]="errorsFor('mobile')" />
              </div>

              <div>
                <label class="block text-sm font-medium text-ink" for="pincode">
                  {{ 'checkout.pincode' | transloco }}
                </label>
                <input id="pincode" inputmode="numeric" maxlength="6" autocomplete="postal-code"
                  [class]="inputClass" [value]="address().pincode"
                  [attr.aria-invalid]="errorsFor('pincode').length > 0"
                  (input)="set('pincode', $event)" />
                <upb-field-errors fieldId="pincode" [errors]="errorsFor('pincode')" />
              </div>

              <div class="sm:col-span-2">
                <label class="block text-sm font-medium text-ink" for="line1">
                  {{ 'checkout.line1' | transloco }}
                </label>
                <input id="line1" autocomplete="address-line1" [class]="inputClass" [value]="address().line1"
                  [attr.aria-invalid]="errorsFor('line1').length > 0"
                  (input)="set('line1', $event)" />
                <upb-field-errors fieldId="line1" [errors]="errorsFor('line1')" />
              </div>

              <div class="sm:col-span-2">
                <label class="block text-sm font-medium text-ink" for="line2">
                  {{ 'checkout.line2' | transloco }}
                  <span class="font-normal text-ink-muted">({{ 'common.optional' | transloco }})</span>
                </label>
                <input id="line2" autocomplete="address-line2" [class]="inputClass" [value]="address().line2 ?? ''"
                  (input)="set('line2', $event)" />
              </div>

              <div class="sm:col-span-2">
                <label class="block text-sm font-medium text-ink" for="landmark">
                  {{ 'checkout.landmark' | transloco }}
                  <span class="font-normal text-ink-muted">({{ 'common.optional' | transloco }})</span>
                </label>
                <input id="landmark" [class]="inputClass" [value]="address().landmark ?? ''"
                  (input)="set('landmark', $event)" />
              </div>

              <div>
                <label class="block text-sm font-medium text-ink" for="city">
                  {{ 'checkout.city' | transloco }}
                </label>
                <input id="city" autocomplete="address-level2" [class]="inputClass" [value]="address().city"
                  [attr.aria-invalid]="errorsFor('city').length > 0"
                  (input)="set('city', $event)" />
                <upb-field-errors fieldId="city" [errors]="errorsFor('city')" />
              </div>

              <div>
                <label class="block text-sm font-medium text-ink" for="district">
                  {{ 'checkout.district' | transloco }}
                  <span class="font-normal text-ink-muted">({{ 'common.optional' | transloco }})</span>
                </label>
                <input id="district" [class]="inputClass" [value]="address().district ?? ''"
                  (input)="set('district', $event)" />
              </div>

              <div class="sm:col-span-2">
                <label class="block text-sm font-medium text-ink" for="state">
                  {{ 'checkout.state' | transloco }}
                </label>
                <select id="state" autocomplete="address-level1" [class]="inputClass"
                  [attr.aria-invalid]="errorsFor('state').length > 0"
                  (change)="set('state', $event)">
                  <option value="" [selected]="!address().state">{{ 'checkout.chooseState' | transloco }}</option>
                  @for (state of states(); track state) {
                  <option [value]="state" [selected]="state === address().state">{{ state }}</option>
                  }
                </select>
                <upb-field-errors fieldId="state" [errors]="errorsFor('state')" />
              </div>
            </div>
          </fieldset>

          <fieldset class="rounded-card border border-border bg-surface p-5">
            <legend class="px-1 font-semibold text-ink">{{ 'checkout.payment' | transloco }}</legend>

            <label
              class="mt-2 flex cursor-pointer items-start gap-3 rounded-control border p-3"
              [class.border-brand-600]="method() === 'CashOnDelivery'"
              [class.border-border]="method() !== 'CashOnDelivery'"
            >
              <input
                type="radio"
                name="payment"
                value="CashOnDelivery"
                class="mt-1"
                [checked]="method() === 'CashOnDelivery'"
                (change)="method.set('CashOnDelivery')"
              />
              <span>
                <span class="block font-medium text-ink">{{ 'checkout.cod' | transloco }}</span>
                <span class="block text-sm text-ink-muted">{{ 'checkout.codNote' | transloco }}</span>
              </span>
            </label>

            <label
              class="mt-3 flex items-start gap-3 rounded-control border p-3"
              [class.cursor-pointer]="onlineEnabled()"
              [class.opacity-60]="!onlineEnabled()"
              [class.border-brand-600]="method() === 'Online'"
              [class.border-border]="method() !== 'Online'"
            >
              <input
                type="radio"
                name="payment"
                value="Online"
                class="mt-1"
                [disabled]="!onlineEnabled()"
                [checked]="method() === 'Online'"
                (change)="method.set('Online')"
              />
              <span>
                <span class="block font-medium text-ink">{{ 'checkout.online' | transloco }}</span>
                <span class="block text-sm text-ink-muted">
                  {{ (onlineEnabled() ? 'checkout.onlineNote' : 'checkout.onlineSoon') | transloco }}
                </span>
              </span>
            </label>
          </fieldset>
        </div>

        <aside class="h-fit rounded-card border border-border bg-surface p-5 lg:sticky lg:top-24">
          <h2 class="font-semibold text-ink">{{ 'checkout.summary' | transloco }}</h2>

          <ul class="mt-4 space-y-3 text-sm">
            @for (line of cart.lines(); track line.productId) {
            <li class="flex justify-between gap-3">
              <span class="min-w-0 text-ink">
                {{ line.name }}
                <span class="text-ink-muted">× {{ line.quantity }}</span>
              </span>
              <span class="shrink-0 font-medium text-ink">
                {{ line.price * line.quantity | inr: 'symbol' : 'auto' }}
              </span>
            </li>
            }
          </ul>

          <upb-coupon-field (applied)="coupon.set($event)" />

          <dl class="mt-4 space-y-2 border-t border-border pt-4 text-sm">
            <div class="flex justify-between">
              <dt class="text-ink-muted">{{ 'cart.subtotal' | transloco }}</dt>
              <dd class="text-ink">{{ cart.subtotal() | inr: 'symbol' : 'auto' }}</dd>
            </div>
            @if (coupon(); as c) {
            <div class="flex justify-between">
              <dt class="text-ink-muted">{{ 'checkout.coupon.discount' | transloco: { code: c.code } }}</dt>
              <dd class="text-success">− {{ c.discount | inr: 'symbol' : 'auto' }}</dd>
            </div>
            }
            <div class="flex justify-between">
              <dt class="text-ink-muted">{{ 'checkout.delivery' | transloco }}</dt>
              <dd class="text-ink">
                @switch (fee()) {
                @case (null) { — }
                @case (0) { {{ 'checkout.free' | transloco }} }
                @default { {{ fee() | inr: 'symbol' : 'auto' }} }
                }
              </dd>
            </div>
            <div class="flex justify-between border-t border-border pt-2 text-base font-bold">
              <dt class="text-ink">{{ 'checkout.total' | transloco }}</dt>
              <dd class="text-ink">{{ total() | inr: 'symbol' : 'auto' }}</dd>
            </div>
          </dl>

          @if (!cart.canCheckOut()) {
          <p class="mt-4 text-sm text-danger" role="alert">
            {{ 'checkout.fixCart' | transloco }}
            <a class="underline" routerLink="/cart">{{ 'cart.viewCart' | transloco }}</a>
          </p>
          }

          <button
            type="submit"
            class="mt-5 w-full rounded-control bg-brand-600 px-6 py-3 font-semibold text-white transition-colors hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-50"
            [disabled]="busy() || !cart.canCheckOut()"
          >
            {{ (busy() ? 'checkout.placing' : method() === 'Online' ? 'checkout.placeAndPay' : 'checkout.place') | transloco }}
          </button>
        </aside>
      </form>
      }
    </section>
  `,
})
export class CheckoutPage {
  protected readonly cart = inject(CartStore);
  protected readonly inputClass =
    'mt-1 w-full rounded-control border border-border bg-surface px-3 py-2 text-ink aria-[invalid=true]:border-danger';

  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly user = inject(CurrentUserStore);
  private readonly lastAddress = inject(LastAddress);
  private readonly payment = inject(OrderPayment);

  protected readonly address = signal<DeliveryAddressDto>(EMPTY);
  protected readonly states = signal<readonly string[]>([]);
  protected readonly busy = signal(false);
  protected readonly method = signal<'CashOnDelivery' | 'Online'>('CashOnDelivery');
  protected readonly onlineEnabled = signal(false);

  /** Field problems found before sending, keyed like the API's, so both render the same way. */
  private readonly clientErrors = signal<Readonly<Record<string, readonly string[]>>>({});
  private readonly problem = signal<ApiProblem | null>(null);

  private readonly userId = computed(() => this.user.user()?.id ?? null);

  private readonly delivery = inject(DeliveryCharge);

  /** The delivery charge on this basket, or null while the rule is loading. */
  protected readonly fee = computed(() => this.delivery.feeFor(this.cart.subtotal()));

  /** The coupon applied at checkout, as last priced; null for none. */
  protected readonly coupon = signal<CouponPreviewDto | null>(null);

  /** What the buyer will pay: goods less the coupon, plus delivery (judged on the goods before the coupon). */
  protected readonly total = computed(() => this.cart.subtotal() - (this.coupon()?.discount ?? 0) + (this.fee() ?? 0));

  constructor() {
    void this.delivery.load();

    inject(SeoService).apply({
      title: 'Checkout',
      description: 'Place your UP Bazaar order.',
      canonicalPath: '/checkout',
      noIndex: true,
    });

    // The guard has loaded the profile, so the user is known by the time this runs.
    effect(() => {
      const id = this.userId();

      if (id !== null) {
        this.address.set(this.prefill(id));
      }
    });

    void this.loadStates();
    void this.payment.isOnlineEnabled().then((enabled) => this.onlineEnabled.set(enabled));
  }

  protected errorsFor(field: AddressField): readonly string[] {
    return this.clientErrors()[field] ?? fieldErrorsFor(this.problem(), field);
  }

  protected set(field: AddressField, event: Event): void {
    const value = (event.target as HTMLInputElement | HTMLSelectElement).value;

    this.address.update((current) => ({ ...current, [field]: value }));

    // Typing into a field clears its message; the next submit re-checks everything.
    this.clientErrors.update((errors) => {
      const next = { ...errors };
      delete next[field];

      return next;
    });
  }

  protected async place(event: Event): Promise<void> {
    event.preventDefault();

    const address = this.cleaned();
    const errors = validate(address);

    this.clientErrors.set(errors);
    this.problem.set(null);

    if (Object.keys(errors).length > 0 || this.busy() || !this.cart.canCheckOut()) {
      return;
    }

    this.busy.set(true);

    try {
      const order = await this.api.invoke(apiV1OrdersPost, {
        body: { paymentMethod: this.method(), deliveryAddress: address, couponCode: this.coupon()?.code ?? null },
      });

      const id = this.userId();

      if (id !== null) {
        this.lastAddress.write(id, address);
      }

      // The server emptied the cart as part of the order; show that before leaving.
      await this.cart.refresh();
      // An online order goes straight on to payment; the order page opens it.
      await this.router.navigate(['/orders', order.id], {
        queryParams: order.status === 'PendingPayment' ? { pay: 1 } : { placed: 1 },
      });
    } catch (error) {
      const problem = toApiProblem(error);

      this.problem.set(problem);

      // A cart problem found at the last moment - a price moved, stock sold out - is shown in
      // the summary once the store has the server's current view of the cart.
      if (problem.code.startsWith('cart.') || problem.code === 'orders.out_of_stock') {
        await this.cart.refresh();
      }
    } finally {
      this.busy.set(false);
    }
  }

  private prefill(userId: string): DeliveryAddressDto {
    const saved = this.lastAddress.read(userId);
    const profile = this.user.user();

    return {
      ...EMPTY,
      fullName: profile?.displayName ?? '',
      mobile: profile?.mobile ?? '',
      ...saved,
    };
  }

  private cleaned(): DeliveryAddressDto {
    const a = this.address();
    const optional = (value: string | null) => (value?.trim() ? value.trim() : null);

    return {
      fullName: a.fullName.trim(),
      mobile: normalizeMobile(a.mobile),
      line1: a.line1.trim(),
      line2: optional(a.line2),
      landmark: optional(a.landmark),
      city: a.city.trim(),
      district: optional(a.district),
      state: a.state,
      pincode: a.pincode.trim(),
    };
  }

  private async loadStates(): Promise<void> {
    try {
      this.states.set(await this.api.invoke(apiV1OrdersDeliveryStatesGet, {}));
    } catch {
      // Already reported by the interceptor; the select stays empty and submit explains why.
    }
  }
}

function validate(address: DeliveryAddressDto): Record<string, readonly string[]> {
  const errors: Record<string, readonly string[]> = {};

  for (const field of REQUIRED) {
    if (!address[field]) {
      errors[field] = ['validation.required'];
    }
  }

  if (address.mobile && !isMobile(address.mobile)) {
    errors['mobile'] = ['validation.mobile'];
  }

  if (address.pincode && !isPincode(address.pincode)) {
    errors['pincode'] = ['validation.pincode'];
  }

  return errors;
}
