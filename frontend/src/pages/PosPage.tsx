import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { api, type CustomerLookup, type PaymentMode, type PosProduct, type Sale } from '../api';
import { useAuth } from '../auth';
import InvoiceView from '../components/InvoiceView';
import { ErrorBanner, Field, Modal, toast, useDebounced } from '../ui';
import { discardFailed, enqueueSale, retryFailed, searchCatalog, useOfflineStatus, type QueuedSale } from '../offline';
import { ApiError } from '../api';
import { dateTime, expiry, money, newId, num, priceLine, round2 } from '../util';

interface CartLine { key: string; product: PosProduct; batchId: string | null; packs: number; loose: number; discount: number }
interface PayRow { key: string; mode: PaymentMode; amount: string; reference: string }
interface Held { id: string; at: string; lines: CartLine[]; customerName: string; customerPhone: string }

const PRESCRIBER_REQUIRED = ['H1', 'X', 'NarcoticPsychotropic'];
const HELD_KEY = 'pharmaos.heldBills';
const loadHeld = (): Held[] => { try { return JSON.parse(localStorage.getItem(HELD_KEY) ?? '[]'); } catch { return []; } };
const saveHeld = (h: Held[]) => { try { localStorage.setItem(HELD_KEY, JSON.stringify(h)); } catch { /* ignore */ } };

const units = (l: CartLine) => l.packs * l.product.packSize + l.loose;
const previewBatch = (l: CartLine) => l.batchId ? l.product.batches.find(b => b.batchId === l.batchId) : l.product.batches[0];
const lineNet = (l: CartLine) => {
  const b = previewBatch(l);
  if (!b) return 0;
  return priceLine(units(l), b.mrp, l.product.packSize, l.product.branchPricePerPack, l.discount).net; // same rule as the server
};

export default function PosPage() {
  const { session } = useAuth();
  const [query, setQuery] = useState('');
  const debounced = useDebounced(query, 200);
  const [results, setResults] = useState<PosProduct[]>([]);
  const [active, setActive] = useState(0);
  const [lines, setLines] = useState<CartLine[]>([]);
  const [customerName, setCustomerName] = useState('');
  const [customerPhone, setCustomerPhone] = useState('');
  const [doctorName, setDoctorName] = useState('');
  const [payments, setPayments] = useState<PayRow[]>([{ key: newId(), mode: 'Cash', amount: '', reference: '' }]);
  const [tendered, setTendered] = useState('');
  const [requestId, setRequestId] = useState(newId);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [done, setDone] = useState<Sale | null>(null);
  const [held, setHeld] = useState<Held[]>(loadHeld);
  const searchRef = useRef<HTMLInputElement>(null);
  const qtyRefs = useRef(new Map<string, HTMLInputElement>());
  const off = useOfflineStatus(session?.branchId, n => toast(`${n} offline bill(s) synced`));
  const [offlineDone, setOfflineDone] = useState<QueuedSale | null>(null);
  const [showFailed, setShowFailed] = useState(false);

  // search online; if the server can't be reached, fall back to the local catalogue snapshot
  const { online, setOnline } = off;
  const branchId = session?.branchId;
  const find = useCallback(async (q: string): Promise<PosProduct[]> => {
    if (!online && branchId) return searchCatalog(branchId, q);
    try { return await api.posSearch(q); }
    catch (e) {
      if (e instanceof ApiError && e.status === 0 && branchId) { setOnline(false); return searchCatalog(branchId, q); }
      return [];
    }
  }, [online, setOnline, branchId]); // stable deps only: a new object here would re-run the search effect every render

  // live search
  useEffect(() => {
    if (!debounced.trim()) { setResults([]); return; }
    let live = true;
    find(debounced).then(r => { if (live) { setResults(r); setActive(0); } });
    return () => { live = false; };
  }, [debounced, find]);

  const subtotal = useMemo(() => round2(lines.reduce((s, l) => s + lineNet(l), 0)), [lines]);
  const [delivery, setDelivery] = useState('');
  const deliveryAmt = Math.max(0, round2(Number(delivery) || 0));
  const total = Math.round(subtotal + deliveryAmt);
  const paid = round2(payments.reduce((s, p) => s + (Number(p.amount) || 0), 0));
  const needsPrescriber = lines.some(l => PRESCRIBER_REQUIRED.includes(l.product.schedule));
  const hasCredit = payments.some(p => p.mode === 'Credit');

  // known customer: fill name, show loyalty points and dues as soon as a full mobile number is typed
  const [known, setKnown] = useState<CustomerLookup | null>(null);
  useEffect(() => {
    const digits = customerPhone.replace(/\D/g, '');
    if (digits.length < 10) { setKnown(null); return; }
    let live = true;
    api.customerLookup(digits).then(c => {
      if (!live) return;
      setKnown(c ?? null);
      if (c) setCustomerName(n => n || c.name);
    }).catch(() => {});
    return () => { live = false; };
  }, [customerPhone]);

  // single payment row follows the bill total automatically
  useEffect(() => {
    setPayments(ps => ps.length === 1 ? [{ ...ps[0], amount: total ? String(total) : '' }] : ps);
  }, [total]);

  const addProduct = useCallback((p: PosProduct) => {
    if (p.availableUnits <= 0) { setError(new Error(`${p.name} is out of stock (non-expired).`)); return; }
    setError(null);
    const key = newId();
    const defaultLoose = p.packSize > 1 && p.availableUnits < p.packSize ? p.availableUnits : 0;
    setLines(ls => [...ls, { key, product: p, batchId: null, packs: defaultLoose ? 0 : 1, loose: defaultLoose, discount: 0 }]);
    setQuery(''); setResults([]);
    setTimeout(() => qtyRefs.current.get(key)?.select(), 0);
  }, []);

  const onSearchKey = async (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown') { e.preventDefault(); setActive(a => Math.min(a + 1, results.length - 1)); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setActive(a => Math.max(a - 1, 0)); }
    else if (e.key === 'Escape') { setQuery(''); setResults([]); }
    else if (e.key === 'Enter') {
      e.preventDefault();
      const q = query.trim();
      if (!q) return;
      // Barcode scanners type fast and press Enter before debounce fires: search immediately.
      const list = debounced === query && results.length ? results : await find(q);
      const pick = list[debounced === query ? active : 0];
      if (pick) addProduct(pick); else setError(new Error(`No product matches "${q}".`));
    }
  };

  const update = (key: string, patch: Partial<CartLine>) => setLines(ls => ls.map(l => l.key === key ? { ...l, ...patch } : l));
  const remove = (key: string) => setLines(ls => ls.filter(l => l.key !== key));

  const reset = useCallback(() => {
    setLines([]); setCustomerName(''); setCustomerPhone(''); setDoctorName(''); setTendered(''); setDelivery('');
    setPayments([{ key: newId(), mode: 'Cash', amount: '', reference: '' }]);
    setRequestId(newId()); setError(null);
    setTimeout(() => searchRef.current?.focus(), 0);
  }, []);

  const save = useCallback(async () => {
    if (busy || lines.length === 0) return;
    setError(null);
    if (paid !== total) { setError(new Error(`Payments (${num(paid)}) must equal the bill total (${num(total)}).`)); return; }
    setBusy(true);
    const req = {
      clientRequestId: requestId, // same id on retry → server returns the same bill instead of billing twice
      customer: customerName || customerPhone ? { name: customerName || 'Walk-in', phone: customerPhone || null } : null,
      doctorName: doctorName || null,
      deliveryCharge: deliveryAmt,
      lines: lines.map(l => ({ productId: l.product.productId, batchId: l.batchId, qtyUnits: units(l), discountPercent: l.discount })),
      payments: payments.filter(p => Number(p.amount) > 0).map(p => ({ mode: p.mode, amount: Number(p.amount), reference: p.reference || null })),
    };
    try {
      if (!off.online) throw new ApiError(0, 'network', 'offline');
      const sale = await api.createSale(req);
      setDone(sale);
      toast(`Saved ${sale.invoiceNo}`);
      reset();
    } catch (err) {
      if (err instanceof ApiError && err.status === 0 && session && !payments.some(p => p.mode === 'Credit' || p.mode === 'Loyalty')) {
        // connection lost: keep the bill locally and sync it later (same clientRequestId → never billed twice)
        off.setOnline(false);
        const q = await enqueueSale(session.branchId, req, { total, items: lines.map(l => `${l.product.name} × ${units(l)}`) });
        setOfflineDone(q);
        await off.refresh();
        reset();
      } else if (err instanceof ApiError && err.status === 0) {
        setError(new Error('Offline: credit and loyalty payments need a connection (balances must be checked). Use cash/UPI or wait.'));
      } else setError(err);
    } finally { setBusy(false); }
  }, [busy, lines, paid, total, requestId, customerName, customerPhone, doctorName, payments, reset, off, session]);

  const hold = useCallback(() => {
    if (!lines.length) return;
    const next = [...held, { id: newId(), at: new Date().toISOString(), lines, customerName, customerPhone }];
    setHeld(next); saveHeld(next); reset(); toast('Bill held');
  }, [lines, held, customerName, customerPhone, reset]);

  const resume = (h: Held) => {
    setLines(h.lines); setCustomerName(h.customerName); setCustomerPhone(h.customerPhone);
    const next = held.filter(x => x.id !== h.id); setHeld(next); saveHeld(next);
  };

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (done) return;
      if (e.key === 'F2') { e.preventDefault(); searchRef.current?.focus(); }
      else if (e.key === 'F9' || (e.ctrlKey && e.key === 'Enter')) { e.preventDefault(); save(); }
      else if (e.key === 'F8') { e.preventDefault(); hold(); }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [save, hold, done]);

  useEffect(() => { searchRef.current?.focus(); }, []);

  const setPay = (key: string, patch: Partial<PayRow>) => setPayments(ps => ps.map(p => p.key === key ? { ...p, ...patch } : p));
  const change = round2((Number(tendered) || 0) - (Number(payments.find(p => p.mode === 'Cash')?.amount) || 0));

  return (
    <>
      <div className="page-head">
        <div><h1>Billing</h1><p>{session?.branchName} · new bill</p></div>
        <div className="row">
          {off.online ? <span className="badge ok">online</span> : <span className="badge danger">offline — bills are saved on this device</span>}
          {off.pending > 0 && <button className="small" onClick={() => off.sync()}>{off.pending} waiting to sync</button>}
          {off.failed.length > 0 && <button className="small danger" onClick={() => setShowFailed(true)}>{off.failed.length} failed to sync</button>}
          {off.catalogAt && <span className="small muted" title="Offline product list">catalogue {dateTime(off.catalogAt)}</span>}
        </div>
        {held.length > 0 && (
          <div className="row">
            <span className="small muted">Held:</span>
            {held.map(h => <button key={h.id} className="small" onClick={() => resume(h)}>{h.customerName || 'Bill'} · {h.lines.length} items</button>)}
          </div>
        )}
      </div>
      <div className="pos">
        <div>
          <div className="pos-search panel" style={{ padding: 10 }}>
            <input ref={searchRef} value={query} onChange={e => setQuery(e.target.value)} onKeyDown={onSearchKey}
              placeholder="Scan barcode or type medicine / composition  (F2)" aria-label="Search product" autoComplete="off" />
            {results.length > 0 && (
              <div className="results" role="listbox">
                {results.map((p, i) => (
                  <div key={p.productId} role="option" aria-selected={i === active} className={`result${i === active ? ' active' : ''}`}
                    onMouseEnter={() => setActive(i)} onMouseDown={e => { e.preventDefault(); addProduct(p); }}>
                    <div>
                      <div><b>{p.name}</b> {p.schedule !== 'None' && <span className="badge warn">{p.schedule}</span>}</div>
                      <div className="meta">{p.composition} {p.manufacturer && `· ${p.manufacturer}`} · {p.packSize} {p.baseUnit}/{p.packUnit}</div>
                    </div>
                    <div style={{ textAlign: 'right' }}>
                      <div className={p.availableUnits > 0 ? '' : 'badge danger'}>{p.availableUnits > 0 ? `${num(p.availableUnits)} ${p.baseUnit}` : 'Out of stock'}</div>
                      {p.batches[0] && <div className="meta">MRP {money(p.batches[0].mrp)} · exp {expiry(p.batches[0].expiryDate)}</div>}
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>

          <div className="panel" style={{ marginTop: 12 }}>
            <ErrorBanner error={error} />
            {lines.length === 0 ? <div className="empty">Scan or search to add medicines.</div> : (
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr><th>Item</th><th>Batch</th><th className="num">Packs</th><th className="num">Loose</th><th className="num">Disc %</th><th className="num">Amount</th><th /></tr>
                  </thead>
                  <tbody>
                    {lines.map(l => {
                      const over = units(l) > (l.batchId ? previewBatch(l)?.qtyUnits ?? 0 : l.product.availableUnits);
                      return (
                        <tr key={l.key}>
                          <td>
                            <b>{l.product.name}</b> {l.product.schedule !== 'None' && <span className="badge warn">{l.product.schedule}</span>}
                            <div className="small muted">{l.product.packSize} {l.product.baseUnit}/{l.product.packUnit} · GST {l.product.gstRate}%</div>
                            {over && <div className="small" style={{ color: 'var(--danger)' }}>Exceeds available stock</div>}
                          </td>
                          <td>
                            <select value={l.batchId ?? ''} onChange={e => update(l.key, { batchId: e.target.value || null })} aria-label="Batch">
                              <option value="">Auto (FEFO)</option>
                              {l.product.batches.map(b => <option key={b.batchId} value={b.batchId}>{b.batchNo} · {expiry(b.expiryDate)} · ₹{b.mrp} · {b.qtyUnits}</option>)}
                            </select>
                          </td>
                          <td className="num">
                            <input className="qty" type="number" min={0} value={l.packs}
                              ref={el => { if (el) qtyRefs.current.set(l.key, el); else qtyRefs.current.delete(l.key); }}
                              onChange={e => update(l.key, { packs: Math.max(0, Math.floor(Number(e.target.value) || 0)) })}
                              onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); searchRef.current?.focus(); } }} aria-label="Packs" />
                          </td>
                          <td className="num">
                            <input className="qty" type="number" min={0} value={l.loose} disabled={!l.product.allowLooseSale || l.product.packSize <= 1}
                              onChange={e => update(l.key, { loose: Math.max(0, Math.floor(Number(e.target.value) || 0)) })}
                              onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); searchRef.current?.focus(); } }} aria-label="Loose units" />
                          </td>
                          <td className="num">
                            <input className="qty" type="number" min={0} max={100} value={l.discount}
                              onChange={e => update(l.key, { discount: Math.min(100, Math.max(0, Number(e.target.value) || 0)) })} aria-label="Discount percent" />
                          </td>
                          <td className="num">{num(lineNet(l))}</td>
                          <td><button className="link" onClick={() => remove(l.key)} aria-label={`Remove ${l.product.name}`}>✕</button></td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
            <div className="keys">
              <span><kbd>F2</kbd> search</span><span><kbd>↑↓</kbd><kbd>Enter</kbd> add</span>
              <span><kbd>F8</kbd> hold</span><span><kbd>F9</kbd> save</span><span><kbd>Esc</kbd> clear search</span>
            </div>
          </div>
        </div>

        <div className="panel grid">
          <h2 style={{ margin: 0 }}>Customer</h2>
          <div className="grid c2">
            <Field label="Mobile"><input value={customerPhone} onChange={e => setCustomerPhone(e.target.value)} inputMode="tel" /></Field>
            <Field label="Name"><input value={customerName} onChange={e => setCustomerName(e.target.value)} /></Field>
          </div>
          {known && (
            <div className="small row" style={{ gap: 6 }}>
              <span className="badge brand">Returning customer</span>
              {known.loyaltyPoints !== 0 && <span className="badge ok">{known.loyaltyPoints} pts = {money(known.loyaltyValue)}</span>}
              {known.balance > 0 && <span className="badge warn">Dues {money(known.balance)}</span>}
            </div>
          )}
          {(needsPrescriber || doctorName) && (
            <Field label={needsPrescriber ? 'Prescriber (required)' : 'Prescriber'}>
              <input value={doctorName} onChange={e => setDoctorName(e.target.value)} />
            </Field>
          )}
          {needsPrescriber && <div className="alert warn small" style={{ margin: 0 }}>Scheduled drug on bill: patient and prescriber details are required.</div>}
          {hasCredit && !customerPhone && <div className="alert warn small" style={{ margin: 0 }}>Credit sale needs customer mobile.</div>}

          <div className="totals">
            <span>Items</span><span>{lines.length}</span>
            <span>Subtotal</span><span>{money(subtotal)}</span>
            <span>Delivery charge</span><span><input className="short" type="number" min={0} placeholder="0" value={delivery} onChange={e => setDelivery(e.target.value)} aria-label="Delivery charge" /></span>
            <span>Round off</span><span>{money(round2(total - subtotal))}</span>
            <span className="grand">Total</span><span className="grand">{money(total)}</span>
          </div>
          <p className="small muted" style={{ margin: 0 }}>Preview; the server recalculates on save (FEFO may split batches).</p>

          <h3 style={{ margin: '4px 0 0' }}>Payment</h3>
          {payments.map(p => (
            <div className="row" key={p.key}>
              <select value={p.mode} onChange={e => setPay(p.key, { mode: e.target.value as PaymentMode })} aria-label="Payment mode">
                <option>Cash</option><option>Upi</option><option>Card</option><option>Credit</option>
                {(p.mode === 'Loyalty' || (known && known.loyaltyPoints > 0)) && <option value="Loyalty">Loyalty points</option>}
              </select>
              <input className="short" type="number" value={p.amount} onChange={e => setPay(p.key, { amount: e.target.value })} aria-label="Amount" />
              {(p.mode === 'Upi' || p.mode === 'Card') && (
                <input style={{ flex: 1, minWidth: 80 }} placeholder="Ref / UTR" value={p.reference} onChange={e => setPay(p.key, { reference: e.target.value })} />
              )}
              {payments.length > 1 && <button className="link" onClick={() => setPayments(ps => ps.filter(x => x.key !== p.key))} aria-label="Remove payment">✕</button>}
            </div>
          ))}
          <div className="row">
            <button className="small" onClick={() => setPayments(ps => [...ps, { key: newId(), mode: 'Upi', amount: String(Math.max(0, round2(total - paid))), reference: '' }])}>+ Split payment</button>
            <span className="spacer" />
            <span className={paid === total ? 'small' : 'small'} style={{ color: paid === total ? 'var(--ok)' : 'var(--danger)' }}>
              {paid === total ? 'Balanced' : `Remaining ${num(round2(total - paid))}`}
            </span>
          </div>
          {payments.some(p => p.mode === 'Cash') && (
            <div className="row">
              <Field label="Cash tendered"><input className="short" type="number" value={tendered} onChange={e => setTendered(e.target.value)} /></Field>
              {Number(tendered) > 0 && <div style={{ fontSize: 16 }}>Return: <b>{money(Math.max(0, change))}</b></div>}
            </div>
          )}
          <p className="small muted" style={{ margin: 0 }}>UPI/card amounts are recorded as entered — confirm the payment on your terminal/app before saving.</p>
          <div className="row">
            <button onClick={hold} disabled={!lines.length}>Hold (F8)</button>
            <button onClick={reset} disabled={!lines.length || busy}>Clear</button>
            <span className="spacer" />
            <button className="primary" onClick={save} disabled={busy || !lines.length}>{busy ? 'Saving…' : 'Save bill (F9)'}</button>
          </div>
        </div>
      </div>

      {offlineDone && (
        <Modal title="Offline bill saved" onClose={() => { setOfflineDone(null); searchRef.current?.focus(); }}>
          <div className="invoice grid">
            <div className="alert warn" style={{ margin: 0 }}>PROVISIONAL RECEIPT — ref <b>{offlineDone.request.offlineRef}</b>. The tax invoice number is assigned when this device is back online; reprint the final bill from Sales.</div>
            <div className="small">{dateTime(offlineDone.request.capturedAt)}</div>
            {offlineDone.display.items.map((i, n) => <div key={n}>{i}</div>)}
            <div className="totals"><span className="grand">Total</span><span className="grand">{money(offlineDone.display.total)}</span></div>
          </div>
          <div className="row end no-print" style={{ marginTop: 12 }}><button onClick={() => window.print()}>Print</button><button className="primary" autoFocus onClick={() => setOfflineDone(null)}>New bill</button></div>
        </Modal>
      )}
      {showFailed && (
        <Modal title="Bills that could not sync" onClose={() => setShowFailed(false)} wide>
          <p className="small muted">These were made offline but the server refused them (for example, the stock was sold elsewhere meanwhile). Fix the cause and retry, or discard and re-bill.</p>
          <table className="small"><tbody>{off.failed.map((f, i) => (
            <tr key={f.request.clientRequestId}><td>{f.request.offlineRef}<div className="muted">{dateTime(f.request.capturedAt)}</div></td>
              <td>{f.display.items.join(', ')}</td><td className="num">{money(f.display.total)}</td><td style={{ color: 'var(--danger)' }}>{f.error}</td>
              <td className="row" style={{ flexWrap: 'nowrap' }}>
                <button className="small" onClick={async () => { await retryFailed(i); await off.refresh(); }}>Retry</button>
                <button className="small danger" onClick={async () => { if (confirm('Discard this offline bill? Make sure the customer is re-billed or refunded.')) { await discardFailed(i); await off.refresh(); } }}>Discard</button>
              </td></tr>
          ))}</tbody></table>
        </Modal>
      )}
      {done && (
        <Modal title={`Bill ${done.invoiceNo}`} onClose={() => { setDone(null); searchRef.current?.focus(); }}>
          <InvoiceView sale={done} />
          <div className="row end no-print" style={{ marginTop: 12 }}>
            <button onClick={() => window.print()}>Print</button>
            <button className="primary" autoFocus onClick={() => { setDone(null); searchRef.current?.focus(); }}>New bill</button>
          </div>
        </Modal>
      )}
    </>
  );
}
