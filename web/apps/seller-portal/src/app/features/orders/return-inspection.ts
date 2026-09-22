import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoPipe } from '@jsverse/transloco';
import { Api, SellerOrderDto, apiV1SellerOrdersOrderIdPartsPartIdReturnInspectionPost } from '@upbazaar/data-access';
import { ToastService } from '@upbazaar/ui';

/**
 * What the seller found in a parcel the courier brought back. Good puts the stock back on sale;
 * Damaged does not. Asked once: the answer cannot be changed afterwards, since changing it would
 * put goods on sale, or take them off, a second time.
 */
@Component({
  selector: 'upb-return-inspection',
  imports: [TranslocoPipe, MatButtonModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="rounded-card border border-warning/50 bg-warning/10 p-5">
      <h2 class="font-medium text-ink">{{ 'returns.inspectTitle' | transloco }}</h2>
      <p class="mt-1 text-sm text-ink">{{ 'returns.inspectBody' | transloco }}</p>
      <mat-form-field class="mt-3 w-full" subscriptSizing="dynamic">
        <mat-label>{{ 'returns.note' | transloco }} ({{ 'common.optional' | transloco }})</mat-label>
        <input matInput name="note" maxlength="500" [value]="note()" (input)="note.set(value($event))" />
      </mat-form-field>
      <div class="mt-3 flex flex-wrap gap-3">
        <button mat-flat-button color="primary" type="button" [disabled]="busy()" (click)="inspect('Good')">
          {{ 'returns.good' | transloco }}
        </button>
        <button mat-stroked-button color="warn" type="button" [disabled]="busy()" (click)="inspect('Damaged')">
          {{ 'returns.damaged' | transloco }}
        </button>
      </div>
    </div>
  `,
})
export class ReturnInspection {
  readonly order = input.required<SellerOrderDto>();
  readonly inspected = output<SellerOrderDto>();

  protected readonly note = signal('');
  protected readonly busy = signal(false);

  private readonly api = inject(Api);
  private readonly toast = inject(ToastService);

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected async inspect(condition: 'Good' | 'Damaged'): Promise<void> {
    this.busy.set(true);

    try {
      const updated = await this.api.invoke(apiV1SellerOrdersOrderIdPartsPartIdReturnInspectionPost, {
        orderId: this.order().orderId,
        partId: this.order().partId,
        body: { condition, note: this.note().trim() || null },
      });

      this.toast.success(condition === 'Good' ? 'returns.restocked' : 'returns.recordedDamaged');
      this.inspected.emit(updated);
    } catch {
      // Reported by the interceptor.
    } finally {
      this.busy.set(false);
    }
  }
}
