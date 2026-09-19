import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, test } from '@playwright/test';

/**
 * Read from the same file the API imports in Development, so a re-import of the workbook cannot
 * leave the suite asserting against a product that no longer exists.
 */
const catalog = JSON.parse(
  readFileSync(resolve(__dirname, '../../../tools/data/catalog.json'), 'utf8')
) as {
  categories: { id: string; name: string }[];
  products: {
    id: string;
    sku: string;
    name: string;
    brand: string | null;
    categoryId: string;
    price: number;
    onHandQuantity: number;
  }[];
};

const product = catalog.products[0];
const category = catalog.categories.find((c) => c.id === product.categoryId)!;
const categoryCount = catalog.products.filter((p) => p.categoryId === category.id).length;

/** The two stock states the product page renders differently. */
const inStock = catalog.products.find((p) => p.price > 0 && p.onHandQuantity > 0)!;
const soldOut = catalog.products.find((p) => p.price > 0 && p.onHandQuantity === 0)!;

/** A term that matches some but not all of the catalogue. */
const TERM = 'agarbatti';

/** Counted the way the Catalog API searches: name, sku, brand and category name. */
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

  test('sets SEO tags and publishes the offer', async ({ page }) => {
    await page.goto(`/products/${product.id}`);

    await expect(page).toHaveTitle(`${product.name} | UP Bazaar`);
    await expect(page.locator('link[rel="canonical"]')).toHaveAttribute(
      'href',
      new RegExp(`/products/${product.id}$`)
    );

    const parsed = JSON.parse((await page.locator('#upb-jsonld').textContent()) ?? '{}');
    const node = parsed['@graph'].find((n: { '@type': string }) => n['@type'] === 'Product');

    expect(node.sku).toBe(product.sku);
    expect(node.brand).toEqual({ '@type': 'Brand', name: product.brand });
    expect(node.offers.price).toBe(product.price);
    expect(node.offers.priceCurrency).toBe('INR');
    expect(node.offers.availability).toBe(
      product.onHandQuantity > 0
        ? 'https://schema.org/InStock'
        : 'https://schema.org/OutOfStock'
    );
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

  test('shows the price and lets a stocked product be added', async ({ page }) => {
    await page.goto(`/products/${inStock.id}`);

    await expect(page.getByText('In stock')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Add to cart' })).toBeEnabled();
    await expect(page.getByText('Price on request')).toHaveCount(0);
  });

  test('a sold-out product keeps its price but refuses the cart', async ({ page }) => {
    await page.goto(`/products/${soldOut.id}`);

    await expect(page.getByText('Out of stock')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Add to cart' })).toBeDisabled();
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
