import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { CurrentUserStore } from '@upbazaar/auth';
import {
  Api,
  ApiProblem,
  CodReceivableDto,
  CodRemittanceDto,
  CodSummaryDto,
  apiV1AdminShippingCodReceivablesGet,
  apiV1AdminShippingCodReceivablesReceivableIdWriteOffPost,
  apiV1AdminShippingCodRemittancesGet,
  apiV1AdminShippingCodRemittancesPost,
  apiV1AdminShippingCodRemittancesRemittanceIdGet,
  apiV1AdminShippingCodSummaryGet,
  fieldErrorsFor,
  toApiProblem,
} from '@upbazaar/data-access';
import { FieldErrors, ToastService } from '@upbazaar/ui';
import { DateIstPipe, InrCurrencyPipe } from '@upbazaar/util';
import { ShippingPermissions } from '../../core/permissions';

type Filter = 'Owed' | 'Overdue' | 'Short' | 'Over' | 'All';

/**
 * Cash on delivery: what the courier collected at buyers' doors and still owes, and the
 * remittances that paid it over.
 *
 * A seller is paid for a cash-on-delivery parcel only once its cash is in, so what is owed here
 * is also what sellers are waiting for. Staff upload each remittance report as it arrives; a
 * parcel paid short is raised with the courier, and written off with a note if the rest will
 * never come - which releases the seller's pay all the same.
 */
@Component({
  selector: 'upb-cod-page',
  imports: [TranslocoPipe, DateIstPipe, InrCurrencyPipe, MatButtonModule, MatFormFieldModule, MatInputModule, FieldErrors],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-5xl space-y-6 px-4 py-8">
      <div>
        <h1 class="text-2xl font-semibold text-ink">{{ 'cod.title' | transloco }}</h1>
        <p class="mt-1 text-sm text-ink-muted">{{ 'cod.subtitle' | transloco }}</p>
      </div>

      @if (summary(); as s) {
      <dl class="grid gap-3 sm:grid-cols-4">
        <div class="upb-card p-4">
          <dt class="text-sm text-ink-muted">{{ 'cod.owedAmount' | transloco }}</dt>
          <dd class="text-xl font-semibold text-ink">{{ s.outstandingAmount | inr }}</dd>
        </div>
        <div class="upb-card p-4">
          <dt class="text-sm text-ink-muted">{{ 'cod.owedCount' | transloco }}</dt>
          <dd class="text-xl font-semibold text-ink">{{ s.outstandingCount }}</dd>
        </div>
        <div class="upb-card p-4">
          <dt class="text-sm text-ink-muted">{{ 'cod.overdueCount' | transloco: { days: s.overdueDays } }}</dt>
          <dd class="text-xl font-semibold" [class.text-danger]="s.overdueCount > 0" [class.text-ink]="s.overdueCount === 0">{{ s.overdueCount }}</dd>
        </div>
        <div class="upb-card p-4">
          <dt class="text-sm text-ink-muted">{{ 'cod.shortCount' | transloco }}</dt>
          <dd class="text-xl font-semibold" [class.text-warning]="s.shortCount > 0" [class.text-ink]="s.shortCount === 0">{{ s.shortCount }}</dd>
        </div>
      </dl>
      }

      @if (canWrite()) {
      <form class="upb-card grid gap-3 p-5 sm:grid-cols-[1fr_12rem_1fr_auto] sm:items-start" (submit)="upload($event)">
        <h2 class="font-medium text-ink sm:col-span-4">{{ 'cod.uploadTitle' | transloco }}</h2>
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'cod.reference' | transloco }}</mat-label>
          <input matInput name="reference" required maxlength="64" [value]="reference()" (input)="reference.set(value($event))" />
        </mat-form-field>
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'cod.remittedOn' | transloco }}</mat-label>
          <input matInput name="remittedOn" type="date" required [value]="remittedOn()" (input)="remittedOn.set(value($event))" />
        </mat-form-field>
        <label class="flex flex-col gap-1 text-sm text-ink">
          {{ 'cod.file' | transloco }}
          <input name="file" type="file" accept=".csv,text/csv" required (change)="file.set(picked($event))" />
        </label>
        <button mat-flat-button color="primary" type="submit" [disabled]="busy() || !reference().trim() || !remittedOn() || !file()">
          {{ 'cod.upload' | transloco }}
        </button>
        <p class="text-xs text-ink-muted sm:col-span-4">{{ 'cod.uploadHint' | transloco }}</p>
        <upb-field-errors class="sm:col-span-4" fieldId="upload" [errors]="errors('upload')" />
      </form>
      }

      <div class="upb-card">
        <div class="flex flex-wrap gap-2 border-b border-border p-3" role="group" [attr.aria-label]="'cod.filterLabel' | transloco">
          @for (f of filters; track f) {
          <button mat-stroked-button type="button" [attr.aria-pressed]="filter() === f" [class.!bg-surface-sunken]="filter() === f" (click)="show(f)">
            {{ 'cod.filters.' + f | transloco }}
          </button>
          }
        </div>

        <ul class="divide-y divide-border">
          @for (r of receivables(); track r.id) {
          <li class="space-y-2 p-4 text-sm">
            <div class="flex flex-wrap items-center justify-between gap-3">
              <div>
                <p class="font-medium text-ink">
                  {{ r.orderNumber }} · <span class="font-mono text-xs">{{ r.awb }}</span>
                  @if (r.isOverdue) { <span class="ml-1 rounded-full bg-danger/10 px-2 py-0.5 text-xs text-danger">{{ 'cod.overdue' | transloco }}</span> }
                </p>
                <p class="text-ink-muted">
                  {{ 'cod.deliveredOn' | transloco: { date: (r.deliveredAtUtc | dateIst) } }} ·
                  {{ amountsKey(r) | transloco: { received: (r.received | inr), expected: (r.expected | inr), excess: (r.received - r.expected | inr) } }} ·
                  {{ 'cod.statuses.' + r.status | transloco }}
                </p>
                @if (r.writeOffNote) { <p class="text-ink-muted">{{ r.writeOffNote }}</p> }
              </div>
              @if (canWrite() && (r.status === 'Outstanding' || r.status === 'ShortPaid') && writingOff() !== r.id) {
              <button mat-button color="warn" type="button" (click)="startWriteOff(r.id)">{{ 'cod.writeOff' | transloco }}</button>
              }
            </div>
            @if (writingOff() === r.id) {
            <form class="flex flex-wrap items-start gap-3" (submit)="writeOff($event, r)">
              <mat-form-field class="min-w-64 flex-1" subscriptSizing="dynamic">
                <mat-label>{{ 'cod.writeOffNote' | transloco }}</mat-label>
                <input matInput name="note" required maxlength="500" [value]="note()" (input)="note.set(value($event))" />
              </mat-form-field>
              <button mat-flat-button color="warn" type="submit" [disabled]="busy() || !note().trim()">{{ 'cod.confirmWriteOff' | transloco }}</button>
              <button mat-button type="button" (click)="writingOff.set(null)">{{ 'common.cancel' | transloco }}</button>
              <upb-field-errors class="w-full" fieldId="writeOff" [errors]="errors('writeOff')" />
            </form>
            }
          </li>
          } @empty {
          <li class="p-8 text-center text-ink-muted">{{ 'cod.none' | transloco }}</li>
          }
        </ul>
        @if (receivableCount() > receivables().length) {
        <p class="border-t border-border p-3 text-center text-sm text-ink-muted">
          {{ 'cod.showingOf' | transloco: { shown: receivables().length, total: receivableCount() } }}
        </p>
        }
      </div>

      <div class="upb-card">
        <h2 class="border-b border-border p-4 font-medium text-ink">{{ 'cod.remittancesTitle' | transloco }}</h2>
        <ul class="divide-y divide-border">
          @for (m of remittances(); track m.id) {
          <li class="p-4 text-sm">
            <button type="button" class="flex w-full flex-wrap items-center justify-between gap-3 text-left" [attr.aria-expanded]="opened()?.id === m.id" (click)="open(m)">
              <span class="font-medium text-ink">{{ m.reference }} · {{ m.remittedOn | dateIst }}</span>
              <span class="text-ink-muted">
                {{ m.total | inr }}
                @if (m.unmatchedCount > 0) { · <span class="text-warning">{{ 'cod.unmatched' | transloco: { count: m.unmatchedCount } }}</span> }
                · {{ m.fileName }}
              </span>
            </button>
            @if (opened()?.id === m.id) {
            <table class="mt-3 w-full text-left">
              <thead class="text-ink-muted">
                <tr><th class="py-1 font-normal">AWB</th><th class="py-1 font-normal">{{ 'cod.order' | transloco }}</th><th class="py-1 text-right font-normal">{{ 'cod.amount' | transloco }}</th></tr>
              </thead>
              <tbody>
                @for (line of opened()!.lines; track $index) {
                <tr>
                  <td class="py-1 font-mono text-xs">{{ line.awb }}</td>
                  <td class="py-1">{{ line.orderNumber ?? ('cod.notMatched' | transloco) }}</td>
                  <td class="py-1 text-right">{{ line.amount | inr }}</td>
                </tr>
                }
              </tbody>
            </table>
            }
          </li>
          } @empty {
          <li class="p-8 text-center text-ink-muted">{{ 'cod.noRemittances' | transloco }}</li>
          }
        </ul>
      </div>
    </section>
  `,
})
export class CodPage {
  protected readonly filters: readonly Filter[] = ['Owed', 'Overdue', 'Short', 'Over', 'All'];

  protected readonly summary = signal<CodSummaryDto | null>(null);
  protected readonly receivables = signal<readonly CodReceivableDto[]>([]);
  protected readonly receivableCount = signal(0);
  protected readonly remittances = signal<readonly CodRemittanceDto[]>([]);
  protected readonly opened = signal<CodRemittanceDto | null>(null);
  protected readonly filter = signal<Filter>('Owed');

  protected readonly reference = signal('');
  protected readonly remittedOn = signal('');
  protected readonly file = signal<File | null>(null);
  protected readonly writingOff = signal<string | null>(null);
  protected readonly note = signal('');
  protected readonly busy = signal(false);

  private readonly api = inject(Api);
  private readonly toast = inject(ToastService);
  private readonly transloco = inject(TranslocoService);
  private readonly currentUser = inject(CurrentUserStore);
  private readonly problems = signal<Record<string, ApiProblem | null>>({});

  protected readonly canWrite = computed(() => this.currentUser.has(ShippingPermissions.CodWrite));

  constructor() {
    void this.refresh();
  }

  /**
   * How much was collected and remitted. The words follow the status: a parcel with nothing
   * remitted yet shows only what was collected, and only an overpaid one says by how much - "paid
   * over" on every row read as though the courier had paid too much.
   */
  protected amountsKey(receivable: CodReceivableDto): string {
    switch (receivable.status) {
      case 'Outstanding':
        return 'cod.amounts.none';
      case 'Over':
        return 'cod.amounts.over';
      default:
        return 'cod.amounts.part';
    }
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected picked(event: Event): File | null {
    return (event.target as HTMLInputElement).files?.[0] ?? null;
  }

  protected errors(form: string): readonly string[] {
    const problem = this.problems()[form] ?? null;

    if (!problem) {
      return [];
    }

    const fields = Object.keys(problem.fieldErrors);

    return fields.length > 0 ? fields.flatMap((f) => fieldErrorsFor(problem, f)) : [problem.title];
  }

  protected async show(filter: Filter): Promise<void> {
    this.filter.set(filter);
    await this.loadReceivables();
  }

  protected async upload(event: Event): Promise<void> {
    event.preventDefault();
    const file = this.file();

    if (!file) {
      return;
    }

    await this.run('upload', async () => {
      const remittance = await this.api.invoke(apiV1AdminShippingCodRemittancesPost, {
        body: { reference: this.reference().trim(), remittedOn: this.remittedOn(), file },
      });

      this.reference.set('');
      this.remittedOn.set('');
      this.file.set(null);
      (event.target as HTMLFormElement).reset();
      this.toast.success(this.transloco.translate('cod.uploaded', { count: remittance.lines.length, unmatched: remittance.unmatchedCount }));
      await this.refresh();
    });
  }

  protected startWriteOff(id: string): void {
    this.note.set('');
    this.problems.update((p) => ({ ...p, writeOff: null }));
    this.writingOff.set(id);
  }

  protected async writeOff(event: Event, receivable: CodReceivableDto): Promise<void> {
    event.preventDefault();

    await this.run('writeOff', async () => {
      await this.api.invoke(apiV1AdminShippingCodReceivablesReceivableIdWriteOffPost, {
        receivableId: receivable.id,
        body: { note: this.note().trim() },
      });

      this.writingOff.set(null);
      this.toast.success('cod.writtenOff');
      await this.refresh();
    });
  }

  protected async open(remittance: CodRemittanceDto): Promise<void> {
    if (this.opened()?.id === remittance.id) {
      this.opened.set(null);

      return;
    }

    this.opened.set(await this.api.invoke(apiV1AdminShippingCodRemittancesRemittanceIdGet, { remittanceId: remittance.id }));
  }

  private async refresh(): Promise<void> {
    const [summary, remittances] = await Promise.all([
      this.api.invoke(apiV1AdminShippingCodSummaryGet, {}),
      this.api.invoke(apiV1AdminShippingCodRemittancesGet, { pageSize: 25 }),
      this.loadReceivables(),
    ]);

    this.summary.set(summary);
    this.remittances.set(remittances.items);
  }

  private async loadReceivables(): Promise<void> {
    const page = await this.api.invoke(apiV1AdminShippingCodReceivablesGet, { filter: this.filter(), pageSize: 100 });

    this.receivables.set(page.items);
    this.receivableCount.set(page.totalCount);
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
}
