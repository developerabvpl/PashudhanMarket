import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideI18n } from '@upbazaar/ui';
import { SalePrice } from './sale-price';

async function render(regular: number, current: number) {
  TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideI18n()] });

  const fixture = TestBed.createComponent(SalePrice);
  fixture.componentRef.setInput('regular', regular);
  fixture.componentRef.setInput('current', current);
  await fixture.whenStable();

  return fixture.nativeElement as HTMLElement;
}

describe('SalePrice', () => {
  it('shows just the price with no sale', async () => {
    const element = await render(120, 120);

    expect(element.textContent).toContain('₹120');
    expect(element.querySelector('.line-through')).toBeNull();
  });

  it('strikes through the regular price and says how much is off, rounded down', async () => {
    const element = await render(120, 99);

    expect(element.querySelector('.line-through')!.textContent).toContain('Regular price');
    expect(element.querySelector('.line-through')!.textContent).toContain('₹120');
    expect(element.textContent).toContain('₹99');
    expect(element.textContent).toContain('17% off');
  });
});
