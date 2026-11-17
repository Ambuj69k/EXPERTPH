import { Fragment, useState } from 'react';
import { api, type Product, type StockRow } from '../api';
import { RoleSets, useAuth } from '../auth';
import ProductPicker from '../components/ProductPicker';
import { ErrorBanner, Field, Modal, PageHead, Pager, toast, useDebounced, useLoad } from '../ui';
import { dateTime, expiry, money, num, packs, parseExpiry } from '../util';

export default function StockPage() {
  const { can } = useAuth();
  const [q, setQ] = useState('');
  const dq = useDebounced(q);
  const [inStock, setInStock] = useState(true);
  const [below, setBelow] = useState(false);
  const [page, setPage] = useState(1);
  const [open, setOpen] = useState<string | null>(null);
  const [history, setHistory] = useState<StockRow | null>(null);
  const [adjust, setAdjust] = useState<'opening' | 'adjust' | null>(null);
  const { data, error, loading, reload } = useLoad(() => api.stock({ q: dq, inStock, belowReorder: below, page }), [dq, inStock, below, page]);

  return (
    <>
      <PageHead title="Stock" sub="On-hand quantity by batch. Stock changes only through purchases, sales, returns and adjustments.">
        <input placeholder="Search product" value={q} onChange={e => { setQ(e.target.value); setPage(1); }} />
        <label className="check"><input type="checkbox" checked={inStock} onChange={e => { setInStock(e.target.checked); setPage(1); }} /> In stock only</label>
        <label className="check"><input type="checkbox" checked={below} onChange={e => { setBelow(e.target.checked); setPage(1); }} /> Below reorder</label>
        {can(...RoleSets.salesAdmin) && <>
          <button onClick={() => setAdjust('adjust')}>Adjust stock</button>
          <button className="primary" onClick={() => setAdjust('opening')}>Opening stock</button>
        </>}
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error} />
        {!loading && data?.items.length === 0 ? <div className="empty">Nothing to show. Post a purchase or enter opening stock.</div> : (
          <div className="table-wrap">
            <table>
              <thead><tr><th>Product</th><th className="num">On hand</th><th className="num">Batches</th><th className="num">Value at cost</th><th /></tr></thead>
              <tbody>
                {data?.items.map(r => (
                  <Fragment key={r.productId}>
                    <tr className="clickable" onClick={() => setOpen(open === r.productId ? null : r.productId)}>
                      <td><b>{r.name}</b> <span className="small muted">{r.manufacturer}</span> {r.belowReorder && <span className="badge warn">reorder</span>}</td>
                      <td className="num">{packs(r.qtyUnits, r.packSize, r.packUnit, r.baseUnit)}<div className="small muted">{num(r.qtyUnits)} {r.baseUnit}</div></td>
                      <td className="num">{r.batches.length}</td>
                      <td className="num">{money(r.valueAtCost)}</td>
                      <td>{can(...RoleSets.reports) && <button className="small" onClick={e => { e.stopPropagation(); setHistory(r); }}>History</button>}</td>
                    </tr>
                    {open === r.productId && r.batches.map(b => (
                      <tr key={b.batchId} className="selected">
                        <td className="small" style={{ paddingLeft: 24 }}>Batch <b>{b.batchNo}</b> Â· exp {expiry(b.expiryDate)} {b.isExpired && <span className="badge danger">expired</span>} Â· MRP {money(b.mrp)}</td>
                        <td className="num small">{num(b.qtyUnits)} {r.baseUnit}</td>
                        <td />
                        <td className="num small">{money(b.qtyUnits * b.costPerUnit)}</td>
                        <td />
                      </tr>
                    ))}
                  </Fragment>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {data && <Pager page={page} total={data.total} pageSize={data.pageSize} onPage={setPage} />}
      </div>
      {history && <HistoryModal row={history} onClose={() => setHistory(null)} />}
      {adjust && <AdjustModal opening={adjust === 'opening'} onClose={() => setAdjust(null)} onDone={() => { setAdjust(null); reload(); }} />}
    </>
  );
}

function HistoryModal({ row, onClose }: { row: StockRow; onClose: () => void }) {
  const { data, error } = useLoad(() => api.movements(row.productId), [row.productId]);
  return (
    <Modal title={`Movements Â· ${row.name}`} onClose={onClose} wide>
      <ErrorBanner error={error} />
      <table>
        <thead><tr><th>When</th><th>Type</th><th>Batch</th><th className="num">Qty ({row.baseUnit})</th><th>Reference</th></tr></thead>
        <tbody>
          {data?.map((m, i) => (
            <tr key={i}>
              <td>{dateTime(m.at)}</td><td>{m.type}</td><td>{m.batchNo}</td>
              <td className="num" style={{ color: m.qtyUnits < 0 ? 'var(--danger)' : 'var(--ok)' }}>{m.qtyUnits > 0 ? '+' : ''}{num(m.qtyUnits)}</td>
              <td className="small">{m.note}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {data?.length === 0 && <div className="empty">No movements.</div>}
    </Modal>
  );
}

interface AdjRow { key: number; product: Product | null; batchId: string; batchNo: string; expiry: string; mrp: string; rate: string; qty: string }

function AdjustModal({ opening, onClose, onDone }: { opening: boolean; onClose: () => void; onDone: () => void }) {
  const [reason, setReason] = useState(opening ? 'Opening stock' : '');
  const [rows, setRows] = useState<AdjRow[]>([{ key: 1, product: null, batchId: '', batchNo: '', expiry: '', mrp: '', rate: '', qty: '' }]);
  const [batches, setBatches] = useState<Record<string, StockRow['batches']>>({});
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const setRow = (key: number, p: Partial<AdjRow>) => setRows(rs => rs.map(r => r.key === key ? { ...r, ...p } : r));

  const pick = async (key: number, p: Product) => {
    setRow(key, { product: p, batchId: '' });
    if (!opening) {
      const s = await api.stock({ q: p.name, inStock: false });
      setBatches(b => ({ ...b, [p.id]: s.items.find(i => i.productId === p.id)?.batches ?? [] }));
    }
  };

  const submit = async () => {
    setError(null);
    try {
      const lines = rows.filter(r => r.product).map((r, i) => {
        const units = Number(r.qty);
        if (!units) throw new Error(`Line ${i + 1}: enter quantity in ${r.product!.baseUnit}.`);
        if (r.batchId) return { productId: r.product!.id, batchId: r.batchId, qtyUnits: units };
        const exp = parseExpiry(r.expiry);
        if (!exp) throw new Error(`Line ${i + 1}: expiry must be MM/YY.`);
        return { productId: r.product!.id, batchNo: r.batchNo, expiryDate: exp, mrp: Number(r.mrp), purchaseRate: Number(r.rate) || 0, qtyUnits: units };
      });
      if (!lines.length) throw new Error('Add at least one line.');
      setBusy(true);
      const res = await api.adjust({ isOpening: opening, reason, lines });
      toast(`${res.adjustmentNo} posted (${res.lineCount} lines)`);
      onDone();
    } catch (e) { setError(e); } finally { setBusy(false); }
  };

  return (
    <Modal title={opening ? 'Opening stock' : 'Stock adjustment'} onClose={onClose} wide>
      <div className="grid">
        <ErrorBanner error={error} />
        <p className="small muted" style={{ margin: 0 }}>
          {opening ? 'Enter stock you already hold (e.g. when moving from another software). Quantities are in base units (tablets, bottles).'
            : 'Use for breakage, counting differences etc. Positive adds, negative removes. Every adjustment is logged.'}
        </p>
        <Field label="Reason"><input value={reason} onChange={e => setReason(e.target.value)} /></Field>
        <table>
          <thead><tr><th>Product</th><th>Batch</th>{<><th>Expiry</th><th className="num">MRP/pack</th><th className="num">Rate/pack</th></>}<th className="num">Qty (units)</th></tr></thead>
          <tbody>
            {rows.map(r => {
              const existing = r.product ? batches[r.product.id] ?? [] : [];
              const newBatch = !r.batchId;
              return (
                <tr key={r.key}>
                  <td style={{ minWidth: 220 }}><ProductPicker value={r.product} onPick={p => pick(r.key, p)} /></td>
                  <td>
                    {!opening && existing.length > 0 && (
                      <select value={r.batchId} onChange={e => setRow(r.key, { batchId: e.target.value })}>
                        <option value="">New batchâ€¦</option>
                        {existing.map(b => <option key={b.batchId} value={b.batchId}>{b.batchNo} Â· {expiry(b.expiryDate)} Â· {b.qtyUnits}</option>)}
                      </select>
                    )}
                    {newBatch && <input className="short" value={r.batchNo} placeholder="Batch" onChange={e => setRow(r.key, { batchNo: e.target.value.toUpperCase() })} />}
                  </td>
                  <td>{newBatch && <input className="short" placeholder="MM/YY" value={r.expiry} onChange={e => setRow(r.key, { expiry: e.target.value })} />}</td>
                  <td className="num">{newBatch && <input className="qty" type="number" value={r.mrp} onChange={e => setRow(r.key, { mrp: e.target.value })} />}</td>
                  <td className="num">{newBatch && <input className="qty" type="number" value={r.rate} onChange={e => setRow(r.key, { rate: e.target.value })} />}</td>
                  <td className="num"><input className="qty" type="number" value={r.qty} onChange={e => setRow(r.key, { qty: e.target.value })} /></td>
                </tr>
              );
            })}
          </tbody>
        </table>
        <div className="row">
          <button className="small" onClick={() => setRows(rs => [...rs, { key: Date.now(), product: null, batchId: '', batchNo: '', expiry: '', mrp: '', rate: '', qty: '' }])}>+ Add line</button>
          <span className="spacer" />
          <button className="primary" disabled={busy || !reason.trim()} onClick={submit}>Post {opening ? 'opening stock' : 'adjustment'}</button>
        </div>
      </div>
    </Modal>
  );
}
