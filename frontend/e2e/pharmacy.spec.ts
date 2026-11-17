import { expect, test } from '@playwright/test';
import { signInWithToken, stockedTenant, uid, watchErrors } from './helpers';

test('new pharmacy: register, add product & supplier, post purchase, bill at the counter', async ({ page }) => {
  const done = watchErrors(page);
  page.on('dialog', d => d.accept());
  const name = `Dolo UI ${uid()}`;

  await page.goto('/register');
  await page.getByLabel('Business name').fill('UI Test Medicos');
  await page.getByLabel('GST state code').fill('27');
  await page.getByLabel('Your name').fill('Asha');
  await page.getByLabel('Email').fill(`ui-${uid()}@test.local`);
  await page.getByLabel('Password (min 8)').fill('Password@123');
  await page.getByRole('button', { name: 'Create account' }).click();
  await expect(page.getByRole('heading', { name: 'Billing' })).toBeVisible();

  await page.getByRole('link', { name: 'Products' }).click();
  await page.getByRole('button', { name: '+ New product' }).click();
  await page.getByLabel('Brand name').fill(name);
  await page.getByLabel('Composition / generic').fill('Paracetamol 650mg');
  await page.getByRole('button', { name: 'Save product' }).click();
  await expect(page.getByRole('cell', { name })).toBeVisible();

  await page.getByRole('link', { name: 'Suppliers' }).click();
  await page.getByRole('button', { name: '+ New supplier' }).click();
  await page.getByLabel('Name').fill('UI Distributors');
  await page.getByRole('button', { name: 'Save supplier' }).click();
  await expect(page.getByRole('cell', { name: 'UI Distributors' })).toBeVisible();

  await page.getByRole('link', { name: 'Purchases' }).click();
  await page.getByRole('button', { name: '+ New purchase' }).click();
  await page.getByLabel('Supplier', { exact: true }).selectOption({ label: 'UI Distributors' });
  await page.getByLabel('Supplier invoice no').fill(`UI-${uid()}`);
  const row = page.locator('tbody tr').first();
  await row.getByLabel('Product').fill(name.slice(0, 6));
  await page.locator('.result', { hasText: name }).click();
  const inputs = row.locator('input');
  await inputs.nth(1).fill('B77');          // batch
  await inputs.nth(2).fill('12/30');        // expiry MM/YY
  await inputs.nth(3).fill('30');           // MRP per strip
  await inputs.nth(4).fill('20');           // rate
  await inputs.nth(5).fill('5');            // qty (strips)
  await page.getByRole('button', { name: 'Save & post to stock' }).click();
  await expect(page.getByText('Posted', { exact: true })).toBeVisible();

  await page.getByRole('link', { name: /Billing/ }).click();
  const search = page.getByLabel('Search product');
  await search.fill(name.slice(0, 6));
  await expect(page.locator('.result', { hasText: name })).toBeVisible();
  await search.press('Enter');
  await expect(page.locator('.pos tbody tr', { hasText: name })).toBeVisible();
  await page.getByLabel('Delivery charge').fill('20');
  await page.keyboard.press('F9');
  const bill = page.getByRole('dialog');
  await expect(bill).toContainText('TAX INVOICE');
  await expect(bill).toContainText('Delivery (incl. 18% GST)');
  await expect(bill).toContainText('₹50.00'); // 1 strip at MRP ₹30 + ₹20 delivery
  await bill.getByRole('button', { name: 'New bill' }).click();

  await page.getByRole('link', { name: 'Stock', exact: true }).click();
  await expect(page.getByRole('row', { name: new RegExp(name) })).toContainText('4 Strip');
  done();
});

test('offline: bill while disconnected, then it syncs when back online', async ({ page, context, request }) => {
  const done = watchErrors(page);
  const t = await stockedTenant(request);
  await signInWithToken(page, t.token);
  await page.goto('/pos');
  await expect(page.getByText(/catalogue /)).toBeVisible(); // local snapshot saved

  await context.setOffline(true);
  await expect(page.getByText('offline — bills are saved on this device')).toBeVisible();
  const search = page.getByLabel('Search product');
  await search.fill(t.product.name.slice(0, 8));
  await expect(page.locator('.result', { hasText: t.product.name })).toBeVisible(); // served from the local catalogue
  await search.press('Enter');
  await page.keyboard.press('F9');
  const receipt = page.getByRole('dialog', { name: 'Offline bill saved' });
  await expect(receipt).toContainText('PROVISIONAL RECEIPT');
  await receipt.getByRole('button', { name: 'New bill' }).click();
  await expect(page.getByRole('button', { name: '1 waiting to sync' })).toBeVisible();

  await context.setOffline(false);
  await expect(page.getByRole('button', { name: /waiting to sync/ })).toHaveCount(0, { timeout: 15_000 });
  const sales = await (await request.get('/api/v1/sales', { headers: t.headers })).json();
  expect(sales.items).toHaveLength(1);
  const sale = await (await request.get(`/api/v1/sales/${sales.items[0].id}`, { headers: t.headers })).json();
  expect(sale.capturedOffline).toBe(true);
  done();
});

test('every main screen renders for an owner without errors', async ({ page, request }) => {
  const done = watchErrors(page);
  const t = await stockedTenant(request);
  await signInWithToken(page, t.token);
  const screens: [string, string | RegExp][] = [
    ['/pos', 'Billing'], ['/sales', 'Sales & Returns'], ['/receivables', 'Customer dues'], ['/shift', 'Cash drawer'],
    ['/stock-counts', 'Stock count'], ['/recall', 'Batch recall & quarantine'], ['/reorder', 'Reorder'], ['/purchase-orders', 'Purchase orders'],
    ['/purchases', 'Purchases'], ['/supplier-returns', 'Returns to supplier'], ['/transfers', 'Stock transfers'], ['/indents', 'Indents'],
    ['/stock', 'Stock'], ['/payables', 'Payables'], ['/accounting', 'Accounts'], ['/products', 'Products'], ['/suppliers', 'Suppliers'],
    ['/customers', 'Customers'], ['/reminders', 'Refill reminders'], ['/reports/daily', 'Daily sales'], ['/reports/expiry', 'Near expiry'],
    ['/reports/recovery', 'Return recovery'], ['/reports/retention', 'Customer retention'], ['/reports/branches', 'Branch comparison'],
    ['/prices', 'Branch prices'], ['/users', 'Users'], ['/branches', 'Branches'], ['/settings', 'Settings'], ['/account', 'My account'], ['/support', 'Help & support'],
  ];
  for (const [path, heading] of screens) {
    await page.goto(path);
    await expect(page.getByRole('heading', { level: 1, name: heading }), path).toBeVisible();
  }
  await page.goto('/accounting');
  await expect(page.getByText('matched').first()).toBeVisible(); // control checks ran
  done();
});

test('branch price set in the UI is used at the counter', async ({ page, request }) => {
  const done = watchErrors(page);
  const t = await stockedTenant(request); // MRP ₹100 per strip of 10
  await signInWithToken(page, t.token);
  await page.goto('/prices');
  await page.getByPlaceholder('Search product').fill(t.product.name.slice(0, 10));
  await page.getByLabel(`Price for ${t.product.name}`).fill('90');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('row', { name: new RegExp(t.product.name) })).toContainText('10%');

  await page.goto('/pos');
  const search = page.getByLabel('Search product');
  await search.fill(t.product.name.slice(0, 10));
  await expect(page.locator('.result', { hasText: t.product.name })).toBeVisible();
  await search.press('Enter');
  await expect(page.locator('.totals').first()).toContainText('₹90.00'); // preview uses the same pricing rule as the server
  await page.keyboard.press('F9');
  const bill = page.getByRole('dialog');
  await expect(bill).toContainText('₹90.00');
  await expect(bill).toContainText('-₹10.00'); // saving vs MRP shown on the bill
  done();
});

test('cashier sees only counter screens', async ({ page, request }) => {
  const done = watchErrors(page);
  const t = await stockedTenant(request);
  const email = `cashier-${uid()}@test.local`;
  await request.post('/api/v1/users', { headers: t.headers, data: { email, displayName: 'Cash Counter', password: 'Password@123', role: 'Cashier' } });
  await page.goto('/');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill('Password@123');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: 'Billing' })).toBeVisible();
  const nav = page.getByRole('navigation', { name: 'Main' });
  await expect(nav.getByRole('link', { name: 'Purchases' })).toHaveCount(0);
  await expect(nav.getByRole('link', { name: 'Accounts' })).toHaveCount(0);
  await page.goto('/accounting');
  await expect(page.getByText('You do not have access to this page.')).toBeVisible();
  done();
});

test('provider console sign-in and dashboard', async ({ page }) => {
  const done = watchErrors(page);
  await page.goto('/platform');
  await page.getByLabel('Email').fill('admin@pharmaos.local');
  await page.getByLabel('Password').fill('Dev-Only-Admin@123');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();
  await page.getByRole('link', { name: 'Customers' }).click();
  await expect(page.getByRole('cell', { name: /E2E Pharmacy|UI Test Medicos|Demo Medicos/ }).first()).toBeVisible();
  done();
});
