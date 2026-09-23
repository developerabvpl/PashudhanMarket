import { ChangeDetectionStrategy, Component, WritableSignal, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe } from '@jsverse/transloco';
import { CurrentUserStore } from '@upbazaar/auth';
import {
  Api,
  SellerCommissionDto,
  SellerNameDto,
  SettlementPolicyDto,
  apiV1AdminSettlementsCommissionsGet,
  apiV1AdminSettlementsCommissionsSellerIdDelete,
  apiV1AdminSettlementsCommissionsSellerIdPut,
  apiV1AdminSettlementsPolicyGet,
  apiV1AdminSettlementsPolicyPut,
  apiV1AdminSettlementsSellersGet,
  fieldErrorsFor,
  toApiProblem,
} from '@upbazaar/data-access';
import { FieldErrors, ToastService } from '@upbazaar/ui';
import { DateIstPipe } from '@upbazaar/util';
import { SettlementsPermissions } from '../../core/permissions';

type RateField = 'defaultCommissionPercent' | 'tcsPercent' | 'tdsPercent';

/**
 * The commission the platform takes and the taxes it withholds, plus the sellers who have agreed
 * a commission of their own.
 *
 * Every change applies to parcels delivered from then on; what sellers have already earned keeps
 * its rates, which the page says so nobody expects a change to reach back. The tax rates are the
 * accountant's to give, and the page says that too.
 */
@Component({
  selector: 'upb-settlement-rates-page',
  imports: [TranslocoPipe, DateIstPipe, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, FieldErrors],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-3xl space-y-6 px-4 py-8">
      <header>
        <h1 class="text-2xl font-semibold text-ink">{{ 'settlements.ratesTitle' | transloco }}</h1>
        <p class="mt-1 text-sm text-ink-muted">{{ 'settlements.ratesSubtitle' | transloco }}</p>
      </header>

      <form class="upb-card space-y-3 p-5" (submit)="savePolicy($event)">
        <div class="grid gap-3 sm:grid-cols-3">
          @for (field of fields; track field.key) {
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ field.label | transloco }}</mat-label>
            <input matInput type="number" min="0" max="100" step="0.01" [name]="field.key" [readonly]="!canWrite()"
              [value]="rates()[field.key]" (input)="setRate(field.key, $event)" />
          </mat-form-field>
          }
        </div>
        <p class="text-sm text-ink-muted">{{ 'settlements.taxNote' | transloco }}</p>
        @if (policy()?.modifiedAtUtc; as changed) {
        <p class="text-xs text-ink-muted">{{ 'settlements.lastChanged' | transloco: { date: (changed | dateIst: 'datetime') } }}</p>
        }
        <upb-field-errors fieldId="policy" [errors]="policyErrors()" />
        @if (canWrite()) {
        <button mat-flat-button color="primary" type="submit" [disabled]="busy()">{{ 'common.save' | transloco }}</button>
        }
      </form>

      <div class="upb-card space-y-3 p-5">
        <h2 class="font-medium text-ink">{{ 'settlements.overridesTitle' | transloco }}</h2>
        <ul class="divide-y divide-border text-sm">
          @for (c of commissions(); track c.sellerId) {
          <li class="flex flex-wrap items-center justify-between gap-2 py-2">
            <span>{{ c.shopName ?? c.sellerId }}</span>
            <span class="flex items-center gap-2">
              <span class="font-medium">{{ c.commissionPercent }}%</span>
              @if (canWrite()) {
              <button mat-button type="button" (click)="remove(c)">{{ 'settlements.useDefault' | transloco }}</button>
              }
            </span>
          </li>
          } @empty {
          <li class="py-2 text-ink-muted">{{ 'settlements.noOverrides' | transloco }}</li>
          }
        </ul>

        @if (canWrite()) {
        <form class="flex flex-wrap items-end gap-3 border-t border-border pt-3" (submit)="setCommission($event)">
          <mat-form-field class="min-w-64 flex-1" subscriptSizing="dynamic">
            <mat-label>{{ 'settlements.seller' | transloco }}</mat-label>
            <mat-select [value]="sellerId()" (valueChange)="sellerId.set($event)">
              @for (s of sellers(); track s.id) {
              <mat-option [value]="s.id">{{ s.shopName }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ 'settlements.commissionLabel' | transloco }}</mat-label>
            <input matInput name="commission" type="number" min="0" max="100" step="0.01" [value]="commission()"
              (input)="commission.set(value($event))" />
          </mat-form-field>
          <button mat-stroked-button type="submit" [disabled]="busy() || !sellerId() || commission() === ''">
            {{ 'settlements.setCommission' | transloco }}
          </button>
          <upb-field-errors class="w-full" fieldId="commission" [errors]="commissionErrors()" />
        </form>
        }
      </div>
    </section>
  `,
})
export class RatesPage {
  protected readonly fields: readonly { key: RateField; label: string }[] = [
    { key: 'defaultCommissionPercent', label: 'settlements.defaultCommission' },
    { key: 'tcsPercent', label: 'settlements.tcsPercent' },
    { key: 'tdsPercent', label: 'settlements.tdsPercent' },
  ];

  protected readonly policy = signal<SettlementPolicyDto | null>(null);
  protected readonly rates = signal<Record<RateField, string>>({ defaultCommissionPercent: '', tcsPercent: '', tdsPercent: '' });
  protected readonly commissions = signal<readonly SellerCommissionDto[]>([]);
  protected readonly sellers = signal<readonly SellerNameDto[]>([]);
  protected readonly sellerId = signal('');
  protected readonly commission = signal('');
  protected readonly busy = signal(false);
  protected readonly policyErrors = signal<readonly string[]>([]);
  protected readonly commissionErrors = signal<readonly string[]>([]);

  private readonly api = inject(Api);
  private readonly toast = inject(ToastService);
  private readonly currentUser = inject(CurrentUserStore);

  protected readonly canWrite = computed(() => this.currentUser.has(SettlementsPermissions.PolicyWrite));

  constructor() {
    void this.load();
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected setRate(field: RateField, event: Event): void {
    const value = this.value(event);
    this.rates.update((r) => ({ ...r, [field]: value }));
  }

  protected async savePolicy(event: Event): Promise<void> {
    event.preventDefault();
    const r = this.rates();

    await this.run(this.policyErrors, async () => {
      this.show(await this.api.invoke(apiV1AdminSettlementsPolicyPut, {
        body: {
          defaultCommissionPercent: Number(r.defaultCommissionPercent),
          tcsPercent: Number(r.tcsPercent),
          tdsPercent: Number(r.tdsPercent),
        },
      }));
      this.toast.success('settlements.ratesSaved');
    });
  }

  protected async setCommission(event: Event): Promise<void> {
    event.preventDefault();

    await this.run(this.commissionErrors, async () => {
      await this.api.invoke(apiV1AdminSettlementsCommissionsSellerIdPut, {
        sellerId: this.sellerId(),
        body: { commissionPercent: Number(this.commission()) },
      });
      this.sellerId.set('');
      this.commission.set('');
      this.toast.success('settlements.overrideSaved');
      await this.loadCommissions();
    });
  }

  protected async remove(commission: SellerCommissionDto): Promise<void> {
    await this.run(this.commissionErrors, async () => {
      await this.api.invoke(apiV1AdminSettlementsCommissionsSellerIdDelete, { sellerId: commission.sellerId });
      this.toast.success('settlements.overrideRemoved');
      await this.loadCommissions();
    });
  }

  private async run(errors: WritableSignal<readonly string[]>, work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    errors.set([]);

    try {
      await work();
    } catch (error) {
      const problem = toApiProblem(error);
      const fields = Object.keys(problem.fieldErrors);

      errors.set(fields.length > 0 ? fields.flatMap((f) => fieldErrorsFor(problem, f)) : [problem.title]);
    } finally {
      this.busy.set(false);
    }
  }

  private show(policy: SettlementPolicyDto): void {
    this.policy.set(policy);
    this.rates.set({
      defaultCommissionPercent: String(policy.defaultCommissionPercent),
      tcsPercent: String(policy.tcsPercent),
      tdsPercent: String(policy.tdsPercent),
    });
  }

  private async load(): Promise<void> {
    try {
      this.show(await this.api.invoke(apiV1AdminSettlementsPolicyGet, {}));
      await this.loadCommissions();

      if (this.canWrite()) {
        this.sellers.set(await this.api.invoke(apiV1AdminSettlementsSellersGet, {}));
      }
    } catch {
      // Reported by the interceptor.
    }
  }

  private async loadCommissions(): Promise<void> {
    this.commissions.set(await this.api.invoke(apiV1AdminSettlementsCommissionsGet, {}));
  }
}
