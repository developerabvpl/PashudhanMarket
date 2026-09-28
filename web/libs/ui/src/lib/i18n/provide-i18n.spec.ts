import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom, isObservable } from 'rxjs';
import { Translation } from '@jsverse/transloco';
import { AppTranslations, BundledTranslocoLoader, provideI18n } from './provide-i18n';

async function load(lang: string, translations?: AppTranslations): Promise<Translation> {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), translations ? provideI18n(translations) : provideI18n()] });

  const result = TestBed.inject(BundledTranslocoLoader).getTranslation(lang);

  return isObservable(result) ? firstValueFrom(result) : result;
}

describe('BundledTranslocoLoader', () => {
  it('serves every string when no app hands in its own', async () => {
    expect((await load('en'))['nav']['team']).toBe('Team');
  });

  it('loads Hindi when it is asked for', async () => {
    expect((await load('hi'))['nav']['team']).toBe('टीम');
  });

  it('falls back to English for a language it does not have', async () => {
    expect((await load('ta'))['nav']['team']).toBe('Team');
  });

  it("serves the app's own copy when it is given one", async () => {
    const own: AppTranslations = {
      en: () => Promise.resolve({ nav: { team: 'Staff' } }),
      hi: () => Promise.resolve({ nav: { team: 'कर्मचारी' } }),
    };

    expect((await load('en', own))['nav']['team']).toBe('Staff');
    expect((await load('hi', own))['nav']['team']).toBe('कर्मचारी');
  });
});
