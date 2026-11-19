import { expect, test } from '@playwright/test';
import { signInWithToken, stockedTenant, uid, watchErrors } from './helpers';

test('hospital: nurse indents, pharmacist issues, high-alert item needs a second pharmacist', async ({ page, browser, request }) => {
  const done = watchErrors(page);
  page.on('dialog', d => d.accept());
  const { token, product, headers } = await stockedTenant(request); // 50 tabs

  const me = await (await request.get('/api/v1/me', { headers })).json();
  const admin = await (await request.post('/api/v1/platform/auth/login', { data: { email: 'admin@pharmaos.local', password: 'Dev-Only-Admin@123' } })).json();
  expect((await request.put(`/api/v1/platform/tenants/${me.tenantId}/subscription`, {
    headers: { Authorization: `Bearer ${admin.token}` }, data: { planCode: 'hospital', status: 'Active', migration: 'Completed' },
  })).ok()).toBeTruthy();
  const nurseEmail = `nurse-${uid()}@test.local`, pharmEmail = `ph-${uid()}@test.local`;
  await request.post('/api/v1/users', { headers, data: { email: nurseEmail, displayName: 'Nurse Asha', password: 'Password@123', role: 'WardStaff' } });
  await request.post('/api/v1/users', { headers, data: { email: pharmEmail, displayName: 'Pharm Two', password: 'Password@123', role: 'Pharmacist' } });

  // owner: department, assign the nurse, formulary with the item marked high-alert
  await signInWithToken(page, token);
  await page.goto('/hospital/departments');
  await page.getByRole('button', { name: 'Add department' }).click();
  let dlg = page.getByRole('dialog');
  await dlg.getByLabel('Name').fill('ICU');
  await dlg.getByLabel('Code (≤10)').fill('ICU');
  await dlg.getByRole('button', { name: 'Save' }).click();
  await page.getByLabel('Department of Nurse Asha').selectOption({ label: 'ICU' });
  await expect(page.getByRole('cell', { name: 'ICU' }).first()).toBeVisible();

  await page.getByRole('link', { name: 'Formulary' }).click();
  await page.getByLabel('Product').fill(product.name.slice(0, 8));
  await page.locator('.result', { hasText: product.name }).click();
  await page.getByRole('button', { name: 'Add', exact: true }).click();
  await page.getByLabel(`High-alert ${product.name}`).check();
  await expect(page.locator('tbody tr', { hasText: product.name })).toContainText('Required');

  // nurse on a tablet raises an urgent indent
  const tab = await browser.newContext({ viewport: { width: 800, height: 1000 } });
  const np = await tab.newPage();
  const npDone = watchErrors(np);
  await np.goto('/');
  await np.getByLabel('Email').fill(nurseEmail);
  await np.getByLabel('Password').fill('Password@123');
  await np.getByRole('button', { name: /sign in/i }).click();
  await expect(np.getByRole('heading', { name: 'Ward indents' })).toBeVisible();
  await np.getByRole('button', { name: 'New indent' }).click();
  dlg = np.getByRole('dialog');
  await dlg.getByLabel('Priority').selectOption('Urgent');
  await dlg.getByLabel('Add item').fill(product.name.slice(0, 8));
  await np.locator('.result', { hasText: product.name }).click();
  await dlg.getByLabel(`Qty ${product.name}`).fill('12');
  await dlg.getByRole('button', { name: 'Send to pharmacy' }).click();
  await expect(np.locator('.card', { hasText: 'ICU' })).toContainText('0 / 12');

  // owner issues it → waits for a second check
  await page.getByRole('link', { name: 'Ward Indents' }).click();
  await page.locator('.card', { hasText: 'ICU' }).getByRole('button', { name: 'Issue' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Issue' }).click();
  await page.getByRole('link', { name: 'Ward Issues' }).click();
  await expect(page.getByText('Waiting for a second check')).toBeVisible();

  // second pharmacist verifies
  const login = await (await request.post('/api/v1/auth/login', { data: { email: pharmEmail, password: 'Password@123' } })).json();
  const ph = await browser.newPage();
  const phDone = watchErrors(ph);
  ph.on('dialog', d => d.accept());
  await signInWithToken(ph, login.token);
  await ph.goto('/hospital/issues');
  await ph.locator('.card', { hasText: 'ICU' }).click();
  await ph.getByRole('button', { name: 'Verify & issue' }).click();
  await expect(ph.getByRole('dialog')).toContainText('checked by Pharm Two');
  await expect(ph.getByRole('dialog')).toContainText('E2E1');

  // nurse sees the indent fulfilled
  await np.reload();
  await np.getByLabel('Status').selectOption('Closed');
  await expect(np.locator('.card', { hasText: 'ICU' })).toContainText('12 / 12');

  await page.getByRole('link', { name: 'Dept Consumption' }).click();
  await expect(page.locator('tbody tr', { hasText: 'ICU' })).toBeVisible();
  npDone(); phDone(); await tab.close(); await ph.close();
  done();
});
