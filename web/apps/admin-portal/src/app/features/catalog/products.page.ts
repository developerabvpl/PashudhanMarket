import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { TranslocoPipe } from '@jsverse/transloco';
import { HasPermissionDirective } from '@upbazaar/auth';
import {
  AdminProductSummaryDto,
  Api,
  CategoryDto,
  apiV1AdminCatalogProductsGet,
  apiV1CatalogCategoriesGet,
} from '@upbazaar/data-access';
import { DateIstPipe, InrCurrencyPipe } from '@upbazaar/util';
import { CatalogPermissions } from '../../core/permissions';

/** Statuses in the order a listing moves through them. */
export const PRODUCT_STATUSES = ['Draft', 'InReview', 'Active', 'Archived'] as const;

/**
 * Every listing on the platform, for moderators and admins.
 *
 * Newest change first, since what somebody just touched is usually what they come back to look
 * at. The seller and category are shown by name, and a missing package is flagged in the row,
 * because a live listing without one is an order nobody can book a courier for.
 */
@Component({
  selector: 'upb-products-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    DateIstPipe,
    InrCurrencyPipe,
    HasPermissionDirective,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatSelectModule,
    MatTableModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-6xl px-4 py-8">
      <header class="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 class="text-2xl font-semibold text-ink">{{ 'catalogAdmin.title' | transloco }}</h1>
          <p class="mt-1 text-sm text-ink-muted">{{ 'catalogAdmin.subtitle' | transloco }}</p>
        </div>
        <a *hasPermission="productsWrite" mat-flat-button color="primary" routerLink="/catalog/products/new">
          {{ 'catalogAdmin.newProduct' | transloco }}
        </a>
      </header>

      <form class="mt-6 flex flex-wrap items-center gap-3" (submit)="applySearch($event)">
        <mat-form-field class="min-w-64 flex-1" subscriptSizing="dynamic">
          <mat-label>{{ 'catalogAdmin.searchLabel' | transloco }}</mat-label>
          <input matInput name="search" type="search" [value]="search()" />
        </mat-form-field>
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'catalogAdmin.status' | transloco }}</mat-label>
          <mat-select [value]="status()" (valueChange)="filterStatus($event)">
            <mat-option value="">{{ 'catalogAdmin.allStatuses' | transloco }}</mat-option>
            @for (s of statuses; track s) {
            <mat-option [value]="s">{{ 'sellerPortal.productStatus.' + s | transloco }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'catalogAdmin.category' | transloco }}</mat-label>
          <mat-select [value]="categoryId()" (valueChange)="filterCategory($event)">
            <mat-option value="">{{ 'catalogAdmin.allCategories' | transloco }}</mat-option>
            @for (c of categories(); track c.id) {
            <mat-option [value]="c.id">{{ c.name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <button mat-stroked-button type="submit">{{ 'common.search' | transloco }}</button>
      </form>

      <div class="upb-card mt-4 overflow-x-auto">
        @if (loading()) {
        <mat-progress-bar mode="indeterminate" />
        }

        <table mat-table [dataSource]="products()" class="w-full">
          <ng-container matColumnDef="name">
            <th mat-header-cell *matHeaderCellDef>{{ 'catalogAdmin.product' | transloco }}</th>
            <td mat-cell *matCellDef="let row">
              <a class="font-medium" [routerLink]="['/catalog/products', row.id]">{{ row.name }}</a>
              <span class="block font-mono text-xs text-ink-muted">{{ row.sku }}</span>
            </td>
          </ng-container>
          <ng-container matColumnDef="seller">
            <th mat-header-cell *matHeaderCellDef>{{ 'catalogAdmin.seller' | transloco }}</th>
            <td mat-cell *matCellDef="let row" class="text-sm">{{ row.sellerName ?? ('catalogAdmin.unknownSeller' | transloco) }}</td>
          </ng-container>
          <ng-container matColumnDef="category">
            <th mat-header-cell *matHeaderCellDef>{{ 'catalogAdmin.category' | transloco }}</th>
            <td mat-cell *matCellDef="let row" class="text-sm">{{ row.categoryName }}</td>
          </ng-container>
          <ng-container matColumnDef="price">
            <th mat-header-cell *matHeaderCellDef>{{ 'catalogAdmin.price' | transloco }}</th>
            <td mat-cell *matCellDef="let row">{{ row.price | inr }}</td>
          </ng-container>
          <ng-container matColumnDef="available">
            <th mat-header-cell *matHeaderCellDef>{{ 'catalogAdmin.available' | transloco }}</th>
            <td mat-cell *matCellDef="let row" [class.text-danger]="row.availableQuantity === 0">{{ row.availableQuantity }}</td>
          </ng-container>
          <ng-container matColumnDef="status">
            <th mat-header-cell *matHeaderCellDef>{{ 'catalogAdmin.status' | transloco }}</th>
            <td mat-cell *matCellDef="let row" class="text-sm">
              {{ 'sellerPortal.productStatus.' + row.status | transloco }}
              @if (!row.hasPackage && row.status !== 'Archived') {
              <span class="block text-xs text-danger">{{ 'catalogAdmin.noPackage' | transloco }}</span>
              }
            </td>
          </ng-container>
          <ng-container matColumnDef="updatedAtUtc">
            <th mat-header-cell *matHeaderCellDef>{{ 'catalogAdmin.lastChanged' | transloco }}</th>
            <td mat-cell *matCellDef="let row" class="text-sm">{{ row.updatedAtUtc | dateIst: 'datetime' }}</td>
          </ng-container>

          <tr mat-header-row *matHeaderRowDef="columns"></tr>
          <tr mat-row *matRowDef="let row; columns: columns"></tr>
        </table>

        @if (!loading() && products().length === 0) {
        <p class="p-8 text-center text-ink-muted">{{ 'catalogAdmin.none' | transloco }}</p>
        }

        <mat-paginator
          [length]="totalCount()"
          [pageSize]="pageSize()"
          [pageIndex]="page() - 1"
          [pageSizeOptions]="[25, 50, 100]"
          (page)="changePage($event)"
        />
      </div>
    </section>
  `,
})
export class ProductsPage {
  private readonly api = inject(Api);

  protected readonly productsWrite = CatalogPermissions.ProductsWrite;
  protected readonly statuses = PRODUCT_STATUSES;
  protected readonly columns = ['name', 'seller', 'category', 'price', 'available', 'status', 'updatedAtUtc'];

  protected readonly products = signal<readonly AdminProductSummaryDto[]>([]);
  protected readonly categories = signal<readonly CategoryDto[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(25);
  protected readonly search = signal('');
  protected readonly status = signal('');
  protected readonly categoryId = signal('');
  protected readonly loading = signal(false);

  constructor() {
    void this.loadCategories();
    void this.load();
  }

  protected applySearch(event: Event): void {
    event.preventDefault();

    this.search.set(new FormData(event.target as HTMLFormElement).get('search')?.toString().trim() ?? '');
    this.reload();
  }

  protected filterStatus(status: string): void {
    this.status.set(status);
    this.reload();
  }

  protected filterCategory(categoryId: string): void {
    this.categoryId.set(categoryId);
    this.reload();
  }

  protected changePage(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    this.pageSize.set(event.pageSize);

    void this.load();
  }

  private reload(): void {
    this.page.set(1);

    void this.load();
  }

  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      const result = await this.api.invoke(apiV1AdminCatalogProductsGet, {
        Page: this.page(),
        PageSize: this.pageSize(),
        Search: this.search() || undefined,
        Status: this.status() || undefined,
        CategoryId: this.categoryId() || undefined,
      });

      this.products.set(result.items);
      this.totalCount.set(result.totalCount);
    } catch {
      // The global interceptor has already raised a toast; leave the table as it was.
    } finally {
      this.loading.set(false);
    }
  }

  private async loadCategories(): Promise<void> {
    try {
      this.categories.set(await this.api.invoke(apiV1CatalogCategoriesGet, {}));
    } catch {
      // Reported by the interceptor; the filter stays at "all".
    }
  }
}
