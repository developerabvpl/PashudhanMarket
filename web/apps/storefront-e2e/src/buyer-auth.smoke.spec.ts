import { expect, test } from '@playwright/test';
import { OTP_CODE, mockAuth } from './fixtures/auth-mocks';

const MOBILE = '9876543210';

test.describe('@smoke buyer sign-in', () => {
  test('signs in with a mobile one-time code from the bottom sheet', async ({ page }) => {
    await mockAuth(page);

    await page.goto('/products');

    await page.getByRole('button', { name: 'Sign in', exact: true }).click();

    // Step one: the number.
    const sheet = page.getByRole('dialog');
    await expect(sheet.getByRole('heading', { name: 'Sign in to UP Bazaar' })).toBeVisible();

    await sheet.getByLabel('Mobile number').fill(MOBILE);
    await sheet.getByRole('button', { name: 'Send code' }).click();

    // Step two: the code.
    await expect(sheet.getByText(`We sent a code to +91 ${MOBILE}.`)).toBeVisible();
    await sheet.getByLabel('Enter the code').fill(OTP_CODE);
    await sheet.getByLabel('Your name').fill('Asha Devi');
    await sheet.getByRole('button', { name: 'Verify and sign in' }).click();

    // The sheet closes and the header shows who is signed in.
    await expect(page.getByRole('dialog')).toBeHidden();
    await expect(page.getByRole('button', { name: 'Asha Devi' })).toBeVisible();
  });

  test('the resend button is disabled until the timer runs down', async ({ page }) => {
    await mockAuth(page);

    await page.goto('/sign-in');
    await page.getByLabel('Mobile number').fill(MOBILE);
    await page.getByRole('button', { name: 'Send code' }).click();

    // Counts down rather than allowing an immediate second text at our expense.
    const resend = page.getByRole('button', { name: /Resend in \d+s/ });
    await expect(resend).toBeVisible();
    await expect(resend).toBeDisabled();
  });

  test('a wrong code is reported without leaving the step', async ({ page }) => {
    await mockAuth(page, { wrongCode: true });

    await page.goto('/sign-in');
    await page.getByLabel('Mobile number').fill(MOBILE);
    await page.getByRole('button', { name: 'Send code' }).click();
    await page.getByLabel('Enter the code').fill('000000');
    await page.getByRole('button', { name: 'Verify and sign in' }).click();

    await expect(page.getByRole('alert')).toContainText('That code is not correct.');
    await expect(page.getByLabel('Enter the code')).toBeVisible();
  });

  test('a malformed number never reaches the API', async ({ page }) => {
    await mockAuth(page);

    await page.goto('/sign-in');
    await page.getByLabel('Mobile number').fill('12345');
    await page.getByRole('button', { name: 'Send code' }).click();

    await expect(page.getByText('Enter a valid 10-digit mobile number.')).toBeVisible();
  });

  test('/account is closed to anonymous visitors and open once signed in', async ({ page }) => {
    await mockAuth(page);

    await page.goto('/account');
    await expect(page).toHaveURL(/\/sign-in\?returnUrl=%2Faccount/);

    await page.getByLabel('Mobile number').fill(MOBILE);
    await page.getByRole('button', { name: 'Send code' }).click();
    await page.getByLabel('Enter the code').fill(OTP_CODE);
    await page.getByRole('button', { name: 'Verify and sign in' }).click();

    // The guard's return URL brings them to the page they asked for.
    await expect(page).toHaveURL(/\/account$/);
    await expect(page.getByRole('heading', { name: 'My account' })).toBeVisible();
    await expect(page.getByText(`+91 ${MOBILE}`)).toBeVisible();
  });

  test('signing out clears the header', async ({ page }) => {
    await mockAuth(page);

    await page.goto('/sign-in');
    await page.getByLabel('Mobile number').fill(MOBILE);
    await page.getByRole('button', { name: 'Send code' }).click();
    await page.getByLabel('Enter the code').fill(OTP_CODE);
    await page.getByRole('button', { name: 'Verify and sign in' }).click();

    await page.getByRole('button', { name: 'Asha Devi' }).click();
    await page.getByRole('menuitem', { name: 'Sign out' }).click();

    await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible();
  });
});
