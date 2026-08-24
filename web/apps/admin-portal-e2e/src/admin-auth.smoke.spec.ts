import { expect, test } from '@playwright/test';
import { mockAdminApi, signIn } from './fixtures/admin-mocks';

test.describe('@smoke admin sign-in and staff list', () => {
  test('signs in and lands on the staff directory', async ({ page }) => {
    await mockAdminApi(page);

    await signIn(page);

    await expect(page).toHaveURL(/\/staff$/);
    await expect(page.getByRole('heading', { name: 'Staff users' })).toBeVisible();

    // The table is populated from the server, one page at a time.
    await expect(page.getByRole('table')).toBeVisible();
    await expect(page.getByRole('cell', { name: 'Staff Member 0' })).toBeVisible();

    // And the toolbar shows who is signed in.
    await expect(page.getByRole('button', { name: 'Ops Admin' })).toBeVisible();
  });

  test('an anonymous visitor is sent to sign-in', async ({ page }) => {
    await mockAdminApi(page);

    await page.goto('/staff');

    await expect(page).toHaveURL(/\/sign-in\?returnUrl=%2Fstaff/);
  });

  test('paging asks the server for the next page', async ({ page }) => {
    await mockAdminApi(page, { totalCount: 42 });

    await signIn(page);
    await expect(page.getByRole('table')).toBeVisible();

    const nextRequest = page.waitForRequest(
      (request) => request.url().includes('/api/v1/admin/users?') && request.url().includes('Page=2')
    );

    await page.getByRole('button', { name: 'Next page' }).click();

    await nextRequest;
  });

  test('search is sent to the server and an empty result says so', async ({ page }) => {
    await mockAdminApi(page);

    await signIn(page);
    await expect(page.getByRole('table')).toBeVisible();

    await page.getByLabel('Search by name, email or mobile').fill('nobody');
    await page.getByRole('button', { name: 'Search' }).click();

    await expect(page.getByText('No accounts match this search.')).toBeVisible();
  });

  test('a two-factor account is challenged before it gets a session', async ({ page }) => {
    await mockAdminApi(page, { requiresTwoFactor: true });

    await signIn(page);

    await expect(page).toHaveURL(/\/two-factor$/);
    await expect(page.getByRole('heading', { name: 'Two-factor verification' })).toBeVisible();

    await page.getByLabel('Authenticator code').fill('123456');
    await page.getByRole('button', { name: 'Verify' }).click();

    await expect(page).toHaveURL(/\/staff$/);
  });

  test('write actions are hidden from an account that only holds users.read', async ({ page }) => {
    await mockAdminApi(page, { permissions: ['identity.users.read'] });

    await signIn(page);
    await expect(page.getByRole('table')).toBeVisible();

    // *hasPermission removes the affordances the API would refuse anyway.
    await expect(page.getByRole('button', { name: 'Add staff user' })).toBeHidden();
    await expect(page.getByRole('button', { name: 'Assign roles' })).toHaveCount(0);
  });

  test('changing roles asks for confirmation and names the consequence', async ({ page }) => {
    await mockAdminApi(page);

    await signIn(page);
    await expect(page.getByRole('table')).toBeVisible();

    await page.getByRole('button', { name: 'Assign roles' }).first().click();

    const rolesDialog = page.getByRole('dialog');
    await expect(rolesDialog).toContainText('Roles for Staff Member 0');

    await rolesDialog.getByRole('checkbox', { name: 'SuperAdmin' }).click();
    await rolesDialog.getByRole('button', { name: 'Save' }).click();

    // The confirmation spells out the resulting role set rather than asking "are you sure".
    const confirmDialog = page.getByRole('dialog');
    await expect(confirmDialog).toContainText('Change roles?');
    await expect(confirmDialog).toContainText('SuperAdmin');
    await expect(confirmDialog).toContainText('recorded in the audit log');
  });

  test('signing out returns to the sign-in page', async ({ page }) => {
    await mockAdminApi(page);

    await signIn(page);

    await page.getByRole('button', { name: 'Ops Admin' }).click();
    await page.getByRole('menuitem', { name: 'Sign out' }).click();

    await expect(page).toHaveURL(/\/sign-in$/);
  });
});
