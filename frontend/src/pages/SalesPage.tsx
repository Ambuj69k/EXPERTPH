import { useState } from 'react';
import { api, type PaymentMode, type Sale } from '../api';
import { RoleSets, useAuth } from '../auth';
import InvoiceView from '../components/InvoiceView';
import { ErrorBanner, Field, Modal, PageHead, Pager, StatusBadge, toast, useDebounced, useLoad } from '../ui';
import { dateTime, money, newId, num, today } from '../util';

export default function SalesPage() {
  const [from, setFrom] = useState(today());
  const [to, setTo] = useState(today());
  const [q, setQ] = useState('');
  const dq = useDebounced(q);
  const [page, setPage] = useState(1);
  const [openId, setOpenId] = useState<string | null>(null);
  const { data, error, loading, reload } = useLoad(() => api.sales({ from, to, q: dq, page }), [from, to, dq, page]);

  return (
    <>
      <PageHead title="Sales & Returns" sub="Find a bill to reprint, return items or cancel." >
        <Field label="From"><input type="date" value={from} onChange={e => { setFrom(e.target.value); setPage(1); }} /></Field>
        <Field label="To"><input type="date" value={to} onChange={e => { setTo(e.target.value); setPage(1); }} /></Field>
        <Field label="Bill no / mobile / name"><input value={q} onChange={e => { setQ(e.target.value); setPage(1); }} /></Field>
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error} />
        {!loading && data?.items.length === 0 ? <div className="empty">No bills in this period.</div> : (
          <div className="table-wrap">
            <table>
              <thead><tr><th>Bill</th><th>Time</th><th>Customer</th><th className="num">Items</th><th className="num">Amount</th><th>Status</th></tr></thead>
              <tbody>
                {data?.items.map(s => (
                  <tr key={s.id} className="clickable" onClick={() => setOpenId(s.id)} tabIndex={0} onKeyDown={e => e.key === 'Enter' && setOpenId(s.id)}>
                    <td><b>{s.invoiceNo}</b></td><td>{dateTime(s.invoiceAt)}</td>
                    <td>{s.customerName ?? <span className="muted">Walk-in</span>} {s.customerPhone && <span className="muted small">{s.customerPhone}</span>}</td>
                    <td className="num">{s.lineCount}</td><td className="num">{money(s.netAmount)}</td><td><StatusBadge status={s.status} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {data && <Pager page={page} total={data.total} pageSize={data.pageSize} onPage={setPage} />}
      </div>
      {openId && <SaleDetail id={openId} onClose={() => setOpenId(null)} onChanged={reload} />}
    </>
  );
}

function SaleDetail({ id, onClose, onChanged }: { id: string; onClose: () => void; onChanged: () => void }) {
  const { can } = useAuth();
  const sale = useLoad(() => api.sale(id), [id]);
  const returns = useLoad(() => api.saleReturns(id), [id]);
  const [mode, setMode] = useState<'view' | 'return' | 'cancel'>('view');

  const refresh = () => { sale.reload(); returns.reload(); onChanged(); setMode('view'); };
  const s = sale.data;

  return (
    <Modal title={s ? `Bill ${s.invoiceNo}` : 'Bill'} onClose={onClose} wide>
      <ErrorBanner error={sale.error} />
      {s && mode === 'view' && (
        <>
          <InvoiceView sale={s} />
          {s.cancelReason && <div className="alert error" style={{ marginTop: 8 }}>Cancelled: {s.cancelReason}</div>}
          {returns.data && returns.data.length > 0 && (
            <div className="panel no-print" style={{ marginTop: 12 }}>
              <h3>Returns</h3>
              <table>
                <thead><tr><th>Return no</th><th>When</th><th>Items</th><th>Refund</th><th className="num">Amount</th></tr></thead>
                <tbody>
                  {returns.data.map(r => (
                    <tr key={r.id}>
                      <td>{r.returnNo}</td><td>{dateTime(r.returnAt)}</td>
                      <td className="small">{r.lines.map(l => `${l.productName} ×${l.qtyUnits}`).join(', ')}<div className="muted">{r.reason}</div></td>
                      <td>{r.refundMode}</td><td className="num">{money(r.refundAmount)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          <div className="row end no-print" style={{ marginTop: 12 }}>
            {s.status === 'Posted' && can(...RoleSets.salesAdmin) && <>
              <button className="danger" onClick={() => setMode('cancel')} disabled={s.lines.some(l => l.returnedQtyUnits > 0)}>Cancel bill</button>
              <button onClick={() => setMode('return')} disabled={s.lines.every(l => l.returnedQtyUnits >= l.qtyUnits)}>Return items</button>
            </>}
            <button className="primary" onClick={() => window.print()}>Print</button>
          </div>
        </>
      )}
      {s && mode === 'return' && <ReturnForm sale={s} onDone={refresh} onBack={() => setMode('view')} />}
      {s && mode === 'cancel' && <CancelForm sale={s} onDone={refresh} onBack={() => setMode('view')} />}
    </Modal>
  );
}

function ReturnForm({ sale, onDone, onBack }: { sale: Sale; onDone: () => void; onBack: () => void }) {
  const [qty, setQty] = useState<Record<string, number>>({});
  const [reason, setReason] = useState('');
  const [refundMode, setRefundMode] = useState<PaymentMode>('Cash');
  const [requestId] = useState(newId);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);

  const estimate = sale.lines.reduce((s, l) => s + (qty[l.id] ? l.netAmount * qty[l.id] / l.qtyUnits : 0), 0);
  const submit = async () => {
    setBusy(true); setError(null);
    try {
      const r = await api.createReturn({
        clientRequestId: requestId, salesInvoiceId: sale.id, reason, refundMode,
        lines: Object.entries(qty).filter(([, v]) => v > 0).map(([salesInvoiceLineId, qtyUnits]) => ({ salesInvoiceLineId, qtyUnits })),
      });
      toast(`Return ${r.returnNo} saved · refund ${money(r.refundAmount)}`);
      onDone();
    } catch (e) { setError(e); } finally { setBusy(false); }
  };

  return (
    <div className="grid">
      <ErrorBanner error={error} />
      <table>
        <thead><tr><th>Item</th><th>Batch</th><th className="num">Sold</th><th className="num">Already returned</th><th className="num">Return qty ({'units'})</th></tr></thead>
        <tbody>
          {sale.lines.map(l => {
            const max = l.qtyUnits - l.returnedQtyUnits;
            return (
              <tr key={l.id}>
                <td>{l.productName}</td><td>{l.batchNo}</td><td className="num">{l.qtyUnits} {l.baseUnit}</td>
                <td className="num">{l.returnedQtyUnits}</td>
                <td className="num"><input className="qty" type="number" min={0} max={max} disabled={max <= 0} value={qty[l.id] ?? 0}
                  onChange={e => setQty({ ...qty, [l.id]: Math.min(max, Math.max(0, Math.floor(Number(e.target.value) || 0))) })} /></td>
              </tr>
            );
          })}
        </tbody>
      </table>
      <div className="grid c2">
        <Field label="Reason"><input value={reason} onChange={e => setReason(e.target.value)} placeholder="e.g. doctor changed prescription" /></Field>
        <Field label="Refund mode">
          <select value={refundMode} onChange={e => setRefundMode(e.target.value as PaymentMode)}>
            <option>Cash</option><option>Upi</option><option>Card</option>{sale.customerId && <option value="Credit">Credit note</option>}
          </select>
        </Field>
      </div>
      <p className="small muted" style={{ margin: 0 }}>Returned units go back to the same batch as available stock. Refund estimate: <b>{money(estimate)}</b> (server calculates exact amount).</p>
      <div className="row end">
        <button onClick={onBack}>Back</button>
        <button className="primary" disabled={busy || !reason.trim() || !Object.values(qty).some(v => v > 0)} onClick={submit}>Save return</button>
      </div>
    </div>
  );
}

function CancelForm({ sale, onDone, onBack }: { sale: Sale; onDone: () => void; onBack: () => void }) {
  const [reason, setReason] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const submit = async () => {
    setBusy(true); setError(null);
    try { await api.cancelSale(sale.id, reason); toast(`Bill ${sale.invoiceNo} cancelled`); onDone(); }
    catch (e) { setError(e); } finally { setBusy(false); }
  };
  return (
    <div className="grid">
      <ErrorBanner error={error} />
      <div className="alert warn">Cancelling returns all {sale.lines.length} item(s) to stock. Refund {money(sale.netAmount)} to the customer separately ({sale.payments.map(p => `${p.mode} ${num(p.amount)}`).join(', ')}).</div>
      <Field label="Reason (required)"><input value={reason} onChange={e => setReason(e.target.value)} autoFocus /></Field>
      <div className="row end">
        <button onClick={onBack}>Back</button>
        <button className="danger" disabled={busy || !reason.trim()} onClick={submit}>Cancel bill</button>
      </div>
    </div>
  );
}
