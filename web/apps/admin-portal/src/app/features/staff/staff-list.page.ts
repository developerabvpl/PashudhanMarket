import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { HasPermissionDirective } from '@upbazaar/auth';
import { RoleDto, UserSummaryDto } from '@upbazaar/data-access';
import { ToastService } from '@upbazaar/ui';
import { DateIstPipe } from '@upbazaar/util';
import { IdentityPermissions } from '../../core/permissions';
import { ConfirmDialog, confirm } from './confirm.dialog';
import { StaffFormDialog, StaffFormData } from './staff-form.dialog';
import { StaffRolesDialog, StaffRolesData } from './staff-roles.dialog';
import { StaffService } from './staff.service';

/**
 * The staff directory.
 *
 * Paging, searching and filtering all happen on the server: an administrator's list can run to
 * thousands of rows, and pulling them all down to filter in the browser would be slow on the
 * connections this is used over.
 */
@Component({
  selector: 'upb-staff-list-page',
  imports: [
    TranslocoPipe,
    DateIstPipe,
    HasPermissionDirective,
    MatTableModule,
    MatPaginatorModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatProgressBarModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-6xl px-4 py-8">
      <header class="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 class="text-2xl font-semibold text-ink">{{ 'staff.title' | transloco }}</h1>
          <p class="mt-1 text-sm text-ink-muted">{{ 'staff.subtitle' | transloco }}</p>
        </div>

        <button
          *hasPermission="usersManage"
          mat-flat-button
          color="primary"
          type="button"
          (click)="create()"
        >
          {{ 'staff.create' | transloco }}
        </button>
      </header>

      <form class="mt-6 flex flex-wrap items-center gap-3" (submit)="applySearch($event)">
        <mat-form-field class="flex-1" subscriptSizing="dynamic">
          <mat-label>{{ 'staff.searchLabel' | transloco }}</mat-label>
          <input matInput name="search" type="search" [value]="search()" />
        </mat-form-field>

        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'staff.filterType' | transloco }}</mat-label>
          <mat-select [value]="userType()" (valueChange)="changeType($event)">
            <mat-option value="">{{ 'staff.allTypes' | transloco }}</mat-option>
            <mat-option value="Staff">Staff</mat-option>
            <mat-option value="Seller">Seller</mat-option>
            <mat-option value="Buyer">Buyer</mat-option>
          </mat-select>
        </mat-form-field>

        <button mat-stroked-button type="submit">{{ 'common.search' | transloco }}</button>
      </form>

      <div class="upb-card mt-4 overflow-x-auto">
        @if (loading()) {
        <mat-progress-bar mode="indeterminate" />
        }

        <table mat-table [dataSource]="rows()" class="w-full">
          <ng-container matColumnDef="displayName">
            <th mat-header-cell *matHeaderCellDef>{{ 'staff.columnName' | transloco }}</th>
            <td mat-cell *matCellDef="let row">{{ row.displayName }}</td>
          </ng-container>

          <ng-container matColumnDef="email">
            <th mat-header-cell *matHeaderCellDef>{{ 'staff.columnEmail' | transloco }}</th>
            <td mat-cell *matCellDef="let row">{{ row.email ?? row.mobile ?? '—' }}</td>
          </ng-container>

          <ng-container matColumnDef="userType">
            <th mat-header-cell *matHeaderCellDef>{{ 'staff.columnType' | transloco }}</th>
            <td mat-cell *matCellDef="let row">{{ row.userType }}</td>
          </ng-container>

          <ng-container matColumnDef="status">
            <th mat-header-cell *matHeaderCellDef>{{ 'staff.columnStatus' | transloco }}</th>
            <td mat-cell *matCellDef="let row">{{ row.status }}</td>
          </ng-container>

          <ng-container matColumnDef="roles">
            <th mat-header-cell *matHeaderCellDef>{{ 'staff.columnRoles' | transloco }}</th>
            <td mat-cell *matCellDef="let row">
              {{ row.roles.length > 0 ? row.roles.join(', ') : ('staff.noRoles' | transloco) }}
            </td>
          </ng-container>

          <ng-container matColumnDef="createdAtUtc">
            <th mat-header-cell *matHeaderCellDef>{{ 'staff.columnCreated' | transloco }}</th>
            <td mat-cell *matCellDef="let row">{{ row.createdAtUtc | dateIst }}</td>
          </ng-container>

          <ng-container matColumnDef="actions">
            <th mat-header-cell *matHeaderCellDef>{{ 'common.actions' | transloco }}</th>
            <td mat-cell *matCellDef="let row">
              <div *hasPermission="usersManage" class="flex gap-1">
                <button mat-button type="button" (click)="edit(row)">
                  {{ 'common.edit' | transloco }}
                </button>
                <button mat-button type="button" (click)="editRoles(row)">
                  {{ 'staff.assignRoles' | transloco }}
                </button>
                <button mat-button color="warn" type="button" (click)="deactivate(row)">
                  {{ 'staff.confirmDeactivate' | transloco }}
                </button>
              </div>
            </td>
          </ng-container>

          <tr mat-header-row *matHeaderRowDef="columns"></tr>
          <tr mat-row *matRowDef="let row; columns: columns"></tr>
        </table>

        @if (!loading() && rows().length === 0) {
        <p class="p-8 text-center text-ink-muted">{{ 'staff.empty' | transloco }}</p>
        }

        <mat-paginator
          [length]="totalCount()"
          [pageSize]="pageSize()"
          [pageIndex]="page() - 1"
          [pageSizeOptions]="[10, 25, 50]"
          (page)="changePage($event)"
        />
      </div>
    </section>
  `,
})
export class StaffListPage {
  private readonly staff = inject(StaffService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);
  private readonly transloco = inject(TranslocoService);

  protected readonly usersManage = IdentityPermissions.UsersManage;

  protected readonly columns = [
    'displayName',
    'email',
    'userType',
    'status',
    'roles',
    'createdAtUtc',
    'actions',
  ];

  protected readonly rows = signal<readonly UserSummaryDto[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(25);
  protected readonly search = signal('');
  protected readonly userType = signal('');
  protected readonly loading = signal(false);

  private roleCatalogue: readonly RoleDto[] = [];

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);

    try {
      const result = await this.staff.list({
        page: this.page(),
        pageSize: this.pageSize(),
        search: this.search(),
        userType: this.userType(),
      });

      this.rows.set(result.items);
      this.totalCount.set(result.totalCount);
    } catch {
      // The global interceptor has already raised a toast; leave the table as it was.
    } finally {
      this.loading.set(false);
    }
  }

  protected applySearch(event: Event): void {
    event.preventDefault();

    const form = event.target as HTMLFormElement;

    this.search.set(new FormData(form).get('search')?.toString() ?? '');
    this.page.set(1);

    void this.load();
  }

  protected changeType(userType: string): void {
    this.userType.set(userType);
    this.page.set(1);

    void this.load();
  }

  protected changePage(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    this.pageSize.set(event.pageSize);

    void this.load();
  }

  protected async create(): Promise<void> {
    const roles = await this.ensureRoles();

    const reference = this.dialog.open<StaffFormDialog, StaffFormData, boolean>(StaffFormDialog, {
      data: { roles },
      width: '32rem',
    });

    if (await closed(reference)) {
      this.toast.success('staff.created');

      await this.load();
    }
  }

  protected async edit(user: UserSummaryDto): Promise<void> {
    const roles = await this.ensureRoles();

    const reference = this.dialog.open<StaffFormDialog, StaffFormData, boolean>(StaffFormDialog, {
      data: { user, roles },
      width: '32rem',
    });

    if (await closed(reference)) {
      this.toast.success('staff.updated');

      await this.load();
    }
  }

  protected async editRoles(user: UserSummaryDto): Promise<void> {
    const roles = await this.ensureRoles();

    const reference = this.dialog.open<StaffRolesDialog, StaffRolesData, string[] | undefined>(
      StaffRolesDialog,
      { data: { user, roles }, width: '34rem' }
    );

    const selected = await closed(reference);

    if (selected === undefined) {
      return;
    }

    // Spelled out before saving: "these will be their roles" is checkable, "are you sure" is not.
    const confirmed = await confirm(this.dialog, {
      title: 'staff.confirmRolesTitle',
      body: selected.length > 0 ? 'staff.confirmRolesBody' : 'staff.confirmRolesNone',
      params: { name: user.displayName, roles: selected.join(', ') },
      confirmLabel: 'common.confirm',
    });

    if (!confirmed) {
      return;
    }

    try {
      await this.staff.assignRoles(user.id, selected);

      this.toast.success('staff.rolesUpdated');

      await this.load();
    } catch {
      // Already reported by the interceptor.
    }
  }

  protected async deactivate(user: UserSummaryDto): Promise<void> {
    const confirmed = await confirm(this.dialog, {
      title: 'staff.confirmDeactivateTitle',
      body: 'staff.confirmDeactivateBody',
      params: { name: user.displayName },
      confirmLabel: 'staff.confirmDeactivate',
      destructive: true,
    });

    if (!confirmed) {
      return;
    }

    try {
      await this.staff.deactivate(user.id);

      this.toast.success('staff.deactivated');

      await this.load();
    } catch {
      // Already reported by the interceptor.
    }
  }

  /** The role catalogue changes rarely, so it is fetched once per visit to this screen. */
  private async ensureRoles(): Promise<readonly RoleDto[]> {
    if (this.roleCatalogue.length === 0) {
      try {
        this.roleCatalogue = await this.staff.roles();
      } catch {
        this.roleCatalogue = [];
      }
    }

    return this.roleCatalogue;
  }
}

/** Awaits a dialog result without dragging RxJS into every call site. */
function closed<TResult>(reference: {
  afterClosed: () => { subscribe: (next: (value: TResult | undefined) => void) => unknown };
}): Promise<TResult | undefined> {
  return new Promise((resolve) => {
    reference.afterClosed().subscribe((value) => resolve(value));
  });
}

export { ConfirmDialog };
