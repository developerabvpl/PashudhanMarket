import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideI18n } from '../i18n/provide-i18n';
import { PageState } from './page-state';

describe('PageState', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideI18n()] });
  });

  it('colours the retry button with the app’s primary token, not the storefront’s brand colour', async () => {
    const fixture = TestBed.createComponent(PageState);
    fixture.componentRef.setInput('state', 'error');
    await fixture.whenStable();

    const retry = (fixture.nativeElement as HTMLElement).querySelector('button') as HTMLButtonElement;

    // bg-primary reads --color-primary: marigold in the storefront, Material's blue in the portals.
    expect(retry.className).toContain('bg-primary');
    expect(retry.className).toContain('hover:bg-primary-hover');
    expect(retry.className).not.toContain('bg-brand-');
  });

  it('asks to retry when the button is pressed', async () => {
    const fixture = TestBed.createComponent(PageState);
    fixture.componentRef.setInput('state', 'error');
    let retried = 0;
    fixture.componentInstance.retry.subscribe(() => retried++);
    await fixture.whenStable();

    (fixture.nativeElement as HTMLElement).querySelector('button')?.click();

    expect(retried).toBe(1);
  });
});
