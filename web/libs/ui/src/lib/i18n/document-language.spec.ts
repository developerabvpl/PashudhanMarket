import { DOCUMENT, PLATFORM_ID, REQUEST, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { LANGUAGE_PENDING_CLASS, provideInitialLanguage } from './language-preference';
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

describe('provideInitialLanguage in the browser', () => {
  afterEach(() => {
    document.cookie = 'upb.lang=; path=/; max-age=0';
    document.documentElement.classList.remove(LANGUAGE_PENDING_CLASS);
    document.documentElement.setAttribute('lang', 'en');
  });

  it('shows a page index.html held back once the app has drawn it in the language the visitor chose', async () => {
    // What index.html does before the app starts, for a Hindi visitor on an English page.
    document.cookie = 'upb.lang=hi; path=/';
    document.documentElement.setAttribute('lang', 'hi');
    document.documentElement.classList.add(LANGUAGE_PENDING_CLASS);

    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideI18n(strings), provideInitialLanguage()] });

    const transloco = TestBed.inject(TranslocoService);
    await new Promise((resolve) => setTimeout(resolve, 20));

    expect(transloco.getActiveLang()).toBe('hi');
    expect(transloco.translate('nav.team')).toBe('टीम');
    expect(document.documentElement.lang).toBe('hi');
    expect(document.documentElement.classList.contains(LANGUAGE_PENDING_CLASS)).toBe(false);
  });

  it('shows the page as it is when the strings for that language cannot be loaded', async () => {
    document.cookie = 'upb.lang=hi; path=/';
    document.documentElement.classList.add(LANGUAGE_PENDING_CLASS);

    const broken = { en: strings.en, hi: () => Promise.reject(new Error('chunk failed to load')) };

    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideI18n(broken), provideInitialLanguage()] });

    try {
      TestBed.inject(TranslocoService);
    } catch {
      // The failed initializer is not what is under test.
    }

    await new Promise((resolve) => setTimeout(resolve, 20));

    expect(document.documentElement.classList.contains(LANGUAGE_PENDING_CLASS)).toBe(false);
  });
});
