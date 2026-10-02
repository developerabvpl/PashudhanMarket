import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { HttpContext, HttpErrorResponse } from '@angular/common/http';
import { Api, apiV1PromotionsCouponsPreviewPost, callerShowsErrors } from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { translations } from '../../i18n/translations';
import { CouponField } from './coupon-field';

async function render(invoke: ReturnType<typeof vi.fn>) {
  TestBed.configureTestingModule({
    providers: [provideZonelessChangeDetection(), provideI18n(translations), { provide: Api, useValue: { invoke } }],
  });

  const fixture = TestBed.createComponent(CouponField);
  const applied = vi.fn();
  fixture.componentInstance.applied.subscribe(applied);
  await fixture.whenStable();

  return { fixture, applied, element: fixture.nativeElement as HTMLElement };
}

function type(element: HTMLElement, text: string): HTMLInputElement {
  const input = element.querySelector('#coupon-code') as HTMLInputElement;
  input.value = text;
  input.dispatchEvent(new Event('input'));

  return input;
}

describe('CouponField', () => {
  it('applies a code on Enter without submitting the checkout, and shows the saving', async () => {
    const preview = { code: 'WELCOME10', description: 'Welcome', discount: 15, deliveryDiscount: 0 };
    const invoke = vi.fn(async () => preview);
    const { fixture, applied, element } = await render(invoke);

    const input = type(element, ' welcome10 ');
    const enter = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true });
    input.dispatchEvent(enter);
    await fixture.whenStable();

    expect(enter.defaultPrevented).toBe(true);
    expect(invoke).toHaveBeenCalledWith(apiV1PromotionsCouponsPreviewPost, { body: { code: 'welcome10' } }, expect.any(HttpContext));
    expect((invoke.mock.calls[0] as unknown[])[2]).toEqual(callerShowsErrors());
    expect(applied).toHaveBeenCalledWith(preview);
    expect(element.textContent).toContain('You save ₹15');
  });

  it('counts a free-delivery coupon’s saving', async () => {
    const { fixture, element } = await render(vi.fn(async () => ({ code: 'SHIPFREE', description: 'Free delivery', discount: 0, deliveryDiscount: 49 })));

    type(element, 'SHIPFREE');
    await fixture.whenStable();
    [...element.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Apply')!.click();
    await fixture.whenStable();

    expect(element.textContent).toContain('You save ₹49');
  });

  it('says why a code takes nothing off', async () => {
    const invoke = vi.fn(async () => {
      throw new HttpErrorResponse({
        status: 400,
        error: { title: 'That coupon has been used as many times as it can be.', code: 'promotions.coupon.used_up' },
      });
    });
    const { fixture, applied, element } = await render(invoke);

    type(element, 'GONE');
    await fixture.whenStable();
    [...element.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Apply')!.click();
    await fixture.whenStable();

    expect(element.textContent).toContain('used as many times as it can be');
    expect(applied).not.toHaveBeenCalled();
  });

  it('says why once, in words, with the minimum, never the raw code', async () => {
    const invoke = vi.fn(async () => {
      throw new HttpErrorResponse({
        status: 400,
        error: { title: 'That coupon needs at least Rs 499 of the goods it covers.', code: 'promotions.coupon.below_minimum' },
      });
    });
    const { fixture, element } = await render(invoke);

    type(element, 'BIG');
    await fixture.whenStable();
    [...element.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Apply')!.click();
    await fixture.whenStable();

    const alerts = element.querySelectorAll('[role=alert]');
    expect(alerts.length).toBe(1);
    expect(alerts[0].textContent).toContain('needs at least ₹499');
    expect(element.textContent).not.toContain('promotions.coupon');
  });

  it('can be taken off again', async () => {
    const { fixture, applied, element } = await render(vi.fn(async () => ({ code: 'WELCOME10', description: 'Welcome', discount: 15, deliveryDiscount: 0 })));

    type(element, 'WELCOME10');
    await fixture.whenStable();
    [...element.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Apply')!.click();
    await fixture.whenStable();
    [...element.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Remove')!.click();
    await fixture.whenStable();

    expect(applied).toHaveBeenLastCalledWith(null);
    expect(element.querySelector('#coupon-code')).not.toBeNull();
  });
});
