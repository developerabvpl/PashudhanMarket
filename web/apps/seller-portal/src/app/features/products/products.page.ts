import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoPipe } from '@jsverse/transloco';
import { Api, ProductSummaryDto, apiV1SellerCatalogProductsGet } from '@upbazaar/data-access';
import { InrCurrencyPipe } from '@upbazaar/util';

/** The seller's listings, filterable by where they are in review. */
@Component({
  selector: 'upb-seller-products-page',
  imports: [RouterLink, TranslocoPipe, InrCurrencyPipe, MatButtonModule, MatButtonToggleModule, MatPaginatorModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-5xl px-4 py-8">
      <header class="flex flex-wrap items-center justify-between gap-3">
        <h1 class="text-2xl font-semibold text-ink">{{ 'sellerPortal.productsTitle' | transloco }}</h1>
        <a mat-flat-button color="primary" routerLink="/products/new">{{ 'seller.newProduct' | transloco }}</a>
      </header>

      <mat-button-toggle-group class="mt-4" [value]="status()" (change)="filter($event.value)">
        <mat-button-toggle value="">{{ 'sellerPortal.allProducts' | transloco }}</mat-button-toggle>
        @for (s of statuses; track s) {
        <mat-button-toggle [value]="s">{{ 'sellerPortal.productStatus.' + s | transloco }}</mat-button-toggle>
        }
      </mat-button-toggle-group>

      <div class="upb-card mt-4">
        @if (loading()) { <mat-progress-bar mode="indeterminate" /> }
        <ul class="divide-y divide-border">
          @for (product of products(); track product.id) {
          <li>
            <a class="flex flex-wrap items-center justify-between gap-3 p-4 hover:bg-surface-sunken" [routerLink]="['/products', product.id]">
              <div class="min-w-0">
                <p class="font-medium text-ink">{{ product.name }}</p>
                <p class="text-sm text-ink-muted">
                  <span class="font-mono text-xs">{{ product.sku }}</span> ·
                  {{ 'sellerPortal.inStock' | transloco: { count: product.availableQuantity } }}
                </p>
              </div>
              <div class="flex items-center gap-3 text-sm">
                <span class="rounded-full bg-surface-sunken px-2.5 py-0.5">{{ 'sellerPortal.productStatus.' + product.status | transloco }}</span>
                <span class="font-semibold text-ink">{{ product.price | inr }}</span>
              </div>
            </a>
          </li>
          } @empty {
          @if (!loading()) { <li class="p-8 text-center text-ink-muted">{{ 'sellerPortal.noProducts' | transloco }}</li> }
          }
        </ul>
        <mat-paginator [length]="total()" [pageSize]="pageSize" [pageIndex]="page() - 1" [hidePageSize]="true" (page)="turn($event)" />
      </div>
    </section>
  `,
})
export class ProductsPage {
  protected readonly statuses = ['Draft', 'InReview', 'Active', 'Archived'] as const;
  protected readonly pageSize = 25;
  protected readonly products = signal<readonly ProductSummaryDto[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly status = signal('');
  protected readonly loading = signal(false);

  private readonly api = inject(Api);

  constructor() {
    void this.load();
  }

  protected filter(status: string): void {
    this.status.set(status);
    this.page.set(1);
    void this.load();
  }

  protected turn(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    void this.load();
  }

  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      const result = await this.api.invoke(apiV1SellerCatalogProductsGet, {
        Page: this.page(),
        PageSize: this.pageSize,
        Status: this.status() || undefined,
      });

      this.products.set(result.items);
      this.total.set(result.totalCount);
    } catch {
      // Reported by the interceptor.
    } finally {
      this.loading.set(false);
    }
  }
}
