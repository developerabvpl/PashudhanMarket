import { Page } from '@playwright/test';

export const SELLER_EMAIL = 'asha@upbazaar.test';
export const SELLER_PASSWORD = 'seller-test-passphrase';

const TOKENS = {
  accessToken: 'test-access-token',
  expiresInSeconds: 900,
  refreshToken: 'test-refresh-token',
  refreshTokenExpiresAtUtc: '2099-01-01T00:00:00Z',
};

function profile(permissions: string[]) {
  return {
    id: '22222222-2222-2222-2222-222222222222',
    userType: 'Seller',
    email: SELLER_EMAIL,
    emailVerified: true,
    mobile: null,
    mobileVerified: false,
    displayName: 'Asha Devi',
    preferredLanguage: 'en',
    status: 'Active',
    twoFactorEnabled: false,
    roles: ['SellerOwner'],
    permissions,
    createdAtUtc: '2026-03-14T10:00:00Z',
  };
}

/**
 * Mocks the identity calls the seller portal makes on the way in.
 *
 * Permissions come from /users/me rather than the token, so what the guards see is whatever
 * this returns — which is the point: the suite can hand a seller a narrow permission set
 * without minting a JWT the client would then have to be persuaded to trust.
 */
export async function mockSellerApi(
  page: Page,
  options: { permissions?: string[] } = {}
): Promise<void> {
  const permissions = options.permissions ?? ['catalog.products.read', 'catalog.products.write'];

  await page.route('**/api/v1/auth/login', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ tokens: TOKENS, requiresTwoFactor: false, twoFactorToken: null }),
    });
  });

  await page.route('**/api/v1/users/me', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(profile(permissions)),
    });
  });

  await page.route('**/api/v1/auth/logout', async (route) => {
    await route.fulfill({ status: 204, body: '' });
  });
}

/** Signs in through the real form, so the test exercises the page rather than the store. */
export async function signIn(page: Page): Promise<void> {
  await page.goto('/sign-in');
  await page.getByLabel('Email address').fill(SELLER_EMAIL);
  await page.getByLabel('Password').fill(SELLER_PASSWORD);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
}
