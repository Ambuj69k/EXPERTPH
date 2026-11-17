import { expect, type APIRequestContext, type Page } from '@playwright/test';

export const uid = () => Math.random().toString(36).slice(2, 8);

/** Fail the test on any uncaught browser error (render crashes, bad hooks, etc.). */
export function watchErrors(page: Page) {
  const errors: string[] = [];
  page.on('pageerror', e => errors.push(e.message));
  page.on('console', m => { if (m.type() === 'error' && !/Failed to load resource|ERR_INTERNET_DISCONNECTED|net::/.test(m.text())) errors.push(m.text()); });
  return () => expect(errors, errors.join('\n')).toEqual([]);
}

/** Create a pharmacy with one stocked OTC product through the API (fast setup for UI tests). */
export async function stockedTenant(request: APIRequestContext) {
  const email = `e2e-${uid()}@test.local`;
  const reg = await (await request.post('/api/v1/auth/register', {
    data: { businessName: 'E2E Pharmacy', branchName: 'Main', branchCode: 'MAIN', stateCode: '27', ownerName: 'E2E Owner', email, password: 'Password@123' },
  })).json();
  const headers = { Authorization: `Bearer ${reg.token}` };
  const product = await (await request.post('/api/v1/products', {
    headers, data: { name: `Paracetamol E2E ${uid()}`, composition: 'Paracetamol 500mg', manufacturer: 'Acme', hsnCode: '3004', gstRate: 12, packSize: 10, packUnit: 'Strip', baseUnit: 'Tab', allowLooseSale: true, schedule: 'None', reorderLevelUnits: 0, isActive: true },
  })).json();
  const supplier = await (await request.post('/api/v1/suppliers', { headers, data: { name: `Supplier ${uid()}`, paymentTermsDays: 30, isActive: true } })).json();
  const expiry = new Date(); expiry.setMonth(expiry.getMonth() + 12);
  const draft = await (await request.post('/api/v1/purchases', {
    headers, data: {
      supplierId: supplier.id, supplierInvoiceNo: `E2E-${uid()}`, invoiceDate: new Date().toISOString().slice(0, 10), otherCharges: 0,
      lines: [{ productId: product.id, batchNo: 'E2E1', expiryDate: expiry.toISOString().slice(0, 10), mrp: 100, rate: 70, qtyPacks: 5, freeQtyPacks: 0, discountPercent: 0 }],
    },
  })).json();
  expect((await request.post(`/api/v1/purchases/${draft.id}/post`, { headers, data: {} })).ok()).toBeTruthy();
  return { token: reg.token as string, email, product, headers };
}

export async function signInWithToken(page: Page, token: string) {
  await page.addInitScript(t => localStorage.setItem('pharmaos.token', t), token);
}
