import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoPipe } from '@jsverse/transloco';
import { HasPermissionDirective } from '@upbazaar/auth';
import {
  Api,
  SellerDto,
  SellerSummaryDto,
  apiV1AdminSellersGet,
  apiV1AdminSellersSellerIdApprovePost,
  apiV1AdminSellersSellerIdGet,
  apiV1AdminSellersSellerIdRejectPost,
} from '@upbazaar/data-access';
import { ToastService } from '@upbazaar/ui';
import { DateIstPipe, joinParts } from '@upbazaar/util';
import { SellersPermissions } from '../../core/permissions';

/**
 * Seller applications. Opens on the pending queue, oldest first. Choosing one shows the whole
 * application beside the list - shop, address, KYC - with approve, or reject with a note the
 * applicant will read. The bank account shows only its last four digits, as the API sends it.
 */
@Component({
  selector: 'upb-sellers-page',
  imports: [TranslocoPipe, DateIstPipe, HasPermissionDirective, MatButtonModule, MatButtonToggleModule, MatFormFieldModule, MatInputModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-6xl px-4 py-8">
      <h1 class="text-2xl font-semibold text-ink">{{ 'sellersAdmin.title' | transloco }}</h1>

      <mat-button-toggle-group class="mt-4" [value]="status()" (change)="filter($event.value)">
        @for (s of statuses; track s) {
        <mat-button-toggle [value]="s">{{ 'sellersAdmin.status.' + s | transloco }}</mat-button-toggle>
        }
      </mat-button-toggle-group>

      <div class="mt-4 grid gap-4 lg:grid-cols-[22rem_1fr]">
        <div class="upb-card h-fit">
          @if (loading()) { <mat-progress-bar mode="indeterminate" /> }
          <ul class="divide-y divide-border">
            @for (seller of sellers(); track seller.id) {
            <li>
              <button type="button" class="w-full p-3 text-left hover:bg-surface-sunken"
                [class.bg-surface-sunken]="selected()?.id === seller.id" (click)="open(seller.id)">
                <p class="font-medium text-ink">{{ seller.shopName }}</p>
                <p class="text-sm text-ink-muted">{{ seller.contactMobile }} · {{ seller.submittedAtUtc | dateIst }}</p>
              </button>
            </li>
            } @empty {
            @if (!loading()) { <li class="p-6 text-center text-ink-muted">{{ 'sellersAdmin.none' | transloco }}</li> }
            }
          </ul>
        </div>

        @if (selected(); as s) {
        <article class="upb-card space-y-4 p-5 text-sm">
          <header class="flex flex-wrap items-center justify-between gap-2">
            <h2 class="text-lg font-semibold text-ink">{{ s.shopName }}</h2>
            <span class="rounded-full bg-surface-sunken px-3 py-1">{{ 'sellersAdmin.status.' + s.status | transloco }}</span>
          </header>
          @if (s.description) { <p class="text-ink-muted">{{ s.description }}</p> }

          <dl class="grid grid-cols-[10rem_1fr] gap-x-4 gap-y-1.5">
            <dt class="text-ink-muted">{{ 'sellerPortal.contactMobile' | transloco }}</dt><dd>{{ s.contactMobile }}</dd>
            <dt class="text-ink-muted">{{ 'sellerPortal.contactEmail' | transloco }}</dt><dd>{{ s.contactEmail ?? '—' }}</dd>
            <dt class="text-ink-muted">{{ 'sellerPortal.sectionAddress' | transloco }}</dt>
            <dd>{{ address(s) }}</dd>
            <dt class="text-ink-muted">{{ 'sellerPortal.legalName' | transloco }}</dt><dd>{{ s.kyc.legalName }}</dd>
            <dt class="text-ink-muted">{{ 'sellerPortal.pan' | transloco }}</dt><dd class="font-mono">{{ s.kyc.pan }}</dd>
            <dt class="text-ink-muted">{{ 'sellerPortal.gstin' | transloco }}</dt><dd class="font-mono">{{ s.kyc.gstin ?? '—' }}</dd>
            <dt class="text-ink-muted">{{ 'sellerPortal.bankAccountHolder' | transloco }}</dt><dd>{{ s.kyc.bankAccountHolder }}</dd>
            <dt class="text-ink-muted">{{ 'sellerPortal.bankAccountNumber' | transloco }}</dt><dd class="font-mono">•••• {{ s.kyc.bankAccountLast4 }}</dd>
            <dt class="text-ink-muted">{{ 'sellerPortal.ifsc' | transloco }}</dt><dd class="font-mono">{{ s.kyc.ifsc }}</dd>
            <dt class="text-ink-muted">{{ 'sellersAdmin.owner' | transloco }}</dt>
            <dd>
              @if (s.ownerName) {
              <!-- The separator's spaces are part of the text bound here: a space left between the name and the block is dropped when the template is compiled. -->
              {{ s.ownerName }}@if (s.ownerEmail) {<span class="text-ink-muted">{{ ' · ' + s.ownerEmail }}</span>}
              } @else if (s.ownerUserId) {
              <span class="font-mono text-xs">{{ s.ownerUserId }}</span>
              } @else { {{ 'sellersAdmin.noOwner' | transloco }} }
            </dd>
          </dl>

          @if (s.reviewNote) { <p class="text-danger">{{ s.reviewNote }}</p> }

          @if (s.status === 'Pending') {
          <div *hasPermission="kycApprove" class="space-y-3 border-t border-border pt-4">
            <button mat-flat-button color="primary" type="button" [disabled]="busy()" (click)="approve(s)">
              {{ 'sellersAdmin.approve' | transloco }}
            </button>
            <mat-form-field class="w-full" subscriptSizing="dynamic">
              <mat-label>{{ 'sellersAdmin.rejectNote' | transloco }}</mat-label>
              <textarea matInput rows="2" [value]="note()" (input)="note.set(value($event))"></textarea>
            </mat-form-field>
            <button mat-stroked-button color="warn" type="button" [disabled]="busy() || !note().trim()" (click)="reject(s)">
              {{ 'sellersAdmin.reject' | transloco }}
            </button>
            <p class="text-xs text-ink-muted">{{ 'staff.auditNote' | transloco }}</p>
          </div>
          }
        </article>
        }
      </div>
    </section>
  `,
})
export class SellersPage {
  protected readonly statuses = ['Pending', 'Approved', 'Rejected'] as const;
  protected readonly kycApprove = SellersPermissions.KycApprove;

  protected readonly sellers = signal<readonly SellerSummaryDto[]>([]);
  protected readonly selected = signal<SellerDto | null>(null);
  protected readonly status = signal<string>('Pending');
  protected readonly note = signal('');
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);

  private readonly api = inject(Api);
  private readonly toast = inject(ToastService);

  constructor() {
    void this.load();
  }

  /** The registered address on one line, leaving out an empty second line. */
  protected address(seller: SellerDto): string {
    const a = seller.address;

    return joinParts([joinParts([a.line1, a.line2, a.city, a.state]), a.pincode], ' ');
  }

  protected value(event: Event): string {
    return (event.target as HTMLTextAreaElement).value;
  }

  protected filter(status: string): void {
    this.status.set(status);
    this.selected.set(null);
    void this.load();
  }

  protected async open(sellerId: string): Promise<void> {
    this.note.set('');

    try {
      this.selected.set(await this.api.invoke(apiV1AdminSellersSellerIdGet, { sellerId }));
    } catch {
      // Reported by the interceptor.
    }
  }

  protected async approve(seller: SellerDto): Promise<void> {
    await this.decide(async () => {
      await this.api.invoke(apiV1AdminSellersSellerIdApprovePost, { sellerId: seller.id });
      this.toast.success('sellersAdmin.approved');
    });
  }

  protected async reject(seller: SellerDto): Promise<void> {
    await this.decide(async () => {
      await this.api.invoke(apiV1AdminSellersSellerIdRejectPost, { sellerId: seller.id, body: { note: this.note().trim() } });
      this.toast.info('sellersAdmin.rejected');
    });
  }

  private async decide(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);

    try {
      await work();
      this.selected.set(null);
      await this.load();
    } catch {
      // Reported by the interceptor.
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      this.sellers.set((await this.api.invoke(apiV1AdminSellersGet, { Status: this.status(), PageSize: 100 })).items);
    } catch {
      // Reported by the interceptor.
    } finally {
      this.loading.set(false);
    }
  }
}
