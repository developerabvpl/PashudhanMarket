import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ToastService } from '@upbazaar/ui';
import { firstValueFrom } from 'rxjs';
import { callerShowsErrors, httpErrorInterceptor } from './http-error.interceptor';

function setUp() {
  TestBed.configureTestingModule({
    providers: [
      provideZonelessChangeDetection(),
      provideHttpClient(withInterceptors([httpErrorInterceptor])),
      provideHttpClientTesting(),
    ],
  });

  return {
    http: TestBed.inject(HttpClient),
    backend: TestBed.inject(HttpTestingController),
    toast: TestBed.inject(ToastService),
  };
}

const COUPON_FAILURE = { title: 'That coupon is not valid now.', code: 'promotions.coupon.not_valid_now' };

describe('httpErrorInterceptor', () => {
  it('toasts a failure nobody else shows', async () => {
    const { http, backend, toast } = setUp();
    const call = firstValueFrom(http.post('/api/x', {}));

    backend.expectOne('/api/x').flush(COUPON_FAILURE, { status: 400, statusText: 'Bad Request' });

    await expect(call).rejects.toBeTruthy();
    expect(toast.toasts().map((t) => t.detail)).toEqual(['promotions.coupon.not_valid_now']);
  });

  it('stays quiet when the caller shows the failure itself, and still rethrows it', async () => {
    const { http, backend, toast } = setUp();
    const call = firstValueFrom(http.post('/api/x', {}, { context: callerShowsErrors() }));

    backend.expectOne('/api/x').flush(COUPON_FAILURE, { status: 400, statusText: 'Bad Request' });

    await expect(call).rejects.toBeTruthy();
    expect(toast.toasts()).toEqual([]);
  });
});
