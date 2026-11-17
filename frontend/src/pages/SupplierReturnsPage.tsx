import { useEffect, useMemo, useState } from 'react';
import { api, type ReturnCandidate, type ReturnReason, type Supplier } from '../api';
import { useAuth } from '../auth';
import { ErrorBanner, Field, Modal, PageHead, Pager, toast, useLoad } from '../ui';
import { dateTime, expiry, money, num, today } from '../util';

const STATUS_CLS: Record<string, string> = { Draft: 'warn', Dispatched: 'brand', Settled: 'ok', Rejected: 'danger' };

export default function SupplierReturnsPage() {
  const [tab, setTab] = useState<'candidates' | 'returns'>('candidates');
  return (
    <>
      <PageHead title="Returns to supplier" sub="Send near-expiry, expired or damaged stock back and track every credit note until it is recovered.">
        <button className={tab === 'candidates' ? 'primary' : ''} onClick={() => setTab('candidates')}>Expiry candidates</button>
        <button className={tab === 'returns' ? 'primary' : ''} onClick={() => setTab('returns')}>Returns & claims</button>
      </PageHead>
      {tab === 'candidates' ? <Candidates onCreated={() => setTab('returns')} /> : <ReturnsList />}
    </>
  );
}

interface Pick { selected: boolean; qty: number; supplierId: string }

function Candidates({ onCreated }: { onCreated: () => void }) {
  const [days, setDays] = useState(90);
  const [reason, setReason] = useState<ReturnReason>('NearExpiry');
  const { data, error, loading } = useLoad(() => api.returnCandidates(days), [days]);
  const [suppliers, setSuppliers] = useState<Supplier[]>([]);
  const [picks, setPicks] = useState<Record<string, Pick>>({});
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<unknown>(null);

  useEffect(() => { api.suppliers('', 1).then(r => setSuppliers(r.items)).catch(() => {}); }, []);
  useEffect(() => {
    setPicks(Object.fromEntries((data ?? []).map(c => [c.batchId, {
      selected: false, qty: Math.max(0, c.qtyOnHand - c.qtyInOpenReturns), supplierId: c.suggestedSupplierId ?? '',
    }])));
  }, [data]);

  const set = (id: string, p: Partial<Pick>) => setPicks(ps => ({ ...ps, [id]: { ...ps[id], ...p } }));
  const chosen = (data ?? []).filter(c => picks[c.batchId]?.selected && picks[c.batchId].qty > 0);
  const claim = chosen.reduce((s, c) => s + c.claimValue * picks[c.batchId].qty / c.qtyOnHand, 0);
  const groups = useMemo(() => {
    const g: Record<string, ReturnCandidate[]> = {};
    chosen.forEach(c => { (g[picks[c.batchId].supplierId] ??= []).push(c); });
    return g;
  }, [chosen, picks]);

  const create = async () => {
    setErr(null);
    if (groups['']) { setErr(new Error('Choose a supplier for every selected batch.')); return; }
    setBusy(true);
    try {
      const made: string[] = [];
      for (const [supplierId, rows] of Object.entries(groups)) {
        const r = await api.createPurchaseReturn({
          supplierId, reason, lines: rows.map(c => ({ batchId: c.batchId, qtyUnits: picks[c.batchId].qty })),
        });
        made.push(r.returnNo);
      }
      toast(`Created ${made.join(', ')}`);
      onCreated();
    } catch (e) { setErr(e); } finally { setBusy(false); }
  };

  return (
    <div className="panel">
      <div className="row" style={{ marginBottom: 12 }}>
        <Field label="Expiring within">
          <select value={days} onChange={e => setDays(Number(e.target.value))}>
            {[30, 60, 90, 120, 180].map(d => <option key={d} value={d}>{d} days</option>)}
          </select>
        </Field>
        <Field label="Reason">
          <select value={reason} onChange={e => setReason(e.target.value as ReturnReason)}>
            <option value="NearExpiry">Near expiry</option><option value="Expired">Expired</option>
            <option value="Damaged">Damaged</option><option value="Recall">Recall</option><option value="Other">Other</option>
          </select>
        </Field>
        <span className="spacer" />
        <div style={{ textAlign: 'right' }}>
          <div className="small muted">{chosen.length} batch(es) · {Object.keys(groups).filter(Boolean).length} supplier(s)</div>
          <b>Claim ≈ {money(claim)}</b>
        </div>
        <button className="primary" disabled={busy || chosen.length === 0} onClick={create}>Create return drafts</button>
      </div>
      <ErrorBanner error={error ?? err} />
      <p className="small muted" style={{ marginTop: 0 }}>
        Supplier is suggested from the last purchase of each batch. Check each supplier's return policy (window, eligible items) before dispatching.
      </p>
      {!loading && data?.length === 0 ? <div className="empty">No stock expiring in this window.</div> : (
        <div className="table-wrap">
          <table>
            <thead><tr><th /><th>Product</th><th>Batch</th><th>Expiry</th><th className="num">On hand</th><th className="num">In drafts</th><th className="num">Return qty</th><th>Supplier</th><th className="num">Claim value</th></tr></thead>
            <tbody>
              {data?.map(c => {
                const p = picks[c.batchId];
                if (!p) return null;
                return (
                  <tr key={c.batchId} className={p.selected ? 'selected' : ''}>
                    <td><input type="checkbox" checked={p.selected} onChange={e => set(c.batchId, { selected: e.target.checked })} aria-label={`Select ${c.productName} ${c.batchNo}`} /></td>
                    <td><b>{c.productName}</b> <span className="small muted">{c.manufacturer}</span></td>
                    <td>{c.batchNo}</td>
                    <td>{expiry(c.expiryDate)} {c.daysToExpiry < 0 ? <span className="badge danger">expired</span> : <span className="small muted">{c.daysToExpiry}d</span>}</td>
                    <td className="num">{num(c.qtyOnHand)}</td>
                    <td className="num">{c.qtyInOpenReturns ? <span className="badge warn">{c.qtyInOpenReturns}</span> : '-'}</td>
                    <td className="num"><input className="qty" type="number" min={0} max={c.qtyOnHand} value={p.qty}
                      onChange={e => set(c.batchId, { qty: Math.min(c.qtyOnHand, Math.max(0, Math.floor(Number(e.target.value) || 0))), selected: true })} /></td>
                    <td>
                      <select value={p.supplierId} onChange={e => set(c.batchId, { supplierId: e.target.value })}>
                        <option value="">Choose…</option>
                        {suppliers.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}
                      </select>
                    </td>
                    <td className="num">{money(c.claimValue * p.qty / c.qtyOnHand)}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function ReturnsList() {
  const [status, setStatus] = useState('');
  const [page, setPage] = useState(1);
  const [open, setOpen] = useState<string | null>(null);
  const { data, error, loading, reload } = useLoad(() => api.purchaseReturns({ status: status || undefined, page }), [status, page]);
  return (
    <div className="panel">
      <div className="row" style={{ marginBottom: 12 }}>
        <select value={status} onChange={e => { setStatus(e.target.value); setPage(1); }} aria-label="Status filter">
          <option value="">All statuses</option><option>Draft</option><option>Dispatched</option><option>Settled</option><option>Rejected</option>
        </select>
        <span className="small muted">Dispatched = goods sent, credit note pending.</span>
      </div>
      <ErrorBanner error={error} />
      {!loading && data?.items.length === 0 ? <div className="empty">No returns yet.</div> : (
        <table>
          <thead><tr><th>Return no</th><th>Supplier</th><th>Reason</th><th>Status</th><th className="num">Pending for</th><th className="num">Claim</th><th className="num">Credit received</th></tr></thead>
          <tbody>
            {data?.items.map(r => (
              <tr key={r.id} className="clickable" onClick={() => setOpen(r.id)}>
                <td><b>{r.returnNo}</b></td><td>{r.supplierName}</td><td>{r.reason}</td>
                <td><span className={`badge ${STATUS_CLS[r.status]}`}>{r.status}</span></td>
                <td className="num">{r.status === 'Dispatched' ? <span className={r.ageDays > 60 ? 'badge danger' : ''}>{r.ageDays} days</span> : '-'}</td>
                <td className="num">{money(r.claimAmount)}</td><td className="num">{r.status === 'Settled' ? money(r.acceptedAmount) : '-'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {data && <Pager page={page} total={data.total} pageSize={data.pageSize} onPage={setPage} />}
      {open && <ReturnDetail id={open} onClose={() => setOpen(null)} onChanged={reload} />}
    </div>
  );
}

function ReturnDetail({ id, onClose, onChanged }: { id: string; onClose: () => void; onChanged: () => void }) {
  const { session } = useAuth();
  const { data: r, error, reload } = useLoad(() => api.purchaseReturn(id), [id]);
  const [mode, setMode] = useState<'view' | 'settle' | 'reject'>('view');
  const [cn, setCn] = useState({ creditNoteNo: '', creditNoteDate: today(), acceptedAmount: '', notes: '' });
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<unknown>(null);

  const act = async (fn: () => Promise<unknown>, msg: string) => {
    setBusy(true); setErr(null);
    try { await fn(); toast(msg); reload(); onChanged(); setMode('view'); } catch (e) { setErr(e); } finally { setBusy(false); }
  };

  return (
    <Modal title={r ? `Return ${r.returnNo}` : 'Return'} onClose={onClose} wide>
      <ErrorBanner error={error ?? err} />
      {r && (
        <div className="invoice">
          <div className="row" style={{ justifyContent: 'space-between', alignItems: 'flex-start' }}>
            <div>
              <div style={{ fontSize: 16, fontWeight: 700 }}>{session?.tenantName}</div>
              <div>{session?.branchName}</div>
              <div style={{ fontWeight: 600, marginTop: 6 }}>DEBIT NOTE / GOODS RETURN</div>
            </div>
            <div style={{ textAlign: 'right' }}>
              <div>No: <b>{r.returnNo}</b> <span className={`badge ${STATUS_CLS[r.status]} no-print`}>{r.status}</span></div>
              <div>To: <b>{r.supplierName}</b></div>
              <div>Reason: {r.reason}</div>
              <div className="small">Created {dateTime(r.createdAt)}{r.dispatchedAt && ` · dispatched ${dateTime(r.dispatchedAt)}`}</div>
            </div>
          </div>
          <table style={{ marginTop: 10 }}>
            <thead><tr><th>#</th><th>Product</th><th>Batch</th><th>Exp</th><th className="num">Qty</th><th className="num">Cost/u</th><th className="num">GST%</th><th className="num">Claim</th></tr></thead>
            <tbody>
              {r.lines.map(l => (
                <tr key={l.id}><td>{l.lineNo}</td><td>{l.productName}</td><td>{l.batchNo}</td><td>{expiry(l.expiryDate)}</td>
                  <td className="num">{num(l.qtyUnits)}</td><td className="num">{num(l.costPerUnit)}</td><td className="num">{l.gstRate}</td><td className="num">{num(l.claimAmount)}</td></tr>
              ))}
            </tbody>
          </table>
          <div className="totals" style={{ maxWidth: 300, marginLeft: 'auto', marginTop: 8 }}>
            <span>Claim</span><span>{money(r.claimAmount)}</span>
            {r.status === 'Settled' && <>
              <span>Credit note {r.supplierCreditNoteNo}</span><span>{money(r.acceptedAmount)}</span>
              <span>Written off</span><span style={{ color: 'var(--danger)' }}>{money(r.writtenOffAmount)}</span>
            </>}
          </div>
          {r.rejectReason && <div className="alert error" style={{ marginTop: 8 }}>Rejected by supplier: {r.rejectReason}</div>}
          {r.notes && <p className="small">Notes: {r.notes}</p>}

          {mode === 'view' && (
            <div className="row end no-print" style={{ marginTop: 12 }}>
              {r.status === 'Draft' && <>
                <button className="danger" disabled={busy} onClick={() => confirm('Delete this draft?') && act(async () => { await api.deletePurchaseReturn(r.id); onClose(); }, 'Draft deleted')}>Delete draft</button>
                <button className="primary" disabled={busy} onClick={() => confirm('Dispatch now? Stock will be removed.') && act(() => api.dispatchPurchaseReturn(r.id), 'Dispatched — stock removed')}>Dispatch goods</button>
              </>}
              {r.status === 'Dispatched' && <>
                <button className="danger" onClick={() => setMode('reject')}>Supplier rejected</button>
                <button className="primary" onClick={() => { setCn({ ...cn, acceptedAmount: String(r.claimAmount) }); setMode('settle'); }}>Record credit note</button>
              </>}
              <button onClick={() => window.print()}>Print</button>
            </div>
          )}
          {mode === 'settle' && (
            <div className="panel grid no-print" style={{ marginTop: 12 }}>
              <div className="grid c3">
                <Field label="Supplier credit note no"><input value={cn.creditNoteNo} onChange={e => setCn({ ...cn, creditNoteNo: e.target.value })} autoFocus /></Field>
                <Field label="Credit note date"><input type="date" max={today()} value={cn.creditNoteDate} onChange={e => setCn({ ...cn, creditNoteDate: e.target.value })} /></Field>
                <Field label="Amount accepted"><input type="number" value={cn.acceptedAmount} onChange={e => setCn({ ...cn, acceptedAmount: e.target.value })} /></Field>
              </div>
              <Field label="Notes"><input value={cn.notes} onChange={e => setCn({ ...cn, notes: e.target.value })} /></Field>
              <p className="small muted" style={{ margin: 0 }}>Shortfall of {money(Math.max(0, r.claimAmount - (Number(cn.acceptedAmount) || 0)))} will be written off. The credit reduces what you owe this supplier.</p>
              <div className="row end"><button onClick={() => setMode('view')}>Back</button>
                <button className="primary" disabled={busy || !cn.creditNoteNo.trim()} onClick={() => act(() => api.settlePurchaseReturn(r.id, { ...cn, acceptedAmount: Number(cn.acceptedAmount) || 0 }), 'Credit note recorded')}>Save</button></div>
            </div>
          )}
          {mode === 'reject' && (
            <div className="panel grid no-print" style={{ marginTop: 12 }}>
              <div className="alert warn" style={{ margin: 0 }}>Use this when the supplier sends the goods back. The quantities return to stock in the same batches.</div>
              <Field label="Reason"><input value={reason} onChange={e => setReason(e.target.value)} autoFocus /></Field>
              <div className="row end"><button onClick={() => setMode('view')}>Back</button>
                <button className="danger" disabled={busy || !reason.trim()} onClick={() => act(() => api.rejectPurchaseReturn(r.id, reason), 'Marked rejected — stock restored')}>Mark rejected</button></div>
            </div>
          )}
        </div>
      )}
    </Modal>
  );
}
