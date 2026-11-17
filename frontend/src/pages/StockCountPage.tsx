import { useState } from 'react';
import { api, type StockCount } from '../api';
import { ErrorBanner, Field, Modal, PageHead, toast, useLoad } from '../ui';
import { dateTime, expiry, money, num } from '../util';

export default function StockCountPage() {
  const list = useLoad(() => api.stockCounts(), []);
  const [open, setOpen] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  return (
    <>
      <PageHead title="Stock count" sub="Count a rack or category; the difference is posted as gain/loss with a full audit trail.">
        <button className="primary" onClick={() => setCreating(true)}>+ New count</button>
      </PageHead>
      <div className="panel">
        <ErrorBanner error={list.error} />
        {list.data?.length === 0 ? <div className="empty">No counts yet.</div> : (
          <table>
            <thead><tr><th>Count</th><th>Title</th><th>Created</th><th className="num">Counted</th><th>Status</th></tr></thead>
            <tbody>
              {list.data?.map(c => (
                <tr key={c.id} className="clickable" onClick={() => setOpen(c.id)}>
                  <td><b>{c.countNo}</b></td><td>{c.title}</td><td>{dateTime(c.createdAt)}</td>
                  <td className="num">{c.countedLines} / {c.lineCount}</td>
                  <td><span className={`badge ${c.status === 'Posted' ? 'ok' : c.status === 'Cancelled' ? 'danger' : 'warn'}`}>{c.status}</span></td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
      {creating && <CreateCount onClose={() => setCreating(false)} onCreated={id => { setCreating(false); list.reload(); setOpen(id); }} />}
      {open && <CountSheet id={open} onClose={() => { setOpen(null); list.reload(); }} />}
    </>
  );
}

function CreateCount({ onClose, onCreated }: { onClose: () => void; onCreated: (id: string) => void }) {
  const [title, setTitle] = useState('');
  const [search, setSearch] = useState('');
  const [error, setError] = useState<unknown>(null);
  const submit = async () => {
    setError(null);
    try { const c = await api.createStockCount({ title, search: search || null }); toast(`${c.countNo}: ${c.lineCount} batches to count`); onCreated(c.id); }
    catch (e) { setError(e); }
  };
  return (
    <Modal title="New stock count" onClose={onClose}>
      <div className="grid">
        <ErrorBanner error={error} />
        <Field label="Title"><input value={title} onChange={e => setTitle(e.target.value)} placeholder="Rack A / Antibiotics / Full count" autoFocus /></Field>
        <Field label="Products whose name contains (blank = everything in stock)"><input value={search} onChange={e => setSearch(e.target.value)} /></Field>
        <p className="small muted" style={{ margin: 0 }}>The sheet lists every batch with stock. You can keep billing while counting — the system quantity is captured at the moment you enter each count.</p>
        <div className="row end"><button className="primary" disabled={!title.trim()} onClick={submit}>Create count sheet</button></div>
      </div>
    </Modal>
  );
}

function CountSheet({ id, onClose }: { id: string; onClose: () => void }) {
  const { data, error, setData } = useLoad(() => api.stockCount(id), [id]);
  const [edits, setEdits] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<unknown>(null);
  const editable = data?.status === 'InProgress';

  const run = async (fn: () => Promise<StockCount | void>, msg: string) => {
    setBusy(true); setErr(null);
    try { const r = await fn(); if (r) setData(r); setEdits({}); toast(msg); } catch (e) { setErr(e); } finally { setBusy(false); }
  };
  const save = () => run(() => api.saveCountEntries(id, Object.entries(edits).map(([lineId, v]) => ({ lineId, countedQty: v === '' ? null : Number(v) }))), 'Counts saved');

  return (
    <Modal title={data ? `${data.countNo} · ${data.title}` : 'Count'} onClose={onClose} wide>
      <ErrorBanner error={error ?? err} />
      {data && (
        <div className="grid">
          <div className="row">
            <span>{data.countedLines} / {data.lineCount} counted</span>
            <span className="badge ok">gain {money(data.gainValue)}</span><span className="badge danger">loss {money(data.lossValue)}</span>
            <span className="spacer" />
            {editable && <>
              <button disabled={busy || !Object.keys(edits).length} onClick={save}>Save counts</button>
              <button className="danger" disabled={busy} onClick={() => confirm('Cancel this count?') && run(async () => { await api.cancelStockCount(id); onClose(); }, 'Count cancelled')}>Cancel count</button>
              <button className="primary" disabled={busy || Object.keys(edits).length > 0 || data.countedLines === 0}
                onClick={() => confirm('Post variances to stock? This cannot be undone.') && run(() => api.postStockCount(id), 'Count posted')}>Post variances</button>
            </>}
            <button onClick={() => window.print()}>Print sheet</button>
          </div>
          {Object.keys(edits).length > 0 && <div className="alert warn" style={{ margin: 0 }}>Unsaved counts — save before posting.</div>}
          <div className="table-wrap">
            <table>
              <thead><tr><th>Product</th><th>Batch</th><th>Exp</th><th className="num">System now</th><th className="num">Counted</th><th className="num">System at count</th><th className="num">Variance</th></tr></thead>
              <tbody>
                {data.lines.map(l => (
                  <tr key={l.id}>
                    <td>{l.productName}</td><td>{l.batchNo}</td><td>{expiry(l.expiryDate)}</td>
                    <td className="num">{num(l.systemQtyNow)}</td>
                    <td className="num">{editable
                      ? <input className="qty" type="number" min={0} value={edits[l.id] ?? (l.countedQty ?? '')} onChange={e => setEdits({ ...edits, [l.id]: e.target.value })} aria-label={`Count ${l.productName} ${l.batchNo}`} />
                      : l.countedQty ?? '-'}</td>
                    <td className="num">{l.systemQtyAtCount ?? '-'}</td>
                    <td className="num">{l.variance == null ? '-' : <span style={{ color: l.variance === 0 ? 'var(--ok)' : 'var(--danger)', fontWeight: 600 }}>{l.variance > 0 ? '+' : ''}{l.variance}</span>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </Modal>
  );
}
