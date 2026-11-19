import { useEffect, useState } from 'react';
import {
  api, ApiError, tradeApi, type Retailer, type RetailerInput, type SettlementMode, type TradeInvoice, type TradeRateRow,
  type TradeReturnReason, type TradeScheme, type User,
} from '../api';
import { useAuth } from '../auth';
import ProductPicker from '../components/ProductPicker';
import { ErrorBanner, Field, Modal, PageHead, StatusBadge, toast, useDebounced, useLoad } from '../ui';
import { dateTime, expiry, money, newId, num, round2, today } from '../util';

const MODES: SettlementMode[] = ['Cash', 'Upi', 'BankTransfer', 'Cheque', 'Card'];

/** Owner may push an invoice through a credit block with a written reason; everyone else sees the block. */
async function withCreditOverride<T>(isOwner: boolean, run: (reason?: string) => Promise<T>): Promise<T | null> {
  try { return await run(); } catch (e) {
    if (!(e instanceof ApiError) || e.code !== 'credit_blocked' || !isOwner) throw e;
    const reason = prompt(`${e.message}\n\nTo bill anyway, type the reason (it is recorded in the audit log):`);
    if (!reason?.trim()) return null;
    return await run(reason.trim());
  }
}

function useRetailers() {
  return useLoad(() => tradeApi.retailers(), []);
}

function RetailerSelect({ value, onChange, retailers }: { value: string; onChange: (id: string) => void; retailers: Retailer[] }) {
  return (
    <select value={value} onChange={e => onChange(e.target.value)} aria-label="Retailer">
      <option value="">Choose retailer…</option>
      {retailers.filter(r => r.isActive).map(r => <option key={r.id} value={r.id}>{r.name}{r.route ? ` · ${r.route}` : ''}{r.licenceExpired ? ' (licence expired)' : ''}</option>)}
    </select>
  );
}

// ---------------- Retailers ----------------

export function RetailersPage() {
  const { can } = useAuth();
  const admin = can('Owner', 'Pharmacist');
  const [q, setQ] = useState('');
  const dq = useDebounced(q);
  const { data, error, reload } = useLoad(() => tradeApi.retailers(dq), [dq]);
  const [edit, setEdit] = useState<Retailer | 'new' | null>(null);
  const [stmt, setStmt] = useState<string | null>(null);
  return (
    <>
      <PageHead title="Retailers" sub="Pharmacies you supply on credit. Licence expiry and credit limits are checked on every invoice.">
        <input placeholder="Search name, route, GSTIN" value={q} onChange={e => setQ(e.target.value)} aria-label="Search retailers" />
        {admin && <button className="primary" onClick={() => setEdit('new')}>Add retailer</button>}
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error} />
        {data?.length === 0 ? <div className="empty">No retailers yet.</div> : (
          <div className="table-wrap">
            <table>
              <thead><tr><th>Retailer</th><th>Route</th><th>GSTIN / State</th><th>Licence till</th><th>Salesman</th><th className="num">Limit</th><th className="num">Outstanding</th><th className="num">Overdue</th><th /></tr></thead>
              <tbody>
                {data?.map(r => (
                  <tr key={r.id} className={r.isActive ? '' : 'muted'}>
                    <td><b>{r.name}</b>{r.phone && <div className="small muted">{r.phone}</div>}</td>
                    <td>{r.route ?? '-'}</td>
                    <td className="small">{r.gstin ?? 'Unregistered'} · {r.stateCode}</td>
                    <td>{r.drugLicenseValidTo ?? '-'} {r.licenceExpired && <span className="badge danger">Expired</span>}</td>
                    <td>{r.salesmanName ?? '-'}</td>
                    <td className="num">{r.creditLimit ? money(r.creditLimit) : 'No limit'}</td>
                    <td className="num">{money(r.outstanding)}</td>
                    <td className="num">{r.overdue > 0 ? <span className="badge danger">{money(r.overdue)}</span> : '-'}</td>
                    <td className="row end">
                      <button className="small" onClick={() => setStmt(r.id)}>Statement</button>
                      {admin && <button className="small" onClick={() => setEdit(r)}>Edit</button>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <p className="small muted">Licence numbers and dates are stored for your records; PharmaOS does not verify them with the drug control department.</p>
      </div>
      {edit && <RetailerForm retailer={edit === 'new' ? null : edit} onClose={saved => { setEdit(null); if (saved) reload(); }} />}
      {stmt && <StatementModal id={stmt} onClose={() => setStmt(null)} />}
    </>
  );
}

function RetailerForm({ retailer, onClose }: { retailer: Retailer | null; onClose: (saved: boolean) => void }) {
  const [f, setF] = useState<RetailerInput>(retailer ? { ...retailer } : {
    name: '', gstin: '', stateCode: '', drugLicenseNo: '', drugLicenseValidTo: '', phone: '', address: '', route: '', creditLimit: 0, creditDays: 30, salesmanUserId: null, isActive: true,
  });
  const [salesmen, setSalesmen] = useState<User[]>([]);
  const [err, setErr] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  useEffect(() => { api.users().then(u => setSalesmen(u.filter(x => x.role === 'Salesman' && x.isActive))).catch(() => {}); }, []);
  const set = (p: Partial<RetailerInput>) => setF({ ...f, ...p });
  const save = async () => {
    setErr(null); setBusy(true);
    try {
      const blank = (s?: string | null) => (s?.trim() ? s.trim() : null);
      await tradeApi.saveRetailer(retailer?.id ?? null, {
        ...f, gstin: blank(f.gstin)?.toUpperCase() ?? null, drugLicenseNo: blank(f.drugLicenseNo), drugLicenseValidTo: blank(f.drugLicenseValidTo),
        phone: blank(f.phone), address: blank(f.address), route: blank(f.route), salesmanUserId: f.salesmanUserId || null, version: retailer?.version ?? null,
      });
      toast('Retailer saved'); onClose(true);
    } catch (e) { setErr(e); } finally { setBusy(false); }
  };
  return (
    <Modal title={retailer ? `Edit ${retailer.name}` : 'Add retailer'} onClose={() => onClose(false)}>
      <ErrorBanner error={err} />
      <div className="grid c2">
        <Field label="Name"><input value={f.name} onChange={e => set({ name: e.target.value })} autoFocus /></Field>
        <Field label="Phone"><input value={f.phone ?? ''} onChange={e => set({ phone: e.target.value })} /></Field>
        <Field label="GSTIN (blank if unregistered)">
          <input value={f.gstin ?? ''} onChange={e => { const g = e.target.value.toUpperCase(); set({ gstin: g, stateCode: g.length >= 2 && /^\d\d/.test(g) ? g.slice(0, 2) : f.stateCode }); }} />
        </Field>
        <Field label="State code (2 digits)"><input value={f.stateCode} maxLength={2} onChange={e => set({ stateCode: e.target.value.replace(/\D/g, '') })} /></Field>
        <Field label="Drug licence no."><input value={f.drugLicenseNo ?? ''} onChange={e => set({ drugLicenseNo: e.target.value })} /></Field>
        <Field label="Licence valid till"><input type="date" value={f.drugLicenseValidTo ?? ''} onChange={e => set({ drugLicenseValidTo: e.target.value })} /></Field>
        <Field label="Route / beat"><input value={f.route ?? ''} onChange={e => set({ route: e.target.value })} /></Field>
        <Field label="Salesman">
          <select value={f.salesmanUserId ?? ''} onChange={e => set({ salesmanUserId: e.target.value || null })}>
            <option value="">None</option>{salesmen.map(s => <option key={s.id} value={s.id}>{s.displayName}</option>)}
          </select>
        </Field>
        <Field label="Credit limit (₹, 0 = no limit)"><input type="number" min={0} value={f.creditLimit} onChange={e => set({ creditLimit: Number(e.target.value) || 0 })} /></Field>
        <Field label="Credit days"><input type="number" min={0} value={f.creditDays} onChange={e => set({ creditDays: Math.max(0, Math.floor(Number(e.target.value) || 0)) })} /></Field>
      </div>
      <Field label="Address"><textarea rows={2} value={f.address ?? ''} onChange={e => set({ address: e.target.value })} /></Field>
      <label className="row"><input type="checkbox" checked={f.isActive} onChange={e => set({ isActive: e.target.checked })} /> Active</label>
      <div className="row end"><button className="primary" disabled={busy || !f.name.trim() || f.stateCode.length !== 2} onClick={save}>Save</button></div>
    </Modal>
  );
}

function StatementModal({ id, onClose }: { id: string; onClose: () => void }) {
  const { data: s, error } = useLoad(() => tradeApi.statement(id), [id]);
  return (
    <Modal title={s ? `Statement — ${s.name}` : 'Statement'} onClose={onClose} wide>
      <ErrorBanner error={error} />
      {s && <div className="grid invoice">
        <div className="row">
          <span>Outstanding <b>{money(s.outstanding)}</b></span>
          <span>Overdue <b className={s.overdue > 0 ? 'danger' : ''}>{money(s.overdue)}</b></span>
          {s.creditLimit > 0 && <span>Limit {money(s.creditLimit)} · available <b>{money(s.availableCredit)}</b></span>}
          <span className="spacer" /><button className="no-print" onClick={() => window.print()}>Print</button>
        </div>
        {s.openInvoices.length > 0 && <>
          <h3>Unpaid invoices</h3>
          <table><thead><tr><th>Invoice</th><th>Date</th><th>Due</th><th className="num">Days late</th><th className="num">Total</th><th className="num">Outstanding</th></tr></thead>
            <tbody>{s.openInvoices.map(d => <tr key={d.id}><td>{d.documentNo}</td><td>{d.date}</td><td>{d.dueDate}</td><td className="num">{d.daysOverdue > 0 ? d.daysOverdue : '-'}</td><td className="num">{money(d.total)}</td><td className="num">{money(d.outstanding)}</td></tr>)}</tbody></table>
        </>}
        <h3>Ledger</h3>
        <table><thead><tr><th>Date</th><th>Type</th><th>No.</th><th className="num">Debit</th><th className="num">Credit</th><th className="num">Balance</th></tr></thead>
          <tbody>{s.lines.map((l, i) => <tr key={i}><td>{l.date}</td><td>{l.type}{l.note && <div className="small muted">{l.note}</div>}</td><td>{l.documentNo}</td><td className="num">{l.debit ? money(l.debit) : ''}</td><td className="num">{l.credit ? money(l.credit) : ''}</td><td className="num">{money(l.balance)}</td></tr>)}</tbody></table>
      </div>}
    </Modal>
  );
}

// ---------------- Rates & schemes ----------------

export function TradeRatesPage() {
  const { can } = useAuth();
  const admin = can('Owner', 'Pharmacist');
  const retailers = useRetailers();
  const [retailerId, setRetailerId] = useState('');
  const [q, setQ] = useState('');
  const dq = useDebounced(q);
  const { data, error, setData } = useLoad(() => tradeApi.rates(dq, retailerId || null), [dq, retailerId]);
  const [err, setErr] = useState<unknown>(null);
  const save = async (row: TradeRateRow, value: string, forRetailer: boolean) => {
    const v = value.trim() === '' ? null : Number(value);
    const current = forRetailer ? row.retailerRate : row.defaultRate;
    if ((v ?? null) === (current ?? null)) return;
    if (v !== null && !(v > 0)) { setErr(new Error('Rate must be a positive number, or blank to remove it.')); return; }
    setErr(null);
    try {
      await tradeApi.setRate(row.productId, forRetailer ? retailerId : null, v);
      setData((data ?? []).map(r => r.productId === row.productId ? { ...r, [forRetailer ? 'retailerRate' : 'defaultRate']: v } : r));
      toast('Rate saved');
    } catch (e) { setErr(e); }
  };
  return (
    <>
      <PageHead title="Trade rates" sub="Rate per pack before GST (PTR). A retailer's own rate wins over the default list.">
        <input placeholder="Search product" value={q} onChange={e => setQ(e.target.value)} aria-label="Search products" />
        <select value={retailerId} onChange={e => setRetailerId(e.target.value)} aria-label="Rates for">
          <option value="">Default list only</option>{retailers.data?.map(r => <option key={r.id} value={r.id}>{r.name}</option>)}
        </select>
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error ?? err} />
        <div className="table-wrap">
          <table>
            <thead><tr><th>Product</th><th className="num">Pack</th><th className="num">GST %</th><th className="num">Default rate</th>{retailerId && <th className="num">This retailer</th>}</tr></thead>
            <tbody>
              {data?.map(r => (
                <tr key={`${r.productId}-${retailerId}`}>
                  <td>{r.productName}</td><td className="num">{r.packSize}</td><td className="num">{r.gstRate}</td>
                  <td className="num">{admin
                    ? <input className="qty" type="number" min={0} step="0.01" defaultValue={r.defaultRate ?? ''} aria-label={`Default rate ${r.productName}`} onBlur={e => save(r, e.target.value, false)} onKeyDown={e => e.key === 'Enter' && (e.target as HTMLInputElement).blur()} />
                    : r.defaultRate != null ? money(r.defaultRate) : '-'}</td>
                  {retailerId && <td className="num">{admin
                    ? <input className="qty" type="number" min={0} step="0.01" defaultValue={r.retailerRate ?? ''} placeholder="default" aria-label={`Retailer rate ${r.productName}`} onBlur={e => save(r, e.target.value, true)} onKeyDown={e => e.key === 'Enter' && (e.target as HTMLInputElement).blur()} />
                    : r.retailerRate != null ? money(r.retailerRate) : '-'}</td>}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <p className="small muted">Leave a box blank to remove that rate. Products without any rate cannot be invoiced to the trade.</p>
      </div>
      <SchemesPanel admin={admin} />
    </>
  );
}

function SchemesPanel({ admin }: { admin: boolean }) {
  const { data, error, reload } = useLoad(() => tradeApi.schemes(), []);
  const [edit, setEdit] = useState<TradeScheme | 'new' | null>(null);
  return (
    <div className="panel">
      <div className="row"><h2 style={{ margin: 0 }}>Schemes</h2><span className="spacer" />{admin && <button onClick={() => setEdit('new')}>Add scheme</button>}</div>
      <ErrorBanner error={error} />
      {data?.length === 0 ? <div className="empty">No schemes. Add "buy 10 get 1" offers or trade discounts here.</div> : (
        <table>
          <thead><tr><th>Product</th><th>Scheme</th><th className="num">Buy + free</th><th className="num">Discount</th><th>Valid</th><th>Status</th>{admin && <th />}</tr></thead>
          <tbody>{data?.map(s => (
            <tr key={s.id}>
              <td>{s.productName}</td><td>{s.name}</td><td className="num">{s.buyQtyPacks > 0 ? `${s.buyQtyPacks} + ${s.freeQtyPacks}` : '-'}</td>
              <td className="num">{s.discountPercent ? `${s.discountPercent}%` : '-'}</td><td className="small">{s.validFrom} → {s.validTo}</td>
              <td>{s.isActive ? <span className="badge ok">Active</span> : <span className="badge">Off</span>}</td>
              {admin && <td><button className="small" onClick={() => setEdit(s)}>Edit</button></td>}
            </tr>
          ))}</tbody>
        </table>
      )}
      {edit && <SchemeForm scheme={edit === 'new' ? null : edit} onClose={saved => { setEdit(null); if (saved) reload(); }} />}
    </div>
  );
}

function SchemeForm({ scheme, onClose }: { scheme: TradeScheme | null; onClose: (saved: boolean) => void }) {
  const in30 = new Date(Date.now() + 30 * 864e5).toISOString().slice(0, 10);
  const [f, setF] = useState(scheme ?? { id: '', productId: '', productName: '', name: '', buyQtyPacks: 10, freeQtyPacks: 1, discountPercent: 0, validFrom: today(), validTo: in30, isActive: true });
  const [err, setErr] = useState<unknown>(null);
  const save = async () => {
    setErr(null);
    try {
      const { id: _id, productName: _n, ...body } = f;
      await tradeApi.saveScheme(scheme?.id ?? null, body);
      toast('Scheme saved'); onClose(true);
    } catch (e) { setErr(e); }
  };
  return (
    <Modal title={scheme ? 'Edit scheme' : 'Add scheme'} onClose={() => onClose(false)}>
      <ErrorBanner error={err} />
      <Field label="Product"><ProductPicker value={f.productId ? { id: f.productId, name: f.productName } : null} onPick={p => setF({ ...f, productId: p.id, productName: p.name })} disabled={!!scheme} /></Field>
      <Field label="Scheme name"><input value={f.name} onChange={e => setF({ ...f, name: e.target.value })} placeholder="e.g. 10+1 Diwali" /></Field>
      <div className="grid c2">
        <Field label="Buy (packs)"><input type="number" min={0} value={f.buyQtyPacks} onChange={e => setF({ ...f, buyQtyPacks: Number(e.target.value) || 0 })} /></Field>
        <Field label="Get free (packs)"><input type="number" min={0} value={f.freeQtyPacks} onChange={e => setF({ ...f, freeQtyPacks: Number(e.target.value) || 0 })} /></Field>
        <Field label="Trade discount %"><input type="number" min={0} max={100} step="0.01" value={f.discountPercent} onChange={e => setF({ ...f, discountPercent: Number(e.target.value) || 0 })} /></Field>
        <span />
        <Field label="Valid from"><input type="date" value={f.validFrom} onChange={e => setF({ ...f, validFrom: e.target.value })} /></Field>
        <Field label="Valid to"><input type="date" value={f.validTo} onChange={e => setF({ ...f, validTo: e.target.value })} /></Field>
      </div>
      <label className="row"><input type="checkbox" checked={f.isActive} onChange={e => setF({ ...f, isActive: e.target.checked })} /> Active</label>
      <p className="small muted">Free packs = whole multiples of "buy" (20 packs on 10+1 → 2 free). Free goods leave stock at zero value.</p>
      <div className="row end"><button className="primary" disabled={!f.productId || !f.name.trim()} onClick={save}>Save</button></div>
    </Modal>
  );
}

// ---------------- Line entry (orders and invoices) ----------------

interface DraftLine { productId: string; productName: string; rate: number | null; qtyPacks: number; discountPercent: number; gstRate: number }

function TradeLines({ retailerId, lines, onChange, withDiscount }: { retailerId: string; lines: DraftLine[]; onChange: (l: DraftLine[]) => void; withDiscount?: boolean }) {
  const [q, setQ] = useState('');
  const dq = useDebounced(q, 200);
  const [hits, setHits] = useState<TradeRateRow[]>([]);
  useEffect(() => {
    if (!dq.trim()) { setHits([]); return; }
    let live = true;
    tradeApi.rates(dq, retailerId || null).then(r => { if (live) setHits(r.slice(0, 10)); }).catch(() => {});
    return () => { live = false; };
  }, [dq, retailerId]);
  const add = (r: TradeRateRow) => {
    setQ(''); setHits([]);
    if (lines.some(l => l.productId === r.productId)) return;
    onChange([...lines, { productId: r.productId, productName: r.productName, rate: r.retailerRate ?? r.defaultRate ?? null, qtyPacks: 1, discountPercent: 0, gstRate: r.gstRate }]);
  };
  const upd = (i: number, p: Partial<DraftLine>) => onChange(lines.map((l, j) => j === i ? { ...l, ...p } : l));
  const est = lines.reduce((s, l) => s + (l.rate ?? 0) * l.qtyPacks * (1 - l.discountPercent / 100) * (1 + l.gstRate / 100), 0);
  return (
    <div className="grid">
      <div style={{ position: 'relative' }}>
        <input placeholder="Add product…" value={q} onChange={e => setQ(e.target.value)} aria-label="Add product" style={{ width: '100%' }} />
        {hits.length > 0 && (
          <div className="results">
            {hits.map(h => (
              <div key={h.productId} className="result" onMouseDown={e => { e.preventDefault(); add(h); }}>
                {h.productName} <span className="small muted">{(h.retailerRate ?? h.defaultRate) != null ? money((h.retailerRate ?? h.defaultRate)!) + ' / pack' : 'no rate set'}</span>
              </div>
            ))}
          </div>
        )}
      </div>
      {lines.length > 0 && (
        <table>
          <thead><tr><th>Product</th><th className="num">Rate</th><th className="num">Packs</th>{withDiscount && <th className="num">Extra disc %</th>}<th /></tr></thead>
          <tbody>{lines.map((l, i) => (
            <tr key={l.productId}>
              <td>{l.productName}{l.rate == null && <div className="small danger">No trade rate set</div>}</td>
              <td className="num">{l.rate != null ? money(l.rate) : '-'}</td>
              <td className="num"><input className="qty" type="number" min={1} inputMode="numeric" value={l.qtyPacks} aria-label={`Packs ${l.productName}`} onChange={e => upd(i, { qtyPacks: Math.max(0, Math.floor(Number(e.target.value) || 0)) })} /></td>
              {withDiscount && <td className="num"><input className="qty" type="number" min={0} max={100} value={l.discountPercent} onChange={e => upd(i, { discountPercent: Math.min(100, Math.max(0, Number(e.target.value) || 0)) })} /></td>}
              <td><button className="link" aria-label={`Remove ${l.productName}`} onClick={() => onChange(lines.filter((_, j) => j !== i))}>✕</button></td>
            </tr>
          ))}</tbody>
        </table>
      )}
      {lines.length > 0 && <div className="small muted">Estimate before schemes: about {money(round2(est))} incl. GST. The invoice applies schemes, batches and exact tax.</div>}
    </div>
  );
}

// ---------------- Orders ----------------

export function TradeOrdersPage() {
  const { can } = useAuth();
  const admin = can('Owner', 'Pharmacist');
  const isOwner = can('Owner');
  const [status, setStatus] = useState('Open');
  const { data, error, reload } = useLoad(() => tradeApi.orders(status || undefined), [status]);
  const [creating, setCreating] = useState(false);
  const [open, setOpen] = useState<string | null>(null);
  const [inv, setInv] = useState<string | null>(null);
  const [err, setErr] = useState<unknown>(null);
  const invoice = async (id: string) => {
    setErr(null);
    try {
      const r = await withCreditOverride(isOwner, reason => tradeApi.invoiceOrder(id, reason));
      if (r) { toast(`Invoice ${r.invoiceNo} created`); setOpen(null); reload(); setInv(r.id); }
    } catch (e) { setErr(e); }
  };
  return (
    <>
      <PageHead title="Sales orders" sub="Orders booked by salesmen or the office. Billing an order picks batches by earliest expiry.">
        <select value={status} onChange={e => setStatus(e.target.value)} aria-label="Status"><option value="">All</option>{['Open', 'Invoiced', 'Cancelled'].map(s => <option key={s}>{s}</option>)}</select>
        <button className="primary" onClick={() => setCreating(true)}>New order</button>
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error ?? err} />
        {data?.length === 0 ? <div className="empty">No orders.</div> : (
          <div className="cards">
            {data?.map(o => (
              <div key={o.id} className="card clickable" onClick={() => setOpen(o.id)}>
                <div className="row"><b>{o.retailerName}</b><span className="spacer" /><StatusBadge status={o.status} /></div>
                <div className="small muted">{o.orderNo} · {dateTime(o.createdAt)}{o.takenBy && ` · ${o.takenBy}`}</div>
                <div className="small">{o.lines.map(l => `${l.productName} × ${num(l.qtyPacks)}`).join(', ')}</div>
              </div>
            ))}
          </div>
        )}
      </div>
      {creating && <OrderForm onClose={saved => { setCreating(false); if (saved) reload(); }} />}
      {open && <OrderDetail id={open} admin={admin} onInvoice={invoice} onClose={() => { setOpen(null); reload(); }} />}
      {inv && <InvoiceModal id={inv} onClose={() => setInv(null)} />}
    </>
  );
}

function OrderForm({ onClose }: { onClose: (saved: boolean) => void }) {
  const retailers = useRetailers();
  const [retailerId, setRetailerId] = useState('');
  const [lines, setLines] = useState<DraftLine[]>([]);
  const [notes, setNotes] = useState('');
  const [err, setErr] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const r = retailers.data?.find(x => x.id === retailerId);
  const save = async () => {
    setErr(null); setBusy(true);
    try {
      const o = await tradeApi.createOrder({ retailerId, notes: notes.trim() || null, lines: lines.filter(l => l.qtyPacks > 0).map(l => ({ productId: l.productId, qtyPacks: l.qtyPacks })) });
      toast(`Order ${o.orderNo} booked`); onClose(true);
    } catch (e) { setErr(e); } finally { setBusy(false); }
  };
  return (
    <Modal title="New order" onClose={() => onClose(false)} wide>
      <ErrorBanner error={retailers.error ?? err} />
      <Field label="Retailer"><RetailerSelect value={retailerId} onChange={setRetailerId} retailers={retailers.data ?? []} /></Field>
      {r && (
        <div className="small">
          Outstanding {money(r.outstanding)}{r.overdue > 0 && <> · <span className="badge danger">overdue {money(r.overdue)}</span></>}
          {r.creditLimit > 0 && <> · limit {money(r.creditLimit)}</>}{r.licenceExpired && <> · <span className="badge danger">licence expired</span></>}
        </div>
      )}
      {retailerId && <TradeLines retailerId={retailerId} lines={lines} onChange={setLines} />}
      <Field label="Notes"><input value={notes} onChange={e => setNotes(e.target.value)} placeholder="Delivery time, special request…" /></Field>
      <div className="row end"><button className="primary" disabled={busy || !retailerId || !lines.some(l => l.qtyPacks > 0)} onClick={save}>Book order</button></div>
    </Modal>
  );
}

function OrderDetail({ id, admin, onInvoice, onClose }: { id: string; admin: boolean; onInvoice: (id: string) => void; onClose: () => void }) {
  const { data: o, error, reload } = useLoad(() => tradeApi.order(id), [id]);
  const [err, setErr] = useState<unknown>(null);
  const cancel = async () => {
    if (!confirm('Cancel this order?')) return;
    try { await tradeApi.cancelOrder(id); toast('Order cancelled'); reload(); } catch (e) { setErr(e); }
  };
  return (
    <Modal title={o ? `Order ${o.orderNo}` : 'Order'} onClose={onClose}>
      <ErrorBanner error={error ?? err} />
      {o && <div className="grid">
        <div className="row"><b>{o.retailerName}</b><StatusBadge status={o.status} /></div>
        <div className="small muted">{dateTime(o.createdAt)}{o.takenBy && ` · booked by ${o.takenBy}`}</div>
        {o.notes && <div className="small">Note: {o.notes}</div>}
        <table>
          <thead><tr><th>Product</th><th className="num">Packs</th><th className="num">In stock</th></tr></thead>
          <tbody>{o.lines.map(l => <tr key={l.productId}><td>{l.productName}</td><td className="num">{num(l.qtyPacks)}</td><td className={`num ${l.availablePacks < l.qtyPacks ? 'danger' : ''}`}>{num(l.availablePacks)}</td></tr>)}</tbody>
        </table>
        {o.status === 'Open' && <div className="row end">
          <button className="danger" onClick={cancel}>Cancel order</button>
          {admin && <button className="primary" onClick={() => onInvoice(o.id)}>Create invoice</button>}
        </div>}
      </div>}
    </Modal>
  );
}

// ---------------- Invoices ----------------

export function TradeInvoicesPage() {
  const { can } = useAuth();
  const admin = can('Owner', 'Pharmacist');
  const retailers = useRetailers();
  const [retailerId, setRetailerId] = useState('');
  const { data, error, reload } = useLoad(() => tradeApi.invoices(retailerId || undefined), [retailerId]);
  const [creating, setCreating] = useState(false);
  const [open, setOpen] = useState<string | null>(null);
  return (
    <>
      <PageHead title="Trade invoices" sub="B2B tax invoices. Other-state retailers are billed IGST; same-state CGST + SGST.">
        <select value={retailerId} onChange={e => setRetailerId(e.target.value)} aria-label="Filter retailer"><option value="">All retailers</option>{retailers.data?.map(r => <option key={r.id} value={r.id}>{r.name}</option>)}</select>
        {admin && <button className="primary" onClick={() => setCreating(true)}>New invoice</button>}
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error} />
        {data?.length === 0 ? <div className="empty">No invoices yet.</div> : (
          <table>
            <thead><tr><th>Invoice</th><th>Date</th><th>Retailer</th><th>Due</th><th className="num">Amount</th><th className="num">Outstanding</th><th>Status</th></tr></thead>
            <tbody>{data?.map(i => (
              <tr key={i.id} className="clickable" onClick={() => setOpen(i.id)}>
                <td><b>{i.invoiceNo}</b></td><td>{i.invoiceDate}</td><td>{i.retailerName}</td>
                <td className={i.outstanding > 0 && i.dueDate < today() ? 'danger' : ''}>{i.dueDate}</td>
                <td className="num">{money(i.netAmount)}</td><td className="num">{i.outstanding > 0 ? money(i.outstanding) : '-'}</td><td><StatusBadge status={i.status} /></td>
              </tr>
            ))}</tbody>
          </table>
        )}
      </div>
      {creating && <InvoiceForm onClose={id => { setCreating(false); reload(); if (id) setOpen(id); }} />}
      {open && <InvoiceModal id={open} onClose={() => { setOpen(null); reload(); }} />}
    </>
  );
}

function InvoiceForm({ onClose }: { onClose: (createdId: string | null) => void }) {
  const { can } = useAuth();
  const retailers = useRetailers();
  const [retailerId, setRetailerId] = useState('');
  const [lines, setLines] = useState<DraftLine[]>([]);
  const [requestId] = useState(newId);
  const [err, setErr] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const save = async () => {
    setErr(null); setBusy(true);
    try {
      const body = { clientRequestId: requestId, retailerId, lines: lines.filter(l => l.qtyPacks > 0).map(l => ({ productId: l.productId, qtyPacks: l.qtyPacks, discountPercent: l.discountPercent })) };
      const r = await withCreditOverride(can('Owner'), reason => tradeApi.createInvoice({ ...body, creditOverrideReason: reason ?? null }));
      if (r) { toast(`Invoice ${r.invoiceNo} created`); onClose(r.id); }
    } catch (e) { setErr(e); } finally { setBusy(false); }
  };
  return (
    <Modal title="New trade invoice" onClose={() => onClose(null)} wide>
      <ErrorBanner error={retailers.error ?? err} />
      <Field label="Retailer"><RetailerSelect value={retailerId} onChange={id => { setRetailerId(id); setLines([]); }} retailers={retailers.data ?? []} /></Field>
      {retailerId && <TradeLines retailerId={retailerId} lines={lines} onChange={setLines} withDiscount />}
      <div className="row end"><button className="primary" disabled={busy || !retailerId || !lines.some(l => l.qtyPacks > 0)} onClick={save}>Post invoice</button></div>
    </Modal>
  );
}

function InvoiceModal({ id, onClose }: { id: string; onClose: () => void }) {
  const { can } = useAuth();
  const admin = can('Owner', 'Pharmacist');
  const { data: inv, error, setData, reload } = useLoad(() => tradeApi.invoice(id), [id]);
  const [credit, setCredit] = useState(false);
  const [err, setErr] = useState<unknown>(null);
  const cancel = async () => {
    const reason = prompt('Reason for cancelling this invoice?');
    if (!reason?.trim()) return;
    try { setData(await tradeApi.cancelInvoice(id, reason.trim())); toast('Invoice cancelled'); } catch (e) { setErr(e); }
  };
  return (
    <Modal title={inv ? `Invoice ${inv.invoiceNo}` : 'Invoice'} onClose={onClose} wide>
      <ErrorBanner error={error ?? err} />
      {inv && <>
        <TradeInvoiceView inv={inv} />
        <div className="row end no-print" style={{ marginTop: 12 }}>
          {admin && inv.status === 'Posted' && <button className="danger" onClick={cancel}>Cancel invoice</button>}
          {admin && inv.status === 'Posted' && <button onClick={() => setCredit(true)}>Credit note (goods return)</button>}
          <button className="primary" onClick={() => window.print()}>Print</button>
        </div>
      </>}
      {credit && inv && <CreditNoteForm inv={inv} onClose={done => { setCredit(false); if (done) reload(); }} />}
    </Modal>
  );
}

/** Printable B2B tax invoice: GSTINs, licence, HSN, batch/expiry, free qty, and IGST or CGST+SGST split. */
export function TradeInvoiceView({ inv }: { inv: TradeInvoice }) {
  const s = inv.seller;
  const half = (n: number) => round2(n / 2);
  return (
    <div className="invoice">
      <div className="row" style={{ alignItems: 'flex-start' }}>
        <div>
          <h2 style={{ margin: 0 }}>{s.businessName}</h2>
          <div className="small">{s.branchName}{s.address && ` · ${s.address}`}{s.phone && ` · ${s.phone}`}</div>
          <div className="small">GSTIN {s.gstin ?? '-'} · DL {s.drugLicenseNo ?? '-'} · State {s.stateCode}</div>
        </div>
        <span className="spacer" />
        <div style={{ textAlign: 'right' }}>
          <b>TAX INVOICE</b>
          <div className="small">{inv.invoiceNo} · {inv.invoiceDate}</div>
          <div className="small">Due {inv.dueDate}</div>
          {inv.status === 'Cancelled' && <div className="badge danger">CANCELLED{inv.cancelReason && `: ${inv.cancelReason}`}</div>}
        </div>
      </div>
      <div className="small" style={{ margin: '8px 0' }}>
        <b>Bill to:</b> {inv.retailerName} · GSTIN {inv.retailerGstin ?? 'Unregistered'} · DL {inv.retailerLicence ?? '-'} · {inv.interState ? 'Inter-state (IGST)' : 'Intra-state (CGST + SGST)'}
      </div>
      <table>
        <thead><tr>
          <th>#</th><th>Product</th><th>HSN</th><th>Batch</th><th>Exp</th><th className="num">Qty</th><th className="num">Free</th><th className="num">Rate</th><th className="num">Disc %</th>
          <th className="num">Taxable</th><th className="num">GST %</th>
          {inv.interState ? <th className="num">IGST</th> : <><th className="num">CGST</th><th className="num">SGST</th></>}
          <th className="num">Total</th>
        </tr></thead>
        <tbody>{inv.lines.map(l => (
          <tr key={l.id}>
            <td>{l.lineNo}</td><td>{l.productName}{l.returnedPacks > 0 && <div className="small muted">returned {num(l.returnedPacks)}</div>}</td><td>{l.hsnCode ?? ''}</td>
            <td>{l.batchNo}</td><td>{expiry(l.expiryDate)}</td><td className="num">{num(l.qtyPacks)}</td><td className="num">{l.freeQtyPacks ? num(l.freeQtyPacks) : ''}</td>
            <td className="num">{num(l.ratePerPack)}</td><td className="num">{l.discountPercent || ''}</td><td className="num">{num(l.taxableAmount)}</td><td className="num">{l.gstRate}</td>
            {inv.interState ? <td className="num">{num(l.taxAmount)}</td> : <><td className="num">{num(half(l.taxAmount))}</td><td className="num">{num(round2(l.taxAmount - half(l.taxAmount)))}</td></>}
            <td className="num">{num(l.lineTotal)}</td>
          </tr>
        ))}</tbody>
      </table>
      <div className="row end" style={{ marginTop: 8 }}>
        <table className="small" style={{ width: 'auto' }}><tbody>
          <tr><td>Gross</td><td className="num">{money(inv.grossAmount)}</td></tr>
          {inv.discountAmount > 0 && <tr><td>Discount</td><td className="num">−{money(inv.discountAmount)}</td></tr>}
          <tr><td>Taxable value</td><td className="num">{money(inv.taxableAmount)}</td></tr>
          {inv.interState ? <tr><td>IGST</td><td className="num">{money(inv.taxAmount)}</td></tr>
            : <><tr><td>CGST</td><td className="num">{money(half(inv.taxAmount))}</td></tr><tr><td>SGST</td><td className="num">{money(round2(inv.taxAmount - half(inv.taxAmount)))}</td></tr></>}
          {inv.roundOff !== 0 && <tr><td>Round off</td><td className="num">{money(inv.roundOff)}</td></tr>}
          <tr><td><b>Invoice total</b></td><td className="num"><b>{money(inv.netAmount)}</b></td></tr>
          {inv.status === 'Posted' && <tr className="no-print"><td>Outstanding</td><td className="num">{money(inv.outstanding)}</td></tr>}
        </tbody></table>
      </div>
      <p className="small muted">Free goods are supplied under scheme at no charge. Goods once sold are returnable only as per agreed terms.</p>
    </div>
  );
}

function CreditNoteForm({ inv, onClose }: { inv: TradeInvoice; onClose: (done: boolean) => void }) {
  const [reason, setReason] = useState<TradeReturnReason>('Saleable');
  const [qty, setQty] = useState<Record<string, number>>({});
  const [notes, setNotes] = useState('');
  const [requestId] = useState(newId);
  const [err, setErr] = useState<unknown>(null);
  const lines = inv.lines.filter(l => (qty[l.id] ?? 0) > 0);
  const save = async () => {
    setErr(null);
    try {
      const cn = await tradeApi.createCreditNote({ clientRequestId: requestId, tradeInvoiceId: inv.id, reason, notes: notes.trim() || null, lines: lines.map(l => ({ tradeInvoiceLineId: l.id, qtyPacks: qty[l.id] })) });
      toast(`Credit note ${cn.creditNoteNo} for ${money(cn.amount)}`); onClose(true);
    } catch (e) { setErr(e); }
  };
  return (
    <Modal title={`Credit note against ${inv.invoiceNo}`} onClose={() => onClose(false)} wide>
      <ErrorBanner error={err} />
      <Field label="Reason">
        <select value={reason} onChange={e => setReason(e.target.value as TradeReturnReason)}>
          <option value="Saleable">Saleable — goods go back to stock</option><option value="Expired">Expired — written off</option>
          <option value="Damaged">Damaged — written off</option><option value="Other">Other — written off</option>
        </select>
      </Field>
      <table>
        <thead><tr><th>Product</th><th>Batch</th><th className="num">Billed</th><th className="num">Already returned</th><th className="num">Return now (paid packs)</th></tr></thead>
        <tbody>{inv.lines.map(l => {
          const max = l.qtyPacks - l.returnedPacks;
          return (
            <tr key={l.id}>
              <td>{l.productName}</td><td>{l.batchNo}</td><td className="num">{num(l.qtyPacks)}</td><td className="num">{num(l.returnedPacks)}</td>
              <td className="num"><input className="qty" type="number" min={0} max={max} disabled={max <= 0} value={qty[l.id] ?? 0} aria-label={`Return ${l.productName}`}
                onChange={e => setQty({ ...qty, [l.id]: Math.min(max, Math.max(0, Math.floor(Number(e.target.value) || 0))) })} /></td>
            </tr>
          );
        })}</tbody>
      </table>
      <Field label="Notes"><input value={notes} onChange={e => setNotes(e.target.value)} /></Field>
      <p className="small muted">Credit is at the billed rate and tax of each line. Scheme free goods are not credited.</p>
      <div className="row end"><button className="primary" disabled={lines.length === 0} onClick={save}>Issue credit note</button></div>
    </Modal>
  );
}

// ---------------- Collections ----------------

export function TradeCollectionsPage() {
  const { can } = useAuth();
  const admin = can('Owner', 'Pharmacist');
  const retailers = useRetailers();
  const { data, error, reload } = useLoad(() => tradeApi.receipts(), []);
  const [f, setF] = useState({ retailerId: '', mode: 'Cash' as SettlementMode, amount: 0, reference: '' });
  const [err, setErr] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const r = retailers.data?.find(x => x.id === f.retailerId);
  const save = async () => {
    setErr(null); setBusy(true);
    try {
      const rc = await tradeApi.createReceipt({ retailerId: f.retailerId, mode: f.mode, amount: f.amount, reference: f.reference.trim() || null });
      toast(`Receipt ${rc.receiptNo}: ${money(rc.amount)}${rc.allocated < rc.amount ? ` (${money(rc.amount - rc.allocated)} kept as advance)` : ''}`);
      setF({ retailerId: '', mode: 'Cash', amount: 0, reference: '' }); reload(); retailers.reload();
    } catch (e) { setErr(e); } finally { setBusy(false); }
  };
  const cancel = async (id: string) => {
    const reason = prompt('Reason for cancelling this receipt?');
    if (!reason?.trim()) return;
    try { await tradeApi.cancelReceipt(id, reason.trim()); toast('Receipt cancelled'); reload(); } catch (e) { setErr(e); }
  };
  return (
    <>
      <PageHead title="Collections" sub="Money received from retailers. It is set against the oldest invoices first." />
      <div className="panel">
        <h2>Record a collection</h2>
        <ErrorBanner error={err} />
        <div className="grid c2">
          <Field label="Retailer"><RetailerSelect value={f.retailerId} onChange={id => setF({ ...f, retailerId: id })} retailers={retailers.data ?? []} /></Field>
          <Field label="Mode"><select value={f.mode} onChange={e => setF({ ...f, mode: e.target.value as SettlementMode })}>{MODES.map(m => <option key={m}>{m}</option>)}</select></Field>
          <Field label="Amount (₹)"><input type="number" min={0} inputMode="decimal" value={f.amount || ''} onChange={e => setF({ ...f, amount: Number(e.target.value) || 0 })} /></Field>
          <Field label="Cheque / UTR no."><input value={f.reference} onChange={e => setF({ ...f, reference: e.target.value })} /></Field>
        </div>
        {r && <div className="small">Outstanding {money(r.outstanding)}{r.overdue > 0 && ` · overdue ${money(r.overdue)}`}</div>}
        <div className="row end"><button className="primary" disabled={busy || !f.retailerId || f.amount <= 0} onClick={save}>Save receipt</button></div>
      </div>
      <div className="panel">
        <ErrorBanner error={error} />
        {data?.length === 0 ? <div className="empty">No collections yet.</div> : (
          <table>
            <thead><tr><th>Receipt</th><th>Date</th><th>Retailer</th><th>Mode</th><th>Collected by</th><th className="num">Amount</th><th>Status</th>{admin && <th />}</tr></thead>
            <tbody>{data?.map(x => (
              <tr key={x.id}>
                <td><b>{x.receiptNo}</b>{x.reference && <div className="small muted">{x.reference}</div>}</td><td>{x.date}</td><td>{x.retailerName}</td><td>{x.mode}</td><td>{x.collectedBy ?? '-'}</td>
                <td className="num">{money(x.amount)}</td><td><StatusBadge status={x.status} /></td>
                {admin && <td>{x.status === 'Posted' && <button className="small danger" onClick={() => cancel(x.id)}>Cancel</button>}</td>}
              </tr>
            ))}</tbody>
          </table>
        )}
      </div>
    </>
  );
}

// ---------------- Reports ----------------

export function TradeReportsPage() {
  const { can } = useAuth();
  const admin = can('Owner', 'Pharmacist');
  const aging = useLoad(() => tradeApi.aging(), []);
  const first = today().slice(0, 8) + '01';
  const [from, setFrom] = useState(first);
  const [to, setTo] = useState(today());
  const sm = useLoad(() => admin ? tradeApi.salesmen(from, to) : Promise.resolve([]), [from, to, admin]);
  return (
    <>
      <PageHead title="Trade reports" sub="Who owes what, and how each salesman is doing." />
      <div className="panel">
        <h2>Receivables aging</h2>
        <ErrorBanner error={aging.error} />
        {aging.data && <>
          <div className="cards">
            {aging.data.buckets.map(b => <div key={b.label} className="card"><div className="small muted">{b.label}</div><b>{money(b.amount)}</b><div className="small muted">{b.count} invoice(s)</div></div>)}
            <div className="card"><div className="small muted">Total outstanding</div><b>{money(aging.data.total)}</b></div>
          </div>
          {aging.data.topOverdue.length > 0 && <>
            <h3>Most overdue retailers</h3>
            <table><thead><tr><th>Retailer</th><th>Route</th><th>Salesman</th><th>Phone</th><th className="num">Overdue</th><th className="num">Outstanding</th></tr></thead>
              <tbody>{aging.data.topOverdue.map(r => <tr key={r.id}><td>{r.name}</td><td>{r.route ?? '-'}</td><td>{r.salesmanName ?? '-'}</td><td>{r.phone ?? '-'}</td><td className="num danger">{money(r.overdue)}</td><td className="num">{money(r.outstanding)}</td></tr>)}</tbody></table>
          </>}
        </>}
      </div>
      {admin && <div className="panel">
        <div className="row"><h2 style={{ margin: 0 }}>Salesman performance</h2><span className="spacer" />
          <Field label="From"><input type="date" value={from} onChange={e => setFrom(e.target.value)} /></Field>
          <Field label="To"><input type="date" value={to} onChange={e => setTo(e.target.value)} /></Field>
        </div>
        <ErrorBanner error={sm.error} />
        <table>
          <thead><tr><th>Salesman</th><th className="num">Orders</th><th className="num">Invoices</th><th className="num">Sales</th><th className="num">Collections</th></tr></thead>
          <tbody>{sm.data?.map(s => <tr key={s.userId ?? 'office'}><td>{s.name}</td><td className="num">{s.orders}</td><td className="num">{s.invoices}</td><td className="num">{money(s.sales)}</td><td className="num">{money(s.collections)}</td></tr>)}</tbody>
        </table>
        <p className="small muted">Sales are credited to the retailer's assigned salesman; collections to whoever recorded them.</p>
      </div>}
    </>
  );
}
