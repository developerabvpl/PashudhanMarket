import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { Api, SellerOrderDto, apiV1SellerOrdersOrderIdPartsPartIdReturnInspectionPost } from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { cameBack } from './came-back';
import { ReturnInspection } from './return-inspection';

const order = {
  orderId: 'o1',
  partId: 'part1',
  lines: [
    { productId: 'p1', sku: 'DIYA-12', name: 'Gobar Diya', unitPrice: 75, quantity: 3, lineTotal: 225, discount: 0, returnQuantity: 2 },
    { productId: 'p2', sku: 'DHOOP', name: 'Dhoop Batti', unitPrice: 50, quantity: 1, lineTotal: 50, discount: 0, returnQuantity: 0 },
  ],
  returnRequest: { status: 'Approved' },
} as unknown as SellerOrderDto;

describe('ReturnInspection', () => {
  it('asks about only what came back of a buyer’s return, or all of an undelivered parcel', () => {
    expect(cameBack(order)).toEqual([{ productId: 'p1', name: 'Gobar Diya', quantity: 2 }]);
    expect(cameBack({ ...order, returnRequest: null })).toEqual([
      { productId: 'p1', name: 'Gobar Diya', quantity: 3 },
      { productId: 'p2', name: 'Dhoop Batti', quantity: 1 },
    ]);
  });

  it('records a condition for each product that came back', async () => {
    const invoke = vi.fn(async () => order);

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideI18n(), { provide: Api, useValue: { invoke } }],
    });

    const fixture = TestBed.createComponent(ReturnInspection);
    fixture.componentRef.setInput('order', order);
    await fixture.whenStable();

    const element = fixture.nativeElement as HTMLElement;
    const damaged = element.querySelector('input[name="condition-p1"][value="Damaged"]') as HTMLInputElement;
    damaged.click();
    await fixture.whenStable();
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1SellerOrdersOrderIdPartsPartIdReturnInspectionPost, {
      orderId: 'o1',
      partId: 'part1',
      body: { condition: null, note: null, lines: [{ productId: 'p1', condition: 'Damaged' }] },
    });
  });
});
