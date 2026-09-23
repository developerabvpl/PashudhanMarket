import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  Api,
  ApiProblem,
  ParcelSuggestionDto,
  SellerOrderDto,
  ShipmentDto,
  apiV1SellerOrdersOrderIdGet,
  apiV1SellerShippingOrdersOrderIdPartsPartIdPackPost,
  apiV1SellerShippingOrdersOrderIdPartsPartIdParcelGet,
  apiV1SellerShippingOrdersOrderIdPartsPartIdReturnPickupPost,
  apiV1SellerShippingOrdersOrderIdShipmentsGet,
  toApiProblem,
} from '@upbazaar/data-access';
import { ToastService } from '@upbazaar/ui';
import { DateIstPipe, InrCurrencyPipe } from '@upbazaar/util';
import { ReturnDecision } from './return-decision';
import { ReturnInspection } from './return-inspection';

type ParcelField = 'weightGrams' | 'lengthCm' | 'breadthCm' | 'heightCm';

/**
 * One of the seller's parts of an order: what to pack, where it goes, and packing it.
 *
 * The packing form sits on the page rather than in a dialog because it is the whole point of
 * the page: the seller reads the lines, packs the box, weighs it, and books the courier here.
 * A booking that stopped half way offers to finish instead.
 */
@Component({
  selector: 'upb-seller-order-page',
  imports: [RouterLink, TranslocoPipe, InrCurrencyPipe, DateIstPipe, MatButtonModule, MatFormFieldModule, MatInputModule, ReturnDecision, ReturnInspection],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-3xl space-y-6 px-4 py-8">
      <a class="text-sm" routerLink="/orders">← {{ 'sellerPortal.ordersTitle' | transloco }}</a>

      @if (order(); as o) {
      <header class="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 class="font-mono text-2xl font-semibold text-ink">{{ o.orderNumber }}</h1>
          <p class="text-sm text-ink-muted">{{ o.placedAtUtc | dateIst: 'datetime' }}</p>
        </div>
        <span class="rounded-full bg-surface-sunken px-3 py-1 text-sm">
          {{ (o.returnRequest?.status === 'Approved' && (o.status === 'Returning' || o.status === 'Returned')
            ? 'orders.returnStatus.' : 'orders.partStatus.') + o.status | transloco }}
        </span>
      </header>

      <div class="upb-card p-5">
        <h2 class="font-medium text-ink">{{ 'sellerPortal.toPack' | transloco }}</h2>
        <ul class="mt-2 divide-y divide-border text-sm">
          @for (line of o.lines; track line.productId) {
          <li class="flex justify-between gap-3 py-2">
            <span><span class="font-mono text-xs">{{ line.sku }}</span> {{ line.name }}</span>
            <span class="font-medium">× {{ line.quantity }}</span>
          </li>
          }
        </ul>
        <p class="mt-3 text-sm">
          {{ 'sellerPortal.partValue' | transloco: { amount: (o.subtotal | inr) } }}
          @if (o.codAmount > 0) { · {{ 'sellerPortal.codCollect' | transloco: { amount: (o.codAmount | inr) } }} }
        </p>
      </div>

      <div class="upb-card p-5 text-sm">
        <h2 class="font-medium text-ink">{{ 'orders.deliverTo' | transloco }}</h2>
        <address class="mt-2 not-italic leading-relaxed text-ink-muted">
          <span class="text-ink">{{ o.deliveryAddress.fullName }}</span> · +91 {{ o.deliveryAddress.mobile }}<br />
          {{ o.deliveryAddress.line1 }}@if (o.deliveryAddress.line2) {, {{ o.deliveryAddress.line2 }} }<br />
          {{ o.deliveryAddress.city }}, {{ o.deliveryAddress.state }} {{ o.deliveryAddress.pincode }}
        </address>
      </div>

      @switch (o.returnRequest?.status) {
      @case ('Requested') {
      <upb-return-decision [order]="o" (decided)="load(o.orderId)" />
      }
      @case ('Rejected') {
      <p class="upb-card p-5 text-sm">{{ 'returns.refusedNote' | transloco: { note: o.returnRequest?.decisionNote ?? '' } }}</p>
      }
      @case ('Approved') { @if (o.status === 'Returning') {
      <div class="upb-card space-y-2 p-5 text-sm">
        <p class="text-ink">{{ 'returns.awaitingPickup' | transloco }}</p>
        @if (returnShipment(); as r) {
        <p>
          {{ r.courierName ?? '—' }} · AWB <span class="font-mono">{{ r.awb ?? '—' }}</span> ·
          {{ 'shipping.statuses.' + r.status | transloco }}
        </p>
        }
        @if (!returnShipment() || returnShipment()?.status === 'Booking') {
        @if (returnShipment()?.lastError; as error) {
        <p class="text-danger">{{ 'returns.pickupFailed' | transloco: { error: error } }}</p>
        }
        <button mat-stroked-button type="button" [disabled]="busy()" (click)="bookReturnPickup(o)">
          {{ 'returns.bookPickup' | transloco }}
        </button>
        }
      </div>
      } }
      }

      @if (o.status === 'Returned') { @if (o.returnCondition) {
      <p class="upb-card p-5 text-sm">{{ 'returns.inspectedAs.' + o.returnCondition | transloco }}</p>
      } @else {
      <upb-return-inspection [order]="o" (inspected)="order.set($event)" />
      } }

      @if (shipment(); as s) {
      <div class="upb-card p-5 text-sm">
        <h2 class="font-medium text-ink">{{ 'sellerPortal.courier' | transloco }}</h2>
        <p class="mt-2">
          {{ s.courierName ?? '—' }} · AWB <span class="font-mono">{{ s.awb ?? '—' }}</span> ·
          {{ 'shipping.statuses.' + s.status | transloco }} · {{ s.pickupLocation }}
        </p>
        @if (s.lastError) { <p class="mt-1 text-danger">{{ s.lastError }}</p> }
      </div>
      }

      @if (canPack()) {
      <form class="upb-card space-y-3 p-5" (submit)="pack($event)">
        <h2 class="font-medium text-ink">
          {{ (shipment() ? 'shipping.resumeBooking' : 'shipping.packAndBook') | transloco }}
        </h2>
        @if (suggestion(); as sg) {
        <p class="text-sm">
          {{ 'shipping.pickupFrom' | transloco: { location: sg.pickupLocation ?? ('shipping.noPickup' | transloco) } }}
        </p>
        @if (sg.missingSkus.length > 0) {
        <p class="text-sm text-danger">{{ 'shipping.missingPackages' | transloco: { skus: sg.missingSkus.join(', ') } }}</p>
        } @else {
        <p class="text-sm text-ink-muted">{{ 'shipping.suggested' | transloco }}</p>
        } }
        <div class="grid grid-cols-2 gap-3 sm:grid-cols-4">
          @for (field of fields; track field.key) {
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ field.label | transloco }}</mat-label>
            <input matInput type="number" min="0" step="any" [name]="field.key" [value]="parcel()[field.key]" (input)="set(field.key, $event)" />
          </mat-form-field>
          }
        </div>
        @if (problem(); as p) { <p class="text-sm text-danger" role="alert">{{ p.title }}</p> }
        <button mat-flat-button color="primary" type="submit" [disabled]="busy() || !complete()">
          {{ (busy() ? 'shipping.booking' : 'shipping.packAndBook') | transloco }}
        </button>
      </form>
      }
      }
    </section>
  `,
})
export class OrderPage {
  /** Route parameter. */
  readonly orderId = input.required<string>();

  protected readonly fields: readonly { key: ParcelField; label: string }[] = [
    { key: 'weightGrams', label: 'shipping.weightGrams' },
    { key: 'lengthCm', label: 'shipping.lengthCm' },
    { key: 'breadthCm', label: 'shipping.breadthCm' },
    { key: 'heightCm', label: 'shipping.heightCm' },
  ];

  protected readonly order = signal<SellerOrderDto | null>(null);
  protected readonly shipment = signal<ShipmentDto | null>(null);

  /** The courier collecting a buyer's return, once booked. */
  protected readonly returnShipment = signal<ShipmentDto | null>(null);
  protected readonly suggestion = signal<ParcelSuggestionDto | null>(null);
  protected readonly parcel = signal<Record<ParcelField, string>>({ weightGrams: '', lengthCm: '', breadthCm: '', heightCm: '' });
  protected readonly busy = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);

  /** A confirmed part with no live booking, or a booking that stopped half way. */
  protected readonly canPack = computed(() => {
    const shipment = this.shipment();

    return shipment ? shipment.status === 'Booking' : this.order()?.status === 'Confirmed';
  });

  private readonly api = inject(Api);
  private readonly toast = inject(ToastService);

  constructor() {
    effect(() => {
      const id = this.orderId();
      untracked(() => void this.load(id));
    });
  }

  protected complete(): boolean {
    return Object.values(this.parcel()).every((v) => Number(v) > 0);
  }

  protected set(field: ParcelField, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.parcel.update((p) => ({ ...p, [field]: value }));
  }

  protected async pack(event: Event): Promise<void> {
    event.preventDefault();
    const order = this.order();

    if (!order) {
      return;
    }

    this.busy.set(true);
    this.problem.set(null);

    const p = this.parcel();

    try {
      this.shipment.set(await this.api.invoke(apiV1SellerShippingOrdersOrderIdPartsPartIdPackPost, {
        orderId: order.orderId,
        partId: order.partId,
        body: {
          parcel: {
            weightGrams: Math.round(Number(p.weightGrams)),
            lengthCm: Number(p.lengthCm),
            breadthCm: Number(p.breadthCm),
            heightCm: Number(p.heightCm),
          },
        },
      }));
      this.toast.success('shipping.booked');
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
      await this.load(order.orderId);
    }
  }

  /** Books the return collection again when booking it on approval did not go through. */
  protected async bookReturnPickup(order: SellerOrderDto): Promise<void> {
    this.busy.set(true);

    try {
      this.returnShipment.set(await this.api.invoke(apiV1SellerShippingOrdersOrderIdPartsPartIdReturnPickupPost, {
        orderId: order.orderId,
        partId: order.partId,
      }));
      this.toast.success('returns.pickupBooked');
    } catch {
      // Reported by the interceptor; reloading shows what the booking got stuck on.
      await this.load(order.orderId);
    } finally {
      this.busy.set(false);
    }
  }

  protected async load(orderId: string): Promise<void> {
    try {
      const order = await this.api.invoke(apiV1SellerOrdersOrderIdGet, { orderId });
      const shipments = await this.api.invoke(apiV1SellerShippingOrdersOrderIdShipmentsGet, { orderId });
      const live = (direction: string) =>
        shipments.find((s) => s.orderPartId === order.partId && s.direction === direction && s.status !== 'Cancelled') ?? null;

      this.order.set(order);
      this.shipment.set(live('Forward'));
      this.returnShipment.set(live('Return'));

      if (this.canPack() && !this.suggestion()) {
        await this.loadSuggestion(order);
      }
    } catch {
      // Reported by the interceptor.
    }
  }

  private async loadSuggestion(order: SellerOrderDto): Promise<void> {
    const suggestion = await this.api.invoke(apiV1SellerShippingOrdersOrderIdPartsPartIdParcelGet, {
      orderId: order.orderId,
      partId: order.partId,
    });

    this.suggestion.set(suggestion);

    if (suggestion.parcel) {
      const p = suggestion.parcel;
      this.parcel.set({
        weightGrams: String(p.weightGrams),
        lengthCm: String(p.lengthCm),
        breadthCm: String(p.breadthCm),
        heightCm: String(p.heightCm),
      });
    }
  }
}
