import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  Api,
  CategoryDto,
  apiV1AdminCatalogCategoriesCategoryIdDelete,
  apiV1CatalogCategoriesGet,
} from '@upbazaar/data-access';
import { ToastService } from '@upbazaar/ui';
import { confirm } from '../staff/confirm.dialog';
import { CategoryDialog, CategoryDialogData } from './category.dialog';
import { toTree } from './category-tree';

/**
 * The shelves products sit on. Top-level categories are the storefront's category rail, and a
 * shopper choosing one sees its children's products too.
 *
 * Deleting is offered on every row, but the API allows it only for an empty category; a refusal
 * comes back as a toast saying to move the products first.
 */
@Component({
  selector: 'upb-categories-page',
  imports: [TranslocoPipe, MatButtonModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-4xl px-4 py-8">
      <header class="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 class="text-2xl font-semibold text-ink">{{ 'categoriesAdmin.title' | transloco }}</h1>
          <p class="mt-1 text-sm text-ink-muted">{{ 'categoriesAdmin.subtitle' | transloco }}</p>
        </div>
        <button mat-flat-button color="primary" type="button" (click)="edit(null)">{{ 'categoriesAdmin.add' | transloco }}</button>
      </header>

      <div class="upb-card mt-6">
        @if (loading()) {
        <mat-progress-bar mode="indeterminate" />
        }
        <ul class="divide-y divide-border">
          @for (row of rows(); track row.category.id) {
          <li class="flex flex-wrap items-center gap-3 px-4 py-2" [style.padding-left.rem]="1 + row.depth * 1.5">
            <div class="min-w-0 flex-1">
              <p [class.font-medium]="row.depth === 0" class="text-ink">{{ row.category.name }}</p>
              <p class="font-mono text-xs text-ink-muted">
                <span class="upb-sr-only">{{ 'categoriesAdmin.webAddress' | transloco }}:</span> {{ row.category.slug }}
              </p>
            </div>
            <button mat-button type="button" (click)="edit(row.category)">{{ 'common.edit' | transloco }}</button>
            <button mat-button color="warn" type="button" (click)="remove(row.category)">{{ 'common.delete' | transloco }}</button>
          </li>
          } @empty {
          @if (!loading()) {
          <li class="p-8 text-center text-ink-muted">{{ 'categoriesAdmin.none' | transloco }}</li>
          }
          }
        </ul>
      </div>
    </section>
  `,
})
export class CategoriesPage {
  private readonly api = inject(Api);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);

  protected readonly categories = signal<readonly CategoryDto[]>([]);
  protected readonly loading = signal(false);
  protected readonly rows = computed(() => toTree(this.categories()));

  constructor() {
    void this.load();
  }

  protected async edit(category: CategoryDto | null): Promise<void> {
    const reference = this.dialog.open<CategoryDialog, CategoryDialogData, CategoryDto>(CategoryDialog, {
      data: { category, all: this.categories() },
      width: '28rem',
    });

    const saved = await new Promise<CategoryDto | undefined>((resolve) =>
      reference.afterClosed().subscribe((result) => resolve(result))
    );

    if (saved) {
      this.toast.success('categoriesAdmin.saved');

      await this.load();
    }
  }

  protected async remove(category: CategoryDto): Promise<void> {
    const confirmed = await confirm(this.dialog, {
      title: 'categoriesAdmin.deleteTitle',
      body: 'categoriesAdmin.deleteBody',
      params: { name: category.name },
      confirmLabel: 'common.delete',
      destructive: true,
    });

    if (!confirmed) {
      return;
    }

    try {
      await this.api.invoke(apiV1AdminCatalogCategoriesCategoryIdDelete, { categoryId: category.id });
      this.toast.success('categoriesAdmin.deleted');

      await this.load();
    } catch {
      // Reported by the interceptor, including the "still has products" refusal.
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      this.categories.set(await this.api.invoke(apiV1CatalogCategoriesGet, {}));
    } catch {
      // Reported by the interceptor.
    } finally {
      this.loading.set(false);
    }
  }
}
