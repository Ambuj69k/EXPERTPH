import { expect, test, type APIRequestContext } from '@playwright/test';
import { signInWithToken, stockedTenant, uid, watchErrors } from './helpers';

async function login(request: APIRequestContext, email: string) {
  return (await (await request.post('/api/v1/auth/login', { data: { email, password: 'Password@123' } })).json()).token as string;
}

test('manufacturing: plan → materials → quarantine → QC signed → QA release with e-signature', async ({ page, browser, request }) => {
  const done = watchErrors(page);
  const { token, product: material, headers } = await stockedTenant(request); // material: 50 units
  const me = await (await request.get('/api/v1/me', { headers })).json();
  const admin = await (await request.post('/api/v1/platform/auth/login', { data: { email: 'admin@pharmaos.local', password: 'Dev-Only-Admin@123' } })).json();
  await request.put(`/api/v1/platform/tenants/${me.tenantId}/subscription`, { headers: { Authorization: `Bearer ${admin.token}` }, data: { planCode: 'manufacturing', status: 'Active', migration: 'Completed' } });

  const fg = await (await request.post('/api/v1/products', { headers, data: { name: `Syrup FG ${uid()}`, gstRate: 12, packSize: 1, packUnit: 'Bottle', baseUnit: 'Bottle', allowLooseSale: false, schedule: 'None', reorderLevelUnits: 0, isActive: true } })).json();
  const qcEmail = `qc-${uid()}@test.local`, qaEmail = `qa-${uid()}@test.local`;
  await request.post('/api/v1/users', { headers, data: { email: qcEmail, displayName: 'Kavya QC', password: 'Password@123', role: 'QcAnalyst' } });
  await request.post('/api/v1/users', { headers, data: { email: qaEmail, displayName: 'Anil QA', password: 'Password@123', role: 'QaManager' } });
  const qaH = { Authorization: `Bearer ${await login(request, qaEmail)}` };
  const recipe = await (await request.post('/api/v1/mfg/recipes', { headers, data: { productId: fg.id, batchSizeUnits: 10, lines: [{ materialProductId: material.id, qtyUnits: 20 }] } })).json();
  expect((await request.post(`/api/v1/mfg/recipes/${recipe.id}/approve`, { headers: qaH, data: { password: 'Password@123' } })).ok()).toBeTruthy();
  const spec = await (await request.post('/api/v1/mfg/specs', { headers, data: { productId: fg.id, tests: [{ name: 'pH', min: 4, max: 6 }, { name: 'Appearance', requirement: 'Clear orange liquid' }] } })).json();
  expect((await request.post(`/api/v1/mfg/specs/${spec.id}/approve`, { headers: qaH, data: { password: 'Password@123' } })).ok()).toBeTruthy();

  // owner acts as production
  await signInWithToken(page, token);
  await page.goto('/mfg/batches');
  await page.getByRole('button', { name: 'Plan batch' }).click();
  let dlg = page.getByRole('dialog');
  await dlg.getByLabel('Recipe').selectOption({ index: 1 });
  await dlg.getByLabel('Batch no.').fill('SY-001');
  await dlg.getByLabel('Expiry').fill(`${new Date().getFullYear() + 2}-06-30`);
  await dlg.getByLabel('MRP per pack').fill('60');
  await dlg.getByRole('button', { name: 'Plan' }).click();
  dlg = page.getByRole('dialog');
  await dlg.getByRole('button', { name: 'Issue materials' }).click();
  await expect(dlg).toContainText('E2E1');
  page.once('dialog', d => d.accept('10'));
  await dlg.getByRole('button', { name: 'Record output' }).click();
  await expect(dlg).toContainText('Quarantine');
  await dlg.getByRole('button', { name: 'Close' }).click();

  // QC analyst logs a sample and signs results
  const qcPage = await browser.newPage();
  const qcDone = watchErrors(qcPage);
  await signInWithToken(qcPage, await login(request, qcEmail));
  await qcPage.goto('/');
  await expect(qcPage.getByRole('heading', { name: 'QC lab' })).toBeVisible();
  await qcPage.getByLabel('Find lot').fill('SY-001');
  await qcPage.getByRole('button', { name: 'Log sample' }).click();
  await qcPage.getByRole('button', { name: 'Enter results' }).click();
  dlg = qcPage.getByRole('dialog');
  await dlg.getByLabel('Result pH').fill('5.1');
  await dlg.getByLabel('Observation Appearance').fill('Clear orange liquid');
  await dlg.getByRole('button', { name: 'Sign results' }).click();
  await qcPage.getByLabel('Your password (e-signature)').fill('Password@123');
  await qcPage.getByRole('button', { name: 'Sign', exact: true }).click();
  await expect(qcPage.locator('tbody tr', { hasText: 'SY-001' })).toContainText('Passed');
  await expect(qcPage.locator('tbody tr', { hasText: 'SY-001' })).toContainText('Tested by Kavya QC');

  // QA releases with an e-signature (wrong password first)
  const qaPage = await browser.newPage();
  const qaDone = watchErrors(qaPage);
  await signInWithToken(qaPage, await login(request, qaEmail));
  await qaPage.goto('/mfg/batches');
  await qaPage.locator('tbody tr', { hasText: 'SY-001' }).click();
  await qaPage.getByRole('button', { name: 'Release' }).click();
  await qaPage.getByLabel('Your password (e-signature)').fill('wrong-password');
  await qaPage.getByRole('button', { name: 'Sign', exact: true }).click();
  await expect(qaPage.getByText('the signature was not applied')).toBeVisible();
  await qaPage.getByLabel('Your password (e-signature)').fill('Password@123');
  await qaPage.getByRole('button', { name: 'Sign', exact: true }).click();
  await expect(qaPage.getByRole('dialog', { name: /^Batch SY-001/ })).toContainText('Released for sale by Anil QA (QA)');

  // genealogy from the owner's screen
  await page.getByRole('link', { name: 'Genealogy' }).click();
  await page.getByLabel('Find lot').fill('SY-001');
  await page.locator('.row.clickable', { hasText: 'SY-001' }).click();
  await expect(page.getByText('Available')).toBeVisible();
  await expect(page.locator('.panel', { hasText: 'Made from' })).toContainText('E2E1');

  qcDone(); qaDone(); await qcPage.close(); await qaPage.close();
  done();
});
