import { useState } from 'react';
import { api, type BatchRow } from '../api';
import { ErrorBanner, Field, Modal, PageHead, toast, useLoad } from '../ui';
import { dateTime, expiry, money, num } from '../util';

export default function RecallPage() {
  const [q, setQ] = useState('');
  const [rows, setRows] = useState<BatchRow[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [open, setOpen] = useState<string | null>(null);

  const search = async (e?: React.FormEvent) => {
    e?.preventDefault(); setError(null);
    try { setRows(await api.searchBatches(q)); } catch (err) { setError(err); }
  };

  return (
    <>
      <PageHead title="Batch recall & quarantine" sub="Find a batch, see where it came from and which customers received it, and block it from sale." />
      <form className="panel row" onSubmit={search}>
        <input style={{ flex: 1 }} placeholder="Batch number or product name" value={q} onChange={e => setQ(e.target.value)} autoFocus />
        <button className="primary" disabled={!q.trim()}>Search</button>
      </form>
      <ErrorBanner error={error} />
      {rows && (
        <div className="panel">
          {rows.length === 0 ? <div className="empty">No batch found.</div> : (
            <table>
              <thead><tr><th>Product</th><th>Batch</th><th>Expiry</th><th className="num">MRP</th><th className="num">On hand</th><th>Status</th></tr></thead>
              <tbody>
                {rows.map(b => (
                  <tr key={b.batchId} className="clickable" onClick={() => setOpen(b.batchId)}>
                    <td><b>{b.productName}</b> <span className="small muted">{b.manufacturer}</span></td><td>{b.batchNo}</td><td>{expiry(b.expiryDate)}</td>
                    <td className="num">{money(b.mrp)}</td><td className="num">{num(b.qtyOnHand)}</td>
                    <td>{b.isBlocked ? <span className="badge danger" title={b.blockReason ?? ''}>blocked</span> : <span className="badge ok">sellable</span>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}
      {open && <TraceModal id={open} onClose={() => setOpen(null)} onChanged={() => search()} />}
    </>
  );
}

function TraceModal({ id, onClose, onChanged }: { id: string; onClose: () => void; onChanged: () => void }) {
  const { data: t, error, reload } = useLoad(() => api.traceBatch(id), [id]);
  const [reason, setReason] = useState('');
  const [err, setErr] = useState<unknown>(null);
  const act = async (fn: () => Promise<void>, msg: string) => {
    setErr(null);
    try { await fn(); toast(msg); reload(); onChanged(); } catch (e) { setErr(e); }
  };
  return (
    <Modal title={t ? `${t.batch.productName} · batch ${t.batch.batchNo}` : 'Batch'} onClose={onClose} wide>
      <ErrorBanner error={error ?? err} />
      {t && (
        <div className="grid">
          <div className="row">
            <span>Expiry {expiry(t.batch.expiryDate)} · MRP {money(t.batch.mrp)}</span>
            <span className="badge">on hand {num(t.batch.qtyOnHand)}</span><span className="badge">sold {num(t.qtySold)}</span>
            <span className="badge">returned to supplier {num(t.qtyReturnedToSupplier)}</span>
          </div>
          {t.batch.isBlocked ? (
            <div className="alert error row" style={{ margin: 0 }}>
              <span>Blocked: {t.batch.blockReason}</span><span className="spacer" />
              <button className="small" onClick={() => act(() => api.unblockBatch(id), 'Batch unblocked')}>Unblock</button>
            </div>
          ) : (
            <div className="row no-print">
              <Field label="Block reason"><input value={reason} onChange={e => setReason(e.target.value)} placeholder="e.g. Manufacturer recall notice dated …" style={{ width: 360 }} /></Field>
              <button className="danger" style={{ alignSelf: 'end' }} disabled={!reason.trim()} onClick={() => act(() => api.blockBatch(id, reason), 'Batch blocked from sale')}>Block from sale</button>
            </div>
          )}
          <p className="small muted" style={{ margin: 0 }}>Blocking stops billing of this batch. To send stock back, create a return with reason “Recall” under Returns to Supplier.</p>
          <h3>Received from</h3>
          <table className="small"><tbody>
            {t.purchases.map(p => <tr key={p.purchaseId}><td>{p.invoiceDate}</td><td>{p.supplierName}</td><td>{p.supplierInvoiceNo}</td><td className="num">{num(p.qtyUnits)} units</td></tr>)}
            {t.purchases.length === 0 && <tr><td className="muted">Opening stock / adjustment only.</td></tr>}
          </tbody></table>
          <h3>Sold to ({t.sales.length} bills)</h3>
          <table className="small">
            <thead><tr><th>When</th><th>Bill</th><th>Customer</th><th>Mobile</th><th className="num">Qty</th><th className="num">Returned</th></tr></thead>
            <tbody>
              {t.sales.map(s => (
                <tr key={s.invoiceId + s.qtyUnits}><td>{dateTime(s.at)}</td><td>{s.invoiceNo} {s.status === 'Cancelled' && <span className="badge danger">cancelled</span>}</td>
                  <td>{s.customerName ?? <span className="muted">walk-in</span>}</td><td>{s.customerPhone}</td><td className="num">{num(s.qtyUnits)}</td><td className="num">{num(s.returnedQtyUnits)}</td></tr>
              ))}
            </tbody>
          </table>
          <p className="small muted">Walk-in sales without a mobile number cannot be traced to a person. Contact customers only as permitted by your consent records.</p>
        </div>
      )}
    </Modal>
  );
}
