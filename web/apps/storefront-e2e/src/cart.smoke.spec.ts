import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, test } from '@playwright/test';

const catalog = JSON.parse(
  readFileSync(resolve(__dirname, '../../../tools/data/catalog.json'), 'utf8')
) as { products: { id: string; name: string; price: number; onHandQuantity: number }[] };

/** Two different products that can actually be bought, so the basket can hold more than one line. */
const [first, second] = catalog.products.filter((p) => p.price > 0 && p.onHandQuantity > 0);

test.describe('@smoke storefront basket', () => {
  test('adds a product and carries the count in the header', async ({ page }) => {
    await page.goto(`/products/${first.id}`);

    const badge = page.locator('header a[href="/cart"] span');
    await expect(badge).toHaveCount(0);

    await page.getByRole('button', { name: 'Add to cart' }).click();

    await expect(page.getByText('Added to your basket.')).toBeVisible();
    await expect(badge).toHaveText('1');
    await expect(page.getByRole('link', { name: 'Basket, 1 items' })).toBeVisible();

    // Adding the same product again tops up the line rather than starting a second one.
    await page.getByRole('button', { name: 'Add to cart' }).click();
    await expect(badge).toHaveText('2');

    await page.getByRole('link', { name: /Basket/ }).click();

    await expect(page).toHaveURL(/\/cart$/);
    await expect(page.locator('main ul > li')).toHaveCount(1);
    await expect(page.getByText('2 items')).toBeVisible();
  });

  test('holds a line per product and totals them', async ({ page }) => {
    const badge = page.locator('header a[href="/cart"] span');

    for (const [index, product] of [first, second].entries()) {
      await page.goto(`/products/${product.id}`);
      await page.getByRole('button', { name: 'Add to cart' }).click();

      // Wait for the basket to actually take it before navigating away. These pages are
      // server-rendered, so a click can land before hydration and is only applied when event
      // replay runs; leaving immediately would drop it.
      await expect(badge).toHaveText(String(index + 1));
    }

    await page.goto('/cart');

    await expect(page.locator('main ul > li')).toHaveCount(2);
    await expect(page.getByText(`₹${(first.price + second.price).toLocaleString('en-IN')}`)).toBeVisible();
  });

  test('the stepper changes the quantity and remove empties the basket', async ({ page }) => {
    await page.goto(`/products/${first.id}`);
    await page.getByRole('button', { name: 'Add to cart' }).click();
    await expect(page.locator('header a[href="/cart"] span')).toHaveText('1');

    await page.goto('/cart');

    await page.getByRole('button', { name: 'Increase quantity' }).click();
    await expect(page.locator('header a[href="/cart"] span')).toHaveText('2');

    await page.getByRole('button', { name: 'Reduce quantity' }).click();
    await expect(page.locator('header a[href="/cart"] span')).toHaveText('1');

    await page.getByRole('button', { name: 'Remove' }).click();

    await expect(page.getByText('Your basket is empty.')).toBeVisible();
    await expect(page.locator('header a[href="/cart"] span')).toHaveCount(0);
  });

  test('a click that lands before hydration is still honoured', async ({ page }) => {
    // No waiting for the network to settle: click the server-rendered button as early as the
    // browser allows. Angular's event replay is what has to catch it.
    await page.goto(`/products/${first.id}`, { waitUntil: 'commit' });
    await page.getByRole('button', { name: 'Add to cart' }).click();

    await expect(page.locator('header a[href="/cart"] span')).toHaveText('1');
  });

  test('the basket survives a reload', async ({ page }) => {
    await page.goto(`/products/${first.id}`);
    await page.getByRole('button', { name: 'Add to cart' }).click();
    await expect(page.locator('header a[href="/cart"] span')).toHaveText('1');

    await page.reload();

    await expect(page.locator('header a[href="/cart"] span')).toHaveText('1');
  });

  test('an empty basket says so and points back at the catalogue', async ({ page }) => {
    await page.goto('/cart');

    await expect(page.getByText('Your basket is empty.')).toBeVisible();

    await page.getByRole('link', { name: 'Browse products' }).click();

    await expect(page).toHaveURL(/\/products$/);
  });

  test('checkout is visibly unavailable rather than silently broken', async ({ page }) => {
    await page.goto(`/products/${first.id}`);
    await page.getByRole('button', { name: 'Add to cart' }).click();
    await expect(page.locator('header a[href="/cart"] span')).toHaveText('1');

    await page.goto('/cart');

    await expect(page.getByRole('button', { name: 'Proceed to checkout' })).toBeDisabled();
    await expect(page.getByText('Checkout opens once ordering goes live.')).toBeVisible();
  });
});
