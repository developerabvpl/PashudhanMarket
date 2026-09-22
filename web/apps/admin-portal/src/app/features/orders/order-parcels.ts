import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { TranslocoPipe } from '@jsverse/transloco';
import { HasPermissionDirective } from '@upbazaar/auth';
import { Api, OrderDto, ShipmentDto, apiV1AdminShippingOrdersOrderIdShipmentsGet } from '@upbazaar/data-access';
import { ToastService } from '@upbazaar/ui';
import { InrCurrencyPipe } from '@upbazaar/util';
import { ShippingPermissions } from '../../core/permissions';
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
            {{ 'orders.partStatus.' + part.status | transloco }} · {{ part.subtotal | inr }}
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
  protected readonly shipments = signal<readonly ShipmentDto[]>([]);

  private readonly api = inject(Api);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);

  constructor() {
    effect(() => {
      const id = this.order().id;
      untracked(() => void this.load(id));
    });
  }

  protected shipmentFor(partId: string): ShipmentDto | undefined {
    return this.shipments().find((s) => s.orderPartId === partId && s.status !== 'Cancelled');
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

  private async load(orderId: string): Promise<void> {
    try {
      this.shipments.set(await this.api.invoke(apiV1AdminShippingOrdersOrderIdShipmentsGet, { orderId }));
    } catch {
      this.shipments.set([]);
    }
  }
}
