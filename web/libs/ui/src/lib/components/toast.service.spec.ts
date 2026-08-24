import { TestBed } from '@angular/core/testing';
import { ToastService } from './toast.service';

describe('ToastService', () => {
  let service: ToastService;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({});
    service = TestBed.inject(ToastService);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('queues a toast with its tone', () => {
    service.error('errors.unexpected', 'catalog.product.not_found');

    expect(service.toasts()).toHaveLength(1);
    expect(service.toasts()[0].tone).toBe('error');
    expect(service.toasts()[0].detail).toBe('catalog.product.not_found');
  });

  it('keeps several toasts in the order they arrived', () => {
    service.info('a');
    service.success('b');

    expect(service.toasts().map((toast) => toast.message)).toEqual(['a', 'b']);
  });

  it('dismisses only the toast asked for', () => {
    const first = service.error('a');
    service.error('b');

    service.dismiss(first);

    expect(service.toasts().map((toast) => toast.message)).toEqual(['b']);
  });

  it('expires a toast on its own after the timeout', () => {
    service.error('a');

    vi.advanceTimersByTime(6000);

    expect(service.toasts()).toHaveLength(0);
  });

  it('clears everything at once', () => {
    service.error('a');
    service.info('b');

    service.clear();

    expect(service.toasts()).toHaveLength(0);
  });
});
