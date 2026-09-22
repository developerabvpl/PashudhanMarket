import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoPipe } from '@jsverse/transloco';
import { HasPermissionDirective } from '@upbazaar/auth';
import {
  Api,
  PickupLocationDto,
  apiV1AdminShippingPickupLocationsGet,
  apiV1AdminShippingPickupLocationsPut,
  apiV1AdminShippingPickupLocationsSellerIdDelete,
  fieldErrorsFor,
  toApiProblem,
} from '@upbazaar/data-access';
import { FieldErrors, ToastService } from '@upbazaar/ui';
import { DateIstPipe } from '@upbazaar/util';
import { ShippingPermissions } from '../../core/permissions';

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Where couriers collect from: the platform warehouse, and any seller with a pickup address of
 * their own. A seller not listed ships from the warehouse.
 *
 * Only names are kept here. Each must match a pickup location already registered - and so
 * address-verified - in the Shiprocket dashboard, because Shiprocket books by that name alone.
 * Keyed by seller id until the Sellers module gives sellers names to pick from.
 */
@Component({
  selector: 'upb-pickup-locations-page',
  imports: [TranslocoPipe, DateIstPipe, HasPermissionDirective, MatButtonModule, MatFormFieldModule, MatInputModule, FieldErrors],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-4xl px-4 py-8">
      <h1 class="text-2xl font-semibold text-ink">{{ 'shipping.pickupTitle' | transloco }}</h1>
      <p class="mt-1 text-sm text-ink-muted">{{ 'shipping.pickupSubtitle' | transloco }}</p>

      <ul class="upb-card mt-6 divide-y divide-border">
        @for (location of locations(); track location.sellerId) {
        <li class="flex flex-wrap items-center justify-between gap-3 p-4 text-sm">
          <div>
            <p class="font-medium text-ink">{{ location.name }}</p>
            <p class="text-ink-muted">
              @if (location.sellerId) {
              {{ 'shipping.forSeller' | transloco }} <span class="font-mono text-xs">{{ location.sellerId }}</span>
              } @else {
              {{ 'shipping.warehouse' | transloco }}
              }
              · {{ location.updatedAtUtc | dateIst }}
            </p>
          </div>
          @if (location.sellerId) {
          <button *hasPermission="shipmentsWrite" mat-button color="warn" type="button" (click)="remove(location.sellerId)">
            {{ 'common.delete' | transloco }}
          </button>
          }
        </li>
        } @empty {
        <li class="p-8 text-center text-ink-muted">{{ 'shipping.noPickups' | transloco }}</li>
        }
      </ul>

      <form *hasPermission="shipmentsWrite" class="upb-card mt-6 grid gap-3 p-4 sm:grid-cols-[1fr_1fr_auto] sm:items-start" (submit)="save($event)">
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'shipping.sellerIdOptional' | transloco }}</mat-label>
          <input matInput name="sellerId" [value]="sellerId()" (input)="sellerId.set(value($event))" />
        </mat-form-field>
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'shipping.pickupName' | transloco }}</mat-label>
          <input matInput name="name" required maxlength="36" [value]="name()" (input)="name.set(value($event))" />
        </mat-form-field>
        <button mat-flat-button color="primary" type="submit" [disabled]="busy() || !name().trim()">
          {{ 'common.save' | transloco }}
        </button>
        <div class="sm:col-span-3">
          <upb-field-errors fieldId="pickup" [errors]="errors()" />
        </div>
      </form>
    </section>
  `,
})
export class PickupLocationsPage {
  protected readonly shipmentsWrite = ShippingPermissions.ShipmentsWrite;

  protected readonly locations = signal<readonly PickupLocationDto[]>([]);
  protected readonly sellerId = signal('');
  protected readonly name = signal('');
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

  protected async save(event: Event): Promise<void> {
    event.preventDefault();

    const seller = this.sellerId().trim();

    if (seller && !GUID.test(seller)) {
      this.errors.set(['shipping.sellerIdInvalid']);
      return;
    }

    this.busy.set(true);
    this.errors.set([]);

    try {
      await this.api.invoke(apiV1AdminShippingPickupLocationsPut, {
        body: { sellerId: seller || null, name: this.name().trim() },
      });

      this.toast.success('shipping.pickupSaved');
      this.sellerId.set('');
      this.name.set('');
      await this.load();
    } catch (error) {
      const problem = toApiProblem(error);

      this.errors.set([...fieldErrorsFor(problem, 'name'), ...fieldErrorsFor(problem, 'sellerId')]);
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(sellerId: string): Promise<void> {
    try {
      await this.api.invoke(apiV1AdminShippingPickupLocationsSellerIdDelete, { sellerId });
      await this.load();
    } catch {
      // Reported by the interceptor.
    }
  }

  private async load(): Promise<void> {
    try {
      this.locations.set(await this.api.invoke(apiV1AdminShippingPickupLocationsGet, {}));
    } catch {
      // Reported by the interceptor; keep what was shown.
    }
  }
}
