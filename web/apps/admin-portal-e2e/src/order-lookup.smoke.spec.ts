import { expect, test } from '@playwright/test';
import { mockAdminApi, signIn } from './fixtures/admin-mocks';

const ORDER_ID = '44444444-4444-4444-4444-444444444444';

const order = {
  id: ORDER_ID,
  orderNumber: 'UPB-20260314-ABCD1234',
  customerId: '55555555-5555-5555-5555-555555555555',
  status: 'Paid',
  subtotal: 2000,
  shippingFee: 49,
  total: 2049,
  currency: 'INR',
  placedAtUtc: '2026-03-14T10:00:00Z',
  lines: [
    {
      productId: '11111111-1111-1111-1111-111111111111',
      sku: 'UPB-SAREE-001',
      name: 'Banarasi Silk Saree',
      unitPrice: 1000,
      quantity: 2,
      lineTotal: 2000,
    },
  ],
  paymentId: '66666666-6666-6666-6666-666666666666',
  gatewayOrderId: 'order_test_0001',
};

test.describe('@smoke admin order lookup', () => {
  test.beforeEach(async ({ page }) => {
    // The order screen needs orders.read; users.read comes along so the toolbar renders the
    // same way it does for a real operator.
    await mockAdminApi(page, { permissions: ['orders.read', 'identity.users.read'] });

    await signIn(page);

    // Sign-in lands on /staff, so navigate to the screen under test explicitly.
    await page.goto('/orders');
    await expect(page.getByRole('heading', { name: 'Order lookup' })).toBeVisible();
  });

  test('finds an order and shows its lines', async ({ page }) => {
    await page.route(`**/api/ordering/orders/${ORDER_ID}`, async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(order),
      });
    });

    await page.locator('input[name="orderId"]').fill(ORDER_ID);
    await page.getByRole('button', { name: 'Find order' }).click();

    await expect(page.getByRole('heading', { level: 2 })).toHaveText('UPB-20260314-ABCD1234');
    await expect(page.getByRole('table')).toContainText('UPB-SAREE-001');
    await expect(page.getByRole('table')).toContainText('₹1,000.00');
  });

  test('reports a missing order without looking like a failure', async ({ page }) => {
    await page.route('**/api/ordering/orders/**', async (route) => {
      await route.fulfill({
        status: 404,
        contentType: 'application/problem+json',
        body: JSON.stringify({ title: 'The order does not exist.', status: 404 }),
      });
    });

    await page.locator('input[name="orderId"]').fill(ORDER_ID);
    await page.getByRole('button', { name: 'Find order' }).click();

    await expect(page.getByText('No order with that ID.')).toBeVisible();
  });
});
