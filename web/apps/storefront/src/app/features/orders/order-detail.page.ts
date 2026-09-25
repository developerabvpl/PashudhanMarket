import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  Api,
  OrderDto,
  ShipmentDto,
  apiV1OrdersOrderIdCancelPost,
  apiV1ShippingOrdersOrderIdShipmentsGet,
  ordersGetMine,
} from '@upbazaar/data-access';
import { PageState, ToastService } from '@upbazaar/ui';
import { DateIstPipe, InrCurrencyPipe } from '@upbazaar/util';
import { SeoService } from '../../core/seo.service';
import { OrderPayPanel } from '../payments/order-pay-panel';
import { orderStatusBadge, partStatusBadge } from './order-labels';
import { ReturnPanel } from './return-panel';
import { ReviewPanel } from './review-panel';

/**
 * One of the buyer's orders, and the confirmation page too: checkout lands here with ?placed=1,
 * which adds the thank-you banner. Keeping one page means the order a buyer sees right after
 * placing it is exactly the one they will find later under My orders.
 *
 * Each seller's part is shown with its own status, because they ship separately and a buyer
 * waiting on one parcel needs to see that the other has already arrived.
 */
@Component({
  selector: 'upb-order-detail-page',
  imports: [RouterLink, TranslocoPipe, InrCurrencyPipe, DateIstPipe, PageState, OrderPayPanel, ReturnPanel, ReviewPanel],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-4xl px-4 py-8 sm:py-12">
      <a class="text-sm text-accent-600 hover:underline" routerLink="/orders">
        ← {{ 'orders.backToList' | transloco }}
      </a>

      @if (order(); as o) {
      @if (placed() === '1' && o.status !== 'Cancelled') {
      <div class="mt-4 rounded-card border border-success/40 bg-success/10 p-5" role="status">
        <p class="text-lg font-semibold text-ink">{{ 'orders.placedTitle' | transloco }}</p>
        <p class="mt-1 text-sm text-ink">{{ 'orders.placedBody' | transloco: { number: o.number } }}</p>
      </div>
      }

      <div class="mt-6 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 class="text-2xl font-bold tracking-tight text-ink">
            {{ 'orders.orderNumber' | transloco: { number: o.number } }}
          </h1>
          <p class="mt-1 text-sm text-ink-muted">
            {{ 'orders.placedOn' | transloco: { date: (o.placedAtUtc | dateIst: 'datetime') } }}
          </p>
        </div>
        <span class="rounded-full px-3 py-1 text-sm font-medium" [class]="badge().tone">
          {{ badge().key | transloco }}
        </span>
      </div>

      @if (o.status === 'PendingPayment') {
      <upb-order-pay-panel [order]="o" [autoStart]="pay() === '1'" (settled)="load()" />
      }

      @if (o.status === 'Cancelled' && o.cancellationReason) {
      <p class="mt-4 text-sm text-ink-muted">
        {{ 'orders.cancelledBecause' | transloco: { reason: o.cancellationReason } }}
      </p>
      }

      <div class="mt-6 space-y-4">
        @for (part of o.parts; track part.id; let i = $index) {
        <div class="rounded-card border border-border bg-surface">
          <div class="flex items-center justify-between border-b border-border px-4 py-3">
            <p class="text-sm font-medium text-ink">
              {{ 'orders.parcel' | transloco: { index: i + 1, count: o.parts.length } }}
            </p>
            <span class="rounded-full px-2.5 py-0.5 text-xs font-medium" [class]="partBadge(part.status).tone">
              {{ partBadge(part.status, part.returnRequest?.status === 'Approved').key | transloco }}
            </span>
          </div>
          <ul class="divide-y divide-border">
            @for (line of part.lines; track line.productId) {
            <li class="px-4 py-3 text-sm">
              <div class="flex justify-between gap-4">
                <a class="min-w-0 text-ink hover:text-brand-700" [routerLink]="['/products', line.productId]">
                  {{ line.name }}
                  <span class="text-ink-muted">× {{ line.quantity }}</span>
                </a>
                <span class="shrink-0 font-medium text-ink" [class.line-through]="part.status === 'Cancelled'">
                  {{ line.lineTotal | inr: 'symbol' : 'auto' }}
                </span>
              </div>
              <!-- Once delivered, always reviewable - even if the parcel later went back. -->
              @if (part.deliveredAtUtc) {
              <upb-review-panel [productId]="line.productId" />
              }
            </li>
            }
          </ul>
          @if (shipmentFor(part.id); as shipment) { @if (shipment.awb) {
          <div class="flex flex-wrap items-center justify-between gap-2 border-t border-border px-4 py-3 text-sm">
            <p class="text-ink-muted">
              {{ 'orders.shippedWith' | transloco: { courier: shipment.courierName ?? '—', awb: shipment.awb } }}
            </p>
            @if (shipment.trackingUrl) {
            <a class="font-medium text-accent-600 hover:underline" [href]="shipment.trackingUrl" target="_blank" rel="noopener">
              {{ 'orders.track' | transloco }}
            </a>
            }
          </div>
          } }
          <upb-return-panel [order]="o" [part]="part" [pickup]="pickupFor(part.id)" (requested)="order.set($event)" />
        </div>
        }
      </div>

      <div class="mt-6 grid gap-4 sm:grid-cols-2">
        <div class="rounded-card border border-border bg-surface p-4 text-sm">
          <h2 class="font-medium text-ink">{{ 'orders.deliverTo' | transloco }}</h2>
          <address class="mt-2 not-italic leading-relaxed text-ink-muted">
            <span class="text-ink">{{ o.deliveryAddress.fullName }}</span><br />
            {{ o.deliveryAddress.line1 }}<br />
            @if (o.deliveryAddress.line2) { {{ o.deliveryAddress.line2 }}<br /> }
            @if (o.deliveryAddress.landmark) { {{ o.deliveryAddress.landmark }}<br /> }
            {{ o.deliveryAddress.city }}@if (o.deliveryAddress.district) {, {{ o.deliveryAddress.district }} }<br />
            {{ o.deliveryAddress.state }} {{ o.deliveryAddress.pincode }}<br />
            +91 {{ o.deliveryAddress.mobile }}
          </address>
        </div>

        <div class="rounded-card border border-border bg-surface p-4 text-sm">
          <h2 class="font-medium text-ink">{{ 'orders.payment' | transloco }}</h2>
          <p class="mt-2 text-ink-muted">{{ 'orders.paymentMethod.' + o.paymentMethod | transloco }}</p>
          <dl class="mt-3 space-y-1.5">
            <div class="flex justify-between">
              <dt class="text-ink-muted">{{ 'cart.subtotal' | transloco }}</dt>
              <dd class="text-ink">{{ o.subtotal | inr: 'symbol' : 'auto' }}</dd>
            </div>
            @if (o.discount > 0) {
            <div class="flex justify-between">
              <dt class="text-ink-muted">{{ 'checkout.coupon.discount' | transloco: { code: o.couponCode ?? '' } }}</dt>
              <dd class="text-success">− {{ o.discount | inr: 'symbol' : 'auto' }}</dd>
            </div>
            }
            <div class="flex justify-between">
              <dt class="text-ink-muted">{{ 'checkout.delivery' | transloco }}</dt>
              <dd class="text-ink">
                @if (o.shippingFee > 0) { {{ o.shippingFee | inr: 'symbol' : 'auto' }} } @else {
                {{ 'checkout.free' | transloco }} }
              </dd>
            </div>
            <div class="flex justify-between border-t border-border pt-1.5 font-bold">
              <dt class="text-ink">{{ 'checkout.total' | transloco }}</dt>
              <dd class="text-ink">{{ o.total | inr: 'symbol' : 'auto' }}</dd>
            </div>
          </dl>
        </div>
      </div>

      @if (o.canCancel) {
      <div class="mt-6">
        @if (!confirming()) {
        <button
          type="button"
          class="text-sm text-ink-muted underline-offset-2 transition-colors hover:text-danger hover:underline"
          (click)="confirming.set(true)"
        >
          {{ 'orders.cancel' | transloco }}
        </button>
        } @else {
        <div class="flex flex-wrap items-center gap-3 rounded-card border border-border bg-surface p-4" role="alertdialog"
          aria-labelledby="cancel-question">
          <p id="cancel-question" class="text-sm text-ink">{{ 'orders.cancelQuestion' | transloco }}</p>
          <button
            type="button"
            class="rounded-control bg-danger px-4 py-2 text-sm font-semibold text-white disabled:opacity-50"
            [disabled]="busy()"
            (click)="cancel(o)"
          >
            {{ 'orders.cancelConfirm' | transloco }}
          </button>
          <button type="button" class="text-sm text-ink-muted hover:underline" (click)="confirming.set(false)">
            {{ 'orders.keep' | transloco }}
          </button>
        </div>
        }
      </div>
      }
      } @else {
      <div class="mt-6">
        <upb-page-state [state]="failed() ? 'error' : 'loading'" (retry)="load()" />
      </div>
      }
    </section>
  `,
})
export class OrderDetailPage {
  /** Route parameter, bound by the router's component input binding. */
  readonly orderId = input.required<string>();

  /** "1" when checkout has just brought the buyer here. */
  readonly placed = input<string | undefined>(undefined);

  /** "1" when checkout placed an online order and payment should open straight away. */
  readonly pay = input<string | undefined>(undefined);

  protected readonly order = signal<OrderDto | null>(null);
  protected readonly shipments = signal<readonly ShipmentDto[]>([]);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);
  protected readonly confirming = signal(false);

  protected readonly badge = computed(() => orderStatusBadge(this.order()?.status ?? ''));
  protected readonly partBadge = partStatusBadge;

  private readonly api = inject(Api);
  private readonly toast = inject(ToastService);

  constructor() {
    inject(SeoService).apply({
      title: 'Your order',
      description: 'An order you placed on UP Bazaar.',
      canonicalPath: '/orders',
      noIndex: true,
    });

    // Reloads when the route moves to another order without leaving this page.
    effect(() => {
      this.orderId();
      untracked(() => void this.load());
    });
  }

  /** The live courier booking that brought a seller's part to the buyer, if it has one. */
  protected shipmentFor(partId: string): ShipmentDto | undefined {
    return this.shipments().find((s) => s.orderPartId === partId && s.direction === 'Forward' && s.status !== 'Cancelled');
  }

  /** The courier booked to collect a part the buyer is returning, once there is one. */
  protected pickupFor(partId: string): ShipmentDto | undefined {
    return this.shipments().find((s) => s.orderPartId === partId && s.direction === 'Return' && s.status !== 'Cancelled');
  }

  /**
   * Tracking is a nicety on top of the order, fetched after it and allowed to fail quietly:
   * an order whose parcels cannot be looked up right now is still an order the buyer can read.
   */
  private async loadShipments(): Promise<void> {
    try {
      this.shipments.set(await this.api.invoke(apiV1ShippingOrdersOrderIdShipmentsGet, { orderId: this.orderId() }));
    } catch {
      this.shipments.set([]);
    }
  }

  protected async load(): Promise<void> {
    this.failed.set(false);

    try {
      this.order.set(await this.api.invoke(ordersGetMine, { orderId: this.orderId() }));
      void this.loadShipments();
    } catch {
      this.failed.set(true);
    }
  }

  protected async cancel(order: OrderDto): Promise<void> {
    this.busy.set(true);

    try {
      this.order.set(await this.api.invoke(apiV1OrdersOrderIdCancelPost, { orderId: order.id, body: { reason: null } }));
      this.toast.info('orders.cancelled');
    } catch {
      // The interceptor has said why (usually: part of it has shipped). Show the order as it is now.
      await this.load();
    } finally {
      this.busy.set(false);
      this.confirming.set(false);
    }
  }
}
