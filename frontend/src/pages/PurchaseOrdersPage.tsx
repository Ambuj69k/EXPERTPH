import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, type ReorderRow, type Supplier } from '../api';
import { ErrorBanner, Field, Modal, PageHead, StatusBadge, toast, useLoad } from '../ui';
import { dateTime, money, num } from '../util';

/** Reorder suggestions → one purchase order per supplier. */
export function ReorderPage() {
  const navigate = useNavigate();
  const [lookback, setLookback] = useState(30);
  const [cover, setCover] = useState(15);
  const { data, error, loading } = useLoad(() => api.reorder(lookback, cover), [lookback, cover]);
  const [suppliers, setSuppliers] = useState<Supplier[]>([]);
  const [pick, setPick] = useState<Record<string, { on: boolean; packs: number; supplierId: string }>>({});
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<unknown>(null);

  useEffect(() => { api.suppliers('', 1).then(r => setSuppliers(r.items)).catch(() => {}); }, []);
  useEffect(() => {
    setPick(Object.fromEntries((data ?? []).map(r => [r.productId, { on: !!r.lastSupplierId, packs: r.suggestedPacks, supplierId: r.lastSupplierId ?? '' }])));
  }, [data]);

  const chosen = (data ?? []).filter(r => pick[r.productId]?.on && pick[r.productId].packs > 0);
  const groups = useMemo(() => {
    const g: Record<string, ReorderRow[]> = {};
    chosen.forEach(r => { (g[pick[r.productId].supplierId] ??= []).push(r); });
    return g;
  }, [chosen, pick]);

  const create = async () => {
    setErr(null);
    if (groups['']) { setErr(new Error('Choose a supplier for every selected product.')); return; }
    setBusy(true);
    try {
      const made: string[] = [];
      for (const [supplierId, rows] of Object.entries(groups)) {
        const po = await api.createPurchaseOrder({ supplierId, lines: rows.map(r => ({ productId: r.productId, qtyPacks: pick[r.productId].packs, expectedRate: r.lastRate ?? null })) });
        made.push(po.poNo);
      }
      toast(`Created ${made.join(', ')}`);
      navigate('/purchase-orders');
    } catch (e) { setErr(e); } finally { setBusy(false); }
  };

  return (
    <>
      <PageHead title="Reorder" sub="What to buy, based on recent sales, your reorder levels, stock on hand and open orders.">
        <Field label="Sales look-back"><select value={lookback} onChange={e => setLookback(Number(e.target.value))}>{[14, 30, 60, 90].map(d => <option key={d} value={d}>{d} days</option>)}</select></Field>
        <Field label="Stock to cover"><select value={cover} onChange={e => setCover(Number(e.target.value))}>{[7, 10, 15, 30, 45].map(d => <option key={d} value={d}>{d} days</option>)}</select></Field>
        <button className="primary" disabled={busy || chosen.length === 0} onClick={create}>Create {Object.keys(groups).filter(Boolean).length || ''} order(s)</button>
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error ?? err} />
        {!loading && data?.length === 0 ? <div className="empty">Nothing to reorder. Set reorder levels on products to get suggestions for slow movers.</div> : (
          <div className="table-wrap">
            <table>
              <thead><tr><th /><th>Product</th><th className="num">Sold</th><th className="num">Per day</th><th className="num">On hand</th><th className="num">On order</th><th className="num">Target</th><th className="num">Order (packs)</th><th>Supplier</th><th className="num">Last rate</th></tr></thead>
              <tbody>
                {data?.map(r => {
                  const p = pick[r.productId];
                  if (!p) return null;
                  return (
                    <tr key={r.productId} className={p.on ? 'selected' : ''}>
                      <td><input type="checkbox" checked={p.on} onChange={e => setPick({ ...pick, [r.productId]: { ...p, on: e.target.checked } })} aria-label={`Select ${r.productName}`} /></td>
                      <td><b>{r.productName}</b> <span className="small muted">{r.manufacturer}</span></td>
                      <td className="num">{num(r.soldUnits)}</td><td className="num">{num(r.avgDailyUnits)}</td>
                      <td className="num">{num(r.onHandUnits)}</td><td className="num">{num(r.onOrderUnits)}</td><td className="num">{num(r.targetUnits)}</td>
                      <td className="num"><input className="qty" type="number" min={0} value={p.packs} onChange={e => setPick({ ...pick, [r.productId]: { ...p, packs: Math.max(0, Math.floor(Number(e.target.value) || 0)), on: true } })} /> <span className="small muted">{r.packUnit}</span></td>
                      <td><select value={p.supplierId} onChange={e => setPick({ ...pick, [r.productId]: { ...p, supplierId: e.target.value } })}><option value="">Choose…</option>{suppliers.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}</select></td>
                      <td className="num">{r.lastRate != null ? money(r.lastRate) : '-'}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
        <p className="small muted">Target = max(average daily sales × days to cover, reorder level). Order = target − sellable stock − open orders, rounded up to whole packs. Units are tablets/base units.</p>
      </div>
    </>
  );
}

export function PurchaseOrdersPage() {
  const navigate = useNavigate();
  const [status, setStatus] = useState('');
  const { data, error, reload } = useLoad(() => api.purchaseOrders(status || undefined), [status]);
  const [open, setOpen] = useState<string | null>(null);
  return (
    <>
      <PageHead title="Purchase orders" sub="Orders placed with suppliers. Deliveries are received as purchases linked to the order.">
        <select value={status} onChange={e => setStatus(e.target.value)} aria-label="Status"><option value="">All</option>{['Draft', 'Sent', 'PartiallyReceived', 'Received', 'Closed', 'Cancelled'].map(s => <option key={s}>{s}</option>)}</select>
        <button className="primary" onClick={() => navigate('/reorder')}>From reorder suggestions</button>
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error} />
        {data?.items.length === 0 ? <div className="empty">No orders yet.</div> : (
          <table>
            <thead><tr><th>Order</th><th>Supplier</th><th>Date</th><th>Expected</th><th className="num">Packs</th><th>Status</th></tr></thead>
            <tbody>
              {data?.items.map(o => (
                <tr key={o.id} className="clickable" onClick={() => setOpen(o.id)}>
                  <td><b>{o.poNo}</b></td><td>{o.supplierName}</td><td>{o.orderDate}</td><td>{o.expectedDate ?? '-'}</td>
                  <td className="num">{num(o.receivedPacks)} / {num(o.orderedPacks)}</td><td><StatusBadge status={o.status} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
      {open && <PoDetail id={open} onClose={() => { setOpen(null); reload(); }} />}
    </>
  );
}

function PoDetail({ id, onClose }: { id: string; onClose: () => void }) {
  const navigate = useNavigate();
  const { data: o, error, setData } = useLoad(() => api.purchaseOrder(id), [id]);
  const [err, setErr] = useState<unknown>(null);
  const act = async (a: 'send' | 'close' | 'cancel', msg: string) => {
    setErr(null);
    try { setData(await api.poAction(id, a)); toast(msg); } catch (e) { setErr(e); }
  };
  const wa = o?.supplierPhone ? `https://wa.me/91${o.supplierPhone.replace(/\D/g, '').slice(-10)}?text=${encodeURIComponent(o.shareText)}` : null;
  return (
    <Modal title={o ? `Order ${o.poNo}` : 'Order'} onClose={onClose} wide>
      <ErrorBanner error={error ?? err} />
      {o && (
        <div className="grid invoice">
          <div className="row"><b>{o.supplierName}</b><StatusBadge status={o.status} /><span className="small muted">ordered {o.orderDate}{o.expectedDate && ` · needed by ${o.expectedDate}`}{o.sentAt && ` · sent ${dateTime(o.sentAt)}`}</span></div>
          <table>
            <thead><tr><th>#</th><th>Product</th><th className="num">Ordered</th><th className="num">Received</th><th className="num">Pending</th><th className="num">Rate</th></tr></thead>
            <tbody>{o.lines.map(l => <tr key={l.id}><td>{l.lineNo}</td><td>{l.productName}</td><td className="num">{l.qtyPacks}</td><td className="num">{l.receivedQtyPacks}</td><td className="num">{l.pendingQtyPacks}</td><td className="num">{l.expectedRate != null ? money(l.expectedRate) : '-'}</td></tr>)}</tbody>
          </table>
          {o.estimatedValue > 0 && <div className="small muted">Estimated value (ex-GST): {money(o.estimatedValue)}</div>}
          {o.receipts.length > 0 && <>
            <h3>Deliveries</h3>
            <table className="small"><tbody>{o.receipts.map(r => <tr key={r.id} className="clickable" onClick={() => navigate(`/purchases/${r.id}`)}><td>{r.invoiceDate}</td><td>{r.supplierInvoiceNo}</td><td><StatusBadge status={r.status} /></td><td className="num">{money(r.netAmount)}</td></tr>)}</tbody></table>
          </>}
          <div className="row end no-print">
            {o.status === 'Draft' && <button className="primary" onClick={() => act('send', 'Marked as sent')}>Mark as sent</button>}
            {(o.status === 'Draft' || o.status === 'Sent') && <button className="danger" onClick={() => confirm('Cancel this order?') && act('cancel', 'Order cancelled')}>Cancel</button>}
            {(o.status === 'Sent' || o.status === 'PartiallyReceived') && <>
              <button onClick={() => confirm('Close this order? No more deliveries will be expected.') && act('close', 'Order closed')}>Close short</button>
              <button className="primary" onClick={() => navigate(`/purchases/new?po=${o.id}`)}>Receive goods</button>
            </>}
            {wa && <a href={wa} target="_blank" rel="noopener noreferrer"><button>WhatsApp to supplier</button></a>}
            <button onClick={() => { navigator.clipboard?.writeText(o.shareText); toast('Order text copied'); }}>Copy text</button>
            <button onClick={() => window.print()}>Print</button>
          </div>
        </div>
      )}
    </Modal>
  );
}
