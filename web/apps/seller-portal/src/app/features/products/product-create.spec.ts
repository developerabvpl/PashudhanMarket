import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { Api } from '@upbazaar/data-access';
import { ToastService, provideI18n } from '@upbazaar/ui';
import { ProductCreate } from './product-create';

function setInput(element: HTMLElement, id: string, value: string): void {
  const input = element.querySelector<HTMLInputElement>(`#${id}`)!;

  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  input.dispatchEvent(new Event('blur', { bubbles: true }));
}

function fillValidProduct(element: HTMLElement): void {
  setInput(element, 'name', 'Banarasi Silk Saree');
  setInput(element, 'sku', 'UPB-SAREE-001');
  setInput(element, 'price', '4599');
  setInput(element, 'initialStock', '25');
  setInput(element, 'categoryId', '11111111-1111-1111-1111-111111111111');
  setInput(element, 'sellerId', '22222222-2222-2222-2222-222222222222');
}

function submit(element: HTMLElement): void {
  element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true, bubbles: true }));
}

describe('ProductCreate', () => {
  let invoke: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    invoke = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideI18n(),
        ToastService,
        { provide: Api, useValue: { invoke } },
      ],
    });
  });

  async function render() {
    const fixture = TestBed.createComponent(ProductCreate);
    await fixture.whenStable();

    return fixture;
  }

  it('does not post an incomplete form', async () => {
    const fixture = await render();

    setInput(fixture.nativeElement, 'name', 'Only a name');
    submit(fixture.nativeElement);
    await fixture.whenStable();

    expect(invoke).not.toHaveBeenCalled();
  });

  it('rejects a price of zero, which the API would refuse anyway', async () => {
    const fixture = await render();

    fillValidProduct(fixture.nativeElement);
    setInput(fixture.nativeElement, 'price', '0');
    submit(fixture.nativeElement);
    await fixture.whenStable();

    expect(invoke).not.toHaveBeenCalled();
  });

  it('posts the product when the form is complete', async () => {
    invoke.mockResolvedValue({ id: 'p1', sku: 'UPB-SAREE-001' });
    const fixture = await render();

    fillValidProduct(fixture.nativeElement);
    submit(fixture.nativeElement);
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledTimes(1);

    const body = invoke.mock.calls[0][1].body;
    expect(body.sku).toBe('UPB-SAREE-001');
    expect(body.price).toBe(4599);
    expect(body.currency).toBe('INR');
  });

  it('shows a server-side field rejection under that field', async () => {
    invoke.mockRejectedValue(
      new HttpErrorResponse({
        status: 400,
        error: { errors: { Sku: ['A product with this SKU already exists.'] } },
      })
    );
    const fixture = await render();

    fillValidProduct(fixture.nativeElement);
    submit(fixture.nativeElement);
    await fixture.whenStable();

    const skuErrors = fixture.nativeElement.querySelector('#sku-errors');
    expect(skuErrors?.textContent).toContain('A product with this SKU already exists.');
  });

  it('clears the form after a successful save so the next product starts blank', async () => {
    invoke.mockResolvedValue({ id: 'p1', sku: 'UPB-SAREE-001' });
    const fixture = await render();

    fillValidProduct(fixture.nativeElement);
    submit(fixture.nativeElement);
    await fixture.whenStable();
    // The reset happens inside the submit action; the bound inputs update on the next pass.
    fixture.detectChanges();
    await fixture.whenStable();

    const name = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('#name');
    expect(name?.value).toBe('');
  });
});
