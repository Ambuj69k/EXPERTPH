import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import {
  api, ApiError, sfaApi, type CallInput, type DailyReport, type Doctor, type DoctorClass, type DoctorInput, type SfaStatus, type TeamMember,
  type Territory, type TourPlan, type User,
} from '../api';
import { useAuth } from '../auth';
import ProductPicker from '../components/ProductPicker';
import { ErrorBanner, Field, Modal, PageHead, toast, useDebounced, useLoad } from '../ui';
import { dateTime, newId, today } from '../util';

const CLASSES: DoctorClass[] = ['A', 'B', 'C'];
const NORM: Record<DoctorClass, number> = { A: 4, B: 2, C: 1 };
const monthOf = (d: string) => d.slice(0, 7) + '-01';
const addDays = (iso: string, n: number) => { const d = new Date(iso + 'T00:00:00'); d.setDate(d.getDate() + n); return d.toLocaleDateString('en-CA'); };

function SfaBadge({ status }: { status: SfaStatus }) {
  const cls = status === 'Approved' ? 'ok' : status === 'Rejected' ? 'danger' : status === 'Submitted' ? 'brand' : 'warn';
  const label = status === 'Rejected' ? 'Sent back' : status;
  return <span className={`badge ${cls}`}>{label}</span>;
}

// ---------------- offline outbox for calls ----------------
// Calls carry a clientRequestId, so a call saved on the phone while offline can be re-sent safely later.

interface PendingCall { reportId: string; doctorName: string; call: CallInput }
const outboxKey = (userId: string) => `pharmaos.sfa.outbox.${userId}`;
const readOutbox = (userId: string): PendingCall[] => { try { return JSON.parse(localStorage.getItem(outboxKey(userId)) ?? '[]'); } catch { return []; } };
const writeOutbox = (userId: string, items: PendingCall[]) => { try { localStorage.setItem(outboxKey(userId), JSON.stringify(items)); } catch { /* storage full/blocked */ } };

function useCallOutbox(userId: string, onSent: () => void) {
  const [pending, setPending] = useState<PendingCall[]>(() => readOutbox(userId));
  const [lastError, setLastError] = useState<string | null>(null);
  const add = (p: PendingCall) => { const next = [...readOutbox(userId), p]; writeOutbox(userId, next); setPending(next); };
  const flush = useCallback(async () => {
    const items = readOutbox(userId);
    if (items.length === 0) return;
    const left: PendingCall[] = [];
    let sent = 0;
    for (const p of items) {
      try { await sfaApi.addCall(p.reportId, p.call); sent++; } catch (e) {
        if (e instanceof ApiError && e.status === 0) left.push(p);         // still offline: keep
        else setLastError(`${p.doctorName}: ${e instanceof Error ? e.message : String(e)}`); // rejected by server: drop and show why
      }
    }
    writeOutbox(userId, left); setPending(left);
    if (sent) { toast(`${sent} saved call(s) sent`); onSent(); }
  }, [userId, onSent]);
  useEffect(() => {
    flush();
    window.addEventListener('online', flush);
    return () => window.removeEventListener('online', flush);
  }, [flush]);
  return { pending, add, flush, lastError, clearError: () => setLastError(null) };
}

// ---------------- My day (DCR) ----------------

export function MyDayPage() {
  const { session } = useAuth();
  const [date, setDate] = useState(today());
  const [report, setReport] = useState<DailyReport | null>(null);
  const [err, setErr] = useState<unknown>(null);
  const [logFor, setLogFor] = useState<string | 'any' | null>(null);
  const [notes, setNotes] = useState('');
  const load = useCallback(() => {
    setErr(null);
    sfaApi.openDay(date).then(r => { setReport(r); setNotes(r.notes ?? ''); }).catch(e => { setReport(null); setErr(e); });
  }, [date]);
  useEffect(load, [load]);
  const outbox = useCallOutbox(session!.userId, load);
  const editable = report && (report.status === 'Draft' || report.status === 'Rejected');
  const calledIds = new Set(report?.calls.map(c => c.doctorId));
  const queuedHere = outbox.pending.filter(p => p.reportId === report?.id);

  const remove = async (callId: string) => {
    if (!report || !confirm('Remove this call? Samples given in it go back to your bag.')) return;
    try { setReport(await sfaApi.removeCall(report.id, callId)); } catch (e) { setErr(e); }
  };
  const submit = async () => {
    if (!report) return;
    if (queuedHere.length) { setErr(new Error('Some calls are still saved only on this phone. Go online so they are sent first.')); return; }
    try { setReport(await sfaApi.submitReport(report.id, report.version, notes.trim() || null)); toast('Report submitted to your manager'); } catch (e) { setErr(e); }
  };

  return (
    <>
      <PageHead title="My day" sub="Log each doctor visit as you go. Submit the report at the end of the day.">
        <Field label="Date"><input type="date" value={date} max={today()} min={addDays(today(), -7)} onChange={e => setDate(e.target.value)} /></Field>
      </PageHead>
      {outbox.pending.length > 0 && <div className="alert warn">{outbox.pending.length} call(s) saved on this phone, waiting for internet. <button className="small" onClick={outbox.flush}>Send now</button></div>}
      {outbox.lastError && <div className="alert error">A saved call was not accepted — {outbox.lastError} <button className="small" onClick={outbox.clearError}>OK</button></div>}
      <ErrorBanner error={err} />
      {report && <>
        <div className="panel">
          <div className="row"><b>{report.date}</b><SfaBadge status={report.status} /><span className="spacer" />
            {editable && <button className="primary" onClick={() => setLogFor('any')}>+ Log a call</button>}
          </div>
          {report.reviewComment && <div className={`alert ${report.status === 'Rejected' ? 'error' : 'ok'}`}>Manager: {report.reviewComment}</div>}
          {report.planned.length > 0 && <>
            <h3>Planned today</h3>
            <div className="cards">
              {report.planned.map(p => (
                <div key={p.doctorId} className="card">
                  <div className="row"><b>{p.doctorName}</b><span className="badge">{p.class}</span><span className="spacer" />
                    {calledIds.has(p.doctorId) ? <span className="badge ok">Met</span> : editable && <button className="small" onClick={() => setLogFor(p.doctorId)}>Log call</button>}
                  </div>
                </div>
              ))}
            </div>
          </>}
          <h3>Calls ({report.calls.length + queuedHere.length})</h3>
          {report.calls.length + queuedHere.length === 0 ? <div className="empty">No calls yet.</div> : (
            <div className="cards">
              {report.calls.map(c => (
                <div key={c.id} className="card">
                  <div className="row"><b>{c.doctorName}</b><span className="badge">{c.class}</span>{!c.wasPlanned && <span className="badge warn">Unplanned</span>}<span className="spacer" />
                    {editable && <button className="link" aria-label={`Remove ${c.doctorName}`} onClick={() => remove(c.id)}>✕</button>}</div>
                  <div className="small muted">{dateTime(c.callAt)}{c.jointWorkWith && ` · with ${c.jointWorkWith}`}{c.hasLocation && ' · location attached'}</div>
                  {c.productsDetailed && <div className="small">Detailed: {c.productsDetailed}</div>}
                  {c.feedback && <div className="small">Feedback: {c.feedback}</div>}
                  {c.samples.length > 0 && <div className="small">Samples: {c.samples.map(s => `${s.productName} × ${s.qty}`).join(', ')}</div>}
                </div>
              ))}
              {queuedHere.map(p => <div key={p.call.clientRequestId} className="card"><div className="row"><b>{p.doctorName}</b><span className="badge warn">Saved on phone</span></div></div>)}
            </div>
          )}
        </div>
        <div className="panel">
          <Field label="Notes for the day (meetings, leave, travel)"><textarea rows={2} value={notes} disabled={!editable} onChange={e => setNotes(e.target.value)} /></Field>
          {editable && <div className="row end"><button className="primary" onClick={submit}>Submit report</button></div>}
        </div>
      </>}
      {logFor && report && <CallForm report={report} doctorId={logFor === 'any' ? null : logFor} onQueue={outbox.add}
        onClose={r => { setLogFor(null); if (r) setReport(r); }} />}
    </>
  );
}

function CallForm({ report, doctorId, onQueue, onClose }: { report: DailyReport; doctorId: string | null; onQueue: (p: PendingCall) => void; onClose: (r: DailyReport | null) => void }) {
  const doctors = useLoad(() => sfaApi.doctors(), []);
  const bag = useLoad(() => sfaApi.samples(report.repUserId), [report.repUserId]);
  const team = useLoad(() => sfaApi.team(), []);
  const [f, setF] = useState({ doctorId: doctorId ?? '', productsDetailed: '', feedback: '', jointWorkUserId: '', location: false });
  const [samples, setSamples] = useState<Record<string, number>>({});
  const [requestId] = useState(newId);
  const [err, setErr] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const done = new Set(report.calls.map(c => c.doctorId));
  const managers = (team.data ?? []).filter(t => t.role === 'AreaManager' || t.role === 'Owner');
  const save = async () => {
    setErr(null); setBusy(true);
    let latitude: number | null = null, longitude: number | null = null;
    if (f.location) {
      try {
        const pos = await new Promise<GeolocationPosition>((ok, fail) => navigator.geolocation.getCurrentPosition(ok, fail, { timeout: 8000 }));
        latitude = Math.round(pos.coords.latitude * 1e6) / 1e6; longitude = Math.round(pos.coords.longitude * 1e6) / 1e6;
      } catch { setErr(new Error('Could not read your location. Untick "attach my location" or allow location access.')); setBusy(false); return; }
    }
    const call: CallInput = {
      clientRequestId: requestId, doctorId: f.doctorId, productsDetailed: f.productsDetailed.trim() || null, feedback: f.feedback.trim() || null,
      jointWorkUserId: f.jointWorkUserId || null, latitude, longitude,
      samples: Object.entries(samples).filter(([, q]) => q > 0).map(([productId, qty]) => ({ productId, qty })),
    };
    try { onClose(await sfaApi.addCall(report.id, call)); toast('Call saved'); }
    catch (e) {
      if (e instanceof ApiError && e.status === 0) {
        onQueue({ reportId: report.id, doctorName: doctors.data?.find(d => d.id === f.doctorId)?.name ?? 'Doctor', call });
        toast('No internet — call saved on this phone'); onClose(null);
      } else setErr(e);
    } finally { setBusy(false); }
  };
  return (
    <Modal title="Log a call" onClose={() => onClose(null)}>
      <ErrorBanner error={err ?? doctors.error} />
      <Field label="Doctor">
        <select aria-label="Doctor" value={f.doctorId} onChange={e => setF({ ...f, doctorId: e.target.value })}>
          <option value="">Choose…</option>
          {doctors.data?.filter(d => !done.has(d.id)).map(d => <option key={d.id} value={d.id}>{d.name} ({d.class}){d.clinic ? ` · ${d.clinic}` : ''}</option>)}
        </select>
      </Field>
      <Field label="Products detailed"><input value={f.productsDetailed} onChange={e => setF({ ...f, productsDetailed: e.target.value })} placeholder="Brand A, Brand B" /></Field>
      <Field label="Doctor's feedback"><textarea rows={2} value={f.feedback} onChange={e => setF({ ...f, feedback: e.target.value })} /></Field>
      {managers.length > 0 && <Field label="Joint work with">
        <select value={f.jointWorkUserId} onChange={e => setF({ ...f, jointWorkUserId: e.target.value })}><option value="">Alone</option>{managers.map(m => <option key={m.userId} value={m.userId}>{m.name}</option>)}</select>
      </Field>}
      {(bag.data ?? []).some(b => b.balance > 0) && <>
        <h3>Samples given</h3>
        <table><tbody>{bag.data!.filter(b => b.balance > 0).map(b => (
          <tr key={b.productId}><td>{b.productName}<div className="small muted">{b.balance} in bag</div></td>
            <td className="num"><input className="qty" type="number" min={0} max={b.balance} inputMode="numeric" aria-label={`Samples ${b.productName}`} value={samples[b.productId] ?? 0}
              onChange={e => setSamples({ ...samples, [b.productId]: Math.min(b.balance, Math.max(0, Math.floor(Number(e.target.value) || 0))) })} /></td></tr>
        ))}</tbody></table>
      </>}
      <label className="check"><input type="checkbox" checked={f.location} onChange={e => setF({ ...f, location: e.target.checked })} /> Attach my current location to this call</label>
      <p className="small muted">Location is taken only when you tick the box, once, for this call.</p>
      <div className="row end"><button className="primary" disabled={busy || !f.doctorId} onClick={save}>Save call</button></div>
    </Modal>
  );
}

// ---------------- Doctors ----------------

export function DoctorsPage() {
  const { can } = useAuth();
  const [q, setQ] = useState('');
  const dq = useDebounced(q);
  const [territoryId, setTerritoryId] = useState('');
  const territories = useLoad(() => sfaApi.territories(), []);
  const { data, error, reload } = useLoad(() => sfaApi.doctors(dq, territoryId || undefined, true), [dq, territoryId]);
  const [edit, setEdit] = useState<Doctor | 'new' | null>(null);
  const [err, setErr] = useState<unknown>(null);
  const anonymise = async (d: Doctor) => {
    if (!confirm(`Remove ${d.name}'s personal details permanently? Visit counts are kept. This cannot be undone.`)) return;
    try { await sfaApi.anonymiseDoctor(d.id); toast('Details removed'); reload(); } catch (e) { setErr(e); }
  };
  return (
    <>
      <PageHead title="Doctors" sub="Doctors in your territories. Class sets how often they should be visited (A 4×, B 2×, C 1× a month).">
        <input placeholder="Search name, clinic, area" value={q} onChange={e => setQ(e.target.value)} aria-label="Search doctors" />
        <select value={territoryId} onChange={e => setTerritoryId(e.target.value)} aria-label="Territory"><option value="">All territories</option>{territories.data?.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}</select>
        <button className="primary" onClick={() => setEdit('new')}>Add doctor</button>
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error ?? err} />
        {data?.length === 0 ? <div className="empty">No doctors yet.</div> : (
          <div className="table-wrap">
            <table>
              <thead><tr><th>Doctor</th><th>Class</th><th>Clinic / area</th><th>Territory</th><th>Last visit</th><th>Consent on file</th><th /></tr></thead>
              <tbody>{data?.map(d => (
                <tr key={d.id} className={d.isActive ? '' : 'muted'}>
                  <td><b>{d.name}</b><div className="small muted">{[d.speciality, d.qualification].filter(Boolean).join(' · ')}</div></td>
                  <td><span className="badge">{d.class}</span></td><td className="small">{[d.clinic, d.area].filter(Boolean).join(', ') || '-'}</td>
                  <td>{d.territoryName}</td><td>{d.lastVisit ?? '-'}</td><td>{d.consentRecordedOn ?? <span className="badge warn">Not recorded</span>}</td>
                  <td className="row end">
                    {!d.anonymised && <button className="small" onClick={() => setEdit(d)}>Edit</button>}
                    {can('Owner') && !d.anonymised && <button className="small danger" onClick={() => anonymise(d)}>Remove details</button>}
                  </td>
                </tr>
              ))}</tbody>
            </table>
          </div>
        )}
        <p className="small muted">Doctor details are personal data. Keep only what visits need, note when the doctor agreed, and remove details when asked.</p>
      </div>
      {edit && <DoctorForm doctor={edit === 'new' ? null : edit} territories={territories.data ?? []} onClose={saved => { setEdit(null); if (saved) reload(); }} />}
    </>
  );
}

function DoctorForm({ doctor, territories, onClose }: { doctor: Doctor | null; territories: Territory[]; onClose: (saved: boolean) => void }) {
  const [f, setF] = useState<DoctorInput>(doctor ? { ...doctor } : {
    name: '', speciality: '', qualification: '', clinic: '', area: '', phone: '', territoryId: territories.length === 1 ? territories[0].id : '', class: 'B', consentRecordedOn: null, isActive: true,
  });
  const [err, setErr] = useState<unknown>(null);
  const set = (p: Partial<DoctorInput>) => setF({ ...f, ...p });
  const save = async () => {
    setErr(null);
    try { await sfaApi.saveDoctor(doctor?.id ?? null, { ...f, consentRecordedOn: f.consentRecordedOn || null, version: doctor?.version ?? null }); toast('Doctor saved'); onClose(true); }
    catch (e) { setErr(e); }
  };
  return (
    <Modal title={doctor ? `Edit ${doctor.name}` : 'Add doctor'} onClose={() => onClose(false)}>
      <ErrorBanner error={err} />
      <div className="grid c2">
        <Field label="Name"><input value={f.name} onChange={e => set({ name: e.target.value })} autoFocus /></Field>
        <Field label="Class"><select value={f.class} onChange={e => set({ class: e.target.value as DoctorClass })}>{CLASSES.map(c => <option key={c} value={c}>{c} — {NORM[c]} visit(s) a month</option>)}</select></Field>
        <Field label="Speciality"><input value={f.speciality ?? ''} onChange={e => set({ speciality: e.target.value })} /></Field>
        <Field label="Qualification"><input value={f.qualification ?? ''} onChange={e => set({ qualification: e.target.value })} /></Field>
        <Field label="Clinic / hospital"><input value={f.clinic ?? ''} onChange={e => set({ clinic: e.target.value })} /></Field>
        <Field label="Area"><input value={f.area ?? ''} onChange={e => set({ area: e.target.value })} /></Field>
        <Field label="Clinic phone"><input value={f.phone ?? ''} onChange={e => set({ phone: e.target.value })} /></Field>
        <Field label="Territory"><select value={f.territoryId} onChange={e => set({ territoryId: e.target.value })}><option value="">Choose…</option>{territories.filter(t => t.isActive).map(t => <option key={t.id} value={t.id}>{t.name}</option>)}</select></Field>
        <Field label="Doctor agreed on (consent date)"><input type="date" max={today()} value={f.consentRecordedOn ?? ''} onChange={e => set({ consentRecordedOn: e.target.value })} /></Field>
      </div>
      <label className="check"><input type="checkbox" checked={f.isActive} onChange={e => set({ isActive: e.target.checked })} /> Active</label>
      <div className="row end"><button className="primary" disabled={!f.name.trim() || !f.territoryId} onClick={save}>Save</button></div>
    </Modal>
  );
}

// ---------------- Tour plan ----------------

export function TourPlanPage() {
  const { can } = useAuth();
  const isRep = can('MedicalRep');
  const [month, setMonth] = useState(monthOf(today()));
  return (
    <>
      <PageHead title="Tour plan" sub={isRep ? 'Plan which doctors you will see on which day, then send the plan to your manager.' : 'Monthly plans from your representatives.'}>
        <Field label="Month"><input type="month" value={month.slice(0, 7)} onChange={e => e.target.value && setMonth(e.target.value + '-01')} /></Field>
      </PageHead>
      {isRep ? <MyPlan month={month} /> : <TeamPlans month={month} />}
    </>
  );
}

function MyPlan({ month }: { month: string }) {
  const { data: plan, error, setData } = useLoad(() => sfaApi.myTourPlan(month), [month]);
  const doctors = useLoad(() => sfaApi.doctors(), []);
  const [date, setDate] = useState(today() >= month ? today() : month);
  const [pick, setPick] = useState('');
  const [err, setErr] = useState<unknown>(null);
  useEffect(() => { setDate(today().slice(0, 7) === month.slice(0, 7) ? today() : month); }, [month]);
  const editable = plan && (plan.status === 'Draft' || plan.status === 'Rejected');
  const save = async (entries: { date: string; doctorId: string }[]) => {
    if (!plan) return; setErr(null);
    try { setData(await sfaApi.saveTourPlan(plan.id, plan.version, entries)); } catch (e) { setErr(e); }
  };
  const entries = plan?.entries ?? [];
  const add = () => { if (pick && !entries.some(e => e.date === date && e.doctorId === pick)) save([...entries.map(x => ({ date: x.date, doctorId: x.doctorId })), { date, doctorId: pick }]); setPick(''); };
  const byDate = useMemo(() => {
    const g: Record<string, typeof entries> = {};
    entries.forEach(e => { (g[e.date] ??= []).push(e); });
    return Object.entries(g).sort(([a], [b]) => a.localeCompare(b));
  }, [entries]);
  const plannedCount = (id: string) => entries.filter(e => e.doctorId === id).length;
  const lastDay = addDays(addDays(month, 32).slice(0, 8) + '01', -1);
  return (
    <div className="panel">
      <ErrorBanner error={error ?? err} />
      {plan && <>
        <div className="row"><SfaBadge status={plan.status} /><span className="small muted">{entries.length} planned visit(s)</span><span className="spacer" />
          {editable && <button className="primary" disabled={entries.length === 0} onClick={async () => { try { setData(await sfaApi.submitTourPlan(plan.id)); toast('Plan sent to your manager'); } catch (e) { setErr(e); } }}>Submit plan</button>}
        </div>
        {plan.reviewComment && <div className={`alert ${plan.status === 'Rejected' ? 'error' : 'ok'}`}>{plan.reviewedBy}: {plan.reviewComment}</div>}
        {editable && <div className="row">
          <Field label="Day"><input type="date" min={month} max={lastDay} value={date} onChange={e => setDate(e.target.value)} /></Field>
          <Field label="Doctor"><select value={pick} onChange={e => setPick(e.target.value)}><option value="">Choose…</option>
            {doctors.data?.map(d => <option key={d.id} value={d.id}>{d.name} ({d.class}) — planned {plannedCount(d.id)}/{NORM[d.class]}</option>)}</select></Field>
          <button disabled={!pick} onClick={add}>Add</button>
        </div>}
        {byDate.length === 0 ? <div className="empty">Nothing planned yet.</div> : (
          <table>
            <thead><tr><th>Day</th><th>Doctors</th></tr></thead>
            <tbody>{byDate.map(([d, list]) => (
              <tr key={d}><td>{new Date(d + 'T00:00:00').toLocaleDateString('en-IN', { weekday: 'short', day: '2-digit', month: 'short' })}</td>
                <td>{list.map(e => <span key={e.doctorId} className="badge" style={{ marginRight: 4 }}>{e.doctorName} ({e.class})
                  {editable && <button className="link" aria-label={`Remove ${e.doctorName} on ${d}`} onClick={() => save(entries.filter(x => !(x.date === d && x.doctorId === e.doctorId)).map(x => ({ date: x.date, doctorId: x.doctorId })))}> ✕</button>}</span>)}</td></tr>
            ))}</tbody>
          </table>
        )}
      </>}
    </div>
  );
}

function TeamPlans({ month }: { month: string }) {
  const { data, error, reload } = useLoad(() => sfaApi.tourPlans(month), [month]);
  const [open, setOpen] = useState<TourPlan | null>(null);
  return (
    <div className="panel">
      <ErrorBanner error={error} />
      {data?.length === 0 ? <div className="empty">No plans for this month yet.</div> : (
        <table>
          <thead><tr><th>Representative</th><th className="num">Visits planned</th><th>Status</th></tr></thead>
          <tbody>{data?.map(p => <tr key={p.id} className="clickable" onClick={() => setOpen(p)}><td>{p.repName}</td><td className="num">{p.entries.length}</td><td><SfaBadge status={p.status} /></td></tr>)}</tbody>
        </table>
      )}
      {open && <ReviewModal title={`Tour plan — ${open.repName}`} status={open.status} onClose={() => { setOpen(null); reload(); }}
        review={(approve, comment) => sfaApi.reviewTourPlan(open.id, approve, comment).then(() => undefined)}>
        <table><tbody>{open.entries.map(e => <tr key={e.date + e.doctorId}><td>{e.date}</td><td>{e.doctorName}</td><td>{e.class}</td></tr>)}</tbody></table>
      </ReviewModal>}
    </div>
  );
}

function ReviewModal({ title, status, review, onClose, children }: {
  title: string; status: SfaStatus; review: (approve: boolean, comment?: string) => Promise<void>; onClose: () => void; children: ReactNode;
}) {
  const [comment, setComment] = useState('');
  const [err, setErr] = useState<unknown>(null);
  const act = async (approve: boolean) => {
    setErr(null);
    try { await review(approve, comment.trim() || undefined); toast(approve ? 'Approved' : 'Sent back'); onClose(); } catch (e) { setErr(e); }
  };
  return (
    <Modal title={title} onClose={onClose} wide>
      <ErrorBanner error={err} />
      <div className="row"><SfaBadge status={status} /></div>
      {children}
      {status === 'Submitted' && <>
        <Field label="Comment (required when sending back)"><input value={comment} onChange={e => setComment(e.target.value)} /></Field>
        <div className="row end"><button className="danger" onClick={() => act(false)}>Send back</button><button className="primary" onClick={() => act(true)}>Approve</button></div>
      </>}
    </Modal>
  );
}

// ---------------- Approvals (daily reports) ----------------

export function FieldReportsPage() {
  const [from, setFrom] = useState(addDays(today(), -14));
  const [to, setTo] = useState(today());
  const [status, setStatus] = useState('Submitted');
  const { data, error, reload } = useLoad(() => sfaApi.reports(from, to, status || undefined), [from, to, status]);
  const [open, setOpen] = useState<string | null>(null);
  return (
    <>
      <PageHead title="Daily reports" sub="Call reports from your representatives. Approve them, or send back with what to fix.">
        <Field label="From"><input type="date" value={from} onChange={e => setFrom(e.target.value)} /></Field>
        <Field label="To"><input type="date" value={to} onChange={e => setTo(e.target.value)} /></Field>
        <select value={status} onChange={e => setStatus(e.target.value)} aria-label="Status"><option value="">All</option>{['Submitted', 'Approved', 'Rejected', 'Draft'].map(s => <option key={s}>{s}</option>)}</select>
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error} />
        {data?.length === 0 ? <div className="empty">Nothing here.</div> : (
          <table>
            <thead><tr><th>Date</th><th>Representative</th><th className="num">Calls</th><th>Submitted</th><th>Status</th></tr></thead>
            <tbody>{data?.map(r => <tr key={r.id} className="clickable" onClick={() => setOpen(r.id)}><td>{r.date}</td><td>{r.repName}</td><td className="num">{r.calls}</td><td>{r.submittedAt ? dateTime(r.submittedAt) : '-'}</td><td><SfaBadge status={r.status} /></td></tr>)}</tbody>
          </table>
        )}
      </div>
      {open && <ReportReview id={open} onClose={() => { setOpen(null); reload(); }} />}
    </>
  );
}

function ReportReview({ id, onClose }: { id: string; onClose: () => void }) {
  const { data: r, error } = useLoad(() => sfaApi.report(id), [id]);
  if (!r) return <Modal title="Daily report" onClose={onClose}><ErrorBanner error={error} /></Modal>;
  return (
    <ReviewModal title={`${r.repName} — ${r.date}`} status={r.status} onClose={onClose} review={(a, c) => sfaApi.reviewReport(r.id, a, c).then(() => undefined)}>
      {r.notes && <div className="small">Notes: {r.notes}</div>}
      <table>
        <thead><tr><th>Doctor</th><th>Time</th><th>Detailed</th><th>Feedback</th><th>Samples</th><th /></tr></thead>
        <tbody>{r.calls.map(c => (
          <tr key={c.id}><td>{c.doctorName} <span className="badge">{c.class}</span></td><td>{dateTime(c.callAt)}</td><td className="small">{c.productsDetailed ?? '-'}</td><td className="small">{c.feedback ?? '-'}</td>
            <td className="small">{c.samples.map(s => `${s.productName} × ${s.qty}`).join(', ') || '-'}</td>
            <td className="small">{c.wasPlanned ? 'Planned' : <span className="badge warn">Unplanned</span>}{c.jointWorkWith && ` · with ${c.jointWorkWith}`}{c.hasLocation && ' · 📍'}</td></tr>
        ))}</tbody>
      </table>
      {r.planned.filter(p => !r.calls.some(c => c.doctorId === p.doctorId)).length > 0 &&
        <div className="small">Planned but not met: {r.planned.filter(p => !r.calls.some(c => c.doctorId === p.doctorId)).map(p => p.doctorName).join(', ')}</div>}
    </ReviewModal>
  );
}

// ---------------- Samples ----------------

export function SamplesPage() {
  const { can } = useAuth();
  const manage = can('Owner', 'AreaManager');
  const { data, error, reload } = useLoad(() => sfaApi.samples(), []);
  const team = useLoad(() => manage ? sfaApi.team() : Promise.resolve([] as TeamMember[]), [manage]);
  const [f, setF] = useState({ kind: 'issue' as 'issue' | 'return', repUserId: '', product: null as { id: string; name: string } | null, qty: 0, note: '' });
  const [err, setErr] = useState<unknown>(null);
  const [ledger, setLedger] = useState<string | null>(null);
  const save = async () => {
    setErr(null);
    try {
      await sfaApi.moveSamples(f.kind, { repUserId: f.repUserId, productId: f.product!.id, qty: f.qty, note: f.note.trim() || null });
      toast(f.kind === 'issue' ? 'Samples issued' : 'Return recorded'); setF({ ...f, product: null, qty: 0, note: '' }); reload();
    } catch (e) { setErr(e); }
  };
  return (
    <>
      <PageHead title="Samples" sub="Sample bag of each representative: issued, given to doctors, returned. Kept separate from saleable stock." />
      {manage && <div className="panel">
        <h2>Issue or take back samples</h2>
        <ErrorBanner error={err} />
        <div className="grid c2">
          <Field label="Action"><select value={f.kind} onChange={e => setF({ ...f, kind: e.target.value as 'issue' | 'return' })}><option value="issue">Issue to representative</option><option value="return">Return from representative</option></select></Field>
          <Field label="Representative"><select value={f.repUserId} onChange={e => setF({ ...f, repUserId: e.target.value })}><option value="">Choose…</option>
            {team.data?.filter(t => t.role === 'MedicalRep').map(t => <option key={t.userId} value={t.userId}>{t.name}</option>)}</select></Field>
          <Field label="Product"><ProductPicker value={f.product} onPick={p => setF({ ...f, product: { id: p.id, name: p.name } })} /></Field>
          <Field label="Packs"><input type="number" min={1} value={f.qty || ''} onChange={e => setF({ ...f, qty: Math.max(0, Math.floor(Number(e.target.value) || 0)) })} /></Field>
        </div>
        <Field label="Note"><input value={f.note} onChange={e => setF({ ...f, note: e.target.value })} /></Field>
        <div className="row end"><button className="primary" disabled={!f.repUserId || !f.product || f.qty <= 0} onClick={save}>Save</button></div>
      </div>}
      <div className="panel">
        <ErrorBanner error={error} />
        {data?.length === 0 ? <div className="empty">No samples issued yet.</div> : (
          <table>
            <thead><tr><th>Representative</th><th>Product</th><th className="num">Issued</th><th className="num">Given</th><th className="num">Returned</th><th className="num">In bag</th></tr></thead>
            <tbody>{data?.map(b => <tr key={b.repUserId + b.productId} className="clickable" onClick={() => setLedger(b.repUserId)}><td>{b.repName}</td><td>{b.productName}</td><td className="num">{b.issued}</td><td className="num">{b.given}</td><td className="num">{b.returned}</td><td className="num"><b>{b.balance}</b></td></tr>)}</tbody>
          </table>
        )}
        <p className="small muted">Rules on samples and gifts to doctors (e.g. the pharma marketing code) must be checked by your compliance team; PharmaOS only records them.</p>
      </div>
      {ledger && <SampleLedger repUserId={ledger} onClose={() => setLedger(null)} />}
    </>
  );
}

function SampleLedger({ repUserId, onClose }: { repUserId: string; onClose: () => void }) {
  const { data, error } = useLoad(() => sfaApi.sampleLedger(repUserId), [repUserId]);
  return (
    <Modal title={`Sample movements${data?.[0] ? ` — ${data[0].repName}` : ''}`} onClose={onClose} wide>
      <ErrorBanner error={error} />
      <table><thead><tr><th>When</th><th>Product</th><th>What</th><th className="num">Packs</th><th>Doctor / note</th></tr></thead>
        <tbody>{data?.map((m, i) => <tr key={i}><td>{dateTime(m.at)}</td><td>{m.productName}</td><td>{m.kind}</td><td className="num">{m.qty > 0 ? `+${m.qty}` : m.qty}</td><td className="small">{[m.doctorName, m.note].filter(Boolean).join(' · ')}</td></tr>)}</tbody></table>
    </Modal>
  );
}

// ---------------- Coverage ----------------

export function CoveragePage() {
  const [month, setMonth] = useState(monthOf(today()));
  const { data, error } = useLoad(() => sfaApi.coverage(month), [month]);
  const [open, setOpen] = useState<string | null>(null);
  return (
    <>
      <PageHead title="Doctor coverage" sub="Doctors met as often as their class needs (A 4×, B 2×, C 1× a month), from submitted and approved reports.">
        <Field label="Month"><input type="month" value={month.slice(0, 7)} onChange={e => e.target.value && setMonth(e.target.value + '-01')} /></Field>
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error} />
        <table>
          <thead><tr><th>Representative</th><th className="num">Doctors</th><th className="num">Visits / needed</th><th className="num">Doctors fully covered</th><th className="num">Plan followed</th><th className="num">Reports to approve</th></tr></thead>
          <tbody>{data?.map(r => (
            <tr key={r.repUserId} className="clickable" onClick={() => setOpen(open === r.repUserId ? null : r.repUserId)}>
              <td>{r.repName}</td><td className="num">{r.doctors}</td><td className="num">{r.visits} / {r.requiredVisits}</td>
              <td className="num">{r.doctorsMet} ({r.coveragePercent}%)</td><td className="num">{r.plannedMet} / {r.planned}</td><td className="num">{r.reportsPending || '-'}</td>
            </tr>
          ))}</tbody>
        </table>
        {open && (() => {
          const r = data?.find(x => x.repUserId === open);
          return r && <>
            <h3>Still to cover — {r.repName}</h3>
            {r.missed.length === 0 ? <div className="empty">Every doctor covered.</div> :
              <table className="small"><tbody>{r.missed.map(m => <tr key={m.doctorId}><td>{m.name}</td><td>{m.class}</td><td className="num">{m.visits} of {m.required}</td></tr>)}</tbody></table>}
          </>;
        })()}
      </div>
    </>
  );
}

// ---------------- Territories (owner) ----------------

export function TerritoriesPage() {
  const { data, error, reload } = useLoad(() => sfaApi.territories(), []);
  const users = useLoad(() => api.users(), []);
  const [edit, setEdit] = useState<Territory | 'new' | null>(null);
  return (
    <>
      <PageHead title="Territories" sub="Each territory has one medical representative and the area manager who supervises it.">
        <button className="primary" onClick={() => setEdit('new')}>Add territory</button>
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error ?? users.error} />
        {data?.length === 0 ? <div className="empty">No territories yet. Add users with the MedicalRep and AreaManager roles first.</div> : (
          <table>
            <thead><tr><th>Territory</th><th>Representative</th><th>Area manager</th><th className="num">Doctors</th><th>Status</th><th /></tr></thead>
            <tbody>{data?.map(t => <tr key={t.id}><td><b>{t.name}</b></td><td>{t.repName ?? '-'}</td><td>{t.managerName ?? '-'}</td><td className="num">{t.doctors}</td>
              <td>{t.isActive ? <span className="badge ok">Active</span> : <span className="badge">Off</span>}</td><td><button className="small" onClick={() => setEdit(t)}>Edit</button></td></tr>)}</tbody>
          </table>
        )}
      </div>
      {edit && <TerritoryForm territory={edit === 'new' ? null : edit} users={users.data ?? []} onClose={saved => { setEdit(null); if (saved) reload(); }} />}
    </>
  );
}

function TerritoryForm({ territory, users, onClose }: { territory: Territory | null; users: User[]; onClose: (saved: boolean) => void }) {
  const [f, setF] = useState({ name: territory?.name ?? '', repUserId: territory?.repUserId ?? '', managerUserId: territory?.managerUserId ?? '', isActive: territory?.isActive ?? true });
  const [err, setErr] = useState<unknown>(null);
  const save = async () => {
    setErr(null);
    try { await sfaApi.saveTerritory(territory?.id ?? null, { name: f.name.trim(), repUserId: f.repUserId || null, managerUserId: f.managerUserId || null, isActive: f.isActive }); toast('Territory saved'); onClose(true); }
    catch (e) { setErr(e); }
  };
  const active = users.filter(u => u.isActive);
  return (
    <Modal title={territory ? `Edit ${territory.name}` : 'Add territory'} onClose={() => onClose(false)}>
      <ErrorBanner error={err} />
      <Field label="Name"><input value={f.name} onChange={e => setF({ ...f, name: e.target.value })} autoFocus /></Field>
      <Field label="Medical representative"><select value={f.repUserId} onChange={e => setF({ ...f, repUserId: e.target.value })}><option value="">None</option>{active.filter(u => u.role === 'MedicalRep').map(u => <option key={u.id} value={u.id}>{u.displayName}</option>)}</select></Field>
      <Field label="Area manager"><select value={f.managerUserId} onChange={e => setF({ ...f, managerUserId: e.target.value })}><option value="">None (owner approves)</option>{active.filter(u => u.role === 'AreaManager').map(u => <option key={u.id} value={u.id}>{u.displayName}</option>)}</select></Field>
      <label className="check"><input type="checkbox" checked={f.isActive} onChange={e => setF({ ...f, isActive: e.target.checked })} /> Active</label>
      <div className="row end"><button className="primary" disabled={!f.name.trim()} onClick={save}>Save</button></div>
    </Modal>
  );
}
