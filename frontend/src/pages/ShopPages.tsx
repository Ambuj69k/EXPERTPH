import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { publicApi, type PublicProduct, type TrackView } from '../api';
import { ErrorBanner, Field, useDebounced, useLoad } from '../ui';
import { dateTime, money } from '../util';

const STATUS_TEXT: Record<string, string> = {
  Submitted: 'Received — a pharmacist will review your order',
  NeedsCustomerApproval: 'The pharmacist changed your order — please review and approve below',
  Accepted: 'Approved — being prepared',
  Packed: 'Packed — waiting for delivery partner',
  ReadyForPickup: 'Ready for pickup at the store',
  OutForDelivery: 'Out for delivery',
  Completed: 'Completed',
  DeliveryFailed: 'Delivery attempt failed — the store will contact you',
  Rejected: 'Not accepted by the pharmacy',
  Cancelled: 'Cancelled',
};

/** Public ordering page for one pharmacy branch. */
export function ShopPage() {
  const { branchId } = useParams();
  const navigate = useNavigate();
  const store = useLoad(() => publicApi.store(branchId!), [branchId]);
  const [q, setQ] = useState('');
  const dq = useDebounced(q);
  const catalog = useLoad(() => publicApi.catalog(branchId!, dq), [branchId, dq]);
  const [cart, setCart] = useState<Record<string, { p: PublicProduct; qty: number }>>({});
  const [form, setForm] = useState({ name: '', phone: '', fulfilment: 'Pickup', address: '', pincode: '', requestText: '', notes: '' });
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const items = Object.values(cart);
  const estimate = items.reduce((s, i) => s + (i.p.pricePerPack ?? i.p.mrpPerPack) / i.p.packSize * i.qty, 0);

  const add = (p: PublicProduct) => setCart(c => ({ ...c, [p.productId]: { p, qty: (c[p.productId]?.qty ?? 0) + p.packSize } }));
  const place = async () => {
    setBusy(true); setError(null);
    try {
      const r = await publicApi.placeOrder(branchId!, {
        ...form, address: form.address || null, pincode: form.pincode || null, requestText: form.requestText || null, notes: form.notes || null,
        lines: items.map(i => ({ productId: i.p.productId, qtyUnits: i.qty })),
      });
      navigate(`/track/${r.trackingToken}`);
    } catch (e) { setError(e); } finally { setBusy(false); }
  };

  if (store.error) return <div className="auth"><div className="panel empty">This store is not taking online orders.</div></div>;
  const s = store.data;
  return (
    <div style={{ maxWidth: 980, margin: '0 auto', padding: 16 }}>
      {s && <div className="panel" style={{ marginBottom: 12 }}>
        <h1>{s.businessName} · {s.branchName}</h1>
        <div className="small muted">{s.address} {s.phone && `· ${s.phone}`} {s.drugLicenseNo && `· DL ${s.drugLicenseNo}`}</div>
        {s.notice && <div className="alert ok" style={{ margin: '8px 0 0' }}>{s.notice}</div>}
      </div>}
      <div className="grid c2" style={{ alignItems: 'start' }}>
        <div className="panel grid">
          <input placeholder="Search medicines and health products" value={q} onChange={e => setQ(e.target.value)} />
          <table className="small"><tbody>
            {catalog.data?.map(p => (
              <tr key={p.productId}>
                <td><b>{p.name}</b><div className="muted">{p.composition} · {p.packSize} {p.baseUnit}/{p.packUnit}</div></td>
                <td className="num">{!p.inStock ? <span className="badge">out of stock</span>
                  : p.pricePerPack ? <><s className="muted">{money(p.mrpPerPack)}</s> <b>{money(p.pricePerPack)}</b></> : money(p.mrpPerPack)}</td>
                <td><button className="small" disabled={!p.inStock} onClick={() => add(p)}>Add</button></td>
              </tr>
            ))}
            {catalog.data?.length === 0 && <tr><td className="muted">No matching products listed online.</td></tr>}
          </tbody></table>
          <p className="small muted" style={{ margin: 0 }}>Prescription medicines aren't listed here. Describe what you need below and upload your prescription after placing the order — a pharmacist will review it.</p>
        </div>
        <div className="panel grid">
          <h2 style={{ margin: 0 }}>Your order</h2>
          {items.map(i => (
            <div className="row" key={i.p.productId}>
              <span style={{ flex: 1 }}>{i.p.name}</span>
              <input className="qty" type="number" min={0} step={i.p.allowLooseSale ? 1 : i.p.packSize} value={i.qty}
                onChange={e => { const v = Math.max(0, Math.floor(Number(e.target.value) || 0)); setCart(c => { const n = { ...c }; if (v) n[i.p.productId] = { ...i, qty: v }; else delete n[i.p.productId]; return n; }); }} />
              <span className="small muted">{i.p.baseUnit}</span>
            </div>
          ))}
          <Field label="Prescription items / anything else you need"><textarea rows={2} value={form.requestText} onChange={e => setForm({ ...form, requestText: e.target.value })} /></Field>
          <div className="grid c2">
            <Field label="Name"><input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} /></Field>
            <Field label="Mobile"><input value={form.phone} inputMode="tel" onChange={e => setForm({ ...form, phone: e.target.value })} /></Field>
          </div>
          <div className="row">
            <label className="check"><input type="radio" checked={form.fulfilment === 'Pickup'} onChange={() => setForm({ ...form, fulfilment: 'Pickup' })} /> Pick up at store</label>
            {s?.deliveryAvailable && <label className="check"><input type="radio" checked={form.fulfilment === 'Delivery'} onChange={() => setForm({ ...form, fulfilment: 'Delivery' })} /> Home delivery
              {s.deliveryCharge > 0 ? `(${money(s.deliveryCharge)}${s.freeDeliveryAbove ? `, free above ${money(s.freeDeliveryAbove)}` : ''})` : '(free)'}</label>}
          </div>
          {form.fulfilment === 'Delivery' && <div className="grid c2">
            <Field label="Address"><input value={form.address} onChange={e => setForm({ ...form, address: e.target.value })} /></Field>
            <Field label={`Pincode (${s?.deliveryPincodes.join(', ')})`}><input value={form.pincode} maxLength={6} onChange={e => setForm({ ...form, pincode: e.target.value })} /></Field>
          </div>}
          <ErrorBanner error={error} />
          <div className="row"><span>Estimate {money(estimate)}</span><span className="spacer" />
            <button className="primary" disabled={busy || (!items.length && !form.requestText.trim()) || !form.name || !form.phone} onClick={place}>Place order</button></div>
          <p className="small muted" style={{ margin: 0 }}>Pay at pickup or on delivery. Final amount is confirmed after pharmacist review. Your details are used only for this order.</p>
        </div>
      </div>
    </div>
  );
}

/** Customer's tracking page (link contains a secret token). */
export function TrackPage() {
  const { token } = useParams();
  const [t, setT] = useState<TrackView | null>(null);
  const [error, setError] = useState<unknown>(null);
  useEffect(() => { publicApi.track(token!).then(setT).catch(setError); }, [token]);
  const act = async (fn: () => Promise<TrackView>) => { setError(null); try { setT(await fn()); } catch (e) { setError(e); } };
  if (!t) return <div className="auth"><ErrorBanner error={error} /></div>;
  const canChange = t.status === 'Submitted' || t.status === 'NeedsCustomerApproval';
  return (
    <div style={{ maxWidth: 640, margin: '0 auto', padding: 16 }} className="grid">
      <div className="panel grid">
        <h1 style={{ margin: 0 }}>Order {t.orderNo}</h1>
        <div className="small muted">{t.storeName}{t.storePhone && ` · ${t.storePhone}`} · placed {dateTime(t.createdAt)}</div>
        <div className={`alert ${t.status === 'Rejected' || t.status === 'Cancelled' || t.status === 'DeliveryFailed' ? 'error' : 'ok'}`} style={{ margin: 0 }}>{STATUS_TEXT[t.status]}</div>
        {t.rejectReason && <div>Reason: {t.rejectReason}</div>}
        {t.pharmacistNotes && <div>Pharmacist: {t.pharmacistNotes}</div>}
        <table className="small"><thead><tr><th>Item</th><th className="num">Asked</th><th className="num">Approved</th></tr></thead><tbody>
          {t.lines.map((l, i) => <tr key={i}><td>{l.productName} {l.addedByPharmacist && <span className="badge brand">added from prescription</span>}</td>
            <td className="num">{l.requestedQtyUnits || '-'}</td><td className="num">{l.approvedQtyUnits ?? '…'}</td></tr>)}
        </tbody></table>
        {t.amountDue != null && <div style={{ fontSize: 18 }}>Amount to pay: <b>{money(t.amountDue)}</b></div>}
        {['Packed', 'OutForDelivery', 'ReadyForPickup'].includes(t.status) && <div className="alert warn" style={{ margin: 0 }}>Handover code: <b style={{ fontSize: 20 }}>{t.handoverCode}</b> — share it only when you receive your order.</div>}
        <ErrorBanner error={error} />
        {t.status === 'NeedsCustomerApproval' && <button className="primary" onClick={() => act(() => publicApi.approve(token!))}>Approve changes</button>}
        {canChange && <>
          <Field label={`Upload prescription (PDF/JPG/PNG, ${t.prescriptionsUploaded}/3 uploaded)`}>
            <input type="file" accept=".pdf,image/jpeg,image/png" onChange={e => e.target.files?.[0] && act(() => publicApi.upload(token!, e.target.files![0]))} />
          </Field>
        </>}
        {(canChange || t.status === 'Accepted') && <button className="danger" onClick={() => confirm('Cancel this order?') && act(() => publicApi.cancel(token!))}>Cancel order</button>}
      </div>
      <p className="small muted">Keep this page's link private — anyone with it can see this order. <Link to="/">Pharmacy staff sign-in</Link></p>
    </div>
  );
}
