import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { TranslocoService } from '@jsverse/transloco';
import { provideI18n } from '@upbazaar/ui';
import { firstValueFrom } from 'rxjs';
import { providePortalPaginatorIntl } from './portal-paginator-intl';

const DASH = String.fromCharCode(0x2013);

describe('providePortalPaginatorIntl', () => {
  let transloco: TranslocoService;
  let intl: MatPaginatorIntl;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideI18n(), providePortalPaginatorIntl()],
    });

    transloco = TestBed.inject(TranslocoService);
    intl = TestBed.inject(MatPaginatorIntl);
  });

  async function useLanguage(lang: 'en' | 'hi'): Promise<void> {
    transloco.setActiveLang(lang);
    await firstValueFrom(transloco.load(lang));
  }

  it('labels the paginator in Hindi once the labels have loaded', async () => {
    await useLanguage('hi');

    await vi.waitFor(() => expect(intl.itemsPerPageLabel).toBe('प्रति पृष्ठ आइटम'));
    expect(intl.nextPageLabel).toBe('अगला पृष्ठ');
    expect(intl.getRangeLabel(0, 25, 2)).toBe(`2 में से 1 ${DASH} 2`);
    expect(intl.getRangeLabel(0, 25, 0)).toBe('0 में से 0');
  });

  it('switches language in place and tells the paginators to redraw', async () => {
    // Hindi first: Material's own English labels would pass for ours.
    await useLanguage('hi');
    await vi.waitFor(() => expect(intl.itemsPerPageLabel).toBe('प्रति पृष्ठ आइटम'));
    await useLanguage('en');
    expect(intl.itemsPerPageLabel).toBe('Items per page');
    expect(intl.getRangeLabel(1, 25, 60)).toBe(`26 ${DASH} 50 of 60`);

    const redraws = vi.fn();
    intl.changes.subscribe(redraws);

    await useLanguage('hi');

    expect(redraws).toHaveBeenCalled();
    expect(intl.itemsPerPageLabel).toBe('प्रति पृष्ठ आइटम');
    expect(intl.getRangeLabel(1, 25, 60)).toBe(`60 में से 26 ${DASH} 50`);
  });
});
