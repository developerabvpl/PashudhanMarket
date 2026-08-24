import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, test } from '@playwright/test';

/**
 * Read from the same file the stub serves, so a re-import of the workbook cannot leave the
 * suite asserting against a product that no longer exists.
 */
const catalog = JSON.parse(
  readFileSync(resolve(__dirname, '../../../tools/data/catalog.json'), 'utf8')
) as {
  categories: { id: string; name: string }[];
  products: { id: string; sku: string; name: string; brand: string | null; categoryId: string }[];
};

const product = catalog.products[0];
const category = catalog.categories.find((c) => c.id === product.categoryId)!;
const categoryCount = catalog.products.filter((p) => p.categoryId === category.id).length;

/** A term that matches some but not all of the catalogue. */
const TERM = 'agarbatti';

/** Counted the way tools/scripts/stub-api.mjs counts: name, sku, brand and category name. */
const termCount = catalog.products.filter((p) =>
  [p.name, p.sku, p.brand ?? '', catalog.categories.find((c) => c.id === p.categoryId)?.name ?? '']
    .join(' ')
    .toLowerCase()
    .includes(TERM)
).length;

test.describe('@smoke storefront catalogue', () => {
  test('lists products and opens one', async ({ page }) => {
    await page.goto('/products');

    await expect(page.getByText(`${catalog.products.length} products`)).toBeVisible();

    await page.getByRole('link', { name: new RegExp(product.sku) }).click();

    await expect(page).toHaveURL(new RegExp(`/products/${product.id}$`));
    await expect(page.getByRole('heading', { level: 1, name: product.name })).toBeVisible();
  });

  test('serves the product in the HTML, not only after hydration', async ({ page }) => {
    // Reading the response body rather than the DOM is the only way to prove the server
    // rendered the content: by the time the DOM settles, hydration has already run.
    const response = await page.goto(`/products/${product.id}`);
    expect(response).not.toBeNull();

    const html = await response!.text();

    expect(html).toContain(product.sku);
    expect(html).toContain('application/ld+json');
  });

  test('sets SEO tags and emits JSON-LD without a price it does not have', async ({ page }) => {
    await page.goto(`/products/${product.id}`);

    await expect(page).toHaveTitle(`${product.name} | UP Bazaar`);
    await expect(page.locator('link[rel="canonical"]')).toHaveAttribute(
      'href',
      new RegExp(`/products/${product.id}$`)
    );

    const parsed = JSON.parse((await page.locator('#upb-jsonld').textContent()) ?? '{}');
    const node = parsed['@graph'].find((n: { '@type': string }) => n['@type'] === 'Product');

    expect(node.sku).toBe(product.sku);
    // The import carries no prices, so there must be no Offer: one would read as "free".
    expect(node.offers).toBeUndefined();
    expect(node.brand).toEqual({ '@type': 'Brand', name: product.brand });
  });

  test('search puts the term in the URL and narrows the list', async ({ page }) => {
    await page.goto('/products');

    await page.getByRole('searchbox').fill(TERM);
    await page.getByRole('button', { name: 'Search products' }).click();

    await expect(page).toHaveURL(new RegExp(`\\?q=${TERM}`));

    await expect(page.getByText(`${termCount} products`)).toBeVisible();
    expect(termCount).toBeLessThan(catalog.products.length);
  });

  test('a category chip filters the catalogue', async ({ page }) => {
    await page.goto('/products');

    await page.getByRole('link', { name: category.name, exact: true }).click();

    await expect(page).toHaveURL(new RegExp(`category=${category.id}`));
    await expect(page.getByText(`${categoryCount} products`)).toBeVisible();
  });

  test('pages through the catalogue', async ({ page }) => {
    await page.goto('/products');

    await expect(page.getByText('Page 1 of 3')).toBeVisible();

    await page.getByRole('link', { name: 'Next' }).click();

    await expect(page).toHaveURL(/page=2/);
    await expect(page.getByText('Page 2 of 3')).toBeVisible();
  });

  test('an unpriced listing says so instead of showing a zero', async ({ page }) => {
    await page.goto(`/products/${product.id}`);

    await expect(page.getByText('Price on request')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Add to cart' })).toBeDisabled();
    await expect(page.getByText('₹0')).toHaveCount(0);
  });

  test('shows an empty state when nothing matches', async ({ page }) => {
    await page.goto('/products?q=nothing');

    await expect(page.getByText('Nothing to show yet.')).toBeVisible();
  });

  test('switches to Hindi', async ({ page }) => {
    await page.goto('/products');

    await page.getByLabel('Language').selectOption('hi');

    await expect(
      page.getByRole('heading', { level: 1, name: /गौशालाओं/ })
    ).toBeVisible();
  });

  test('offers a skip link before anything else in the tab order', async ({ page }) => {
    await page.goto('/products');

    await page.keyboard.press('Tab');

    await expect(page.getByRole('link', { name: 'Skip to main content' })).toBeFocused();
  });
});
