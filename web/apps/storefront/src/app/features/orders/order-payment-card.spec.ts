import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { OrderDto } from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { translations } from '../../i18n/translations';
import { OrderPaymentCard } from './order-payment-card';

/** An online order of ₹476 that was paid, then cancelled: it keeps nothing, so totals nothing. */
const cancelledPaid = {
  id: 'o1',
  number: 'UPB-260929-JPPXA9',
  status: 'Cancelled',
  paymentMethod: 'Online',
  paymentStatus: 'Paid',
  subtotal: 0,
  discount: 0,
  shippingFee: 0,
  deliveryDiscount: 0,
  total: 0,
  couponCode: null,
  amountPaid: 476,
  refundTotal: 476,
  parts: [],
} as unknown as OrderDto;

/** The card as a screen reads it, spacing collapsed: a term runs straight into its amount. */
async function render(order: OrderDto): Promise<string> {
  TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideI18n(translations)] });

  const fixture = TestBed.createComponent(OrderPaymentCard);
  fixture.componentRef.setInput('order', order);
  await fixture.whenStable();

  return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
}

describe('OrderPaymentCard', () => {
  it('tells a buyer whose paid order was cancelled what they paid and what is coming back', async () => {
    const text = await render(cancelledPaid);

    expect(text).toContain('Total₹0');
    expect(text).toContain('Paid online₹476');
    expect(text).toContain('Refund₹476');
    expect(text).toContain('Refunds go back to the account you paid from.');
  });

  it('says nothing about refunds while nothing is being refunded', async () => {
    const text = await render({ ...cancelledPaid, status: 'Confirmed', subtotal: 476, total: 476, refundTotal: 0 });

    expect(text).toContain('Total₹476');
    expect(text).not.toContain('Paid online');
    expect(text).not.toContain('Refund');
  });

  it('never shows a refund on cash on delivery, which collected nothing online', async () => {
    const text = await render({ ...cancelledPaid, paymentMethod: 'CashOnDelivery', amountPaid: null, refundTotal: 0 });

    expect(text).toContain('Cash on delivery');
    expect(text).not.toContain('Refund');
  });
});
