import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  Api,
  CartDto,
  CartLineDto,
  ProductDto,
  apiV1CartAcknowledgePricesPost,
  apiV1CartGet,
  apiV1CartItemsProductIdPut,
  apiV1CartMergePost,
} from '@upbazaar/data-access';
import { CurrentUserStore } from '@upbazaar/auth';
import { ToastService } from '@upbazaar/ui';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CartStore, MAX_QUANTITY } from './cart.store';

function product(overrides: Partial<ProductDto> = {}): ProductDto {
  return {
    id: 'p1',
    sku: 'UPB-AGB-001',
    name: 'Gurushraddha Cow Dung Dhoop Agarbatti',
    slug: 'gurushraddha-cow-dung-dhoop-agarbatti',
    brand: 'Gurushraddha',
    description: null,
    price: 139,
    currency: 'INR',
    status: 'Active',
    sellerId: 's1',
    category: { id: 'c1', name: 'Gobar Agarbatti / Dhoop Batti', slug: 'agb', parentId: null },
    onHandQuantity: 20,
    reservedQuantity: 0,
    createdAtUtc: '2026-08-24T00:00:00Z',
    modifiedAtUtc: null,
    package: null,
    ...overrides,
  };
}

function cartLine(overrides: Partial<CartLineDto> = {}): CartLineDto {
  return {
    productId: 'p1',
    sku: 'UPB-AGB-001',
    name: 'Gurushraddha Cow Dung Dhoop Agarbatti',
    quantity: 1,
    unitPrice: 139,
    priceWhenAdded: 139,
    lineTotal: 139,
    availableQuantity: 20,
    problem: null,
    ...overrides,
  };
}

function cartDto(lines: CartLineDto[]): CartDto {
  const ok = lines.filter((line) => line.problem === null);

  return {
    lines,
    subtotal: ok.reduce((total, line) => total + line.lineTotal, 0),
    currency: 'INR',
    itemCount: lines.reduce((total, line) => total + line.quantity, 0),
    canCheckOut: lines.length > 0 && ok.length === lines.length,
  };
}

/**
 * A fresh store with the API and the signed-in user stubbed out.
 *
 * `api.invoke` answers from `server`, keyed by the generated function, so a test says what the
 * Cart API would return without an HttpClient in sight; `user` is flipped to sign in and out.
 */
function fresh() {
  const server = new Map<unknown, (params: unknown) => unknown>();
  const api = {
    invoke: vi.fn(async (fn: unknown, params: unknown) => {
      const handler = server.get(fn);

      if (!handler) {
        throw new Error('unexpected call');
      }

      return handler(params);
    }),
  };
  const permissions = signal<readonly string[]>([]);
  const signedIn = signal(false);
  const user = {
    isSignedIn: signedIn.asReadonly(),
    has: (permission: string) => permissions().includes(permission),
    signIn(granted: readonly string[] = ['cart.read', 'cart.write']) {
      permissions.set(granted);
      signedIn.set(true);
    },
    signOut() {
      permissions.set([]);
      signedIn.set(false);
    },
  };

  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      { provide: Api, useValue: api },
      { provide: CurrentUserStore, useValue: user },
    ],
  });

  return { cart: TestBed.inject(CartStore), api, server, user, toast: TestBed.inject(ToastService) };
}

/** Runs the mode-switch effect and lets the calls it starts settle. */
async function settle(): Promise<void> {
  TestBed.tick();
  await new Promise((resolve) => setTimeout(resolve));
}

describe('CartStore', () => {
  let cart: CartStore;

  beforeEach(() => {
    localStorage.clear();
    cart = fresh().cart;
  });

  it('starts empty', async () => {
    expect(cart.isEmpty()).toBe(true);
    expect(cart.count()).toBe(0);
    expect(cart.subtotal()).toBe(0);
  });

  it('adds a product and totals it', async () => {
    expect(await cart.add(product())).toBe(true);

    expect(cart.count()).toBe(1);
    expect(cart.subtotal()).toBe(139);
    expect(cart.lines()[0].sku).toBe('UPB-AGB-001');
  });

  it('tops up the existing line rather than repeating the product', async () => {
    await cart.add(product());
    await cart.add(product(), 2);

    expect(cart.lines()).toHaveLength(1);
    expect(cart.count()).toBe(3);
    expect(cart.subtotal()).toBe(417);
  });

  it('refuses a product with no price, which would total as free', async () => {
    expect(await cart.add(product({ price: 0 }))).toBe(false);
    expect(cart.isEmpty()).toBe(true);
  });

  it('refuses a non-positive quantity', async () => {
    expect(await cart.add(product(), 0)).toBe(false);
    expect(await cart.add(product(), -3)).toBe(false);
    expect(cart.isEmpty()).toBe(true);
  });

  it('caps a line rather than letting a stuck key run away', async () => {
    await cart.add(product(), MAX_QUANTITY + 50);

    expect(cart.count()).toBe(MAX_QUANTITY);

    await cart.add(product(), 10);

    expect(cart.count()).toBe(MAX_QUANTITY);
  });

  it('removes the line when the quantity reaches zero', async () => {
    await cart.add(product(), 2);
    await cart.setQuantity('p1', 0);

    expect(cart.isEmpty()).toBe(true);
  });

  it('reports how many of one product are held', async () => {
    await cart.add(product(), 4);

    expect(cart.quantityOf('p1')).toBe(4);
    expect(cart.quantityOf('nothing')).toBe(0);
  });

  it('survives a reload', async () => {
    await cart.add(product(), 3);
    await cart.add(product({ id: 'p2', sku: 'UPB-ARK-005', price: 69 }));

    const reloaded = fresh().cart;

    expect(reloaded.count()).toBe(4);
    expect(reloaded.subtotal()).toBe(3 * 139 + 69);
  });

  it('clears the stored basket rather than leaving an empty array behind', async () => {
    await cart.add(product());
    await cart.clear();

    expect(cart.isEmpty()).toBe(true);
    expect(localStorage.getItem('upb.cart')).toBeNull();
  });

  it('drops stored lines that are shaped wrong instead of totalling NaN', async () => {
    localStorage.setItem(
      'upb.cart',
      JSON.stringify([
        { productId: 'good', sku: 'S', name: 'N', price: 100, currency: 'INR', quantity: 2 },
        { productId: 'no price', sku: 'S', name: 'N', currency: 'INR', quantity: 1 },
        { productId: 'zero qty', sku: 'S', name: 'N', price: 5, currency: 'INR', quantity: 0 },
      ])
    );

    const reloaded = fresh().cart;

    expect(reloaded.lines()).toHaveLength(1);
    expect(reloaded.subtotal()).toBe(200);
  });

  it('ignores stored junk', async () => {
    localStorage.setItem('upb.cart', 'not json');

    expect(fresh().cart.isEmpty()).toBe(true);
  });
});

describe('CartStore with an account cart', () => {
  beforeEach(() => localStorage.clear());

  it('loads the account cart at sign-in when there is nothing to merge', async () => {
    const { cart, server, user } = fresh();
    server.set(apiV1CartGet, () => cartDto([cartLine({ quantity: 2, lineTotal: 278 })]));

    user.signIn();
    await settle();

    expect(cart.isAccountCart()).toBe(true);
    expect(cart.count()).toBe(2);
    expect(cart.subtotal()).toBe(278);
  });

  it('merges the guest basket at sign-in and forgets it locally', async () => {
    const { cart, api, server, user, toast } = fresh();
    await cart.add(product(), 3);
    server.set(apiV1CartMergePost, () => ({
      cart: cartDto([cartLine({ quantity: 3, lineTotal: 417 })]),
      skipped: [],
    }));

    user.signIn();
    await settle();

    expect(api.invoke).toHaveBeenCalledWith(apiV1CartMergePost, {
      body: { lines: [{ productId: 'p1', quantity: 3 }] },
    });
    expect(cart.count()).toBe(3);
    expect(localStorage.getItem('upb.cart')).toBeNull();
    expect(toast.toasts()).toHaveLength(0);
  });

  it('says so when the merge had to leave something out', async () => {
    const { cart, server, user, toast } = fresh();
    await cart.add(product());
    server.set(apiV1CartMergePost, () => ({ cart: cartDto([]), skipped: ['p1'] }));

    user.signIn();
    await settle();

    expect(toast.toasts().map((t) => t.message)).toEqual(['cart.mergeSkipped']);
  });

  it('keeps the guest basket when the merge fails, so the next sign-in can retry', async () => {
    const { cart, server, user } = fresh();
    await cart.add(product(), 2);
    server.set(apiV1CartGet, () => cartDto([]));

    user.signIn();
    await settle();

    expect(cart.isEmpty()).toBe(true);
    expect(localStorage.getItem('upb.cart')).not.toBeNull();
  });

  it('stays a guest basket for a user without cart.write, such as staff', async () => {
    const { cart, api, user } = fresh();

    user.signIn(['catalog.read']);
    await settle();

    expect(cart.isAccountCart()).toBe(false);
    expect(api.invoke).not.toHaveBeenCalled();
  });

  it('sends a top-up as the new line quantity and shows what comes back', async () => {
    const { cart, api, server, user } = fresh();
    server.set(apiV1CartGet, () => cartDto([cartLine({ quantity: 2, lineTotal: 278 })]));
    server.set(apiV1CartItemsProductIdPut, () => cartDto([cartLine({ quantity: 5, lineTotal: 695 })]));
    user.signIn();
    await settle();

    expect(await cart.add(product(), 3)).toBe(true);

    expect(api.invoke).toHaveBeenLastCalledWith(apiV1CartItemsProductIdPut, {
      productId: 'p1',
      body: { quantity: 5 },
    });
    expect(cart.count()).toBe(5);
    expect(localStorage.getItem('upb.cart')).toBeNull();
  });

  it('reloads from the server when a change is refused', async () => {
    const { cart, server, user } = fresh();
    server.set(apiV1CartGet, () => cartDto([cartLine({ quantity: 2, lineTotal: 278 })]));
    user.signIn();
    await settle();

    expect(await cart.add(product(), 50)).toBe(false);
    await settle();

    expect(cart.count()).toBe(2);
  });

  it('leaves problem lines out of the subtotal and blocks checkout, as the server does', async () => {
    const { cart, server, user } = fresh();
    server.set(apiV1CartGet, () =>
      cartDto([
        cartLine(),
        cartLine({ productId: 'p2', unitPrice: 80, priceWhenAdded: 69, lineTotal: 80, problem: 'PriceChanged' }),
      ])
    );
    user.signIn();
    await settle();

    expect(cart.subtotal()).toBe(139);
    expect(cart.canCheckOut()).toBe(false);
    expect(cart.hasPriceChanges()).toBe(true);
    expect(cart.lines()[1]).toMatchObject({ price: 80, priceWhenAdded: 69, problem: 'PriceChanged' });

    server.set(apiV1CartAcknowledgePricesPost, () =>
      cartDto([cartLine(), cartLine({ productId: 'p2', unitPrice: 80, priceWhenAdded: 80, lineTotal: 80 })])
    );
    await cart.acknowledgePrices();

    expect(cart.subtotal()).toBe(219);
    expect(cart.canCheckOut()).toBe(true);
  });

  it('says it is loading until the account cart arrives, so checkout does not see it as empty', async () => {
    const { cart, server, user } = fresh();
    let answer: (cart: CartDto) => void = () => undefined;
    server.set(apiV1CartGet, () => new Promise<CartDto>((resolve) => (answer = resolve)));

    user.signIn();
    TestBed.tick();

    expect(cart.isLoading()).toBe(true);

    answer(cartDto([cartLine()]));
    await settle();

    expect(cart.isLoading()).toBe(false);
    expect(cart.count()).toBe(1);
  });

  it('refetches the account cart on request, which is how checkout shows the emptied cart', async () => {
    const { cart, server, user } = fresh();
    server.set(apiV1CartGet, () => cartDto([cartLine()]));
    user.signIn();
    await settle();

    server.set(apiV1CartGet, () => cartDto([]));
    await cart.refresh();

    expect(cart.isEmpty()).toBe(true);
  });

  it('has nothing to refetch for a guest', async () => {
    const { cart, api } = fresh();

    await cart.refresh();

    expect(api.invoke).not.toHaveBeenCalled();
  });

  it('clears the lines at sign-out and ignores a response that lands afterwards', async () => {
    const { cart, server, user } = fresh();
    let answer: (cart: CartDto) => void = () => undefined;
    server.set(apiV1CartGet, () => new Promise<CartDto>((resolve) => (answer = resolve)));

    user.signIn();
    TestBed.tick();
    user.signOut();
    await settle();
    answer(cartDto([cartLine()]));
    await settle();

    expect(cart.isAccountCart()).toBe(false);
    expect(cart.isEmpty()).toBe(true);
  });
});
