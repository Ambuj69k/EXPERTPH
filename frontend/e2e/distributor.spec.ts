import { expect, test } from '@playwright/test';
import { signInWithToken, stockedTenant, uid, watchErrors } from './helpers';

test('distributor: salesman books an order, office bills it (CGST+SGST print), salesman collects', async ({ page, browser, request }) => {
  const done = watchErrors(page);
  page.on('dialog', d => d.accept());
  const { token, product, headers } = await stockedTenant(request); // 5 strips in stock

  // switch the tenant to the distributor plan (platform admin)
  const me = await (await request.get('/api/v1/me', { headers })).json();
  const admin = await (await request.post('/api/v1/platform/auth/login', { data: { email: 'admin@pharmaos.local', password: 'Dev-Only-Admin@123' } })).json();
  expect((await request.put(`/api/v1/platform/tenants/${me.tenantId}/subscription`, {
    headers: { Authorization: `Bearer ${admin.token}` }, data: { planCode: 'distributor', status: 'Active', migration: 'Completed' },
  })).ok()).toBeTruthy();

  const salesmanEmail = `sm-${uid()}@test.local`;
  expect((await request.post('/api/v1/users', { headers, data: { email: salesmanEmail, displayName: 'Ravi Salesman', password: 'Password@123', role: 'Salesman' } })).ok()).toBeTruthy();
  expect((await request.put('/api/v1/distribution/rates', { headers, data: { productId: product.id, retailerId: null, ratePerPack: 60 } })).ok()).toBeTruthy();

  // owner adds the retailer and assigns the salesman
  await signInWithToken(page, token);
  await page.goto('/trade/retailers');
  await page.getByRole('button', { name: 'Add retailer' }).click();
  const form = page.getByRole('dialog');
  await form.getByLabel('Name').fill('Shree Medical');
  await form.getByLabel('GSTIN (blank if unregistered)').fill('27ABCDE1234F1Z5');
  await expect(form.getByLabel('State code (2 digits)')).toHaveValue('27');
  await form.getByLabel('Licence valid till').fill(`${new Date().getFullYear() + 2}-03-31`);
  await form.getByLabel('Salesman').selectOption({ label: 'Ravi Salesman' });
  await form.getByLabel('Credit limit (₹, 0 = no limit)').fill('10000');
  await form.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('cell', { name: 'Shree Medical' })).toBeVisible();

  // salesman signs in on a phone-sized screen and books an order
  const phone = await browser.newContext({ viewport: { width: 390, height: 800 } });
  const sp = await phone.newPage();
  const spDone = watchErrors(sp);
  await sp.goto('/');
  await sp.getByLabel('Email').fill(salesmanEmail);
  await sp.getByLabel('Password').fill('Password@123');
  await sp.getByRole('button', { name: /sign in/i }).click();
  await expect(sp.getByRole('heading', { name: 'Sales orders' })).toBeVisible();
  await sp.getByRole('button', { name: 'New order' }).click();
  const od = sp.getByRole('dialog');
  await od.getByLabel('Retailer').selectOption({ label: 'Shree Medical' });
  await od.getByLabel('Add product').fill(product.name.slice(0, 8));
  await sp.locator('.result', { hasText: product.name }).click();
  await od.getByLabel(`Packs ${product.name}`).fill('3');
  await od.getByRole('button', { name: 'Book order' }).click();
  await expect(sp.locator('.card', { hasText: 'Shree Medical' })).toBeVisible();

  // office bills the order: 3 × ₹60 = 180 + 12% GST = 201.60 → ₹202
  await page.getByRole('link', { name: 'Sales Orders' }).click();
  await page.locator('.card', { hasText: 'Shree Medical' }).click();
  await page.getByRole('button', { name: 'Create invoice' }).click();
  const inv = page.getByRole('dialog');
  await expect(inv).toContainText('TAX INVOICE');
  await expect(inv).toContainText('CGST');
  await expect(inv).toContainText('SGST');
  await expect(inv).toContainText('₹202.00');
  await inv.getByRole('button', { name: 'Close' }).click();

  // salesman records the cash collection
  await sp.getByRole('link', { name: 'Collections' }).click();
  await sp.getByLabel('Retailer').selectOption({ label: 'Shree Medical' });
  await expect(sp.getByText('Outstanding ₹202.00')).toBeVisible();
  await sp.getByLabel('Amount (₹)').fill('202');
  await sp.getByRole('button', { name: 'Save receipt' }).click();
  await expect(sp.getByRole('cell', { name: 'Ravi Salesman' })).toBeVisible();

  await page.getByRole('link', { name: 'Trade Invoices' }).click();
  await expect(page.locator('tbody tr', { hasText: 'Shree Medical' })).toContainText('Posted');
  await expect(page.locator('tbody tr', { hasText: 'Shree Medical' }).locator('td').nth(5)).toHaveText('-'); // fully paid
  await page.getByRole('link', { name: 'Trade Rates & Schemes' }).click();
  await expect(page.getByLabel(`Default rate ${product.name}`)).toHaveValue('60');
  await page.getByRole('link', { name: 'Trade Reports' }).click();
  await expect(page.getByRole('cell', { name: 'Ravi Salesman' })).toBeVisible();
  await expect(page.getByRole('row', { name: /Ravi Salesman/ })).toContainText('₹202.00');
  spDone(); await phone.close();
  done();
});
