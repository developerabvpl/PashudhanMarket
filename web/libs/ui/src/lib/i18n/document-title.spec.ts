import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { provideDocumentTitle } from './document-title';
import { provideI18n } from './provide-i18n';

describe('provideDocumentTitle', () => {
  it('names the tab in the language showing, and follows a change', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideI18n({
          en: () => Promise.resolve({ app: { adminPortal: 'UP Bazaar Admin' } }),
          hi: () => Promise.resolve({ app: { adminPortal: 'यूपी बाज़ार प्रशासन' } }),
        }),
        provideDocumentTitle('app.adminPortal'),
      ],
    });

    const transloco = TestBed.inject(TranslocoService);
    const title = TestBed.inject(Title);

    await firstValueFrom(transloco.load('en'));
    await new Promise((resolve) => setTimeout(resolve));
    expect(title.getTitle()).toBe('UP Bazaar Admin');

    transloco.setActiveLang('hi');
    await firstValueFrom(transloco.load('hi'));
    await new Promise((resolve) => setTimeout(resolve));
    expect(title.getTitle()).toBe('यूपी बाज़ार प्रशासन');
  });
});
