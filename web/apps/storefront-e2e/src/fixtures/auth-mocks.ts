import { Page } from '@playwright/test';

export const OTP_CODE = '123456';

export const BUYER_PROFILE = {
  id: '22222222-2222-2222-2222-222222222222',
  userType: 'Buyer',
  email: null,
  emailVerified: false,
  mobile: '9876543210',
  mobileVerified: true,
  displayName: 'Asha Devi',
  preferredLanguage: 'en',
  status: 'Active',
  twoFactorEnabled: false,
  roles: ['Buyer'],
  permissions: [],
  createdAtUtc: '2026-03-14T10:00:00Z',
};

const TOKENS = {
  accessToken: 'test-access-token',
  expiresInSeconds: 900,
  refreshToken: 'test-refresh-token',
  refreshTokenExpiresAtUtc: '2099-01-01T00:00:00Z',
};

/**
 * Mocks the auth endpoints in the browser.
 *
 * Sign-in on the storefront happens entirely client-side — the routes are client-rendered
 * precisely because they are per-user — so page.route() reaches every call. The catalogue is
 * a different matter and is served by the real stub, because those pages render on the server.
 */
export async function mockAuth(page: Page, options: { wrongCode?: boolean } = {}): Promise<void> {
  let otpRequests = 0;

  await page.route('**/api/v1/auth/request-otp', async (route) => {
    otpRequests++;

    await route.fulfill({ status: 204, body: '' });
  });

  await page.route('**/api/v1/auth/verify-otp', async (route) => {
    const body = route.request().postDataJSON() as { code?: string };

    if (options.wrongCode || body.code !== OTP_CODE) {
      await route.fulfill({
        status: 401,
        contentType: 'application/problem+json',
        body: JSON.stringify({
          type: 'https://upbazaar.dev/errors/identity.otp.invalid',
          title: 'That code is not correct.',
          status: 401,
        }),
      });

      return;
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(TOKENS),
    });
  });

  await page.route('**/api/v1/users/me', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(BUYER_PROFILE),
    });
  });

  await page.route('**/api/v1/auth/logout', async (route) => {
    await route.fulfill({ status: 204, body: '' });
  });

  // Exposed for assertions about resend behaviour.
  await page.exposeFunction('__otpRequestCount', () => otpRequests);
}
