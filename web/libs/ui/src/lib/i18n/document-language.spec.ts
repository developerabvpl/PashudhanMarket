import { DOCUMENT, PLATFORM_ID, REQUEST, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { provideInitialLanguage } from './language-preference';
import { provideI18n } from './provide-i18n';

const strings = {
  en: () => Promise.resolve({ nav: { team: 'Team' } }),
  hi: () => Promise.resolve({ nav: { team: 'टीम' } }),
};

describe('provideDocumentLanguage', () => {
  afterEach(() => document.documentElement.setAttribute('lang', 'en'));

  it('marks the page with the language showing, and follows a change', () => {
    document.documentElement.setAttribute('lang', 'xx');
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideI18n(strings)] });

    const transloco = TestBed.inject(TranslocoService);
    expect(document.documentElement.lang).toBe('en');

    transloco.setActiveLang('hi');
    expect(document.documentElement.lang).toBe('hi');

    transloco.setActiveLang('en');
    expect(document.documentElement.lang).toBe('en');
  });

  it('writes Hindi into the server-rendered page for a visitor whose cookie asks for it', async () => {
    // The server has no browser document: Angular hands it one of its own, and a request to read.
    const served = document.implementation.createHTMLDocument('served');
    served.documentElement.setAttribute('lang', 'en');

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: PLATFORM_ID, useValue: 'server' },
        { provide: DOCUMENT, useValue: served },
        { provide: REQUEST, useValue: { headers: new Headers({ cookie: 'upb.lang=hi' }) } },
        provideI18n(strings),
        provideInitialLanguage(),
      ],
    });

    // Creating the injector runs the app initializer, which reads the cookie and sets the language.
    TestBed.inject(TranslocoService);
    await new Promise((resolve) => setTimeout(resolve));

    expect(served.documentElement.lang).toBe('hi');
  });

  it('stops following once the app is torn down', () => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideI18n(strings)] });

    const transloco = TestBed.inject(TranslocoService);
    TestBed.resetTestingModule();

    transloco.setActiveLang('hi');
    expect(document.documentElement.lang).toBe('en');
  });
});
