import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { TranslocoPipe } from '@jsverse/transloco';
import { HasPermissionDirective } from '@upbazaar/auth';
import {
  Api,
  OrderDto,
  ShipmentDto,
  apiV1AdminOrdersOrderIdPartsPartIdReturnDecisionPost,
  apiV1AdminOrdersOrderIdPartsPartIdReturnInspectionPost,
  apiV1AdminShippingOrdersOrderIdPartsPartIdReturnPickupPost,
  apiV1AdminShippingOrdersOrderIdShipmentsGet,
} from '@upbazaar/data-access';
import { ToastService } from '@upbazaar/ui';
import { InrCurrencyPipe } from '@upbazaar/util';
import { OrderingPermissions, ShippingPermissions } from '../../core/permissions';
import { PackParcelData, PackParcelDialog } from './pack-parcel.dialog';

/**
 * One row per seller's parcel: its status, its courier booking, and "Pack and book" for a part
 * that is ready to go - or whose booking stopped half way, which packing again resumes.
 * Emits `changed` after a booking so the page reloads the order and its part statuses.
 */
@Component({
  selector: 'upb-order-parcels',
  imports: [MatButtonModule, TranslocoPipe, InrCurrencyPipe, HasPermissionDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h3 class="mt-6 font-medium text-ink">{{ 'shipping.parcels' | transloco }}</h3>

    <ul class="mt-2 divide-y divide-border rounded-control border border-border">
      @for (part of order().parts; track part.id; let i = $index) {
      <li class="flex flex-wrap items-start justify-between gap-3 p-3 text-sm">
        <div>
          <p class="font-medium text-ink">
            {{ 'orders.parcel' | transloco: { index: i + 1, count: order().parts.length } }} ·
            {{ (part.returnRequest?.status === 'Approved' && (part.status === 'Returning' || part.status === 'Returned')
              ? 'orders.returnStatus.' : 'orders.partStatus.') + part.status | transloco }} · {{ part.subtotal | inr }}
          </p>

          @if (shipmentFor(part.id); as shipment) {
          <p class="mt-1 text-ink-muted">
            {{ shipment.courierName ?? '—' }} · <span class="font-mono text-xs">{{ shipment.awb ?? '—' }}</span> ·
            {{ 'shipping.statuses.' + shipment.status | transloco }} · {{ shipment.pickupLocation }}
          </p>
          @if (shipment.lastError) {
          <p class="mt-1 text-danger">{{ shipment.lastError }}</p>
          } }
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

          @if (request.status === 'Approved' && part.status === 'Returning') {
          @if (returnFor(part.id); as pickup) {
          <p class="mt-1 text-ink-muted">
            {{ pickup.courierName ?? '—' }} · <span class="font-mono text-xs">{{ pickup.awb ?? '—' }}</span> ·
            {{ 'shipping.statuses.' + pickup.status | transloco }}
          </p>
          @if (pickup.lastError) { <p class="mt-1 text-danger">{{ 'returns.pickupFailed' | transloco: { error: pickup.lastError } }}</p> }
          }
          @if (!returnFor(part.id) || returnFor(part.id)?.status === 'Booking') {
          <button *hasPermission="shipmentsWrite" class="mt-2" mat-stroked-button type="button" (click)="bookReturnPickup(part.id)">
            {{ 'returns.bookPickup' | transloco }}
          </button>
          }
          }
        </div>
        }

        @if (part.status === 'Returned' && !part.returnCondition) {
        <div *hasPermission="ordersWrite" class="flex gap-2">
          <button mat-stroked-button type="button" (click)="inspect(part.id, 'Good')">{{ 'returns.good' | transloco }}</button>
          <button mat-stroked-button color="warn" type="button" (click)="inspect(part.id, 'Damaged')">{{ 'returns.damaged' | transloco }}</button>
        </div>
        } @else if (part.returnCondition) {
        <span class="text-sm text-ink-muted">{{ 'returns.inspectedAs.' + part.returnCondition | transloco }}</span>
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

  private readonly api = inject(Api);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);

  constructor() {
    effect(() => {
      const id = this.order().id;
      untracked(() => void this.load(id));
    });
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

  /**
   * Inspects a returned parcel on the seller's behalf - for a seller without portal access, or
   * one who asked support to do it. Good restocks it; Damaged does not.
   */
  protected async inspect(partId: string, condition: 'Good' | 'Damaged'): Promise<void> {
    try {
      await this.api.invoke(apiV1AdminOrdersOrderIdPartsPartIdReturnInspectionPost, {
        orderId: this.order().id,
        partId,
        body: { condition, note: null },
      });
      this.toast.success(condition === 'Good' ? 'returns.restocked' : 'returns.recordedDamaged');
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
