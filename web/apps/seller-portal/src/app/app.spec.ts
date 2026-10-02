import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { AuthService, CurrentUserStore } from '@upbazaar/auth';
import { provideI18n } from '@upbazaar/ui';
import { App } from './app';
import { SellerAccess, SellerPermissions } from './core/seller-access';
import { translations } from './i18n/translations';

async function render(permissions: readonly string[]) {
  TestBed.configureTestingModule({
    providers: [
      provideZonelessChangeDetection(),
      provideRouter([]),
      provideI18n(translations),
      { provide: AuthService, useValue: { logout: vi.fn() } },
      { provide: SellerAccess, useValue: { canSell: signal(true), load: vi.fn(), clear: vi.fn() } },
      {
        provide: CurrentUserStore,
        useValue: {
          isSignedIn: signal(true),
          displayName: signal('Ramesh'),
          has: (p: string) => permissions.includes(p),
          ensureLoaded: vi.fn(async () => null),
        },
      },
    ],
  });

  const fixture = TestBed.createComponent(App);
  await fixture.whenStable();

  return fixture.nativeElement as HTMLElement;
}

function wideEntries(element: HTMLElement): string[] {
  const nav = element.querySelector('nav')!;

  return [...nav.querySelectorAll(':scope > a, :scope > button, :scope upb-nav-group-menu > button')].map(
    (e) => e.textContent?.trim() ?? ''
  );
}

describe('App toolbar', () => {
  it('keeps the daily pages one click away and the rest in one drop-down, for the owner', async () => {
    const element = await render(Object.values(SellerPermissions));

    expect(wideEntries(element)).toEqual(['Orders', 'Returns', 'Products', 'Earnings', 'Your shop']);
  });

  it('folds everything into one menu below 1024px', async () => {
    const element = await render(Object.values(SellerPermissions));

    expect(element.querySelector('nav')!.className).toContain('hidden');
    expect(element.querySelector('nav')!.className).toContain('lg:flex');
    expect(element.querySelector('[class~="lg:hidden"]')!.textContent?.trim()).toBe('Menu');
  });

  it('shows a dispatch member only orders and returns', async () => {
    const element = await render([SellerPermissions.Orders]);

    expect(wideEntries(element)).toEqual(['Orders', 'Returns']);
  });
});
