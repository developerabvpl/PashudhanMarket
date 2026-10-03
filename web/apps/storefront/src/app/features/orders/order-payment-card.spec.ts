import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { OrderDto, OrderPartDto } from '@upbazaar/data-access';
import { TranslocoService } from '@jsverse/transloco';
import { provideI18n } from '@upbazaar/ui';
import { firstValueFrom } from 'rxjs';
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

/** One seller's parcel, as far as the card reads it. */
function part(status: string): OrderPartDto {
  return { id: status, status, lines: [{ quantity: 2, returnQuantity: 0 }], returnRequest: null } as unknown as OrderPartDto;
}

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

  it('shows no refund on a cancelled cash on delivery order, which collected nothing', async () => {
    const text = await render({ ...cancelledPaid, paymentMethod: 'CashOnDelivery', amountPaid: null, refundTotal: 0 });

    expect(text).toContain('Cash on delivery');
    expect(text).not.toContain('Refund');
  });

  it('does not call delivery free on a cancelled order whose charge was paid and is being refunded', async () => {
    // ₹49 delivery was paid; the cancelled parcel gave it back, so the charge now reads zero.
    const text = await render({ ...cancelledPaid, amountPaid: 49, refundTotal: 49, parts: [part('Cancelled')] });

    expect(text).not.toContain('Free');
    expect(text).not.toContain('Delivery');
    expect(text).toContain('Paid online₹49');
    expect(text).toContain('Refund₹49');
  });

  it('still calls delivery free while a parcel that ships free is on its way', async () => {
    const text = await render({ ...cancelledPaid, status: 'Confirmed', subtotal: 600, total: 600, amountPaid: 600, refundTotal: 0, parts: [part('Packed')] });

    expect(text).toContain('Delivery Free');
  });

  it('shows a delivery charge that was paid, whatever became of the parcels', async () => {
    const text = await render({ ...cancelledPaid, shippingFee: 49, total: 49, parts: [part('Cancelled')] });

    expect(text).toContain('Delivery ₹49');
  });

  it('tells a cash on delivery buyer what their accepted return sends back to their UPI id', async () => {
    const text = await render({
      ...cancelledPaid,
      status: 'Completed',
      paymentMethod: 'CashOnDelivery',
      paymentStatus: 'CashOnDelivery',
      subtotal: 478,
      total: 478,
      amountPaid: null,
      refundTotal: 0,
      parts: [
        { ...part('Returned'), returnRequest: { status: 'Approved', refundDue: 79, refundUpiId: 'asha@okicici' } },
        // Asked for, not accepted: nothing is owed for it yet.
        { ...part('Delivered'), returnRequest: { status: 'Requested', refundDue: null } },
      ],
    } as unknown as OrderDto);

    expect(text).toContain('Refund to your UPI ID₹79');
    expect(text).toContain('sent to the UPI ID you gave');
    expect(text).not.toContain('Paid online');
    expect(text).not.toContain('account you paid from');
  });

  it('says where the cash refund goes in Hindi too', async () => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideI18n(translations)] });

    const transloco = TestBed.inject(TranslocoService);
    transloco.setActiveLang('hi');
    await firstValueFrom(transloco.load('hi'));

    const fixture = TestBed.createComponent(OrderPaymentCard);
    fixture.componentRef.setInput('order', {
      ...cancelledPaid,
      status: 'Completed',
      paymentMethod: 'CashOnDelivery',
      amountPaid: null,
      refundTotal: 0,
      parts: [{ ...part('Returning'), returnRequest: { status: 'Approved', refundDue: 79 } }],
    });
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('आपकी UPI आईडी पर रिफ़ंड');
  });
});
