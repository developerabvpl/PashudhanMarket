import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormField, form, min, minLength, required, submit } from '@angular/forms/signals';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  Api,
  ApiProblem,
  ProductDto,
  apiCatalogProductsPost,
  fieldErrorsFor,
  toApiProblem,
} from '@upbazaar/data-access';
import { FieldErrors, ToastService } from '@upbazaar/ui';

interface NewProduct {
  sellerId: string;
  sku: string;
  name: string;
  description: string;
  categoryId: string;
  price: number;
  currency: string;
  initialStock: number;
}

const EMPTY_PRODUCT: NewProduct = {
  sellerId: '',
  sku: '',
  name: '',
  description: '',
  categoryId: '',
  price: 0,
  currency: 'INR',
  initialStock: 0,
};

/**
 * Creates a draft product.
 *
 * Client validation catches the obvious mistakes; the API is still the authority, so a
 * rejected submit maps problem-details back onto the offending fields rather than only
 * raising a toast. A duplicate SKU, for instance, is a 409 the browser cannot predict.
 */
@Component({
  selector: 'upb-product-create',
  imports: [FormField, TranslocoPipe, FieldErrors],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-2xl px-4 py-8">
      <h1 class="text-2xl font-semibold text-ink">{{ 'seller.newProduct' | transloco }}</h1>

      <form class="upb-card mt-6 space-y-5 p-6" (submit)="save($event)">
        <div>
          <label class="block text-sm font-medium text-ink" for="name">
            {{ 'seller.productName' | transloco }}
          </label>
          <input
            id="name"
            type="text"
            class="mt-1 w-full rounded-control border border-border bg-surface px-3 py-2 text-ink"
            [formField]="productForm.name"
            [attr.aria-invalid]="nameErrors().length > 0"
            [attr.aria-describedby]="nameErrors().length > 0 ? 'name-errors' : 'name-hint'"
          />
          <p id="name-hint" class="mt-1 text-sm text-ink-muted">
            {{ 'seller.productNameHint' | transloco }}
          </p>
          <upb-field-errors fieldId="name" [errors]="nameErrors()" />
        </div>

        <div>
          <label class="block text-sm font-medium text-ink" for="sku">
            {{ 'seller.sku' | transloco }}
          </label>
          <input
            id="sku"
            type="text"
            class="mt-1 w-full rounded-control border border-border bg-surface px-3 py-2 font-mono text-ink"
            [formField]="productForm.sku"
            [attr.aria-invalid]="skuErrors().length > 0"
            [attr.aria-describedby]="skuErrors().length > 0 ? 'sku-errors' : 'sku-hint'"
          />
          <p id="sku-hint" class="mt-1 text-sm text-ink-muted">
            {{ 'seller.skuHint' | transloco }}
          </p>
          <upb-field-errors fieldId="sku" [errors]="skuErrors()" />
        </div>

        <div>
          <label class="block text-sm font-medium text-ink" for="description">
            {{ 'seller.description' | transloco }}
          </label>
          <textarea
            id="description"
            rows="4"
            class="mt-1 w-full rounded-control border border-border bg-surface px-3 py-2 text-ink"
            [formField]="productForm.description"
          ></textarea>
        </div>

        <div class="grid gap-4 sm:grid-cols-2">
          <div>
            <label class="block text-sm font-medium text-ink" for="price">
              {{ 'seller.price' | transloco }}
            </label>
            <input
              id="price"
              type="number"
              step="0.01"
              class="mt-1 w-full rounded-control border border-border bg-surface px-3 py-2 text-ink"
              [formField]="productForm.price"
              [attr.aria-invalid]="priceErrors().length > 0"
              [attr.aria-describedby]="priceErrors().length > 0 ? 'price-errors' : null"
            />
            <upb-field-errors fieldId="price" [errors]="priceErrors()" />
          </div>

          <div>
            <label class="block text-sm font-medium text-ink" for="initialStock">
              {{ 'seller.initialStock' | transloco }}
            </label>
            <input
              id="initialStock"
              type="number"
              class="mt-1 w-full rounded-control border border-border bg-surface px-3 py-2 text-ink"
              [formField]="productForm.initialStock"
            />
          </div>
        </div>

        <div class="grid gap-4 sm:grid-cols-2">
          <div>
            <label class="block text-sm font-medium text-ink" for="categoryId">
              {{ 'seller.category' | transloco }}
            </label>
            <input
              id="categoryId"
              type="text"
              class="mt-1 w-full rounded-control border border-border bg-surface px-3 py-2 font-mono text-xs text-ink"
              [formField]="productForm.categoryId"
              [attr.aria-describedby]="categoryErrors().length > 0 ? 'categoryId-errors' : null"
            />
            <upb-field-errors fieldId="categoryId" [errors]="categoryErrors()" />
          </div>

          <div>
            <label class="block text-sm font-medium text-ink" for="sellerId">
              {{ 'seller.sellerId' | transloco }}
            </label>
            <input
              id="sellerId"
              type="text"
              class="mt-1 w-full rounded-control border border-border bg-surface px-3 py-2 font-mono text-xs text-ink"
              [formField]="productForm.sellerId"
              [attr.aria-describedby]="sellerErrors().length > 0 ? 'sellerId-errors' : null"
            />
            <upb-field-errors fieldId="sellerId" [errors]="sellerErrors()" />
          </div>
        </div>

        <button
          type="submit"
          class="w-full rounded-control bg-brand-600 px-4 py-2 font-medium text-white hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-50"
          [disabled]="saving()"
        >
          {{ (saving() ? 'seller.saving' : 'seller.save') | transloco }}
        </button>
      </form>

      @if (created(); as product) {
      <p class="mt-4 text-sm text-success" role="status">
        {{ 'seller.created' | transloco }} — {{ product.sku }}
      </p>
      }
    </section>
  `,
})
export class ProductCreate {
  private readonly api = inject(Api);
  private readonly toast = inject(ToastService);

  private readonly model = signal<NewProduct>({ ...EMPTY_PRODUCT });

  /** Server-side rejections, keyed by field, merged into the messages under each input. */
  private readonly problem = signal<ApiProblem | null>(null);

  protected readonly saving = signal(false);
  protected readonly created = signal<ProductDto | null>(null);

  protected readonly productForm = form(this.model, (path) => {
    required(path.name, { message: 'validation.required' });
    minLength(path.name, 2, { message: 'validation.required' });
    required(path.sku, { message: 'validation.required' });
    required(path.categoryId, { message: 'validation.required' });
    required(path.sellerId, { message: 'validation.required' });
    min(path.price, 0.01, { message: 'validation.positivePrice' });
    min(path.initialStock, 0, { message: 'validation.required' });
  });

  protected readonly nameErrors = this.errorsFor('name');
  protected readonly skuErrors = this.errorsFor('sku');
  protected readonly priceErrors = this.errorsFor('price');
  protected readonly categoryErrors = this.errorsFor('categoryId');
  protected readonly sellerErrors = this.errorsFor('sellerId');

  async save(event: Event): Promise<void> {
    event.preventDefault();

    this.saving.set(true);
    this.problem.set(null);

    await submit(this.productForm, async () => {
      try {
        const product = await this.api.invoke(apiCatalogProductsPost, {
          body: {
            sellerId: this.model().sellerId,
            sku: this.model().sku,
            name: this.model().name,
            description: this.model().description || null,
            categoryId: this.model().categoryId,
            price: Number(this.model().price),
            currency: this.model().currency,
            initialStock: Number(this.model().initialStock),
          },
        });

        this.created.set(product);
        this.toast.success('seller.created');

        // Clearing the model alone leaves the inputs showing the old values, because the
        // controls buffer their own value; reset() re-syncs them and clears touched/dirty.
        this.model.set({ ...EMPTY_PRODUCT });
        this.productForm().reset();
      } catch (error) {
        // The global interceptor already toasted; keep the detail for the fields.
        this.problem.set(toApiProblem(error));
      }

      return undefined;
    });

    this.saving.set(false);
  }

  /**
   * Merges the two sources of truth for one control: the schema's own validation errors and
   * anything the API said about that field.
   */
  private errorsFor(field: keyof NewProduct) {
    return computed<readonly string[]>(() => {
      const state = this.productForm[field]();
      const local = state.touched()
        ? state.errors().map((error) => error.message ?? `validation.${error.kind}`)
        : [];

      return [...local, ...fieldErrorsFor(this.problem(), field)];
    });
  }
}
