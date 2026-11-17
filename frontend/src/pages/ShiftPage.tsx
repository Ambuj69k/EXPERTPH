import { useState } from 'react';
import { api, type ShiftSummary } from '../api';
import { RoleSets, useAuth } from '../auth';
import { ErrorBanner, Field, Modal, PageHead, toast, useLoad } from '../ui';
import { dateTime, money, today } from '../util';

export default function ShiftPage() {
  const { can } = useAuth();
  const cur = useLoad(() => api.currentShift(), []);
  return (
    <>
      <PageHead title="Cash drawer" sub="Open a shift with the cash in the drawer, record petty cash, and count at close." />
      <ErrorBanner error={cur.error} />
      {!cur.loading && (cur.data ? <OpenShift shift={cur.data} onChange={cur.reload} /> : <OpenForm onOpened={cur.reload} />)}
      {can(...RoleSets.reports) && <ShiftHistory />}
    </>
  );
}

function OpenForm({ onOpened }: { onOpened: () => void }) {
  const [cash, setCash] = useState('');
  const [error, setError] = useState<unknown>(null);
  const submit = async (e: React.FormEvent) => {
    e.preventDefault(); setError(null);
    try { const s = await api.openShift(Number(cash) || 0); toast(`Shift ${s.shiftNo} opened`); onOpened(); } catch (err) { setError(err); }
  };
  return (
    <form className="panel grid" style={{ maxWidth: 420 }} onSubmit={submit}>
      <h2 style={{ margin: 0 }}>No open shift</h2>
      <ErrorBanner error={error} />
      <Field label="Opening cash in drawer"><input type="number" min={0} value={cash} onChange={e => setCash(e.target.value)} autoFocus /></Field>
      <button className="primary">Open shift</button>
    </form>
  );
}

export function ShiftBreakdown({ s }: { s: ShiftSummary }) {
  return (
    <div className="totals" style={{ maxWidth: 380 }}>
      <span>Opening cash</span><span>{money(s.openingCash)}</span>
      <span>+ Cash sales ({s.billCount} bills)</span><span>{money(s.cashSales)}</span>
      <span>− Cash on cancelled bills</span><span>{money(s.cancelledCash)}</span>
      <span>+ Customer payments (cash)</span><span>{money(s.cashReceipts)}</span>
      <span>− Cash refunds</span><span>{money(s.cashRefunds)}</span>
      <span>− Supplier paid in cash</span><span>{money(s.cashSupplierPayments)}</span>
      <span>+ Pay-ins</span><span>{money(s.payIns)}</span>
      <span>− Pay-outs</span><span>{money(s.payOuts)}</span>
      <span className="grand">Expected cash</span><span className="grand">{money(s.expectedCash)}</span>
      {s.countedCash != null && <>
        <span>Counted</span><span>{money(s.countedCash)}</span>
        <span>Variance</span><span style={{ color: (s.variance ?? 0) === 0 ? 'var(--ok)' : 'var(--danger)', fontWeight: 700 }}>{money(s.variance)}</span>
      </>}
      <span className="small muted">Other modes</span>
      <span className="small muted">UPI {money(s.upiSales)} · Card {money(s.cardSales)} · Credit {money(s.creditSales)}</span>
    </div>
  );
}

function OpenShift({ shift, onChange }: { shift: ShiftSummary; onChange: () => void }) {
  const [mv, setMv] = useState({ kind: 'PayOut', amount: '', reason: '' });
  const [counted, setCounted] = useState('');
  const [notes, setNotes] = useState('');
  const [closed, setClosed] = useState<ShiftSummary | null>(null);
  const [error, setError] = useState<unknown>(null);

  const addMv = async () => {
    setError(null);
    try { await api.cashMovement({ ...mv, amount: Number(mv.amount) }); setMv({ ...mv, amount: '', reason: '' }); onChange(); } catch (e) { setError(e); }
  };
  const close = async () => {
    if (!confirm('Close the shift with this counted amount?')) return;
    setError(null);
    try { setClosed(await api.closeShift(Number(counted) || 0, notes)); } catch (e) { setError(e); }
  };

  return (
    <div className="grid c2" style={{ alignItems: 'start' }}>
      <div className="panel">
        <h2>Shift {shift.shiftNo} <span className="badge ok">open</span></h2>
        <p className="small muted" style={{ marginTop: -8 }}>{shift.userName} · since {dateTime(shift.openedAt)}</p>
        <ShiftBreakdown s={shift} />
        {shift.movements.length > 0 && (
          <table className="small" style={{ marginTop: 12 }}>
            <tbody>{shift.movements.map((m, i) => <tr key={i}><td>{dateTime(m.at)}</td><td>{m.kind}</td><td>{m.reason}</td><td className="num">{money(m.amount)}</td></tr>)}</tbody>
          </table>
        )}
      </div>
      <div className="grid">
        <ErrorBanner error={error} />
        <div className="panel grid">
          <h3 style={{ margin: 0 }}>Petty cash</h3>
          <div className="row">
            <select value={mv.kind} onChange={e => setMv({ ...mv, kind: e.target.value })}><option value="PayOut">Pay-out</option><option value="PayIn">Pay-in</option></select>
            <input className="short" type="number" placeholder="Amount" value={mv.amount} onChange={e => setMv({ ...mv, amount: e.target.value })} />
            <input style={{ flex: 1 }} placeholder="Reason (tea, courier, change from bank…)" value={mv.reason} onChange={e => setMv({ ...mv, reason: e.target.value })} />
            <button disabled={!(Number(mv.amount) > 0) || !mv.reason.trim()} onClick={addMv}>Add</button>
          </div>
        </div>
        <div className="panel grid">
          <h3 style={{ margin: 0 }}>Close shift</h3>
          <Field label="Cash counted in drawer"><input type="number" min={0} value={counted} onChange={e => setCounted(e.target.value)} /></Field>
          {counted !== '' && <div>Variance: <b style={{ color: Number(counted) - shift.expectedCash === 0 ? 'var(--ok)' : 'var(--danger)' }}>{money(Number(counted) - shift.expectedCash)}</b></div>}
          <Field label="Notes"><input value={notes} onChange={e => setNotes(e.target.value)} /></Field>
          <button className="primary" disabled={counted === ''} onClick={close}>Close shift</button>
        </div>
      </div>
      {closed && (
        <Modal title={`Shift ${closed.shiftNo} closed`} onClose={() => { setClosed(null); onChange(); }}>
          <ShiftBreakdown s={closed} />
          <div className="row end" style={{ marginTop: 12 }}><button onClick={() => window.print()}>Print</button><button className="primary" onClick={() => { setClosed(null); onChange(); }}>Done</button></div>
        </Modal>
      )}
    </div>
  );
}

function ShiftHistory() {
  const [from, setFrom] = useState(today());
  const { data } = useLoad(() => api.shifts(from), [from]);
  const [open, setOpen] = useState<string | null>(null);
  const detail = useLoad(() => open ? api.shift(open) : Promise.resolve(null), [open]);
  return (
    <div className="panel" style={{ marginTop: 16 }}>
      <div className="row"><h2 style={{ margin: 0 }}>All shifts</h2><span className="spacer" /><Field label="From"><input type="date" value={from} onChange={e => setFrom(e.target.value)} /></Field></div>
      <table style={{ marginTop: 8 }}>
        <thead><tr><th>Shift</th><th>User</th><th>Opened</th><th>Closed</th><th className="num">Expected</th><th className="num">Counted</th><th className="num">Variance</th></tr></thead>
        <tbody>
          {data?.map(s => (
            <tr key={s.id} className="clickable" onClick={() => setOpen(s.id)}>
              <td>{s.shiftNo}</td><td>{s.userName}</td><td>{dateTime(s.openedAt)}</td><td>{s.closedAt ? dateTime(s.closedAt) : <span className="badge ok">open</span>}</td>
              <td className="num">{s.expectedCash != null ? money(s.expectedCash) : '-'}</td><td className="num">{s.countedCash != null ? money(s.countedCash) : '-'}</td>
              <td className="num">{s.variance != null ? <span className={s.variance === 0 ? 'badge ok' : 'badge danger'}>{money(s.variance)}</span> : '-'}</td>
            </tr>
          ))}
          {data?.length === 0 && <tr><td colSpan={7} className="muted">No shifts.</td></tr>}
        </tbody>
      </table>
      {open && detail.data && <Modal title={`Shift ${detail.data.shiftNo}`} onClose={() => setOpen(null)}><ShiftBreakdown s={detail.data} /></Modal>}
    </div>
  );
}
