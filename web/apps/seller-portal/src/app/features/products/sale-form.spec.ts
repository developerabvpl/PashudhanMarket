import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import {
  Api,
  ProductDto,
  apiV1SellerCatalogProductsProductIdSaleDelete,
  apiV1SellerCatalogProductsProductIdSalePut,
} from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { translations } from '../../i18n/translations';
import { SaleForm } from './sale-form';

const product: ProductDto = {
  id: 'p1',
  sku: 'DIYA-12',
  name: 'Cow Dung Diya, pack of 12',
  slug: 'cow-dung-diya-pack-of-12',
  brand: null,
  description: null,
  price: 120,
  currency: 'INR',
  status: 'Active',
  sellerId: 's1',
  category: { id: 'c1', name: 'Diyas', slug: 'diyas', parentId: null },
  onHandQuantity: 10,
  reservedQuantity: 0,
  createdAtUtc: '2026-09-22T10:00:00Z',
  modifiedAtUtc: null,
  package: null,
  reviewNote: null,
  currentPrice: 120,
  sale: null,
};

const onSale: ProductDto = {
  ...product,
  currentPrice: 99,
  sale: { price: 99, startsAtUtc: '2026-09-25T00:00:00Z', endsAtUtc: '2026-10-31T18:29:59Z', isRunning: true },
};

async function render(shown: ProductDto) {
  const invoke = vi.fn(async (fn: unknown) => (fn === apiV1SellerCatalogProductsProductIdSalePut ? onSale : product));

  TestBed.configureTestingModule({
    providers: [provideZonelessChangeDetection(), provideI18n(translations), { provide: Api, useValue: { invoke } }],
  });

  const fixture = TestBed.createComponent(SaleForm);
  fixture.componentRef.setInput('product', shown);
  const changed = vi.fn();
  fixture.componentInstance.changed.subscribe(changed);
  await fixture.whenStable();

  return { fixture, invoke, changed, element: fixture.nativeElement as HTMLElement };
}

function fill(element: HTMLElement, name: string, value: string): void {
  const input = element.querySelector(`input[name="${name}"]`) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

describe('SaleForm', () => {
  it('puts the product on sale to the end of the chosen day in India', async () => {
    const { fixture, invoke, changed, element } = await render(product);

    fill(element, 'salePrice', '99');
    fill(element, 'endsOn', '2026-10-31');
    await fixture.whenStable();
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1SellerCatalogProductsProductIdSalePut, {
      productId: 'p1',
      body: { salePrice: 99, startsAtUtc: null, endsAtUtc: '2026-10-31T18:29:59.000Z' },
    });
    expect(changed).toHaveBeenCalledWith(onSale);
  });

  it('will not offer a sale price at or above the regular price', async () => {
    const { fixture, element } = await render(product);

    fill(element, 'salePrice', '120');
    fill(element, 'endsOn', '2026-10-31');
    await fixture.whenStable();

    const submit = element.querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(submit.disabled).toBe(true);
  });

  it('shows a running sale and ends it', async () => {
    const { fixture, invoke, element } = await render(onSale);

    expect(element.textContent).toContain('On sale at ₹99');

    [...element.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'End sale')!.click();
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1SellerCatalogProductsProductIdSaleDelete, { productId: 'p1' });
  });
});
