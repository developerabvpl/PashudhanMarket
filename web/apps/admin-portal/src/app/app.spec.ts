import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { AuthService, CurrentUserStore } from '@upbazaar/auth';
import { provideI18n } from '@upbazaar/ui';
import { App } from './app';
import { translations } from './i18n/translations';

async function render(permissions: readonly string[] | 'all') {
  TestBed.configureTestingModule({
    providers: [
      provideZonelessChangeDetection(),
      provideRouter([]),
      provideI18n(translations),
      { provide: AuthService, useValue: { logout: vi.fn() } },
      {
        provide: CurrentUserStore,
        useValue: {
          isSignedIn: signal(true),
          displayName: signal('Asha'),
          has: (p: string) => permissions === 'all' || permissions.includes(p),
          ensureLoaded: vi.fn(async () => null),
        },
      },
    ],
  });

  const fixture = TestBed.createComponent(App);
  await fixture.whenStable();

  return fixture.nativeElement as HTMLElement;
}

/** The wide toolbar's top-level entries, by their text. */
function wideEntries(element: HTMLElement): string[] {
  const nav = element.querySelector('nav')!;

  return [...nav.querySelectorAll(':scope > a, :scope > button, :scope upb-nav-group-menu > button')].map(
    (e) => e.textContent?.trim() ?? ''
  );
}

describe('App toolbar', () => {
  it('groups thirteen pages into six entries, so the toolbar fits from 1024px up', async () => {
    const element = await render('all');

    expect(wideEntries(element)).toEqual(['Staff users', 'Orders', 'Money', 'Shipping', 'Sellers', 'Catalogue']);
  });

  it('shows the entries only on wide screens and one menu below 1024px', async () => {
    const element = await render('all');

    expect(element.querySelector('nav')!.className).toContain('hidden');
    expect(element.querySelector('nav')!.className).toContain('lg:flex');
    const narrow = element.querySelector('[class~="lg:hidden"]')!;
    expect(narrow.textContent?.trim()).toBe('Menu');
  });

  it('offers only what the user may open, and a group of one as a plain link', async () => {
    const element = await render(['payments.read']);

    expect(wideEntries(element)).toEqual(['Payments']);
    expect(element.querySelector('nav a')!.getAttribute('href')).toBe('/payments');
  });
});
