import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { TranslocoPipe } from '@jsverse/transloco';
import { HasPermissionDirective } from '@upbazaar/auth';
import {
  Api,
  OrderDto,
  OrderPartDto,
  ShipmentDto,
  apiV1AdminOrdersOrderIdPartsPartIdReturnDecisionPost,
  apiV1AdminOrdersOrderIdPartsPartIdReturnInspectionPost,
  apiV1AdminShippingOrdersOrderIdPartsPartIdReturnPickupPost,
  apiV1AdminShippingOrdersOrderIdShipmentsGet,
  partStatusKey,
} from '@upbazaar/data-access';
import { ToastService } from '@upbazaar/ui';
import { InrCurrencyPipe } from '@upbazaar/util';
import { OrderingPermissions, ShippingPermissions } from '../../core/permissions';
import { PackParcelData, PackParcelDialog } from './pack-parcel.dialog';
import { ShipmentCharges } from './shipment-charges';

/** A product that is coming, or came, back, and how many of it. */
interface ReturnedLine {
  productId: string;
  name: string;
  quantity: number;
}

/**
 * One row per seller's parcel: its status, its courier booking, and "Pack and book" for a part
 * that is ready to go - or whose booking stopped half way, which packing again resumes.
 * Emits `changed` after a booking so the page reloads the order and its part statuses.
 */
@Component({
  selector: 'upb-order-parcels',
  imports: [MatButtonModule, TranslocoPipe, InrCurrencyPipe, HasPermissionDirective, ShipmentCharges],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h3 class="mt-6 font-medium text-ink">{{ 'shipping.parcels' | transloco }}</h3>

    <ul class="mt-2 divide-y divide-border rounded-control border border-border">
      @for (part of order().parts; track part.id; let i = $index) {
      <li class="flex flex-wrap items-start justify-between gap-3 p-3 text-sm">
        <div>
          <p class="font-medium text-ink">
            {{ 'orders.parcel' | transloco: { index: i + 1, count: order().parts.length } }} ·
            {{ statusKey(part) | transloco }} · {{ part.subtotal | inr }}
            <!-- The subtotal is at full price; the coupon's share shows beside it so the parcels add up to the order. -->
            @if (part.discount > 0) { · {{ 'admin.partCoupon' | transloco: { amount: (-part.discount | inr) } }} }
          </p>

          @if (shipmentFor(part.id); as shipment) {
          <p class="mt-1 text-ink-muted">
            {{ 'shipping.forwardShipment' | transloco }}:
            {{ shipment.courierName ?? '—' }} · <span class="font-mono text-xs">{{ shipment.awb ?? '—' }}</span> ·
            {{ 'shipping.statuses.' + shipment.status | transloco }} · {{ shipment.pickupLocation }}
          </p>
          @if (shipment.lastError) {
          <p class="mt-1 text-danger">{{ shipment.lastError }}</p>
          }
          <upb-shipment-charges [shipment]="shipment" (changed)="replace($event)" />
          }
        </div>

        @if (part.returnRequest; as request) {
        <div class="w-full rounded-control bg-surface-sunken p-3">
          <p class="text-ink">
            {{ 'orders.return.reasons.' + request.reason | transloco }} ·
            {{ 'returns.statuses.' + request.status | transloco }}
            @if (request.refundUpiId) { · {{ 'payments.upiTo' | transloco: { upi: request.refundUpiId } }} }
          </p>
          @if (request.comment) { <p class="mt-1 text-ink-muted">{{ 'returns.buyerSays' | transloco: { comment: request.comment } }}</p> }
          @if (request.decisionNote) { <p class="mt-1 text-ink-muted">{{ request.decisionNote }}</p> }
          @if (returned(part).length > 0) {
          <p class="mt-1 text-ink">
            {{ (part.status === 'Returned' ? 'returns.itemsCameBack' : 'returns.itemsBack') | transloco }}
            @for (line of returned(part); track line.productId) { <span class="mr-2">{{ line.quantity }} × {{ line.name }}</span> }
          </p>
          }
          @if (request.refundDue !== null && request.refundDue !== undefined) {
          <p class="mt-1 text-ink-muted">{{ 'returns.refundDue' | transloco: { amount: (request.refundDue | inr) } }}</p>
          }

          @if (request.status === 'Requested') {
          <div *hasPermission="ordersWrite" class="mt-2 flex flex-wrap items-center gap-2">
            <button mat-flat-button color="primary" type="button" (click)="decide(part.id, true)">{{ 'returns.approve' | transloco }}</button>
            <input class="min-w-48 flex-1 rounded-control border border-border bg-surface px-2 py-1.5" maxlength="500"
              [attr.aria-label]="'returns.rejectNote' | transloco" [placeholder]="'returns.rejectNote' | transloco"
              [value]="rejectNote()" (input)="rejectNote.set(value($event))" />
            <button mat-stroked-button color="warn" type="button" [disabled]="!rejectNote().trim()" (click)="decide(part.id, false)">
              {{ 'returns.reject' | transloco }}
            </button>
          </div>
          }

          <!-- The return pickup and what it costs stay listed after the parcel is back and inspected. -->
          @if (returnFor(part.id); as pickup) {
          <p class="mt-1 text-ink-muted">
            {{ 'shipping.returnShipment' | transloco }}:
            {{ pickup.courierName ?? '—' }} · <span class="font-mono text-xs">{{ pickup.awb ?? '—' }}</span> ·
            {{ 'shipping.statuses.' + pickup.status | transloco }}
          </p>
          @if (pickup.lastError) { <p class="mt-1 text-danger">{{ 'returns.pickupFailed' | transloco: { error: pickup.lastError } }}</p> }
          <upb-shipment-charges [shipment]="pickup" (changed)="replace($event)" />
          }

          @if (request.status === 'Approved' && part.status === 'Returning') {
          @if (!returnFor(part.id) || returnFor(part.id)?.status === 'Booking') {
          <button *hasPermission="shipmentsWrite" class="mt-2" mat-stroked-button type="button" (click)="bookReturnPickup(part.id)">
            {{ 'returns.bookPickup' | transloco }}
          </button>
          }
          }
        </div>
        }

        @if (part.status === 'Returned' && !part.returnCondition) {
        <div *hasPermission="ordersWrite" class="w-full space-y-1">
          @for (line of returned(part); track line.productId) {
          <label class="flex items-center justify-between gap-3">
            <span>{{ line.quantity }} × {{ line.name }}</span>
            <select class="rounded-control border border-border bg-surface px-2 py-1" (change)="setCondition(part.id, line.productId, value($event))">
              <option value="Good" [selected]="conditionOf(part.id, line.productId) === 'Good'">{{ 'returns.good' | transloco }}</option>
              <option value="Damaged" [selected]="conditionOf(part.id, line.productId) === 'Damaged'">{{ 'returns.damaged' | transloco }}</option>
            </select>
          </label>
          }
          <button mat-stroked-button type="button" (click)="inspect(part)">{{ 'returns.recordInspection' | transloco }}</button>
        </div>
        } @else if (part.returnCondition) {
        <!-- Titled, so the sentence reads as the outcome of the inspection rather than a stray note under the parcel. -->
        <p class="w-full text-sm text-ink-muted">
          <span class="font-medium text-ink">{{ 'returns.inspectionTitle' | transloco }}:</span>
          {{ 'returns.inspectedAs.' + part.returnCondition | transloco }}
        </p>
        }

        @if (canPack(part.id, part.status)) {
        <button *hasPermission="shipmentsWrite" mat-stroked-button type="button" (click)="pack(part.id)">
          {{ (shipmentFor(part.id) ? 'shipping.resumeBooking' : 'shipping.packAndBook') | transloco }}
        </button>
        }
      </li>
      }
    </ul>
  `,
})
export class OrderParcels {
  readonly order = input.required<OrderDto>();
  readonly changed = output<void>();

  protected readonly shipmentsWrite = ShippingPermissions.ShipmentsWrite;
  protected readonly ordersWrite = OrderingPermissions.Write;
  protected readonly shipments = signal<readonly ShipmentDto[]>([]);
  protected readonly rejectNote = signal('');
  protected readonly statusKey = partStatusKey;

  /** Conditions chosen for what came back, by part and product; Good until changed. */
  private readonly conditions = signal<Readonly<Record<string, string>>>({});

  private readonly api = inject(Api);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);

  constructor() {
    effect(() => {
      const id = this.order().id;
      untracked(() => void this.load(id));
    });
  }

  /** Shows a shipment as it is after a charge was corrected, without reloading the order. */
  protected replace(updated: ShipmentDto): void {
    this.shipments.update((list) => list.map((s) => (s.id === updated.id ? updated : s)));
  }

  /** The live booking that took the part to the buyer. */
  protected shipmentFor(partId: string): ShipmentDto | undefined {
    return this.shipments().find((s) => s.orderPartId === partId && s.direction === 'Forward' && s.status !== 'Cancelled');
  }

  /** The live booking collecting a buyer's return. */
  protected returnFor(partId: string): ShipmentDto | undefined {
    return this.shipments().find((s) => s.orderPartId === partId && s.direction === 'Return' && s.status !== 'Cancelled');
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  /**
   * Accepts or refuses a buyer's return on the seller's behalf: for a seller who has not
   * answered, or a dispute the buyer took to support.
   */
  protected async decide(partId: string, approve: boolean): Promise<void> {
    try {
      await this.api.invoke(apiV1AdminOrdersOrderIdPartsPartIdReturnDecisionPost, {
        orderId: this.order().id,
        partId,
        body: { approve, note: approve ? null : this.rejectNote().trim() },
      });
      this.rejectNote.set('');
      this.toast.success(approve ? 'returns.approvedToast' : 'returns.rejectedToast');
      this.changed.emit();
    } catch {
      // Reported by the interceptor.
    }
  }

  /** Books the return collection again when booking it on approval did not go through. */
  protected async bookReturnPickup(partId: string): Promise<void> {
    try {
      await this.api.invoke(apiV1AdminShippingOrdersOrderIdPartsPartIdReturnPickupPost, { orderId: this.order().id, partId });
      this.toast.success('returns.pickupBooked');
    } catch {
      // Reported by the interceptor; the reload below shows where the booking stopped.
    }

    await this.load(this.order().id);
  }

  /** Ready to pack, or booked only part way. */
  protected canPack(partId: string, status: string): boolean {
    const shipment = this.shipmentFor(partId);

    return shipment ? shipment.status === 'Booking' : status === 'Confirmed';
  }

  protected async pack(partId: string): Promise<void> {
    const reference = this.dialog.open<PackParcelDialog, PackParcelData, ShipmentDto>(PackParcelDialog, {
      data: { orderId: this.order().id, partId, orderNumber: this.order().number },
      width: '30rem',
    });

    const shipment = await new Promise<ShipmentDto | undefined>((resolve) =>
      reference.afterClosed().subscribe((result) => resolve(result))
    );

    // A failed booking step still changed the shipment, so reload either way.
    await this.load(this.order().id);

    if (shipment) {
      this.toast.success('shipping.booked');
      this.changed.emit();
    }
  }

  /** What goes back to the seller from a part: returned units, or all of an undelivered parcel. */
  protected returned(part: OrderPartDto): ReturnedLine[] {
    const named = part.lines.some((l) => (l.returnQuantity ?? 0) > 0);
    const buyerReturn = part.returnRequest?.status === 'Approved';

    return part.lines
      .map((l) => ({ productId: l.productId, name: l.name, quantity: buyerReturn && named ? (l.returnQuantity ?? 0) : l.quantity }))
      .filter((l) => l.quantity > 0);
  }

  protected conditionOf(partId: string, productId: string): string {
    return this.conditions()[`${partId}:${productId}`] ?? 'Good';
  }

  protected setCondition(partId: string, productId: string, condition: string): void {
    this.conditions.update((c) => ({ ...c, [`${partId}:${productId}`]: condition }));
  }

  /**
   * Inspects what came back on the seller's behalf - for a seller without portal access, or one
   * who asked support to do it - product by product. Good units are restocked; damaged ones are not.
   */
  protected async inspect(part: OrderPartDto): Promise<void> {
    const lines = this.returned(part).map((l) => ({ productId: l.productId, condition: this.conditionOf(part.id, l.productId) }));

    try {
      await this.api.invoke(apiV1AdminOrdersOrderIdPartsPartIdReturnInspectionPost, {
        orderId: this.order().id,
        partId: part.id,
        body: { condition: null, note: null, lines },
      });
      this.toast.success(lines.some((l) => l.condition === 'Good') ? 'returns.restocked' : 'returns.recordedDamaged');
      this.changed.emit();
    } catch {
      // Reported by the interceptor.
    }
  }

  private async load(orderId: string): Promise<void> {
    try {
      this.shipments.set(await this.api.invoke(apiV1AdminShippingOrdersOrderIdShipmentsGet, { orderId }));
    } catch {
      this.shipments.set([]);
    }
  }
}
