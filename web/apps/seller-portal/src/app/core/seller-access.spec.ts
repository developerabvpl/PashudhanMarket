import { HttpContext, HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { AuthService, CurrentUserStore } from '@upbazaar/auth';
import { Api, CALLER_SHOWS_ERRORS, SellerAccessDto, SellerDto, apiV1SellersMeAccessGet } from '@upbazaar/data-access';
import { ToastService } from '@upbazaar/ui';
import { SellerAccess, SellerPermissions, approvedSellerGuard } from './seller-access';

function seller(status: string): SellerDto {
  return {
    id: 's1',
    ownerUserId: 'u1',
    status,
    shopName: 'Shri Krishna Gaushala',
    description: null,
    contactMobile: '9812345678',
    contactEmail: null,
    address: { line1: 'Plot 4', line2: null, city: 'Mathura', state: 'Uttar Pradesh', pincode: '281001' },
    kyc: { legalName: 'Trust', gstin: null, pan: 'ABCDE1234F', bankAccountHolder: 'Trust', bankAccountLast4: '6789', ifsc: 'SBIN0001234' },
    reviewNote: null,
    submittedAtUtc: '2026-09-22T10:00:00Z',
    reviewedAtUtc: null,
  };
}

describe('approvedSellerGuard', () => {
  let answer: () => Promise<SellerDto>;
  let accessAnswer: () => Promise<SellerAccessDto>;
  let invoke: ReturnType<typeof vi.fn>;
  let permissions: ReturnType<typeof signal<readonly string[]>>;
  let refresh: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    permissions = signal<readonly string[]>([]);
    refresh = vi.fn(async () => true);
    invoke = vi.fn((fn: unknown) => (fn === apiV1SellersMeAccessGet ? accessAnswer() : answer()));
    accessAnswer = async () => { const s = await answer(); return { sellerId: s.id, shopName: s.shopName, status: s.status, role: 'Owner' }; };

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: Api, useValue: { invoke } },
        { provide: AuthService, useValue: { refresh } },
        {
          provide: CurrentUserStore,
          useValue: {
            has: (p: string) => permissions().includes(p),
            // Refreshing the profile is when newly granted permissions arrive.
            refresh: vi.fn(async () => permissions.set([SellerPermissions.Orders, SellerPermissions.Manage])),
          },
        },
      ],
    });
  });

  function run(): Promise<boolean | UrlTree> {
    return TestBed.runInInjectionContext(
      () => approvedSellerGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot) as Promise<boolean | UrlTree>
    );
  }

  function urlOf(result: boolean | UrlTree): string {
    return typeof result === 'boolean' ? String(result) : TestBed.inject(Router).serializeUrl(result);
  }

  it('sends someone who has not applied to the application', async () => {
    answer = () => Promise.reject(new HttpErrorResponse({ status: 404 }));

    expect(urlOf(await run())).toBe('/apply');
  });

  it('keeps a pending applicant on their application', async () => {
    answer = async () => seller('Pending');

    expect(urlOf(await run())).toBe('/apply');
    expect(refresh).not.toHaveBeenCalled();
  });

  it('refreshes the session of a newly approved seller and lets them in', async () => {
    answer = async () => seller('Approved');

    expect(await run()).toBe(true);
    expect(refresh).toHaveBeenCalledOnce();
    expect(TestBed.inject(SellerAccess).canSell()).toBe(true);
  });

  it('lets a team member of an approved shop in without loading the owner record', async () => {
    accessAnswer = async () => ({ sellerId: 's1', shopName: 'Shri Krishna Gaushala', status: 'Approved', role: 'Dispatch' });
    permissions.set([SellerPermissions.Orders]);

    expect(await run()).toBe(true);
    expect(invoke).toHaveBeenCalledOnce();
    expect(TestBed.inject(SellerAccess).seller()).toBeNull();
  });

  it('turns away a team member whose shop is no longer approved', async () => {
    accessAnswer = async () => ({ sellerId: 's1', shopName: 'Shri Krishna Gaushala', status: 'Rejected', role: 'Manager' });

    expect(urlOf(await run())).toBe('/forbidden');
  });

  it('lets an approved seller whose session already knows straight in', async () => {
    answer = async () => seller('Approved');
    permissions.set([SellerPermissions.Orders, SellerPermissions.Manage]);

    expect(await run()).toBe(true);
    expect(refresh).not.toHaveBeenCalled();
  });
});

describe('SellerAccess.load', () => {
  function setup(invoke: ReturnType<typeof vi.fn>): { access: SellerAccess; toast: ToastService } {
    TestBed.configureTestingModule({
      providers: [
        { provide: Api, useValue: { invoke } },
        { provide: AuthService, useValue: { refresh: vi.fn() } },
        { provide: CurrentUserStore, useValue: { has: () => false, refresh: vi.fn() } },
      ],
    });

    return { access: TestBed.inject(SellerAccess), toast: TestBed.inject(ToastService) };
  }

  it('treats "not applied yet" as an empty state: the requests are marked quiet and no toast is raised', async () => {
    const invoke = vi.fn(() => Promise.reject(new HttpErrorResponse({ status: 404, error: { title: 'Seller not found.', code: 'sellers.not_found' } })));
    const { access, toast } = setup(invoke);

    expect(await access.load()).toBeNull();

    expect(access.loaded()).toBe(true);
    expect(access.role()).toBeNull();
    expect(toast.toasts()).toEqual([]);
    expect(invoke).toHaveBeenCalledTimes(2);

    for (const call of invoke.mock.calls as unknown as [unknown, unknown, HttpContext][]) {
      expect(call[2].get(CALLER_SHOWS_ERRORS)).toBe(true);
    }
  });

  it('sends one pair of requests when the shell and the page ask at the same moment', async () => {
    const invoke = vi.fn(() => Promise.reject(new HttpErrorResponse({ status: 404 })));
    const { access } = setup(invoke);

    await Promise.all([access.load(), access.load()]);

    expect(invoke).toHaveBeenCalledTimes(2);

    // Once that load is over, asking again loads afresh: the page wants the application as it stands now.
    await access.load();

    expect(invoke).toHaveBeenCalledTimes(4);
  });

  it('still reports a real failure, once, since the error reporter was told to stay quiet', async () => {
    const invoke = vi.fn(() => Promise.reject(new HttpErrorResponse({ status: 500, error: { title: 'Something went wrong.' } })));
    const { access, toast } = setup(invoke);

    await expect(access.load()).rejects.toBeInstanceOf(HttpErrorResponse);

    expect(toast.toasts()).toHaveLength(1);
    expect(toast.toasts()[0].tone).toBe('error');
  });
});
