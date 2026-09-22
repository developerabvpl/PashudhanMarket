import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  Api,
  ApiProblem,
  SellerApplicationRequest,
  SellerDto,
  apiV1SellersMeApplicationPost,
  apiV1SellersMeApplicationPut,
  fieldErrorsFor,
  toApiProblem,
} from '@upbazaar/data-access';
import { FieldErrors, ToastService } from '@upbazaar/ui';
import { SellerAccess } from '../../core/seller-access';

type Field = keyof SellerApplicationRequest;

const EMPTY: SellerApplicationRequest = {
  shopName: '',
  description: null,
  contactMobile: '',
  contactEmail: null,
  addressLine1: '',
  addressLine2: null,
  city: '',
  state: '',
  pincode: '',
  legalName: '',
  gstin: null,
  pan: '',
  bankAccountHolder: '',
  bankAccountNumber: '',
  ifsc: '',
};

/** The form, in the sections a seller thinks of it in. Optional fields are marked. */
const SECTIONS: readonly { title: string; fields: readonly { key: Field; label: string; optional?: boolean; wide?: boolean }[] }[] = [
  {
    title: 'sellerPortal.sectionShop',
    fields: [
      { key: 'shopName', label: 'sellerPortal.shopName', wide: true },
      { key: 'description', label: 'sellerPortal.description', optional: true, wide: true },
      { key: 'contactMobile', label: 'sellerPortal.contactMobile' },
      { key: 'contactEmail', label: 'sellerPortal.contactEmail', optional: true },
    ],
  },
  {
    title: 'sellerPortal.sectionAddress',
    fields: [
      { key: 'addressLine1', label: 'sellerPortal.addressLine1', wide: true },
      { key: 'addressLine2', label: 'sellerPortal.addressLine2', optional: true, wide: true },
      { key: 'city', label: 'sellerPortal.city' },
      { key: 'state', label: 'sellerPortal.state' },
      { key: 'pincode', label: 'sellerPortal.pincode' },
    ],
  },
  {
    title: 'sellerPortal.sectionKyc',
    fields: [
      { key: 'legalName', label: 'sellerPortal.legalName', wide: true },
      { key: 'pan', label: 'sellerPortal.pan' },
      { key: 'gstin', label: 'sellerPortal.gstin', optional: true },
      { key: 'bankAccountHolder', label: 'sellerPortal.bankAccountHolder', wide: true },
      { key: 'bankAccountNumber', label: 'sellerPortal.bankAccountNumber' },
      { key: 'ifsc', label: 'sellerPortal.ifsc' },
    ],
  },
];

/**
 * Applying to sell, and where the application stands.
 *
 * One page for every state: nothing submitted yet shows the empty form; Pending shows the
 * application with a note that it is being reviewed; Rejected shows the reviewer's note above
 * the form, filled in, to correct and resubmit; Approved refreshes the session so seller access
 * is live, and moves on to the orders queue.
 */
@Component({
  selector: 'upb-apply-page',
  imports: [TranslocoPipe, MatButtonModule, MatFormFieldModule, MatInputModule, FieldErrors],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-3xl px-4 py-8">
      <h1 class="text-2xl font-semibold text-ink">{{ 'sellerPortal.applyTitle' | transloco }}</h1>

      @switch (access.seller()?.status) { @case ('Pending') {
      <div class="upb-card mt-4 p-4" role="status">
        <p class="font-medium text-ink">{{ 'sellerPortal.pending' | transloco }}</p>
        <p class="mt-1 text-sm text-ink-muted">{{ 'sellerPortal.pendingNote' | transloco }}</p>
      </div>
      } @case ('Rejected') {
      <div class="mt-4 rounded-card border border-danger/40 bg-danger/5 p-4" role="alert">
        <p class="font-medium text-ink">{{ 'sellerPortal.rejected' | transloco }}</p>
        <p class="mt-1 text-sm text-ink">{{ access.seller()?.reviewNote }}</p>
      </div>
      } @default {
      <p class="mt-1 text-sm text-ink-muted">{{ 'sellerPortal.applyIntro' | transloco }}</p>
      } }

      <form class="mt-6 space-y-6" (submit)="submit($event)">
        @for (section of sections; track section.title) {
        <fieldset class="upb-card p-5">
          <legend class="px-1 font-medium text-ink">{{ section.title | transloco }}</legend>
          <div class="mt-2 grid gap-x-4 sm:grid-cols-2">
            @for (field of section.fields; track field.key) {
            <div [class.sm:col-span-2]="field.wide">
              <mat-form-field class="w-full" subscriptSizing="dynamic">
                <mat-label>
                  {{ field.label | transloco }}
                  @if (field.optional) { ({{ 'common.optional' | transloco }}) }
                </mat-label>
                <input matInput [name]="field.key" [required]="!field.optional" [readonly]="readOnly()"
                  [value]="form()[field.key] ?? ''" (input)="set(field.key, $event)" />
              </mat-form-field>
              <upb-field-errors [fieldId]="field.key" [errors]="errorsFor(field.key)" />
            </div>
            }
          </div>
        </fieldset>
        }

        @if (!readOnly()) {
        <p class="text-sm text-ink-muted">{{ 'sellerPortal.kycNote' | transloco }}</p>
        <button mat-flat-button color="primary" type="submit" [disabled]="busy()">
          {{ (busy() ? 'common.working' : access.seller() ? 'sellerPortal.resubmit' : 'sellerPortal.submit') | transloco }}
        </button>
        }
      </form>
    </section>
  `,
})
export class ApplyPage implements OnInit {
  protected readonly access = inject(SellerAccess);
  protected readonly sections = SECTIONS;
  protected readonly form = signal<SellerApplicationRequest>(EMPTY);
  protected readonly busy = signal(false);
  private readonly problem = signal<ApiProblem | null>(null);

  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  /** A pending application waits for review; nothing to edit until a reviewer says so. */
  protected readOnly(): boolean {
    return this.access.seller()?.status === 'Pending';
  }

  async ngOnInit(): Promise<void> {
    const seller = await this.access.load();

    if (seller?.status === 'Approved') {
      await this.continueApproved();
    } else if (seller) {
      this.form.set(fromSeller(seller));
    }
  }

  protected set(field: Field, event: Event): void {
    const value = (event.target as HTMLInputElement).value;

    this.form.update((current) => ({ ...current, [field]: value }));
  }

  protected errorsFor(field: string): readonly string[] {
    return fieldErrorsFor(this.problem(), field);
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.busy.set(true);
    this.problem.set(null);

    const body = cleaned(this.form());

    try {
      const seller = this.access.seller()
        ? await this.api.invoke(apiV1SellersMeApplicationPut, { body })
        : await this.api.invoke(apiV1SellersMeApplicationPost, { body });

      this.access.set(seller);
      this.toast.success('sellerPortal.submitted');
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }

  private async continueApproved(): Promise<void> {
    await this.access.refreshAccess();
    await this.router.navigate(['/orders']);
  }
}

/**
 * The form as the reviewer saw it. The bank account number is never sent back, only its last
 * four digits, so a resubmission asks for it again rather than silently sending the mask.
 */
function fromSeller(seller: SellerDto): SellerApplicationRequest {
  return {
    shopName: seller.shopName,
    description: seller.description,
    contactMobile: seller.contactMobile,
    contactEmail: seller.contactEmail,
    addressLine1: seller.address.line1,
    addressLine2: seller.address.line2,
    city: seller.address.city,
    state: seller.address.state,
    pincode: seller.address.pincode,
    legalName: seller.kyc.legalName,
    gstin: seller.kyc.gstin,
    pan: seller.kyc.pan,
    bankAccountHolder: seller.kyc.bankAccountHolder,
    bankAccountNumber: '',
    ifsc: seller.kyc.ifsc,
  };
}

function cleaned(form: SellerApplicationRequest): SellerApplicationRequest {
  const optional = (value: string | null) => (value?.trim() ? value.trim() : null);

  return {
    ...form,
    shopName: form.shopName.trim(),
    description: optional(form.description),
    contactMobile: form.contactMobile.replace(/\D/g, '').slice(-10),
    contactEmail: optional(form.contactEmail),
    addressLine2: optional(form.addressLine2),
    gstin: optional(form.gstin)?.toUpperCase() ?? null,
    pan: form.pan.trim().toUpperCase(),
    ifsc: form.ifsc.trim().toUpperCase(),
    bankAccountNumber: form.bankAccountNumber.replace(/\s/g, ''),
  };
}
