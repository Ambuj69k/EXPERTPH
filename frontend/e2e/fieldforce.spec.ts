import { expect, test } from '@playwright/test';
import { signInWithToken, stockedTenant, uid, watchErrors } from './helpers';

test('field force: MR logs calls on a phone (one while offline), submits; manager approves', async ({ page, browser, request }) => {
  const done = watchErrors(page);
  page.on('dialog', d => d.accept());
  const { product, headers } = await stockedTenant(request);

  const me = await (await request.get('/api/v1/me', { headers })).json();
  const admin = await (await request.post('/api/v1/platform/auth/login', { data: { email: 'admin@pharmaos.local', password: 'Dev-Only-Admin@123' } })).json();
  expect((await request.put(`/api/v1/platform/tenants/${me.tenantId}/subscription`, {
    headers: { Authorization: `Bearer ${admin.token}` }, data: { planCode: 'fieldforce', status: 'Active', migration: 'Completed' },
  })).ok()).toBeTruthy();

  const repEmail = `mr-${uid()}@test.local`, mgrEmail = `am-${uid()}@test.local`;
  await request.post('/api/v1/users', { headers, data: { email: repEmail, displayName: 'Rahul MR', password: 'Password@123', role: 'MedicalRep' } });
  await request.post('/api/v1/users', { headers, data: { email: mgrEmail, displayName: 'Meera AM', password: 'Password@123', role: 'AreaManager' } });
  const users = await (await request.get('/api/v1/users', { headers })).json();
  const id = (n: string) => users.find((u: { displayName: string }) => u.displayName === n).id;
  const terr = await (await request.post('/api/v1/sfa/territories', { headers, data: { name: 'Andheri', repUserId: id('Rahul MR'), managerUserId: id('Meera AM'), isActive: true } })).json();
  for (const [name, cls] of [['Dr Mehta', 'A'], ['Dr Shah', 'B']])
    expect((await request.post('/api/v1/sfa/doctors', { headers, data: { name, territoryId: terr.id, class: cls, isActive: true } })).ok()).toBeTruthy();
  expect((await request.post('/api/v1/sfa/samples/issue', { headers, data: { repUserId: id('Rahul MR'), productId: product.id, qty: 5 } })).ok()).toBeTruthy();

  // MR on a phone
  const phone = await browser.newContext({ viewport: { width: 390, height: 800 } });
  const sp = await phone.newPage();
  const spDone = watchErrors(sp);
  sp.on('dialog', d => d.accept());
  await sp.goto('/');
  await sp.getByLabel('Email').fill(repEmail);
  await sp.getByLabel('Password').fill('Password@123');
  await sp.getByRole('button', { name: /sign in/i }).click();
  await expect(sp.getByRole('heading', { name: 'My day' })).toBeVisible();

  await sp.getByRole('button', { name: '+ Log a call' }).click();
  let dlg = sp.getByRole('dialog');
  await dlg.getByLabel('Doctor', { exact: true }).selectOption({ label: 'Dr Mehta (A)' });
  await dlg.getByLabel('Products detailed').fill('Paracetamol E2E');
  await dlg.getByLabel(`Samples ${product.name}`).fill('2');
  await dlg.getByRole('button', { name: 'Save call' }).click();
  await expect(sp.locator('.card', { hasText: 'Dr Mehta' })).toContainText(`${product.name} × 2`);

  // network drops: the call is kept on the phone, then sent when back online
  await sp.getByRole('button', { name: '+ Log a call' }).click();
  dlg = sp.getByRole('dialog');
  await dlg.getByLabel('Doctor', { exact: true }).selectOption({ label: 'Dr Shah (B)' });
  await phone.setOffline(true);
  await dlg.getByRole('button', { name: 'Save call' }).click();
  await expect(sp.locator('.card', { hasText: 'Dr Shah' })).toContainText('Saved on phone');
  await phone.setOffline(false);
  await expect(sp.locator('.card', { hasText: 'Dr Shah' })).not.toContainText('Saved on phone');
  await expect(sp.getByText('Calls (2)')).toBeVisible();

  await sp.getByLabel(/Notes for the day/).fill('Covered Andheri west');
  await sp.getByRole('button', { name: 'Submit report' }).click();
  await expect(sp.getByText('Submitted', { exact: true })).toBeVisible();

  // manager approves on desktop
  const login = await (await request.post('/api/v1/auth/login', { data: { email: mgrEmail, password: 'Password@123' } })).json();
  await signInWithToken(page, login.token);
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Daily reports' })).toBeVisible();
  await page.locator('tbody tr', { hasText: 'Rahul MR' }).click();
  const review = page.getByRole('dialog');
  await expect(review).toContainText('Dr Mehta');
  await expect(review).toContainText('Dr Shah');
  await review.getByRole('button', { name: 'Approve' }).click();
  await expect(review).toBeHidden();

  await page.getByRole('link', { name: 'Coverage' }).click();
  await expect(page.locator('tbody tr', { hasText: 'Rahul MR' })).toContainText('2 / 6');
  await page.getByRole('link', { name: 'Samples' }).click();
  await expect(page.locator('tbody tr', { hasText: 'Rahul MR' })).toContainText('3');
  await page.getByRole('link', { name: 'Doctors' }).click();
  await expect(page.getByRole('cell', { name: /Dr Mehta/ })).toBeVisible();
  await page.getByRole('link', { name: 'Tour Plan' }).click();
  await expect(page.getByText('No plans for this month yet.')).toBeVisible();

  spDone(); await phone.close();
  done();
});
