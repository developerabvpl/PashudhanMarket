import { expect, test } from '@playwright/test';

/** Matches the fixture served by tools/scripts/stub-api.mjs. */
const PRODUCT_ID = '11111111-1111-1111-1111-111111111111';

test.describe('@smoke storefront catalogue', () => {
  test('lists products and opens one', async ({ page }) => {
    await page.goto('/products');

    await expect(page.getByRole('heading', { level: 1, name: 'Products' })).toBeVisible();
    await expect(page.getByRole('link', { name: /Banarasi Silk Saree/ })).toBeVisible();

    await page.getByRole('link', { name: /Banarasi Silk Saree/ }).click();

    await expect(page).toHaveURL(new RegExp(`/products/${PRODUCT_ID}$`));
    await expect(page.getByRole('heading', { level: 1, name: 'Banarasi Silk Saree' })).toBeVisible();
  });

  test('serves the product in the HTML, not only after hydration', async ({ page }) => {
    // Reading the response body rather than the DOM is the only way to prove the server
    // rendered the content: by the time the DOM settles, hydration has already run.
    const response = await page.goto(`/products/${PRODUCT_ID}`);
    expect(response).not.toBeNull();

    const html = await response!.text();

    expect(html).toContain('Banarasi Silk Saree');
    expect(html).toContain('application/ld+json');
  });

  test('sets SEO tags and emits valid JSON-LD', async ({ page }) => {
    await page.goto(`/products/${PRODUCT_ID}`);

    await expect(page).toHaveTitle('Banarasi Silk Saree | UP Bazaar');
    await expect(page.locator('link[rel="canonical"]')).toHaveAttribute(
      'href',
      new RegExp(`/products/${PRODUCT_ID}$`)
    );
    await expect(page.locator('meta[name="description"]')).toHaveAttribute(
      'content',
      /Banarasi Silk Saree/
    );

    const parsed = JSON.parse((await page.locator('#upb-jsonld').textContent()) ?? '{}');
    const product = parsed['@graph'].find((node: { '@type': string }) => node['@type'] === 'Product');

    expect(product.offers.price).toBe(4599);
    expect(product.offers.priceCurrency).toBe('INR');
    expect(product.offers.availability).toBe('https://schema.org/InStock');
  });

  test('search puts the term in the URL and reloads the list', async ({ page }) => {
    await page.goto('/products');

    await page.getByRole('searchbox').fill('saree');
    await page.getByRole('button', { name: 'Search products' }).click();

    await expect(page).toHaveURL(/\?q=saree/);
    await expect(page.getByRole('link', { name: /Banarasi Silk Saree/ })).toBeVisible();
  });

  test('shows an empty state when nothing matches', async ({ page }) => {
    await page.goto('/products?q=nothing');

    await expect(page.getByText('Nothing to show yet.')).toBeVisible();
  });

  test('switches to Hindi', async ({ page }) => {
    await page.goto('/products');

    await page.getByLabel('Language').selectOption('hi');

    await expect(page.getByRole('heading', { level: 1, name: 'उत्पाद' })).toBeVisible();
  });

  test('offers a skip link before anything else in the tab order', async ({ page }) => {
    await page.goto('/products');

    await page.keyboard.press('Tab');

    await expect(page.getByRole('link', { name: 'Skip to main content' })).toBeFocused();
  });
});
