import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  Api,
  ParcelSuggestionDto,
  ShipmentDto,
  apiV1AdminShippingOrdersOrderIdPartsPartIdPackPost,
  apiV1AdminShippingOrdersOrderIdPartsPartIdParcelGet,
  fieldErrorsFor,
  toApiProblem,
} from '@upbazaar/data-access';
import { FieldErrors } from '@upbazaar/ui';

/** Which part is being packed. */
export interface PackParcelData {
  readonly orderId: string;
  readonly partId: string;
  readonly orderNumber: string;
}

type Field = 'weightGrams' | 'lengthCm' | 'breadthCm' | 'heightCm';

/**
 * Packs one seller's part and books the courier.
 *
 * Opens with the parcel worked out from the products' recorded packages. The figures stay
 * editable, because the box actually used is the truth and the listing is only an estimate;
 * when a product has no package the form starts empty and says which ones. Resolves to the
 * shipment once booked, or undefined if cancelled.
 */
@Component({
  selector: 'upb-pack-parcel-dialog',
  imports: [MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatProgressBarModule, TranslocoPipe, FieldErrors],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ 'shipping.packTitle' | transloco: { number: data.orderNumber } }}</h2>

    <form (submit)="pack($event)">
      <mat-dialog-content>
        @if (!suggestion()) {
        <mat-progress-bar mode="indeterminate" />
        } @else {
        <p class="text-sm">
          {{ 'shipping.pickupFrom' | transloco: { location: suggestion()!.pickupLocation ?? ('shipping.noPickup' | transloco) } }}
        </p>

        @if (suggestion()!.missingSkus.length > 0) {
        <p class="mt-2 text-sm text-danger">
          {{ 'shipping.missingPackages' | transloco: { skus: suggestion()!.missingSkus.join(', ') } }}
        </p>
        } @else {
        <p class="mt-2 text-sm text-ink-muted">{{ 'shipping.suggested' | transloco }}</p>
        }

        <div class="mt-4 grid grid-cols-2 gap-3">
          @for (field of fields; track field.key) {
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ field.label | transloco }}</mat-label>
            <input matInput type="number" inputmode="decimal" min="0" step="any" [name]="field.key"
              [value]="values()[field.key]" (input)="set(field.key, $event)" required />
          </mat-form-field>
          }
        </div>
        <upb-field-errors fieldId="parcel" [errors]="errors()" />
        }
      </mat-dialog-content>

      <mat-dialog-actions align="end">
        <button mat-button type="button" [mat-dialog-close]="undefined">{{ 'common.cancel' | transloco }}</button>
        <button mat-flat-button color="primary" type="submit" [disabled]="busy() || !complete()">
          {{ (busy() ? 'shipping.booking' : 'shipping.packAndBook') | transloco }}
        </button>
      </mat-dialog-actions>
    </form>
  `,
})
export class PackParcelDialog implements OnInit {
  protected readonly data = inject<PackParcelData>(MAT_DIALOG_DATA);

  protected readonly fields: readonly { key: Field; label: string }[] = [
    { key: 'weightGrams', label: 'shipping.weightGrams' },
    { key: 'lengthCm', label: 'shipping.lengthCm' },
    { key: 'breadthCm', label: 'shipping.breadthCm' },
    { key: 'heightCm', label: 'shipping.heightCm' },
  ];

  protected readonly suggestion = signal<ParcelSuggestionDto | null>(null);
  protected readonly values = signal<Record<Field, string>>({ weightGrams: '', lengthCm: '', breadthCm: '', heightCm: '' });
  protected readonly busy = signal(false);
  protected readonly errors = signal<readonly string[]>([]);

  private readonly reference = inject<MatDialogRef<PackParcelDialog, ShipmentDto>>(MatDialogRef);
  private readonly api = inject(Api);

  async ngOnInit(): Promise<void> {
    try {
      const suggestion = await this.api.invoke(apiV1AdminShippingOrdersOrderIdPartsPartIdParcelGet, {
        orderId: this.data.orderId,
        partId: this.data.partId,
      });

      if (suggestion.parcel) {
        const p = suggestion.parcel;
        this.values.set({
          weightGrams: String(p.weightGrams),
          lengthCm: String(p.lengthCm),
          breadthCm: String(p.breadthCm),
          heightCm: String(p.heightCm),
        });
      }

      this.suggestion.set(suggestion);
    } catch {
      // Reported by the interceptor; closing is the only useful thing left to do.
      this.reference.close(undefined);
    }
  }

  protected complete(): boolean {
    return Object.values(this.values()).every((v) => Number(v) > 0);
  }

  protected set(field: Field, event: Event): void {
    const value = (event.target as HTMLInputElement).value;

    this.values.update((current) => ({ ...current, [field]: value }));
  }

  protected async pack(event: Event): Promise<void> {
    event.preventDefault();
    this.busy.set(true);
    this.errors.set([]);

    const v = this.values();

    try {
      const shipment = await this.api.invoke(apiV1AdminShippingOrdersOrderIdPartsPartIdPackPost, {
        orderId: this.data.orderId,
        partId: this.data.partId,
        body: {
          parcel: {
            weightGrams: Math.round(Number(v.weightGrams)),
            lengthCm: Number(v.lengthCm),
            breadthCm: Number(v.breadthCm),
            heightCm: Number(v.heightCm),
          },
        },
      });

      this.reference.close(shipment);
    } catch (error) {
      const problem = toApiProblem(error);

      this.errors.set(this.fields.flatMap((f) => fieldErrorsFor(problem, f.key)));
    } finally {
      this.busy.set(false);
    }
  }
}
