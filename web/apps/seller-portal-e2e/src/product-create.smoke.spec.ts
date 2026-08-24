import { expect, test } from '@playwright/test';
import { mockSellerApi, signIn } from './fixtures/seller-mocks';

/** Fills the create-product form with a valid draft. */
async function fillProductForm(page: import('@playwright/test').Page): Promise<void> {
  await page.getByLabel('Product name').fill('Banarasi Silk Saree');
  await page.getByLabel('SKU').fill('UPB-SAREE-001');
  await page.getByLabel('Price').fill('4599');
  await page.getByLabel('Opening stock').fill('25');
  await page.getByLabel('Category').fill('33333333-3333-3333-3333-333333333333');
  await page.getByLabel('Seller ID').fill('22222222-2222-2222-2222-222222222222');
}

test.describe('@smoke seller product creation', () => {
  test('sends an anonymous visitor to sign-in', async ({ page }) => {
    await page.goto('/products');

    await expect(page).toHaveURL(/\/sign-in\?returnUrl=%2Fproducts/);
  });

  test('refuses a seller without the write permission', async ({ page }) => {
    await mockSellerApi(page, { permissions: ['catalog.products.read'] });

    await signIn(page);

    await expect(page).toHaveURL(/\/forbidden/);
    await expect(page.getByText('catalog.products.write')).toBeVisible();
  });

  test('creates a product', async ({ page }) => {
    await mockSellerApi(page);

    await page.route('**/api/catalog/products', async (route) => {
      expect(route.request().method()).toBe('POST');
      expect(route.request().headers()['authorization']).toMatch(/^Bearer /);

      await route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify({ id: 'p1', sku: 'UPB-SAREE-001', name: 'Banarasi Silk Saree' }),
      });
    });

    await signIn(page);
    await expect(page).toHaveURL(/\/products/);

    await fillProductForm(page);
    await page.getByRole('button', { name: 'Save product' }).click();

    // Scoped to the toast region: the page also shows an inline confirmation with the same
    // wording, and an unscoped role=status matches both.
    await expect(
      page.getByRole('region', { name: 'Notifications' }).getByRole('status')
    ).toContainText('Product created as a draft.');
  });

  test('surfaces a field rejection from the API under the field', async ({ page }) => {
    await mockSellerApi(page);

    await page.route('**/api/catalog/products', async (route) => {
      await route.fulfill({
        status: 400,
        contentType: 'application/problem+json',
        body: JSON.stringify({
          title: 'One or more validation errors occurred.',
          status: 400,
          errors: { Sku: ['A product with this SKU already exists.'] },
        }),
      });
    });

    await signIn(page);
    await expect(page).toHaveURL(/\/products/);

    await fillProductForm(page);
    await page.getByRole('button', { name: 'Save product' }).click();

    await expect(page.locator('#sku-errors')).toContainText(
      'A product with this SKU already exists.'
    );
  });
});
