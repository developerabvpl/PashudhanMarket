import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoPipe } from '@jsverse/transloco';
import { Api, SellerOrderDto, apiV1SellerOrdersOrderIdPartsPartIdReturnInspectionPost } from '@upbazaar/data-access';
import { ToastService } from '@upbazaar/ui';
import { cameBack } from './came-back';

type Condition = 'Good' | 'Damaged';

/**
 * What the seller found in what came back - an undelivered parcel, or the units a buyer returned -
 * product by product. Good puts those units back on sale; Damaged does not. Asked once: the answer
 * cannot be changed afterwards, since changing it would put goods on sale, or take them off, a
 * second time.
 */
@Component({
  selector: 'upb-return-inspection',
  imports: [TranslocoPipe, MatButtonModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="rounded-card border border-warning/50 bg-warning/10 p-5" (submit)="inspect($event)">
      <h2 class="font-medium text-ink">
        {{ (order().returnRequest?.status === 'Approved' ? 'returns.inspectTitleBuyerReturn' : 'returns.inspectTitle') | transloco }}
      </h2>
      <p class="mt-1 text-sm text-ink">{{ 'returns.inspectBody' | transloco }}</p>

      <ul class="mt-3 divide-y divide-border text-sm">
        @for (line of lines(); track line.productId) {
        <li class="flex flex-wrap items-center justify-between gap-3 py-2">
          <span class="text-ink">{{ line.name }} × {{ line.quantity }}</span>
          <span class="flex gap-4" role="radiogroup" [attr.aria-label]="line.name">
            @for (c of options; track c) {
            <label class="flex items-center gap-1">
              <input type="radio" [name]="'condition-' + line.productId" [value]="c" [checked]="conditionOf(line.productId) === c" (change)="set(line.productId, c)" />
              {{ (c === 'Good' ? 'returns.good' : 'returns.damaged') | transloco }}
            </label>
            }
          </span>
        </li>
        }
      </ul>

      <mat-form-field class="mt-3 w-full" subscriptSizing="dynamic">
        <mat-label>{{ 'returns.note' | transloco }} ({{ 'common.optional' | transloco }})</mat-label>
        <input matInput name="note" maxlength="500" [value]="note()" (input)="note.set(value($event))" />
      </mat-form-field>
      <button mat-flat-button color="primary" class="mt-3" type="submit" [disabled]="busy()">
        {{ 'returns.recordInspection' | transloco }}
      </button>
    </form>
  `,
})
export class ReturnInspection {
  readonly order = input.required<SellerOrderDto>();
  readonly inspected = output<SellerOrderDto>();

  protected readonly options: readonly Condition[] = ['Good', 'Damaged'];
  protected readonly note = signal('');
  protected readonly busy = signal(false);

  /** What came back, and how many of each. */
  protected readonly lines = computed(() => cameBack(this.order()));

  /** Conditions chosen so far; a product not in here is taken to be Good. */
  private readonly conditions = signal<Readonly<Record<string, Condition>>>({});

  private readonly api = inject(Api);
  private readonly toast = inject(ToastService);

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected conditionOf(productId: string): Condition {
    return this.conditions()[productId] ?? 'Good';
  }

  protected set(productId: string, condition: Condition): void {
    this.conditions.update((c) => ({ ...c, [productId]: condition }));
  }

  protected async inspect(event: Event): Promise<void> {
    event.preventDefault();
    this.busy.set(true);

    try {
      const lines = this.lines().map((l) => ({ productId: l.productId, condition: this.conditionOf(l.productId) }));
      const updated = await this.api.invoke(apiV1SellerOrdersOrderIdPartsPartIdReturnInspectionPost, {
        orderId: this.order().orderId,
        partId: this.order().partId,
        body: { condition: null, note: this.note().trim() || null, lines },
      });

      this.toast.success(lines.some((l) => l.condition === 'Good') ? 'returns.restocked' : 'returns.recordedDamaged');
      this.inspected.emit(updated);
    } catch {
      // Reported by the interceptor.
    } finally {
      this.busy.set(false);
    }
  }
}
