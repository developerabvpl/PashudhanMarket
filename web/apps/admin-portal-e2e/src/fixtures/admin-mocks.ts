import { Page } from '@playwright/test';

export const ADMIN_EMAIL = 'admin@upbazaar.test';
export const ADMIN_PASSWORD = 'admin-test-passphrase';

const TOKENS = {
  accessToken: 'test-access-token',
  expiresInSeconds: 900,
  refreshToken: 'test-refresh-token',
  refreshTokenExpiresAtUtc: '2099-01-01T00:00:00Z',
};

const ROLES = [
  {
    name: 'SuperAdmin',
    description: 'Unrestricted access.',
    permissions: ['identity.users.read', 'identity.users.manage', 'identity.roles.read'],
  },
  {
    name: 'SupportAgent',
    description: 'Answers customer queries.',
    permissions: ['identity.users.read'],
  },
];

function profile(permissions: string[]) {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    userType: 'Staff',
    email: ADMIN_EMAIL,
    emailVerified: true,
    mobile: null,
    mobileVerified: false,
    displayName: 'Ops Admin',
    preferredLanguage: 'en',
    status: 'Active',
    twoFactorEnabled: false,
    roles: ['SuperAdmin'],
    permissions,
    createdAtUtc: '2026-03-14T10:00:00Z',
  };
}

function staffRow(index: number) {
  return {
    id: `3333333${index}-3333-3333-3333-333333333333`,
    userType: 'Staff',
    email: `staff${index}@upbazaar.test`,
    mobile: null,
    displayName: `Staff Member ${index}`,
    status: 'Active',
    roles: index % 2 === 0 ? ['SupportAgent'] : [],
    createdAtUtc: '2026-03-14T10:00:00Z',
  };
}

/**
 * Mocks the admin API in the browser.
 *
 * The admin portal is a plain SPA, so every call is interceptable here and the suite needs
 * neither the .NET API nor a database to prove the screens are wired together.
 */
export async function mockAdminApi(
  page: Page,
  options: { permissions?: string[]; requiresTwoFactor?: boolean; totalCount?: number } = {}
): Promise<void> {
  const permissions = options.permissions
    ?? ['identity.users.read', 'identity.users.manage', 'identity.roles.read'];

  const totalCount = options.totalCount ?? 42;

  await page.route('**/api/v1/auth/login', async (route) => {
    if (options.requiresTwoFactor) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          tokens: null,
          requiresTwoFactor: true,
          twoFactorToken: 'test-2fa-ticket',
        }),
      });

      return;
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ tokens: TOKENS, requiresTwoFactor: false, twoFactorToken: null }),
    });
  });

  await page.route('**/api/v1/auth/verify-2fa', async (route) => {
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
      body: JSON.stringify(profile(permissions)),
    });
  });

  await page.route('**/api/v1/admin/roles', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(ROLES),
    });
  });

  // Query string included, so the paging assertions can read what the screen asked for.
  await page.route('**/api/v1/admin/users?**', async (route) => {
    const url = new URL(route.request().url());
    const pageNumber = Number(url.searchParams.get('Page') ?? 1);
    const pageSize = Number(url.searchParams.get('PageSize') ?? 25);
    const search = url.searchParams.get('Search');

    const items = search === 'nobody'
      ? []
      : Array.from({ length: Math.min(pageSize, 5) }, (_, index) =>
          staffRow(index + ((pageNumber - 1) * pageSize)));

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        items,
        page: pageNumber,
        pageSize,
        totalCount: search === 'nobody' ? 0 : totalCount,
        totalPages: Math.ceil(totalCount / pageSize),
        hasNextPage: pageNumber * pageSize < totalCount,
      }),
    });
  });

  await page.route('**/api/v1/auth/logout', async (route) => {
    await route.fulfill({ status: 204, body: '' });
  });
}

/** Signs in through the real form, so the test exercises the page rather than the store. */
export async function signIn(page: Page): Promise<void> {
  await page.goto('/sign-in');
  await page.getByLabel('Email address').fill(ADMIN_EMAIL);
  await page.getByLabel('Password').fill(ADMIN_PASSWORD);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
}
