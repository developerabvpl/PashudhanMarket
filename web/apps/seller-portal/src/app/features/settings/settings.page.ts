import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  Api,
  PickupLocationDto,
  apiV1SellerShippingPickupLocationGet,
  apiV1SellerShippingPickupLocationPut,
  apiV1SellersMeProfilePut,
  toApiProblem,
} from '@upbazaar/data-access';
import { ToastService } from '@upbazaar/ui';
import { SellerAccess } from '../../core/seller-access';

/**
 * The shop's profile, and where couriers collect from. The legal and bank details approval
 * vouched for are shown elsewhere and not editable: changing them goes through support.
 */
@Component({
  selector: 'upb-seller-settings-page',
  imports: [TranslocoPipe, MatButtonModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-2xl space-y-6 px-4 py-8">
      <h1 class="text-2xl font-semibold text-ink">{{ 'sellerPortal.settingsTitle' | transloco }}</h1>

      <form class="upb-card space-y-1 p-5" (submit)="saveProfile($event)">
        <h2 class="font-medium text-ink">{{ 'sellerPortal.sectionShop' | transloco }}</h2>
        <mat-form-field class="w-full" subscriptSizing="dynamic">
          <mat-label>{{ 'sellerPortal.shopName' | transloco }}</mat-label>
          <input matInput name="shopName" required [value]="shopName()" (input)="shopName.set(value($event))" />
        </mat-form-field>
        <mat-form-field class="w-full" subscriptSizing="dynamic">
          <mat-label>{{ 'sellerPortal.description' | transloco }} ({{ 'common.optional' | transloco }})</mat-label>
          <textarea matInput name="description" rows="3" [value]="description()" (input)="description.set(value($event))"></textarea>
        </mat-form-field>
        <div class="grid gap-3 sm:grid-cols-2">
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ 'sellerPortal.contactMobile' | transloco }}</mat-label>
            <input matInput name="contactMobile" required [value]="mobile()" (input)="mobile.set(value($event))" />
          </mat-form-field>
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ 'sellerPortal.contactEmail' | transloco }} ({{ 'common.optional' | transloco }})</mat-label>
            <input matInput name="contactEmail" [value]="email()" (input)="email.set(value($event))" />
          </mat-form-field>
        </div>
        @if (profileError(); as e) { <p class="text-sm text-danger" role="alert">{{ e }}</p> }
        <button mat-flat-button color="primary" type="submit" [disabled]="busy()">{{ 'common.save' | transloco }}</button>
      </form>

      <form class="upb-card space-y-2 p-5" (submit)="savePickup($event)">
        <h2 class="font-medium text-ink">{{ 'sellerPortal.pickupTitle' | transloco }}</h2>
        <p class="text-sm text-ink-muted">
          @if (pickup()?.sellerId) {
          {{ 'sellerPortal.pickupOwn' | transloco: { name: pickup()!.name } }}
          } @else if (pickup()) {
          {{ 'sellerPortal.pickupWarehouse' | transloco: { name: pickup()!.name } }}
          } @else {
          {{ 'shipping.noPickup' | transloco }}
          }
        </p>
        <p class="text-sm text-ink-muted">{{ 'sellerPortal.pickupHelp' | transloco }}</p>
        <div class="flex flex-wrap items-end gap-3">
          <mat-form-field class="flex-1" subscriptSizing="dynamic">
            <mat-label>{{ 'shipping.pickupName' | transloco }}</mat-label>
            <input matInput name="pickup" maxlength="36" [value]="pickupName()" (input)="pickupName.set(value($event))" />
          </mat-form-field>
          <button mat-stroked-button type="submit" [disabled]="busy() || !pickupName().trim()">{{ 'common.save' | transloco }}</button>
        </div>
      </form>
    </section>
  `,
})
export class SettingsPage implements OnInit {
  protected readonly shopName = signal('');
  protected readonly description = signal('');
  protected readonly mobile = signal('');
  protected readonly email = signal('');
  protected readonly pickup = signal<PickupLocationDto | null>(null);
  protected readonly pickupName = signal('');
  protected readonly busy = signal(false);
  protected readonly profileError = signal<string | null>(null);

  private readonly api = inject(Api);
  private readonly access = inject(SellerAccess);
  private readonly toast = inject(ToastService);

  async ngOnInit(): Promise<void> {
    const seller = this.access.seller() ?? (await this.access.load());

    if (seller) {
      this.shopName.set(seller.shopName);
      this.description.set(seller.description ?? '');
      this.mobile.set(seller.contactMobile);
      this.email.set(seller.contactEmail ?? '');
    }

    await this.loadPickup();
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected async saveProfile(event: Event): Promise<void> {
    event.preventDefault();
    this.busy.set(true);
    this.profileError.set(null);

    try {
      this.access.set(await this.api.invoke(apiV1SellersMeProfilePut, {
        body: {
          shopName: this.shopName().trim(),
          description: this.description().trim() || null,
          contactMobile: this.mobile().replace(/\D/g, '').slice(-10),
          contactEmail: this.email().trim() || null,
        },
      }));
      this.toast.success('sellerPortal.saved');
    } catch (error) {
      const problem = toApiProblem(error);
      this.profileError.set(Object.values(problem.fieldErrors).flat()[0] ?? problem.title);
    } finally {
      this.busy.set(false);
    }
  }

  protected async savePickup(event: Event): Promise<void> {
    event.preventDefault();
    this.busy.set(true);

    try {
      this.pickup.set(await this.api.invoke(apiV1SellerShippingPickupLocationPut, { body: { name: this.pickupName().trim() } }));
      this.pickupName.set('');
      this.toast.success('shipping.pickupSaved');
    } catch {
      // Reported by the interceptor.
    } finally {
      this.busy.set(false);
    }
  }

  private async loadPickup(): Promise<void> {
    try {
      this.pickup.set(await this.api.invoke(apiV1SellerShippingPickupLocationGet, {}));
    } catch {
      this.pickup.set(null);
    }
  }
}
