import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { provideI18n } from '@upbazaar/ui';
import { firstValueFrom } from 'rxjs';
import { PortalForbiddenPage } from '../portal/portal-forbidden.page';
import { ForbiddenPage } from './forbidden.page';

async function render<T>(page: new () => T, required: string | null = 'settlements.own.read'): Promise<HTMLElement> {
  TestBed.configureTestingModule({
    providers: [
      provideZonelessChangeDetection(),
      provideI18n(),
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(required ? { required } : {}) } } },
    ],
  });

  // The shared strings load on first use; have them in hand before the page draws.
  await firstValueFrom(TestBed.inject(TranslocoService).load('en'));

  const fixture = TestBed.createComponent(page);
  await fixture.whenStable();

  return fixture.nativeElement as HTMLElement;
}

describe('forbidden pages', () => {
  it('say in plain words that the account may not open the page, and whom to ask', async () => {
    for (const page of [ForbiddenPage, PortalForbiddenPage]) {
      const element = await render(page);
      const sentence = element.querySelector('h1 + p')?.textContent ?? '';

      expect(sentence).toContain('Your account is not allowed to open this page');
      expect(sentence).not.toContain('settlements.own.read');
      // The old "Required: <permission key>" line is gone.
      expect(element.textContent).not.toContain('Required:');

      TestBed.resetTestingModule();
    }
  });

  it('keep the permission key out of the sentence, small and for developers only', async () => {
    // Specs run in dev mode, where the key is shown - small, beneath the sentence, never in it.
    const element = await render(PortalForbiddenPage);
    const key = element.querySelector('p.font-mono');

    expect(key?.textContent?.trim()).toBe('settlements.own.read');
    expect(key?.className).toContain('text-xs');

    TestBed.resetTestingModule();

    expect((await render(PortalForbiddenPage, null)).querySelector('p.font-mono')).toBeNull();
  });

  it('give the portals a Material button and leave the storefront its own', async () => {
    const portal = (await render(PortalForbiddenPage)).querySelector('button') as HTMLButtonElement;

    expect(portal.hasAttribute('mat-flat-button')).toBe(true);
    expect(portal.className).not.toContain('bg-brand-600');

    TestBed.resetTestingModule();

    const storefront = (await render(ForbiddenPage)).querySelector('button') as HTMLButtonElement;

    expect(storefront.hasAttribute('mat-flat-button')).toBe(false);
    expect(storefront.className).toContain('bg-brand-600');
  });
});
