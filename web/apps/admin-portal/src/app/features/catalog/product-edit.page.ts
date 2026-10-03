import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe } from '@jsverse/transloco';
import { CurrentUserStore, HasPermissionDirective } from '@upbazaar/auth';
import {
  Api,
  ApiProblem,
  CategoryDto,
  ProductDto,
  SellerNameDto,
  StockDetailDto,
  apiV1AdminCatalogProductsPost,
  apiV1AdminCatalogProductsProductIdDelete,
  apiV1AdminCatalogProductsProductIdPackagePut,
  apiV1AdminCatalogProductsProductIdPublishPost,
  apiV1AdminCatalogProductsProductIdPut,
  apiV1AdminCatalogProductsProductIdSaleDelete,
  apiV1AdminCatalogSellersGet,
  apiV1AdminInventoryStockProductIdGet,
  apiV1AdminInventoryStockProductIdPut,
  apiV1CatalogCategoriesGet,
  catalogGetProduct,
  fieldErrorsFor,
  toApiProblem,
} from '@upbazaar/data-access';
import { FieldErrors, ToastService } from '@upbazaar/ui';
import { DateIstPipe, InrCurrencyPipe } from '@upbazaar/util';
import { CatalogPermissions, InventoryPermissions } from '../../core/permissions';
import { confirm } from '../staff/confirm.dialog';

interface Details {
  sellerId: string;
  sku: string;
  name: string;
  brand: string;
  description: string;
  price: string;
  categoryId: string;
  onHandQuantity: string;
}

interface Package {
  weightGrams: string;
  lengthCm: string;
  breadthCm: string;
  heightCm: string;
}

/**
 * One listing as staff see it: create one for a seller, correct any listing's wording, price and
 * category, publish it, record a stock count, set how it ships, or archive it.
 *
 * Unlike the seller's own screen, a live listing's wording stays editable here: moderators are
 * the ones who approve wording, so their corrections need no second review. Sending a listing
 * back with a note stays on the review queue, where the note is written.
 */
@Component({
  selector: 'upb-admin-product-edit-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    HasPermissionDirective,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    FieldErrors,
    DateIstPipe,
    InrCurrencyPipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-3xl space-y-6 px-4 py-8">
      <a class="text-sm" routerLink="/catalog/products">← {{ 'catalogAdmin.allProducts' | transloco }}</a>

      <!--
        Title left, status right, as on the seller's own product page. The row does not wrap: with a
        long name the status used to drop to a line of its own, at the left under the title.
      -->
      <header class="flex items-start justify-between gap-3" data-product-header>
        <div class="min-w-0">
          <h1 class="text-2xl font-semibold text-ink">
            {{ isNew() ? ('catalogAdmin.newProduct' | transloco) : product()?.name }}
          </h1>
          @if (product(); as p) {
          <p class="text-sm text-ink-muted">
            <span class="font-mono">{{ p.sku }}</span> · {{ sellerName() }}
          </p>
          }
        </div>
        @if (product(); as p) {
        <span class="shrink-0 whitespace-nowrap rounded-full bg-surface-sunken px-3 py-1 text-sm">{{ 'sellerPortal.productStatus.' + p.status | transloco }}</span>
        }
      </header>

      @if (product(); as p) {
      @if (p.status === 'InReview') {
      <div class="rounded-card border border-border bg-surface-sunken p-4 text-sm" role="status">
        {{ 'catalogAdmin.inReview' | transloco }}
        <a *hasPermission="productsWrite" class="ml-1" routerLink="/catalog/review">{{ 'catalogAdmin.openQueue' | transloco }}</a>
      </div>
      } @else if (p.status === 'Archived') {
      <p class="rounded-card bg-surface-sunken p-4 text-sm text-ink-muted" role="status">{{ 'catalogAdmin.archivedNote' | transloco }}</p>
      }
      @if (p.reviewNote) {
      <div class="rounded-card border border-danger/40 bg-danger/5 p-4 text-sm">
        <p class="font-medium text-ink">{{ 'sellerPortal.sentBack' | transloco }}</p>
        <p class="mt-1 text-ink">{{ p.reviewNote }}</p>
      </div>
      }
      }

      @if (product()?.sale; as sale) {
      <div class="upb-card flex flex-wrap items-center justify-between gap-3 p-5 text-sm" role="status">
        <p class="text-ink">
          {{ (sale.isRunning ? 'sellerPortal.sale.running' : 'sellerPortal.sale.scheduled') | transloco: {
            price: (sale.price | inr),
            starts: (sale.startsAtUtc | dateIst: 'datetime'),
            ends: (sale.endsAtUtc | dateIst: 'datetime')
          } }}
        </p>
        @if (canWrite()) {
        <button mat-stroked-button color="warn" type="button" [disabled]="busy()" (click)="endSale()">
          {{ 'sellerPortal.sale.end' | transloco }}
        </button>
        }
      </div>
      }

      @if (!canWrite()) {
      <p class="text-sm text-ink-muted">{{ 'catalogAdmin.readOnly' | transloco }}</p>
      }

      <form class="upb-card space-y-1 p-5" (submit)="saveDetails($event)">
        <h2 class="font-medium text-ink">{{ 'sellerPortal.sectionDetails' | transloco }}</h2>
        <div class="grid gap-x-4 sm:grid-cols-2">
          @if (isNew()) {
          <mat-form-field class="sm:col-span-2" subscriptSizing="dynamic">
            <mat-label>{{ 'catalogAdmin.seller' | transloco }}</mat-label>
            <mat-select [value]="details().sellerId" required (valueChange)="setField('sellerId', $event)">
              @for (seller of sellers(); track seller.id) {
              <mat-option [value]="seller.id">{{ seller.shopName }}</mat-option>
              }
            </mat-select>
            <mat-hint>{{ 'catalogAdmin.sellerHint' | transloco }}</mat-hint>
          </mat-form-field>
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ 'seller.sku' | transloco }}</mat-label>
            <input matInput name="sku" required [value]="details().sku" (input)="setDetail('sku', $event)" />
          </mat-form-field>
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ 'seller.initialStock' | transloco }}</mat-label>
            <input matInput name="onHandQuantity" type="number" min="0" [value]="details().onHandQuantity" (input)="setDetail('onHandQuantity', $event)" />
          </mat-form-field>
          }
          <mat-form-field class="sm:col-span-2" subscriptSizing="dynamic">
            <mat-label>{{ 'seller.productName' | transloco }}</mat-label>
            <input matInput name="name" required [readonly]="!editable()" [value]="details().name" (input)="setDetail('name', $event)" />
          </mat-form-field>
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ 'sellerPortal.brand' | transloco }} ({{ 'common.optional' | transloco }})</mat-label>
            <input matInput name="brand" [readonly]="!editable()" [value]="details().brand" (input)="setDetail('brand', $event)" />
          </mat-form-field>
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ 'seller.category' | transloco }}</mat-label>
            <mat-select [value]="details().categoryId" [disabled]="!editable()" (valueChange)="setField('categoryId', $event)">
              @for (category of categories(); track category.id) {
              <mat-option [value]="category.id">{{ category.name }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ 'seller.price' | transloco }}</mat-label>
            <input matInput name="price" type="number" min="1" step="0.01" required [readonly]="!editable()" [value]="details().price" (input)="setDetail('price', $event)" />
          </mat-form-field>
          <mat-form-field class="sm:col-span-2" subscriptSizing="dynamic">
            <mat-label>{{ 'seller.description' | transloco }} ({{ 'common.optional' | transloco }})</mat-label>
            <textarea matInput name="description" rows="5" [readonly]="!editable()" [value]="details().description"
              (input)="setDetail('description', $event)"></textarea>
          </mat-form-field>
        </div>
        <upb-field-errors fieldId="details" [errors]="errors('details')" />
        @if (editable()) {
        <div class="flex flex-wrap gap-3 pt-2">
          <button mat-flat-button color="primary" type="submit" [disabled]="busy()">{{ 'common.save' | transloco }}</button>
          @if (product()?.status === 'Draft' || product()?.status === 'InReview') {
          <button mat-stroked-button type="button" [disabled]="busy()" (click)="publish()">{{ 'seller.publish' | transloco }}</button>
          }
          @if (product()) {
          <span class="flex-1"></span>
          <button mat-button color="warn" type="button" [disabled]="busy()" (click)="archive()">{{ 'catalogAdmin.archive' | transloco }}</button>
          }
        </div>
        }
      </form>

      @if (product(); as p) {
      @if (canReadStock()) {
      <form class="upb-card space-y-2 p-5" (submit)="saveStock($event)">
        <h2 class="font-medium text-ink">{{ 'sellerPortal.stock' | transloco }}</h2>
        @if (stock(); as s) {
        <p class="text-sm text-ink-muted">
          {{ 'sellerPortal.stockNow' | transloco: { onHand: s.level.onHandQuantity, reserved: s.level.reservedQuantity } }}
        </p>
        }
        @if (canWriteStock() && p.status !== 'Archived') {
        <div class="flex flex-wrap items-end gap-3">
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ 'sellerPortal.countedOnHand' | transloco }}</mat-label>
            <input matInput name="counted" type="number" min="0" [value]="counted()" (input)="counted.set(value($event))" />
          </mat-form-field>
          <mat-form-field class="flex-1" subscriptSizing="dynamic">
            <mat-label>{{ 'catalogAdmin.stockReason' | transloco }}</mat-label>
            <input matInput name="reason" [value]="stockReason()" (input)="stockReason.set(value($event))" />
          </mat-form-field>
          <button mat-stroked-button type="submit" [disabled]="busy() || counted() === ''">{{ 'sellerPortal.recordCount' | transloco }}</button>
        </div>
        <upb-field-errors fieldId="stock" [errors]="errors('stock')" />
        }
      </form>
      }

      <form class="upb-card space-y-2 p-5" (submit)="savePackage($event)">
        <h2 class="font-medium text-ink">{{ 'sellerPortal.package' | transloco }}</h2>
        <p class="text-sm text-ink-muted">{{ 'sellerPortal.packageNote' | transloco }}</p>
        <div class="grid grid-cols-2 gap-3 sm:grid-cols-4">
          @for (field of packageFields; track field.key) {
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ field.label | transloco }}</mat-label>
            <input matInput type="number" min="0" step="any" [name]="field.key" [readonly]="!canWrite()" [value]="pack()[field.key]" (input)="setPack(field.key, $event)" />
          </mat-form-field>
          }
        </div>
        <upb-field-errors fieldId="package" [errors]="errors('package')" />
        @if (canWrite()) {
        <button mat-stroked-button type="submit" [disabled]="busy()">{{ 'sellerPortal.savePackage' | transloco }}</button>
        }
      </form>
      }
    </section>
  `,
})
export class ProductEditPage {
  /** Route parameter: a product id, or "new". */
  readonly productId = input.required<string>();

  protected readonly productsWrite = CatalogPermissions.ProductsWrite;
  protected readonly packageFields: readonly { key: keyof Package; label: string }[] = [
    { key: 'weightGrams', label: 'shipping.weightGrams' },
    { key: 'lengthCm', label: 'shipping.lengthCm' },
    { key: 'breadthCm', label: 'shipping.breadthCm' },
    { key: 'heightCm', label: 'shipping.heightCm' },
  ];

  protected readonly product = signal<ProductDto | null>(null);
  protected readonly stock = signal<StockDetailDto | null>(null);
  protected readonly categories = signal<readonly CategoryDto[]>([]);
  protected readonly sellers = signal<readonly SellerNameDto[]>([]);
  protected readonly details = signal<Details>(emptyDetails());
  protected readonly pack = signal<Package>({ weightGrams: '', lengthCm: '', breadthCm: '', heightCm: '' });
  protected readonly counted = signal('');
  protected readonly stockReason = signal('');
  protected readonly busy = signal(false);
  private readonly problems = signal<Record<string, ApiProblem | null>>({});

  private readonly currentUser = inject(CurrentUserStore);
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);

  protected readonly isNew = computed(() => this.productId() === 'new');
  protected readonly canWrite = computed(() => this.currentUser.has(CatalogPermissions.ProductsWrite));
  protected readonly canReadStock = computed(() => this.currentUser.has(InventoryPermissions.StockRead));
  protected readonly canWriteStock = computed(() => this.currentUser.has(InventoryPermissions.StockWrite));

  /** An archived listing is frozen; everything else is a moderator's to correct. */
  protected readonly editable = computed(() => this.canWrite() && this.product()?.status !== 'Archived');

  /** The seller's shop name, when the picker list has it; the bare id otherwise. */
  protected readonly sellerName = computed(() => {
    const id = this.product()?.sellerId;

    return this.sellers().find((s) => s.id === id)?.shopName ?? id;
  });

  constructor() {
    void this.loadCategories();

    if (this.canWrite()) {
      void this.loadSellers();
    }

    effect(() => {
      const id = this.productId();
      untracked(() => void (id === 'new' ? this.reset() : this.load(id)));
    });
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected setDetail(field: keyof Details, event: Event): void {
    this.setField(field, this.value(event));
  }

  protected setField(field: keyof Details, value: string): void {
    this.details.update((d) => ({ ...d, [field]: value }));
  }

  protected setPack(field: keyof Package, event: Event): void {
    const value = this.value(event);
    this.pack.update((p) => ({ ...p, [field]: value }));
  }

  /** Server messages for one form, whichever field they name. */
  protected errors(form: string): readonly string[] {
    const problem = this.problems()[form] ?? null;

    if (!problem) {
      return [];
    }

    const fields = Object.keys(problem.fieldErrors);

    return fields.length > 0 ? fields.flatMap((f) => fieldErrorsFor(problem, f)) : [problem.title];
  }

  protected async saveDetails(event: Event): Promise<void> {
    event.preventDefault();
    const d = this.details();
    const body = {
      name: d.name.trim(),
      brand: d.brand.trim() || null,
      description: d.description.trim() || null,
      price: Number(d.price),
      categoryId: d.categoryId,
    };

    await this.run('details', async () => {
      if (this.isNew()) {
        const created = await this.api.invoke(apiV1AdminCatalogProductsPost, {
          body: { ...body, sellerId: d.sellerId, sku: d.sku.trim(), onHandQuantity: Number(d.onHandQuantity || 0) },
        });

        this.toast.success('seller.created');
        await this.router.navigate(['/catalog/products', created.id]);
      } else {
        this.show(await this.api.invoke(apiV1AdminCatalogProductsProductIdPut, { productId: this.productId(), body }));
        this.toast.success('sellerPortal.saved');
      }
    });
  }

  protected async publish(): Promise<void> {
    await this.run('details', async () => {
      this.show(await this.api.invoke(apiV1AdminCatalogProductsProductIdPublishPost, { productId: this.productId() }));
      this.toast.success('seller.published');
    });
  }

  /** Ends a seller's sale - one that misleads, say. The seller sets sales; staff can only end them. */
  protected async endSale(): Promise<void> {
    await this.run('details', async () => {
      this.show(await this.api.invoke(apiV1AdminCatalogProductsProductIdSaleDelete, { productId: this.productId() }));
      this.toast.success('sellerPortal.sale.ended');
    });
  }

  protected async archive(): Promise<void> {
    const confirmed = await confirm(this.dialog, {
      title: 'catalogAdmin.archiveTitle',
      body: 'catalogAdmin.archiveBody',
      params: { name: this.product()?.name },
      confirmLabel: 'catalogAdmin.archive',
      destructive: true,
    });

    if (!confirmed) {
      return;
    }

    await this.run('details', async () => {
      await this.api.invoke(apiV1AdminCatalogProductsProductIdDelete, { productId: this.productId() });
      this.toast.success('catalogAdmin.archived');
      await this.load(this.productId());
    });
  }

  protected async saveStock(event: Event): Promise<void> {
    event.preventDefault();

    await this.run('stock', async () => {
      await this.api.invoke(apiV1AdminInventoryStockProductIdPut, {
        productId: this.productId(),
        body: { onHandQuantity: Number(this.counted()), reason: this.stockReason().trim() || 'Staff count' },
      });
      this.counted.set('');
      this.stockReason.set('');
      await this.loadStock(this.productId());
      this.toast.success('sellerPortal.saved');
    });
  }

  protected async savePackage(event: Event): Promise<void> {
    event.preventDefault();
    const p = this.pack();
    const all = Object.values(p).every((v) => v !== '');

    await this.run('package', async () => {
      this.show(await this.api.invoke(apiV1AdminCatalogProductsProductIdPackagePut, {
        productId: this.productId(),
        body: all
          ? { weightGrams: Math.round(Number(p.weightGrams)), lengthCm: Number(p.lengthCm), breadthCm: Number(p.breadthCm), heightCm: Number(p.heightCm) }
          : { weightGrams: null, lengthCm: null, breadthCm: null, heightCm: null },
      }));
      this.toast.success('sellerPortal.saved');
    });
  }

  private async run(form: string, work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.problems.update((p) => ({ ...p, [form]: null }));

    try {
      await work();
    } catch (error) {
      this.problems.update((p) => ({ ...p, [form]: toApiProblem(error) }));
    } finally {
      this.busy.set(false);
    }
  }

  private reset(): void {
    this.product.set(null);
    this.stock.set(null);
    this.details.set(emptyDetails());
  }

  private async load(productId: string): Promise<void> {
    try {
      this.show(await this.api.invoke(catalogGetProduct, { productId }));

      if (this.canReadStock()) {
        await this.loadStock(productId);
      }
    } catch {
      await this.router.navigate(['/catalog/products']);
    }
  }

  private async loadStock(productId: string): Promise<void> {
    try {
      this.stock.set(await this.api.invoke(apiV1AdminInventoryStockProductIdGet, { productId }));
    } catch {
      this.stock.set(null);
    }
  }

  private show(product: ProductDto): void {
    this.product.set(product);
    this.details.set({
      sellerId: product.sellerId,
      sku: product.sku,
      name: product.name,
      brand: product.brand ?? '',
      description: product.description ?? '',
      price: String(product.price),
      categoryId: product.category.id,
      onHandQuantity: '0',
    });

    const pk = product.package;
    this.pack.set(pk
      ? { weightGrams: String(pk.weightGrams), lengthCm: String(pk.lengthCm), breadthCm: String(pk.breadthCm), heightCm: String(pk.heightCm) }
      : { weightGrams: '', lengthCm: '', breadthCm: '', heightCm: '' });
  }

  private async loadCategories(): Promise<void> {
    try {
      this.categories.set(await this.api.invoke(apiV1CatalogCategoriesGet, {}));
    } catch {
      // Reported by the interceptor; the select stays empty.
    }
  }

  private async loadSellers(): Promise<void> {
    try {
      this.sellers.set(await this.api.invoke(apiV1AdminCatalogSellersGet, {}));
    } catch {
      // Reported by the interceptor; the seller shows by id.
    }
  }
}

function emptyDetails(): Details {
  return { sellerId: '', sku: '', name: '', brand: '', description: '', price: '', categoryId: '', onHandQuantity: '0' };
}
