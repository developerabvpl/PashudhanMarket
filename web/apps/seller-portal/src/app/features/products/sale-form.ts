import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  Api,
  ApiProblem,
  ProductDto,
  apiV1SellerCatalogProductsProductIdSaleDelete,
  apiV1SellerCatalogProductsProductIdSalePut,
  fieldErrorsFor,
  toApiProblem,
} from '@upbazaar/data-access';
import { FieldErrors, ToastService } from '@upbazaar/ui';
import { DateIstPipe, InrCurrencyPipe, endOfIstDay, startOfIstDay } from '@upbazaar/util';

/**
 * A seller putting their product on sale: a lower price between two days, which they pay for -
 * they are paid on the sale price. A sale set shows here with a way to end it; setting another
 * replaces it. Days are days in India: a sale ending on the 31st runs all through the 31st.
 */
@Component({
  selector: 'upb-sale-form',
  // A custom element is inline by default, and the page's spacing between cards skips inline boxes.
  host: { class: 'block' },
  imports: [MatButtonModule, MatFormFieldModule, MatInputModule, TranslocoPipe, FieldErrors, DateIstPipe, InrCurrencyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="upb-card space-y-3 p-5" (submit)="save($event)">
      <h2 class="font-medium text-ink">{{ 'sellerPortal.sale.title' | transloco }}</h2>
      <p class="text-sm text-ink-muted">{{ 'sellerPortal.sale.note' | transloco }}</p>

      @if (product().sale; as sale) {
      <div class="flex flex-wrap items-center justify-between gap-3 rounded-control bg-surface-sunken p-3 text-sm" role="status">
        <p class="text-ink">
          {{ (sale.isRunning ? 'sellerPortal.sale.running' : 'sellerPortal.sale.scheduled') | transloco: {
            price: (sale.price | inr),
            starts: (sale.startsAtUtc | dateIst: 'datetime'),
            ends: (sale.endsAtUtc | dateIst: 'datetime')
          } }}
        </p>
        <button mat-stroked-button color="warn" type="button" [disabled]="busy()" (click)="end()">
          {{ 'sellerPortal.sale.end' | transloco }}
        </button>
      </div>
      }

      <div class="flex flex-wrap items-end gap-3">
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'sellerPortal.sale.price' | transloco }}</mat-label>
          <input matInput name="salePrice" type="number" min="1" step="0.01" required [value]="salePrice()" (input)="salePrice.set(value($event))" />
        </mat-form-field>
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'sellerPortal.sale.startsOn' | transloco }}</mat-label>
          <input matInput name="startsOn" type="date" [value]="startsOn()" (input)="startsOn.set(value($event))" />
        </mat-form-field>
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'sellerPortal.sale.endsOn' | transloco }}</mat-label>
          <input matInput name="endsOn" type="date" required [value]="endsOn()" (input)="endsOn.set(value($event))" />
        </mat-form-field>
        <button mat-stroked-button type="submit" [disabled]="busy() || !canSave()">
          {{ (product().sale ? 'sellerPortal.sale.replace' : 'sellerPortal.sale.set') | transloco }}
        </button>
      </div>
      <p class="text-xs text-ink-muted">{{ 'sellerPortal.sale.startsHint' | transloco }}</p>
      <upb-field-errors fieldId="sale" [errors]="errors()" />
    </form>
  `,
})
export class SaleForm {
  /** The seller's product. */
  readonly product = input.required<ProductDto>();

  /** The product as it stands after a sale is set or ended. */
  readonly changed = output<ProductDto>();

  protected readonly salePrice = signal('');
  protected readonly startsOn = signal('');
  protected readonly endsOn = signal('');
  protected readonly busy = signal(false);

  protected readonly canSave = computed(() => {
    const price = Number(this.salePrice());

    return price > 0 && price < this.product().price && this.endsOn() !== '';
  });

  private readonly problem = signal<ApiProblem | null>(null);
  private readonly api = inject(Api);
  private readonly toast = inject(ToastService);

  protected readonly errors = computed(() => {
    const problem = this.problem();

    if (!problem) {
      return [];
    }

    const fields = Object.keys(problem.fieldErrors);

    return fields.length > 0 ? fields.flatMap((f) => fieldErrorsFor(problem, f)) : [problem.title];
  });

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected async save(event: Event): Promise<void> {
    event.preventDefault();

    await this.run('sellerPortal.sale.saved', () =>
      this.api.invoke(apiV1SellerCatalogProductsProductIdSalePut, {
        productId: this.product().id,
        body: {
          salePrice: Number(this.salePrice()),
          startsAtUtc: this.startsOn() ? startOfIstDay(this.startsOn()) : null,
          endsAtUtc: endOfIstDay(this.endsOn()),
        },
      })
    );
  }

  protected async end(): Promise<void> {
    await this.run('sellerPortal.sale.ended', () =>
      this.api.invoke(apiV1SellerCatalogProductsProductIdSaleDelete, { productId: this.product().id })
    );
  }

  private async run(done: string, work: () => Promise<ProductDto>): Promise<void> {
    this.busy.set(true);
    this.problem.set(null);

    try {
      this.changed.emit(await work());
      this.salePrice.set('');
      this.startsOn.set('');
      this.endsOn.set('');
      this.toast.success(done);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
