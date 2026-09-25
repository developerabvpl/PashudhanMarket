import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoPipe } from '@jsverse/transloco';
import { HasPermissionDirective } from '@upbazaar/auth';
import { Api, ShipmentChargeDto, ShipmentDto, apiV1AdminShippingShipmentsShipmentIdChargesTripPut } from '@upbazaar/data-access';
import { ToastService } from '@upbazaar/ui';
import { InrCurrencyPipe } from '@upbazaar/util';
import { ShippingPermissions } from '../../core/permissions';

/**
 * What each courier trip of a shipment costs the seller: quoted when it was booked, charged when
 * the courier made the trip. Staff correct a charge from Shiprocket's invoice - when a parcel could
 * not be priced, or was re-weighed and billed more - and the seller is charged the difference.
 */
@Component({
  selector: 'upb-shipment-charges',
  imports: [TranslocoPipe, InrCurrencyPipe, HasPermissionDirective, MatButtonModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (shipment().quoteError; as error) {
    <p class="mt-1 text-warning">{{ 'shipping.charges.notQuoted' | transloco: { reason: error } }}</p>
    }
    @for (charge of shipment().charges; track charge.trip) {
    <div class="mt-1 flex flex-wrap items-center gap-x-2 text-ink-muted">
      <span>{{ 'shipping.charges.trips.' + charge.trip | transloco }}:</span>
      @if (charge.amount !== null) {
      <span class="text-ink">{{ charge.amount | inr }}</span>
      } @else {
      <span class="text-warning">{{ 'shipping.charges.unknown' | transloco }}</span>
      }
      <span>· {{ (charge.incurredAtUtc ? 'shipping.charges.charged' : 'shipping.charges.notYet') | transloco: { amount: (charge.charged | inr) } }}</span>
      @if (charge.note) { <span>· {{ charge.note }}</span> }
      @if (editing() !== charge.trip) {
      <button *hasPermission="shipmentsWrite" mat-button type="button" (click)="edit(charge)">{{ 'shipping.charges.correct' | transloco }}</button>
      }
    </div>
    @if (editing() === charge.trip) {
    <form class="mt-2 flex flex-wrap items-start gap-2" (submit)="save($event, charge.trip)">
      <mat-form-field class="w-32" subscriptSizing="dynamic">
        <mat-label>{{ 'shipping.charges.amount' | transloco }}</mat-label>
        <input matInput type="number" min="0" step="0.01" name="amount" [value]="amount()" (input)="amount.set(value($event))" />
      </mat-form-field>
      <mat-form-field class="min-w-48 flex-1" subscriptSizing="dynamic">
        <mat-label>{{ 'shipping.charges.note' | transloco }}</mat-label>
        <input matInput name="note" maxlength="200" [value]="note()" (input)="note.set(value($event))" />
      </mat-form-field>
      <button mat-flat-button color="primary" type="submit" [disabled]="busy() || !isAmount()">{{ 'common.save' | transloco }}</button>
      <button mat-button type="button" (click)="editing.set(null)">{{ 'common.cancel' | transloco }}</button>
    </form>
    }
    }
  `,
})
export class ShipmentCharges {
  readonly shipment = input.required<ShipmentDto>();

  /** The shipment as it is after a correction. */
  readonly changed = output<ShipmentDto>();

  protected readonly shipmentsWrite = ShippingPermissions.ShipmentsWrite;
  protected readonly editing = signal<string | null>(null);
  protected readonly amount = signal('');
  protected readonly note = signal('');
  protected readonly busy = signal(false);

  private readonly api = inject(Api);
  private readonly toast = inject(ToastService);

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected isAmount(): boolean {
    const amount = Number(this.amount());

    return this.amount().trim() !== '' && Number.isFinite(amount) && amount >= 0;
  }

  protected edit(charge: ShipmentChargeDto): void {
    this.amount.set(charge.amount === null ? '' : String(charge.amount));
    this.note.set('');
    this.editing.set(charge.trip);
  }

  protected async save(event: Event, trip: string): Promise<void> {
    event.preventDefault();
    this.busy.set(true);

    try {
      const updated = await this.api.invoke(apiV1AdminShippingShipmentsShipmentIdChargesTripPut, {
        shipmentId: this.shipment().id,
        trip,
        body: { amount: Number(this.amount()), note: this.note().trim() || null },
      });

      this.toast.success('shipping.charges.saved');
      this.editing.set(null);
      this.changed.emit(updated);
    } catch {
      // Reported by the interceptor.
    } finally {
      this.busy.set(false);
    }
  }
}
