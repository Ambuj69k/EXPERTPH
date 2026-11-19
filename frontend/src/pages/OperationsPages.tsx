import { useState } from 'react';
import {
  api, controlledDrugRegister, deviationApi, dispatchApi, mfgApi, sfaExtraApi, type Deviation, type DeviationSeverity, type DispatchTrip, type ExpenseCategory,
} from '../api';
import { useAuth } from '../auth';
import { ErrorBanner, Field, Modal, PageHead, toast, useLoad } from '../ui';
import { dateTime, expiry, money, today } from '../util';
import { SignModal } from './ManufacturingPages';

const qty = (n: number) => n.toLocaleString('en-IN', { maximumFractionDigits: 2 });
const Badge = ({ s }: { s: string }) => {
  const cls = ['Completed', 'Delivered', 'Approved', 'Closed'].includes(s) ? 'ok' : ['Failed', 'Cancelled', 'Rejected'].includes(s) ? 'danger' : ['Packing', 'Submitted', 'Open'].includes(s) ? 'warn' : 'brand';
  const label = s === 'OnRoute' ? 'On route' : s === 'CapaInProgress' ? 'CAPA in progress' : s;
  return <span className={`badge ${cls}`}>{label}</span>;
};

// ================= Distributor: dispatch =================

export function DispatchPage() {
  const { can } = useAuth();
  const admin = can('Owner', 'Pharmacist');
  const pending = useLoad(() => admin ? dispatchApi.pending() : Promise.resolve([]), [admin]);
  const trips = useLoad(() => dispatchApi.list(), []);
  const [pick, setPick] = useState<Record<string, boolean>>({});
  const [f, setF] = useState({ route: '', vehicle: '', driverName: '' });
  const [err, setErr] = useState<unknown>(null);
  const [open, setOpen] = useState<string | null>(null);
  const chosen = (pending.data ?? []).filter(p => pick[p.id]);
  const create = async () => {
    setErr(null);
    try {
      const t = await dispatchApi.create({ route: f.route || null, vehicle: f.vehicle || null, driverName: f.driverName || null, invoiceIds: chosen.map(c => c.id) });
      toast(`Trip ${t.dispatchNo} created`); setPick({}); pending.reload(); trips.reload(); setOpen(t.id);
    } catch (e) { setErr(e); }
  };
  return (
    <>
      <PageHead title="Dispatch" sub="Group invoices into a vehicle trip, print the pick list, and confirm each delivery." />
      {admin && <div className="panel">
        <h2>Invoices waiting for dispatch</h2>
        <ErrorBanner error={pending.error ?? err} />
        {pending.data?.length === 0 ? <div className="empty">Nothing to dispatch.</div> : <>
          <table>
            <thead><tr><th /><th>Invoice</th><th>Retailer</th><th>Route</th><th className="num">Amount</th><th /></tr></thead>
            <tbody>{pending.data?.map(p => <tr key={p.id}>
              <td><input type="checkbox" aria-label={`Select ${p.invoiceNo}`} checked={!!pick[p.id]} onChange={e => setPick({ ...pick, [p.id]: e.target.checked })} /></td>
              <td>{p.invoiceNo}<div className="small muted">{p.invoiceDate}</div></td><td>{p.retailerName}</td><td>{p.route ?? '-'}</td><td className="num">{money(p.netAmount)}</td>
              <td>{p.previouslyFailed && <span className="badge danger">Delivery failed before</span>}</td></tr>)}</tbody>
          </table>
          <div className="row">
            <Field label="Route"><input value={f.route} onChange={e => setF({ ...f, route: e.target.value })} /></Field>
            <Field label="Vehicle no."><input value={f.vehicle} onChange={e => setF({ ...f, vehicle: e.target.value.toUpperCase() })} /></Field>
            <Field label="Driver"><input value={f.driverName} onChange={e => setF({ ...f, driverName: e.target.value })} /></Field>
            <span className="spacer" /><button className="primary" disabled={chosen.length === 0} onClick={create}>Create trip ({chosen.length})</button>
          </div>
        </>}
      </div>}
      <div className="panel">
        <h2>Trips</h2>
        <ErrorBanner error={trips.error} />
        <table>
          <thead><tr><th>Trip</th><th>Vehicle / driver</th><th className="num">Stops</th><th className="num">Value</th><th>Status</th></tr></thead>
          <tbody>{trips.data?.map(t => <tr key={t.id} className="clickable" onClick={() => setOpen(t.id)}><td><b>{t.dispatchNo}</b><div className="small muted">{t.date}{t.route && ` · ${t.route}`}</div></td>
            <td>{[t.vehicle, t.driverName].filter(Boolean).join(' · ') || '-'}</td><td className="num">{t.stops.filter(s => s.status === 'Delivered').length}/{t.stops.length}</td>
            <td className="num">{money(t.value)}</td><td><Badge s={t.status} /></td></tr>)}</tbody>
        </table>
      </div>
      {open && <TripModal id={open} admin={admin} onClose={() => { setOpen(null); trips.reload(); pending.reload(); }} />}
    </>
  );
}

function TripModal({ id, admin, onClose }: { id: string; admin: boolean; onClose: () => void }) {
  const { data: t, error, setData } = useLoad(() => dispatchApi.get(id), [id]);
  const pick = useLoad(() => dispatchApi.pickList(id), [id]);
  const [err, setErr] = useState<unknown>(null);
  const act = async (fn: () => Promise<DispatchTrip>, msg: string) => { setErr(null); try { setData(await fn()); toast(msg); } catch (e) { setErr(e); } };
  const confirmStop = (stopId: string, delivered: boolean) => {
    const v = prompt(delivered ? 'Received by (name)?' : 'Why did the delivery fail?');
    if (!v?.trim()) return;
    act(() => dispatchApi.confirm(id, stopId, delivered ? { delivered, receivedBy: v.trim() } : { delivered, note: v.trim() }), delivered ? 'Delivered' : 'Marked failed');
  };
  return (
    <Modal title={t ? `Trip ${t.dispatchNo}` : 'Trip'} onClose={onClose} wide>
      <ErrorBanner error={error ?? err ?? pick.error} />
      {t && <div className="grid invoice">
        <div className="row"><Badge s={t.status} /><span className="small muted">{t.date}{t.route && ` · ${t.route}`}{t.vehicle && ` · ${t.vehicle}`}{t.driverName && ` · ${t.driverName}`}{t.departedAt && ` · left ${dateTime(t.departedAt)}`}</span></div>
        <h3>Pick list</h3>
        <table className="small"><thead><tr><th>Product</th><th>Batch</th><th>Exp</th><th className="num">Packs</th><th className="num">Free</th><th>Invoices</th></tr></thead>
          <tbody>{pick.data?.map((p, i) => <tr key={i}><td>{p.productName}</td><td>{p.batchNo}</td><td>{expiry(p.expiryDate)}</td><td className="num">{qty(p.packs)}</td><td className="num">{p.freePacks ? qty(p.freePacks) : ''}</td><td>{p.invoices.join(', ')}</td></tr>)}</tbody></table>
        <h3>Stops</h3>
        <table><tbody>{t.stops.map(s => (
          <tr key={s.id}><td>{s.seq}</td><td><b>{s.retailerName}</b><div className="small muted">{s.invoiceNo}{s.address && ` · ${s.address}`}{s.phone && ` · ${s.phone}`}</div></td>
            <td className="num">{money(s.netAmount)}</td><td><Badge s={s.status} />{s.receivedBy && <div className="small muted">by {s.receivedBy}</div>}{s.note && <div className="small muted">{s.note}</div>}</td>
            <td className="no-print">{t.status === 'OnRoute' && s.status === 'Pending' && <div className="row">
              <button className="small" onClick={() => confirmStop(s.id, false)}>Failed</button><button className="small primary" onClick={() => confirmStop(s.id, true)}>Delivered</button></div>}</td></tr>
        ))}</tbody></table>
        <div className="row end no-print">
          {admin && t.status === 'Packing' && <><button className="danger" onClick={() => act(() => dispatchApi.cancel(id), 'Trip cancelled')}>Cancel trip</button>
            <button className="primary" onClick={() => act(() => dispatchApi.depart(id), 'Trip on route')}>Loaded — depart</button></>}
          <button onClick={() => window.print()}>Print</button>
        </div>
      </div>}
    </Modal>
  );
}

// ================= MR: expenses & scorecard =================

const CATS: ExpenseCategory[] = ['Travel', 'Food', 'Lodging', 'Other'];

export function ExpensesPage() {
  const { can } = useAuth();
  const isRep = can('MedicalRep');
  const manage = can('Owner', 'AreaManager');
  const [month, setMonth] = useState(today().slice(0, 7) + '-01');
  const { data, error, reload } = useLoad(() => sfaExtraApi.expenses(month), [month]);
  const [f, setF] = useState({ date: today(), category: 'Travel' as ExpenseCategory, amount: 0, km: '', note: '' });
  const [err, setErr] = useState<unknown>(null);
  const claim = async () => {
    setErr(null);
    try { await sfaExtraApi.claim({ date: f.date, category: f.category, amount: f.amount, km: f.km ? Number(f.km) : null, note: f.note.trim() || null }); toast('Claim sent to your manager'); setF({ ...f, amount: 0, km: '', note: '' }); reload(); }
    catch (e) { setErr(e); }
  };
  const review = async (id: string, approve: boolean) => {
    const comment = approve ? undefined : prompt('Reason for rejecting?') ?? '';
    if (!approve && !comment?.trim()) return;
    setErr(null);
    try { await sfaExtraApi.review(id, approve, comment); reload(); } catch (e) { setErr(e); }
  };
  return (
    <>
      <PageHead title="Field expenses" sub="Daily travel and other field expenses, approved by the area manager.">
        <Field label="Month"><input type="month" value={month.slice(0, 7)} onChange={e => e.target.value && setMonth(e.target.value + '-01')} /></Field>
      </PageHead>
      <ErrorBanner error={err} />
      {isRep && <div className="panel">
        <div className="grid c2">
          <Field label="Date"><input type="date" max={today()} value={f.date} onChange={e => setF({ ...f, date: e.target.value })} /></Field>
          <Field label="Category"><select aria-label="Category" value={f.category} onChange={e => setF({ ...f, category: e.target.value as ExpenseCategory })}>{CATS.map(c => <option key={c}>{c}</option>)}</select></Field>
          <Field label="Amount (₹)"><input type="number" min={0} inputMode="decimal" value={f.amount || ''} onChange={e => setF({ ...f, amount: Number(e.target.value) || 0 })} /></Field>
          <Field label="Km (travel)"><input type="number" min={0} value={f.km} onChange={e => setF({ ...f, km: e.target.value })} /></Field>
        </div>
        <Field label="Note"><input value={f.note} onChange={e => setF({ ...f, note: e.target.value })} /></Field>
        <div className="row end"><button className="primary" disabled={f.amount <= 0} onClick={claim}>Claim</button></div>
      </div>}
      <div className="panel">
        <ErrorBanner error={error} />
        {data?.length === 0 ? <div className="empty">No claims this month.</div> : (
          <table>
            <thead><tr><th>Date</th>{!isRep && <th>Representative</th>}<th>Category</th><th className="num">Amount</th><th>Note</th><th>Status</th>{manage && <th />}</tr></thead>
            <tbody>{data?.map(e => <tr key={e.id}><td>{e.date}</td>{!isRep && <td>{e.repName}</td>}<td>{e.category}{e.km && <span className="small muted"> · {e.km} km</span>}</td>
              <td className="num">{money(e.amount)}</td><td className="small">{e.note ?? ''}{e.reviewComment && <div className="muted">{e.reviewedBy}: {e.reviewComment}</div>}</td><td><Badge s={e.status} /></td>
              {manage && <td>{e.status === 'Submitted' && <div className="row"><button className="small" onClick={() => review(e.id, false)}>Reject</button><button className="small primary" onClick={() => review(e.id, true)}>Approve</button></div>}</td>}</tr>)}</tbody>
          </table>
        )}
      </div>
    </>
  );
}

export function ScorecardPage() {
  const { can } = useAuth();
  const manage = can('Owner', 'AreaManager');
  const [month, setMonth] = useState(today().slice(0, 7) + '-01');
  const { data, error, reload } = useLoad(() => sfaExtraApi.scorecard(month), [month]);
  const [err, setErr] = useState<unknown>(null);
  const setTarget = async (repUserId: string, callsTarget: number, coverageTargetPercent: number) => {
    setErr(null);
    try { await sfaExtraApi.setTarget({ repUserId, month, callsTarget, coverageTargetPercent }); toast('Target saved'); reload(); } catch (e) { setErr(e); }
  };
  return (
    <>
      <PageHead title="Targets & scorecard" sub="Monthly call and coverage targets against what was reported, with expenses.">
        <Field label="Month"><input type="month" value={month.slice(0, 7)} onChange={e => e.target.value && setMonth(e.target.value + '-01')} /></Field>
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error ?? err} />
        <table>
          <thead><tr><th>Representative</th><th className="num">Calls / target</th><th className="num">Coverage / target</th><th className="num">Expenses approved</th><th className="num">Pending</th>{manage && <th>Set target</th>}</tr></thead>
          <tbody>{data?.map(r => <tr key={r.repUserId}><td>{r.repName}</td>
            <td className="num"><span className={r.callsTarget ? (r.callsMet ? 'badge ok' : 'badge warn') : ''}>{r.calls} / {r.callsTarget || '-'}</span></td>
            <td className="num"><span className={r.coverageTargetPercent ? (r.coverageMet ? 'badge ok' : 'badge warn') : ''}>{r.coveragePercent}% / {r.coverageTargetPercent ? `${r.coverageTargetPercent}%` : '-'}</span></td>
            <td className="num">{money(r.expensesApproved)}</td><td className="num">{r.expensesPending ? money(r.expensesPending) : '-'}</td>
            {manage && <td><TargetEditor calls={r.callsTarget} coverage={r.coverageTargetPercent} name={r.repName} onSave={(a, b) => setTarget(r.repUserId, a, b)} /></td>}</tr>)}</tbody>
        </table>
      </div>
    </>
  );
}

function TargetEditor({ calls, coverage, name, onSave }: { calls: number; coverage: number; name: string; onSave: (calls: number, coverage: number) => void }) {
  const [a, setA] = useState(String(calls || ''));
  const [b, setB] = useState(String(coverage || ''));
  return (
    <div className="row">
      <input className="qty" type="number" min={0} aria-label={`Calls target ${name}`} placeholder="calls" value={a} onChange={e => setA(e.target.value)} />
      <input className="qty" type="number" min={0} max={100} aria-label={`Coverage target ${name}`} placeholder="%" value={b} onChange={e => setB(e.target.value)} />
      <button className="small" onClick={() => onSave(Number(a) || 0, Number(b) || 0)}>Save</button>
    </div>
  );
}

// ================= Controlled drugs register =================

export function ControlledDrugsPage() {
  const [from, setFrom] = useState(today().slice(0, 8) + '01');
  const [to, setTo] = useState(today());
  const { data, error } = useLoad(() => controlledDrugRegister(from, to), [from, to]);
  return (
    <>
      <PageHead title="Narcotic & Schedule X register" sub="Every receipt and issue of controlled items, per batch, with running balance. Confirm the statutory format with your drug controller.">
        <Field label="From"><input type="date" value={from} onChange={e => setFrom(e.target.value)} /></Field>
        <Field label="To"><input type="date" value={to} onChange={e => setTo(e.target.value)} /></Field>
        <button onClick={() => window.print()}>Print</button>
      </PageHead>
      <ErrorBanner error={error} />
      {data?.length === 0 && <div className="panel empty">No controlled items moved in this period.</div>}
      {data?.map(b => (
        <div key={b.batchId} className="panel invoice">
          <div className="row"><b>{b.productName}</b><span className="badge danger">{b.schedule === 'NarcoticPsychotropic' ? 'Narcotic/Psychotropic' : `Schedule ${b.schedule}`}</span>
            <span className="small muted">Batch {b.batchNo} · exp {expiry(b.expiryDate)}</span><span className="spacer" />
            <span className="small">Opening <b>{qty(b.opening)}</b> · In {qty(b.received)} · Out {qty(b.issued)} · Closing <b>{qty(b.closing)}</b> {b.baseUnit}</span></div>
          <table className="small">
            <thead><tr><th>Date/time</th><th>Type</th><th>Document</th><th>Party</th><th>Prescriber</th><th className="num">In</th><th className="num">Out</th><th className="num">Balance</th></tr></thead>
            <tbody>{b.entries.map((e, i) => <tr key={i}><td>{dateTime(e.at)}</td><td>{e.type}</td><td>{e.documentNo ?? '-'}</td><td>{e.party ?? '-'}</td><td>{e.prescriber ?? '-'}</td>
              <td className="num">{e.in ? qty(e.in) : ''}</td><td className="num">{e.out ? qty(e.out) : ''}</td><td className="num">{qty(e.balance)}</td></tr>)}</tbody>
          </table>
        </div>
      ))}
    </>
  );
}

// ================= Manufacturing: deviations & CAPA =================

export function DeviationsPage() {
  const { can } = useAuth();
  const [status, setStatus] = useState('');
  const { data, error, reload } = useLoad(() => deviationApi.list(status || undefined), [status]);
  const [raising, setRaising] = useState(false);
  const [open, setOpen] = useState<Deviation | null>(null);
  return (
    <>
      <PageHead title="Deviations & CAPA" sub="Open Major/Critical deviations on a batch block its release until QA closes them.">
        <select aria-label="Status" value={status} onChange={e => setStatus(e.target.value)}><option value="">All</option><option>Open</option><option value="CapaInProgress">CAPA in progress</option><option>Closed</option></select>
        <button className="primary" onClick={() => setRaising(true)}>Raise deviation</button>
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error} />
        {data?.length === 0 ? <div className="empty">No deviations.</div> : (
          <table>
            <thead><tr><th>No.</th><th>Title</th><th>Batch</th><th>Severity</th><th>CAPA due</th><th>Status</th></tr></thead>
            <tbody>{data?.map(d => <tr key={d.id} className="clickable" onClick={() => setOpen(d)}><td><b>{d.deviationNo}</b><div className="small muted">{dateTime(d.raisedAt)} · {d.raisedBy}</div></td>
              <td>{d.title}</td><td>{d.batchNo ?? '-'}</td><td><span className={`badge ${d.severity === 'Minor' ? '' : 'danger'}`}>{d.severity}</span></td>
              <td>{d.capaDueDate ?? '-'}{d.overdue && <span className="badge danger"> overdue</span>}</td><td><Badge s={d.status} /></td></tr>)}</tbody>
          </table>
        )}
      </div>
      {raising && <RaiseForm onClose={() => { setRaising(false); reload(); }} />}
      {open && <DeviationModal dev={open} qa={can('Owner', 'QaManager')} onClose={() => { setOpen(null); reload(); }} />}
    </>
  );
}

function RaiseForm({ onClose }: { onClose: () => void }) {
  const batches = useLoad(() => mfgApi.batches(), []);
  const [f, setF] = useState({ productionBatchId: '', title: '', description: '', severity: 'Minor' as DeviationSeverity });
  const [err, setErr] = useState<unknown>(null);
  const save = async () => {
    setErr(null);
    try { const d = await deviationApi.raise({ ...f, productionBatchId: f.productionBatchId || null }); toast(`${d.deviationNo} raised`); onClose(); } catch (e) { setErr(e); }
  };
  return (
    <Modal title="Raise deviation" onClose={onClose}>
      <ErrorBanner error={err} />
      <Field label="Batch (optional)"><select aria-label="Batch" value={f.productionBatchId} onChange={e => setF({ ...f, productionBatchId: e.target.value })}><option value="">Not batch-specific</option>
        {batches.data?.map(b => <option key={b.id} value={b.id}>{b.batchNo} — {b.productName}</option>)}</select></Field>
      <Field label="Title"><input value={f.title} onChange={e => setF({ ...f, title: e.target.value })} /></Field>
      <Field label="What happened"><textarea rows={3} value={f.description} onChange={e => setF({ ...f, description: e.target.value })} /></Field>
      <Field label="Severity"><select aria-label="Severity" value={f.severity} onChange={e => setF({ ...f, severity: e.target.value as DeviationSeverity })}><option>Minor</option><option>Major</option><option>Critical</option></select></Field>
      <div className="row end"><button className="primary" disabled={!f.title.trim() || !f.description.trim()} onClick={save}>Raise</button></div>
    </Modal>
  );
}

function DeviationModal({ dev, qa, onClose }: { dev: Deviation; qa: boolean; onClose: () => void }) {
  const [d, setD] = useState(dev);
  const users = useLoad(() => api.users().catch(() => []), []);
  const [f, setF] = useState({ rootCause: dev.rootCause ?? '', correctiveAction: dev.correctiveAction ?? '', preventiveAction: dev.preventiveAction ?? '', capaOwnerUserId: '', capaDueDate: dev.capaDueDate ?? '' });
  const [err, setErr] = useState<unknown>(null);
  const [signing, setSigning] = useState(false);
  const save = async () => {
    setErr(null);
    try {
      setD(await deviationApi.investigate(d.id, { ...f, preventiveAction: f.preventiveAction || null, capaOwnerUserId: f.capaOwnerUserId || null, capaDueDate: f.capaDueDate || null, version: d.version }));
      toast('Investigation saved');
    } catch (e) { setErr(e); }
  };
  const closed = d.status === 'Closed';
  return (
    <Modal title={`${d.deviationNo} — ${d.title}`} onClose={onClose} wide>
      <ErrorBanner error={err} />
      <div className="row"><Badge s={d.status} /><span className={`badge ${d.severity === 'Minor' ? '' : 'danger'}`}>{d.severity}</span><span className="small muted">{d.batchNo && `batch ${d.batchNo} · `}raised by {d.raisedBy} {dateTime(d.raisedAt)}</span></div>
      <p>{d.description}</p>
      {d.closureSignature && <div className="alert ok">{d.closureSignature}</div>}
      <Field label="Root cause"><textarea rows={2} disabled={closed} value={f.rootCause} onChange={e => setF({ ...f, rootCause: e.target.value })} /></Field>
      <Field label="Corrective action"><textarea rows={2} disabled={closed} value={f.correctiveAction} onChange={e => setF({ ...f, correctiveAction: e.target.value })} /></Field>
      <Field label="Preventive action"><textarea rows={2} disabled={closed} value={f.preventiveAction} onChange={e => setF({ ...f, preventiveAction: e.target.value })} /></Field>
      {!closed && <div className="grid c2">
        <Field label="CAPA owner"><select aria-label="CAPA owner" value={f.capaOwnerUserId} onChange={e => setF({ ...f, capaOwnerUserId: e.target.value })}><option value="">{d.capaOwner ?? 'Choose…'}</option>
          {users.data?.filter(u => u.isActive).map(u => <option key={u.id} value={u.id}>{u.displayName}</option>)}</select></Field>
        <Field label="CAPA due"><input type="date" value={f.capaDueDate} onChange={e => setF({ ...f, capaDueDate: e.target.value })} /></Field>
      </div>}
      {!closed && <div className="row end">
        <button disabled={!f.rootCause.trim() || !f.correctiveAction.trim()} onClick={save}>Save investigation</button>
        {qa && d.status === 'CapaInProgress' && <button className="primary" onClick={() => setSigning(true)}>Close (QA)</button>}
      </div>}
      {signing && <SignModal title={`Close ${d.deviationNo}`} meaning="The root cause is established and the CAPA is in place and effective." onClose={() => setSigning(false)}
        onSign={async (pw, note) => { setD(await deviationApi.close(d.id, pw, note || null)); toast('Deviation closed'); }} />}
    </Modal>
  );
}

