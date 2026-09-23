import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  AdminProductSummaryDto,
  Api,
  ProductDto,
  apiV1AdminCatalogProductsGet,
  apiV1AdminCatalogProductsProductIdPublishPost,
  apiV1AdminCatalogProductsProductIdSendBackPost,
  catalogGetProduct,
} from '@upbazaar/data-access';
import { ToastService } from '@upbazaar/ui';
import { InrCurrencyPipe } from '@upbazaar/util';

/**
 * Sellers' listings waiting to go live. A moderator reads each one and either publishes it or
 * sends it back as a draft with a note - the note is what the seller sees, so it should say what
 * to fix, not just that something is wrong.
 *
 * Oldest first, so the seller who has waited longest is answered first.
 */
@Component({
  selector: 'upb-listing-review-page',
  imports: [RouterLink, TranslocoPipe, InrCurrencyPipe, MatButtonModule, MatFormFieldModule, MatInputModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-6xl px-4 py-8">
      <h1 class="text-2xl font-semibold text-ink">{{ 'listingReview.title' | transloco }}</h1>
      <p class="mt-1 text-sm text-ink-muted">{{ 'listingReview.subtitle' | transloco }}</p>

      <div class="mt-4 grid gap-4 lg:grid-cols-[22rem_1fr]">
        <div class="upb-card h-fit">
          @if (loading()) { <mat-progress-bar mode="indeterminate" /> }
          <ul class="divide-y divide-border">
            @for (product of queue(); track product.id) {
            <li>
              <button type="button" class="w-full p-3 text-left hover:bg-surface-sunken"
                [class.bg-surface-sunken]="selected()?.id === product.id" (click)="open(product.id)">
                <p class="font-medium text-ink">{{ product.name }}</p>
                <p class="text-sm text-ink-muted"><span class="font-mono text-xs">{{ product.sku }}</span> · {{ product.price | inr }}</p>
                <p class="text-xs text-ink-muted">{{ product.sellerName ?? ('catalogAdmin.unknownSeller' | transloco) }}</p>
              </button>
            </li>
            } @empty {
            @if (!loading()) { <li class="p-6 text-center text-ink-muted">{{ 'listingReview.none' | transloco }}</li> }
            }
          </ul>
        </div>

        @if (selected(); as p) {
        <article class="upb-card space-y-3 p-5 text-sm">
          <h2 class="text-lg font-semibold text-ink">{{ p.name }}</h2>
          <p class="text-ink-muted">
            <span class="font-mono">{{ p.sku }}</span> · {{ p.category.name }} · {{ p.price | inr }}
            @if (p.brand) { · {{ p.brand }} }
          </p>
          <p class="whitespace-pre-line text-ink">{{ p.description ?? ('listingReview.noDescription' | transloco) }}</p>
          <p class="text-xs text-ink-muted">
            {{ 'listingReview.seller' | transloco }}: {{ selectedSellerName() ?? p.sellerId }}
            · <a [routerLink]="['/catalog/products', p.id]">{{ 'listingReview.openListing' | transloco }}</a>
          </p>

          <div class="space-y-3 border-t border-border pt-4">
            <button mat-flat-button color="primary" type="button" [disabled]="busy()" (click)="publish(p)">
              {{ 'seller.publish' | transloco }}
            </button>
            <mat-form-field class="w-full" subscriptSizing="dynamic">
              <mat-label>{{ 'listingReview.note' | transloco }}</mat-label>
              <textarea matInput rows="2" [value]="note()" (input)="note.set(value($event))"></textarea>
            </mat-form-field>
            <button mat-stroked-button type="button" [disabled]="busy() || !note().trim()" (click)="sendBack(p)">
              {{ 'listingReview.sendBack' | transloco }}
            </button>
          </div>
        </article>
        }
      </div>
    </section>
  `,
})
export class ListingReviewPage {
  protected readonly queue = signal<readonly AdminProductSummaryDto[]>([]);
  protected readonly selected = signal<ProductDto | null>(null);
  protected readonly note = signal('');

  /** The queue row already carries the shop name; the full product only has the id. */
  protected readonly selectedSellerName = computed(
    () => this.queue().find((row) => row.id === this.selected()?.id)?.sellerName ?? null
  );
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);

  private readonly api = inject(Api);
  private readonly toast = inject(ToastService);

  constructor() {
    void this.load();
  }

  protected value(event: Event): string {
    return (event.target as HTMLTextAreaElement).value;
  }

  protected async open(productId: string): Promise<void> {
    this.note.set('');

    try {
      this.selected.set(await this.api.invoke(catalogGetProduct, { productId }));
    } catch {
      // Reported by the interceptor.
    }
  }

  protected async publish(product: ProductDto): Promise<void> {
    await this.decide(async () => {
      await this.api.invoke(apiV1AdminCatalogProductsProductIdPublishPost, { productId: product.id });
      this.toast.success('seller.published');
    });
  }

  protected async sendBack(product: ProductDto): Promise<void> {
    await this.decide(async () => {
      await this.api.invoke(apiV1AdminCatalogProductsProductIdSendBackPost, { productId: product.id, body: { note: this.note().trim() } });
      this.toast.info('listingReview.sentBack');
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
      this.queue.set((await this.api.invoke(apiV1AdminCatalogProductsGet, { Status: 'InReview', OldestFirst: true, PageSize: 100 })).items);
    } catch {
      // Reported by the interceptor.
    } finally {
      this.loading.set(false);
    }
  }
}
