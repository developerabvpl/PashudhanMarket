import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  Api,
  ApiProblem,
  CategoryDto,
  ProductDto,
  StockDetailDto,
  apiV1CatalogCategoriesGet,
  apiV1SellerCatalogProductsPost,
  apiV1SellerCatalogProductsProductIdGet,
  apiV1SellerCatalogProductsProductIdPackagePut,
  apiV1SellerCatalogProductsProductIdPricePut,
  apiV1SellerCatalogProductsProductIdPut,
  apiV1SellerCatalogProductsProductIdSubmitPost,
  apiV1SellerInventoryStockProductIdGet,
  apiV1SellerInventoryStockProductIdPut,
  fieldErrorsFor,
  toApiProblem,
} from '@upbazaar/data-access';
import { FieldErrors, ToastService } from '@upbazaar/ui';

interface Details {
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
 * Creating a listing, or looking after one.
 *
 * What can change depends on where the listing is. A draft is the seller's to edit freely and
 * then submit for review. Once live, its wording belongs to what the moderator approved, so only
 * the operational parts stay open here: price, stock and how it ships.
 */
@Component({
  selector: 'upb-product-edit-page',
  imports: [RouterLink, TranslocoPipe, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, FieldErrors],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-3xl space-y-6 px-4 py-8">
      <a class="text-sm" routerLink="/products">← {{ 'sellerPortal.productsTitle' | transloco }}</a>

      <header class="flex flex-wrap items-center justify-between gap-3">
        <h1 class="text-2xl font-semibold text-ink">
          {{ isNew() ? ('seller.newProduct' | transloco) : product()?.name }}
        </h1>
        @if (product(); as p) {
        <span class="rounded-full bg-surface-sunken px-3 py-1 text-sm">{{ 'sellerPortal.productStatus.' + p.status | transloco }}</span>
        }
      </header>

      @if (product()?.reviewNote; as note) {
      <div class="rounded-card border border-danger/40 bg-danger/5 p-4 text-sm" role="alert">
        <p class="font-medium text-ink">{{ 'sellerPortal.sentBack' | transloco }}</p>
        <p class="mt-1 text-ink">{{ note }}</p>
      </div>
      }

      <form class="upb-card space-y-1 p-5" (submit)="saveDetails($event)">
        <h2 class="font-medium text-ink">{{ 'sellerPortal.sectionDetails' | transloco }}</h2>
        @if (!editable()) {
        <p class="text-sm text-ink-muted">{{ 'sellerPortal.liveLocked' | transloco }}</p>
        }
        <div class="grid gap-x-4 sm:grid-cols-2">
          @if (isNew()) {
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ 'seller.sku' | transloco }}</mat-label>
            <input matInput name="sku" required [value]="details().sku" (input)="setDetail('sku', $event)" />
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
            <mat-select [value]="details().categoryId" [disabled]="!editable()" (valueChange)="setCategory($event)">
              @for (category of categories(); track category.id) {
              <mat-option [value]="category.id">{{ category.name }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
          <mat-form-field class="sm:col-span-2" subscriptSizing="dynamic">
            <mat-label>{{ 'seller.description' | transloco }} ({{ 'common.optional' | transloco }})</mat-label>
            <textarea matInput name="description" rows="4" [readonly]="!editable()" [value]="details().description"
              (input)="setDetail('description', $event)"></textarea>
          </mat-form-field>
          @if (isNew()) {
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ 'seller.price' | transloco }}</mat-label>
            <input matInput name="price" type="number" min="1" step="0.01" required [value]="details().price" (input)="setDetail('price', $event)" />
          </mat-form-field>
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ 'seller.initialStock' | transloco }}</mat-label>
            <input matInput name="onHandQuantity" type="number" min="0" [value]="details().onHandQuantity" (input)="setDetail('onHandQuantity', $event)" />
          </mat-form-field>
          }
        </div>
        <upb-field-errors fieldId="details" [errors]="errors('details')" />
        @if (editable()) {
        <div class="flex flex-wrap gap-3 pt-2">
          <button mat-flat-button color="primary" type="submit" [disabled]="busy()">{{ 'common.save' | transloco }}</button>
          @if (product()?.status === 'Draft') {
          <button mat-stroked-button type="button" [disabled]="busy()" (click)="submitForReview()">
            {{ 'sellerPortal.submitForReview' | transloco }}
          </button>
          }
        </div>
        }
      </form>

      @if (product(); as p) {
      <form class="upb-card flex flex-wrap items-end gap-3 p-5" (submit)="savePrice($event)">
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'seller.price' | transloco }}</mat-label>
          <input matInput name="price" type="number" min="1" step="0.01" [value]="price()" (input)="price.set(value($event))" />
        </mat-form-field>
        <button mat-stroked-button type="submit" [disabled]="busy() || Number(price()) <= 0">{{ 'sellerPortal.updatePrice' | transloco }}</button>
        <upb-field-errors class="w-full" fieldId="price" [errors]="errors('price')" />
      </form>

      <form class="upb-card space-y-2 p-5" (submit)="saveStock($event)">
        <h2 class="font-medium text-ink">{{ 'sellerPortal.stock' | transloco }}</h2>
        @if (stock(); as s) {
        <p class="text-sm text-ink-muted">
          {{ 'sellerPortal.stockNow' | transloco: { onHand: s.level.onHandQuantity, reserved: s.level.reservedQuantity } }}
        </p>
        }
        <div class="flex flex-wrap items-end gap-3">
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ 'sellerPortal.countedOnHand' | transloco }}</mat-label>
            <input matInput name="counted" type="number" min="0" [value]="counted()" (input)="counted.set(value($event))" />
          </mat-form-field>
          <button mat-stroked-button type="submit" [disabled]="busy() || counted() === ''">{{ 'sellerPortal.recordCount' | transloco }}</button>
        </div>
        <upb-field-errors fieldId="stock" [errors]="errors('stock')" />
      </form>

      <form class="upb-card space-y-2 p-5" (submit)="savePackage($event)">
        <h2 class="font-medium text-ink">{{ 'sellerPortal.package' | transloco }}</h2>
        <p class="text-sm text-ink-muted">{{ 'sellerPortal.packageNote' | transloco }}</p>
        <div class="grid grid-cols-2 gap-3 sm:grid-cols-4">
          @for (field of packageFields; track field.key) {
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ field.label | transloco }}</mat-label>
            <input matInput type="number" min="0" step="any" [name]="field.key" [value]="pack()[field.key]" (input)="setPack(field.key, $event)" />
          </mat-form-field>
          }
        </div>
        <upb-field-errors fieldId="package" [errors]="errors('package')" />
        <button mat-stroked-button type="submit" [disabled]="busy()">{{ 'sellerPortal.savePackage' | transloco }}</button>
      </form>
      }
    </section>
  `,
})
export class ProductEditPage {
  /** Route parameter: a product id, or "new". */
  readonly productId = input.required<string>();

  protected readonly Number = Number;
  protected readonly packageFields: readonly { key: keyof Package; label: string }[] = [
    { key: 'weightGrams', label: 'shipping.weightGrams' },
    { key: 'lengthCm', label: 'shipping.lengthCm' },
    { key: 'breadthCm', label: 'shipping.breadthCm' },
    { key: 'heightCm', label: 'shipping.heightCm' },
  ];

  protected readonly product = signal<ProductDto | null>(null);
  protected readonly stock = signal<StockDetailDto | null>(null);
  protected readonly categories = signal<readonly CategoryDto[]>([]);
  protected readonly details = signal<Details>({ sku: '', name: '', brand: '', description: '', price: '', categoryId: '', onHandQuantity: '0' });
  protected readonly pack = signal<Package>({ weightGrams: '', lengthCm: '', breadthCm: '', heightCm: '' });
  protected readonly price = signal('');
  protected readonly counted = signal('');
  protected readonly busy = signal(false);
  private readonly problems = signal<Record<string, ApiProblem | null>>({});

  protected readonly isNew = computed(() => this.productId() === 'new');

  /** Wording and category are the seller's to change only on a new listing or a draft. */
  protected readonly editable = computed(() => this.isNew() || this.product()?.status === 'Draft');

  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  constructor() {
    void this.loadCategories();

    effect(() => {
      const id = this.productId();
      untracked(() => void (id === 'new' ? Promise.resolve() : this.load(id)));
    });
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected setDetail(field: keyof Details, event: Event): void {
    const value = this.value(event);
    this.details.update((d) => ({ ...d, [field]: value }));
  }

  protected setCategory(categoryId: string): void {
    this.details.update((d) => ({ ...d, categoryId }));
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
      price: Number(d.price || this.product()?.price || 0),
      categoryId: d.categoryId,
    };

    await this.run('details', async () => {
      if (this.isNew()) {
        const created = await this.api.invoke(apiV1SellerCatalogProductsPost, {
          body: { ...body, sku: d.sku.trim(), onHandQuantity: Number(d.onHandQuantity || 0) },
        });

        this.toast.success('seller.created');
        await this.router.navigate(['/products', created.id]);
      } else {
        this.show(await this.api.invoke(apiV1SellerCatalogProductsProductIdPut, { productId: this.productId(), body }));
        this.toast.success('sellerPortal.saved');
      }
    });
  }

  protected async submitForReview(): Promise<void> {
    await this.run('details', async () => {
      this.show(await this.api.invoke(apiV1SellerCatalogProductsProductIdSubmitPost, { productId: this.productId() }));
      this.toast.success('sellerPortal.submittedForReview');
    });
  }

  protected async savePrice(event: Event): Promise<void> {
    event.preventDefault();

    await this.run('price', async () => {
      this.show(await this.api.invoke(apiV1SellerCatalogProductsProductIdPricePut, {
        productId: this.productId(),
        body: { price: Number(this.price()) },
      }));
      this.toast.success('sellerPortal.saved');
    });
  }

  protected async saveStock(event: Event): Promise<void> {
    event.preventDefault();

    await this.run('stock', async () => {
      await this.api.invoke(apiV1SellerInventoryStockProductIdPut, {
        productId: this.productId(),
        body: { onHandQuantity: Number(this.counted()), reason: 'Seller count' },
      });
      this.counted.set('');
      await this.loadStock(this.productId());
      this.toast.success('sellerPortal.saved');
    });
  }

  protected async savePackage(event: Event): Promise<void> {
    event.preventDefault();
    const p = this.pack();
    const all = Object.values(p).every((v) => v !== '');

    await this.run('package', async () => {
      this.show(await this.api.invoke(apiV1SellerCatalogProductsProductIdPackagePut, {
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

  private async load(productId: string): Promise<void> {
    try {
      this.show(await this.api.invoke(apiV1SellerCatalogProductsProductIdGet, { productId }));
      await this.loadStock(productId);
    } catch {
      await this.router.navigate(['/products']);
    }
  }

  private async loadStock(productId: string): Promise<void> {
    try {
      this.stock.set(await this.api.invoke(apiV1SellerInventoryStockProductIdGet, { productId }));
    } catch {
      this.stock.set(null);
    }
  }

  private show(product: ProductDto): void {
    this.product.set(product);
    this.details.set({
      sku: product.sku,
      name: product.name,
      brand: product.brand ?? '',
      description: product.description ?? '',
      price: String(product.price),
      categoryId: product.category.id,
      onHandQuantity: '0',
    });
    this.price.set(String(product.price));

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
}
