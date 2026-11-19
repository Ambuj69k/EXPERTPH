import { expect, test, type APIRequestContext } from '@playwright/test';
import { signInWithToken, stockedTenant, uid, watchErrors } from './helpers';

async function setPlan(request: APIRequestContext, headers: Record<string, string>, plan: string) {
  const me = await (await request.get('/api/v1/me', { headers })).json();
  const admin = await (await request.post('/api/v1/platform/auth/login', { data: { email: 'admin@pharmaos.local', password: 'Dev-Only-Admin@123' } })).json();
  expect((await request.put(`/api/v1/platform/tenants/${me.tenantId}/subscription`, { headers: { Authorization: `Bearer ${admin.token}` }, data: { planCode: plan, status: 'Active', migration: 'Completed' } })).ok()).toBeTruthy();
}

test('distributor dispatch: pick list, depart, confirm delivery', async ({ page, request }) => {
  const done = watchErrors(page);
  const { token, product, headers } = await stockedTenant(request);
  await setPlan(request, headers, 'distributor');
  await request.put('/api/v1/distribution/rates', { headers, data: { productId: product.id, retailerId: null, ratePerPack: 60 } });
  const r = await (await request.post('/api/v1/distribution/retailers', { headers, data: { name: 'Gamma Chemist', stateCode: '27', route: 'East', creditLimit: 0, creditDays: 30, isActive: true } })).json();
  const inv = await (await request.post('/api/v1/distribution/invoices', { headers, data: { clientRequestId: crypto.randomUUID(), retailerId: r.id, lines: [{ productId: product.id, qtyPacks: 2 }] } })).json();

  await signInWithToken(page, token);
  await page.goto('/trade/dispatch');
  await page.getByLabel(`Select ${inv.invoiceNo}`).check();
  await page.getByLabel('Vehicle no.').fill('mh12xy9999');
  await page.getByRole('button', { name: 'Create trip (1)' }).click();
  const dlg = page.getByRole('dialog');
  await expect(dlg).toContainText('Pick list');
  await expect(dlg).toContainText('E2E1');
  await dlg.getByRole('button', { name: 'Loaded — depart' }).click();
  await expect(dlg).toContainText('On route');
  page.once('dialog', d => d.accept('Shop owner'));
  await dlg.getByRole('button', { name: 'Delivered', exact: true }).click();
  await expect(dlg).toContainText('Completed');
  await expect(dlg).toContainText('by Shop owner');
  done();
});

test('MR expense approved by manager; narcotic register and deviations screens', async ({ page, browser, request }) => {
  const done = watchErrors(page);
  const { token, headers } = await stockedTenant(request);
  await setPlan(request, headers, 'enterprise');
  const repEmail = `mr-${uid()}@test.local`, mgrEmail = `am-${uid()}@test.local`;
  await request.post('/api/v1/users', { headers, data: { email: repEmail, displayName: 'Exp MR', password: 'Password@123', role: 'MedicalRep' } });
  await request.post('/api/v1/users', { headers, data: { email: mgrEmail, displayName: 'Exp AM', password: 'Password@123', role: 'AreaManager' } });
  const users = await (await request.get('/api/v1/users', { headers })).json();
  const id = (n: string) => users.find((u: { displayName: string }) => u.displayName === n).id;
  await request.post('/api/v1/sfa/territories', { headers, data: { name: 'Exp T', repUserId: id('Exp MR'), managerUserId: id('Exp AM'), isActive: true } });

  const rep = await browser.newPage();
  const repDone = watchErrors(rep);
  await signInWithToken(rep, (await (await request.post('/api/v1/auth/login', { data: { email: repEmail, password: 'Password@123' } })).json()).token);
  await rep.goto('/field/expenses');
  await rep.getByLabel('Amount (₹)').fill('320');
  await rep.getByLabel('Km (travel)').fill('64');
  await rep.getByRole('button', { name: 'Claim' }).click();
  await expect(rep.locator('tbody tr')).toContainText('Submitted');

  const mgr = await browser.newPage();
  const mgrDone = watchErrors(mgr);
  await signInWithToken(mgr, (await (await request.post('/api/v1/auth/login', { data: { email: mgrEmail, password: 'Password@123' } })).json()).token);
  await mgr.goto('/field/expenses');
  await mgr.locator('tbody tr', { hasText: 'Exp MR' }).getByRole('button', { name: 'Approve' }).click();
  await expect(mgr.locator('tbody tr', { hasText: 'Exp MR' })).toContainText('Approved');
  await mgr.getByRole('link', { name: 'Targets' }).click();
  await mgr.getByLabel('Calls target Exp MR').fill('40');
  await mgr.getByLabel('Coverage target Exp MR').fill('80');
  await mgr.getByRole('button', { name: 'Save' }).click();
  await expect(mgr.locator('tbody tr', { hasText: 'Exp MR' })).toContainText('0 / 40');
  await expect(mgr.locator('tbody tr', { hasText: 'Exp MR' })).toContainText('₹320.00');

  await signInWithToken(page, token);
  await page.goto('/reports/controlled-drugs');
  await expect(page.getByRole('heading', { name: 'Narcotic & Schedule X register' })).toBeVisible();
  await page.getByRole('link', { name: 'Deviations & CAPA' }).click();
  await page.getByRole('button', { name: 'Raise deviation' }).click();
  const dlg = page.getByRole('dialog');
  await dlg.getByLabel('Title').fill('Temperature excursion');
  await dlg.getByLabel('What happened').fill('Store reached 28°C for 2 hours');
  await dlg.getByLabel('Severity').selectOption('Major');
  await dlg.getByRole('button', { name: 'Raise' }).click();
  await expect(page.locator('tbody tr', { hasText: 'Temperature excursion' })).toContainText('Major');

  repDone(); mgrDone(); await rep.close(); await mgr.close();
  done();
});
