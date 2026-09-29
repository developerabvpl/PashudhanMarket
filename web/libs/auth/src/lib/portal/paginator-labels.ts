import { DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { TranslocoService } from '@jsverse/transloco';

/**
 * Points the app's one MatPaginatorIntl at Transloco. Run in the root injector's context.
 *
 * The labels are rewritten, and every paginator told to redraw (`changes`), whenever the active
 * language's strings arrive: on the first load and after each switch from the toolbar.
 * selectTranslation waits for the strings rather than just the language change; translating
 * before they are in would hand back the keys themselves.
 *
 * Loaded on demand by providePortalPaginatorIntl, which says why.
 */
export function translatePaginator(): void {
  const intl = inject(MatPaginatorIntl);
  const transloco = inject(TranslocoService);

  // Material's own arithmetic, with the words translated: "1 – 25 of 120", or "0 of 0".
  intl.getRangeLabel = (page: number, pageSize: number, length: number): string => {
    if (length === 0 || pageSize === 0) {
      return transloco.translate('paginator.rangeEmpty', { length });
    }

    const start = page * pageSize;
    // A page past the end (the list shrank under it) still shows a sensible range.
    const end = start < length ? Math.min(start + pageSize, length) : start + pageSize;

    return transloco.translate('paginator.range', { start: start + 1, end, length });
  };

  transloco
    .selectTranslation()
    .pipe(takeUntilDestroyed(inject(DestroyRef)))
    .subscribe(() => {
      intl.itemsPerPageLabel = transloco.translate('paginator.itemsPerPage');
      intl.firstPageLabel = transloco.translate('paginator.firstPage');
      intl.previousPageLabel = transloco.translate('paginator.previousPage');
      intl.nextPageLabel = transloco.translate('paginator.nextPage');
      intl.lastPageLabel = transloco.translate('paginator.lastPage');
      intl.changes.next();
    });
}
