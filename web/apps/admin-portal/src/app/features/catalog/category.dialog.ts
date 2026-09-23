import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  Api,
  CategoryDto,
  apiV1AdminCatalogCategoriesCategoryIdPut,
  apiV1AdminCatalogCategoriesPost,
  fieldErrorsFor,
  toApiProblem,
} from '@upbazaar/data-access';
import { FieldErrors } from '@upbazaar/ui';
import { descendantsOf } from './category-tree';

/** The category being edited, or none to create one, and every category to choose a parent from. */
export interface CategoryDialogData {
  readonly category: CategoryDto | null;
  readonly all: readonly CategoryDto[];
}

/**
 * Creates a category, or renames and moves one. Resolves to the saved category, or undefined
 * if cancelled.
 *
 * The parent list leaves out the category itself and everything beneath it, since the API
 * refuses a move that would put a category under its own child.
 */
@Component({
  selector: 'upb-category-dialog',
  imports: [MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, TranslocoPipe, FieldErrors],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ (data.category ? 'categoriesAdmin.editTitle' : 'categoriesAdmin.addTitle') | transloco }}</h2>

    <form (submit)="save($event)">
      <mat-dialog-content>
        <mat-form-field class="w-full">
          <mat-label>{{ 'categoriesAdmin.name' | transloco }}</mat-label>
          <input matInput name="name" required maxlength="128" autocomplete="off" [value]="name()" (input)="name.set(value($event))" />
        </mat-form-field>

        <mat-form-field class="w-full">
          <mat-label>{{ 'categoriesAdmin.parent' | transloco }}</mat-label>
          <mat-select [value]="parentId()" (valueChange)="parentId.set($event)">
            <mat-option value="">{{ 'categoriesAdmin.topLevel' | transloco }}</mat-option>
            @for (c of parents(); track c.id) {
            <mat-option [value]="c.id">{{ c.name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>

        <upb-field-errors fieldId="category" [errors]="errors()" />
      </mat-dialog-content>

      <mat-dialog-actions align="end">
        <button mat-button type="button" [mat-dialog-close]="undefined">{{ 'common.cancel' | transloco }}</button>
        <button mat-flat-button color="primary" type="submit" [disabled]="busy() || !name().trim()">
          {{ 'common.save' | transloco }}
        </button>
      </mat-dialog-actions>
    </form>
  `,
})
export class CategoryDialog {
  protected readonly data = inject<CategoryDialogData>(MAT_DIALOG_DATA);

  protected readonly name = signal(this.data.category?.name ?? '');
  protected readonly parentId = signal(this.data.category?.parentId ?? '');
  protected readonly busy = signal(false);
  protected readonly errors = signal<readonly string[]>([]);

  protected readonly parents = computed(() => {
    const own = this.data.category;
    const excluded = own ? new Set([own.id, ...descendantsOf(own.id, this.data.all)]) : new Set<string>();

    return this.data.all.filter((c) => !excluded.has(c.id));
  });

  private readonly reference = inject<MatDialogRef<CategoryDialog, CategoryDto>>(MatDialogRef);
  private readonly api = inject(Api);

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected async save(event: Event): Promise<void> {
    event.preventDefault();
    this.busy.set(true);
    this.errors.set([]);

    const body = { name: this.name().trim(), parentId: this.parentId() || null };

    try {
      const saved = this.data.category
        ? await this.api.invoke(apiV1AdminCatalogCategoriesCategoryIdPut, { categoryId: this.data.category.id, body })
        : await this.api.invoke(apiV1AdminCatalogCategoriesPost, { body });

      this.reference.close(saved);
    } catch (error) {
      const problem = toApiProblem(error);
      const fields = Object.keys(problem.fieldErrors);

      this.errors.set(fields.length > 0 ? fields.flatMap((f) => fieldErrorsFor(problem, f)) : [problem.title]);
    } finally {
      this.busy.set(false);
    }
  }
}
