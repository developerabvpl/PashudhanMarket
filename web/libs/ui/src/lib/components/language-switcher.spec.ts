import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { provideI18n } from '../i18n/provide-i18n';
import { LanguageSwitcher } from './language-switcher';

describe('LanguageSwitcher', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideI18n()] });
  });

  async function useHindi(): Promise<void> {
    const transloco = TestBed.inject(TranslocoService);
    transloco.setActiveLang('hi');
    await firstValueFrom(transloco.load('hi'));
  }

  it('shows Hindi when the page starts in Hindi', async () => {
    // What provideInitialLanguage does from the cookie, before anything renders.
    await useHindi();

    const fixture = TestBed.createComponent(LanguageSwitcher);
    await fixture.whenStable();

    expect((fixture.nativeElement.querySelector('select') as HTMLSelectElement).value).toBe('hi');
  });

  it('follows a language set elsewhere after it has rendered', async () => {
    const fixture = TestBed.createComponent(LanguageSwitcher);
    await fixture.whenStable();
    const select = fixture.nativeElement.querySelector('select') as HTMLSelectElement;
    expect(select.value).toBe('en');

    await useHindi();
    await fixture.whenStable();

    expect(select.value).toBe('hi');
  });
});
