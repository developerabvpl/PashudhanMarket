import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  Api,
  SellerMemberDto,
  apiV1SellersMeTeamGet,
  apiV1SellersMeTeamMemberIdDelete,
  apiV1SellersMeTeamMemberIdPut,
  apiV1SellersMeTeamPost,
  toApiProblem,
} from '@upbazaar/data-access';
import { ToastService } from '@upbazaar/ui';
import { DateIstPipe } from '@upbazaar/util';

type Role = 'Manager' | 'Dispatch';

/**
 * The owner's team: people who work in the shop's seller portal under their own accounts.
 *
 * Someone joins by registering a seller-portal account themselves; the owner then adds them here
 * by its email. A Manager does everything but manage the team, see earnings or change the shop's
 * details; Dispatch only packs, ships and handles returns. Taking someone off the team shuts
 * them out at once.
 */
@Component({
  selector: 'upb-team-page',
  imports: [TranslocoPipe, DateIstPipe, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-3xl space-y-6 px-4 py-8">
      <div>
        <h1 class="text-2xl font-semibold text-ink">{{ 'team.title' | transloco }}</h1>
        <p class="mt-1 text-sm text-ink-muted">{{ 'team.subtitle' | transloco }}</p>
      </div>

      <form class="upb-card grid gap-3 p-5 sm:grid-cols-[1fr_12rem_auto] sm:items-start" (submit)="add($event)">
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'team.email' | transloco }}</mat-label>
          <input matInput name="email" type="email" required maxlength="256" [value]="email()" (input)="email.set(value($event))" />
        </mat-form-field>
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'team.role' | transloco }}</mat-label>
          <mat-select name="role" [value]="role()" (selectionChange)="role.set($event.value)">
            @for (r of roles; track r) {
            <mat-option [value]="r">{{ 'team.roles.' + r | transloco }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <button mat-flat-button color="primary" type="submit" [disabled]="busy() || !email().trim()">{{ 'team.add' | transloco }}</button>
        <p class="text-xs text-ink-muted sm:col-span-3">{{ 'team.addHint' | transloco }}</p>
        @if (error(); as e) { <p class="text-sm text-danger sm:col-span-3" role="alert">{{ e }}</p> }
      </form>

      <ul class="upb-card divide-y divide-border">
        @for (m of members(); track m.id) {
        <li class="flex flex-wrap items-center justify-between gap-3 p-4 text-sm">
          <div>
            <p class="font-medium text-ink">{{ m.displayName || m.email }}</p>
            <p class="text-ink-muted">{{ m.email }} · {{ 'team.addedOn' | transloco: { date: (m.addedAtUtc | dateIst) } }}</p>
            <p class="text-xs text-ink-muted">{{ 'team.roleHelp.' + m.role | transloco }}</p>
          </div>
          <div class="flex items-center gap-2">
            <mat-form-field class="w-36" subscriptSizing="dynamic">
              <mat-label>{{ 'team.role' | transloco }}</mat-label>
              <mat-select [value]="m.role" [attr.aria-label]="'team.role' | transloco" (selectionChange)="change(m, $event.value)">
                @for (r of roles; track r) {
                <mat-option [value]="r">{{ 'team.roles.' + r | transloco }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
            <button mat-button color="warn" type="button" [disabled]="busy()" (click)="remove(m)">{{ 'team.remove' | transloco }}</button>
          </div>
        </li>
        } @empty {
        <li class="p-8 text-center text-ink-muted">{{ 'team.none' | transloco }}</li>
        }
      </ul>
    </section>
  `,
})
export class TeamPage {
  protected readonly roles: readonly Role[] = ['Manager', 'Dispatch'];
  protected readonly members = signal<readonly SellerMemberDto[]>([]);
  protected readonly email = signal('');
  protected readonly role = signal<Role>('Dispatch');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  private readonly api = inject(Api);
  private readonly toast = inject(ToastService);

  constructor() {
    void this.load();
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected async add(event: Event): Promise<void> {
    event.preventDefault();

    await this.run(async () => {
      await this.api.invoke(apiV1SellersMeTeamPost, { body: { email: this.email().trim(), role: this.role() } });
      this.email.set('');
      this.toast.success('team.added');
    });
  }

  protected async change(member: SellerMemberDto, role: Role): Promise<void> {
    await this.run(async () => {
      await this.api.invoke(apiV1SellersMeTeamMemberIdPut, { memberId: member.id, body: { role } });
      this.toast.success('team.changed');
    });
  }

  protected async remove(member: SellerMemberDto): Promise<void> {
    await this.run(async () => {
      await this.api.invoke(apiV1SellersMeTeamMemberIdDelete, { memberId: member.id });
      this.toast.success('team.removed');
    });
  }

  private async run(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);

    try {
      await work();
    } catch (error) {
      this.error.set(toApiProblem(error).title);
    }

    // Reloaded either way: after a refused change the list must show what is actually saved,
    // not the role the owner picked.
    try {
      await this.load();
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    this.members.set(await this.api.invoke(apiV1SellersMeTeamGet, {}));
  }
}
