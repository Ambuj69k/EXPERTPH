import { useEffect, useState } from 'react';
import { api, hospitalApi, type Department, type FormularyRow, type WardIndent, type WardIssue, type WardReturnReason } from '../api';
import { useAuth } from '../auth';
import ProductPicker from '../components/ProductPicker';
import { ErrorBanner, Field, Modal, PageHead, toast, useDebounced, useLoad } from '../ui';
import { dateTime, expiry, money, today } from '../util';

/** Unit counts (tablets, vials) without forced decimals. */
const num = (n: number) => n.toLocaleString('en-IN', { maximumFractionDigits: 2 });

function Badge({ status }: { status: string }) {
  const cls = status === 'Issued' || status === 'Closed' ? 'ok' : status === 'Cancelled' ? 'danger' : status === 'PendingCheck' ? 'warn' : 'brand';
  const label = status === 'PendingCheck' ? 'Awaiting 2nd check' : status === 'PartlyIssued' ? 'Part issued' : status;
  return <span className={`badge ${cls}`}>{label}</span>;
}

// ---------------- Indents ----------------

export function WardIndentsPage() {
  const { can } = useAuth();
  const pharmacy = can('Owner', 'Pharmacist');
  const [status, setStatus] = useState('');
  const { data, error, reload } = useLoad(() => hospitalApi.indents(status || undefined), [status]);
  const [creating, setCreating] = useState(false);
  const [issueFor, setIssueFor] = useState<WardIndent | null>(null);
  const [err, setErr] = useState<unknown>(null);
  const cancel = async (i: WardIndent) => {
    const reason = prompt(i.status === 'PartlyIssued' ? 'Close this indent short? Reason:' : 'Cancel this indent? Reason:');
    if (!reason?.trim()) return;
    try { await hospitalApi.cancelIndent(i.id, reason.trim()); toast('Indent closed'); reload(); } catch (e) { setErr(e); }
  };
  return (
    <>
      <PageHead title="Ward indents" sub={pharmacy ? 'Requests from wards, urgent first. Issue picks batches by earliest expiry.' : 'Ask the pharmacy for medicines for your ward.'}>
        <select value={status} onChange={e => setStatus(e.target.value)} aria-label="Status"><option value="">Open & recent</option>{['Open', 'PartlyIssued', 'Closed', 'Cancelled'].map(s => <option key={s}>{s}</option>)}</select>
        <button className="primary" onClick={() => setCreating(true)}>New indent</button>
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error ?? err} />
        {data?.length === 0 ? <div className="empty">No indents.</div> : (
          <div className="cards">
            {data?.map(i => (
              <div key={i.id} className="card">
                <div className="row"><b>{i.departmentName}</b>{i.priority === 'Urgent' && <span className="badge danger">Urgent</span>}<span className="spacer" /><Badge status={i.status} /></div>
                <div className="small muted">{i.indentNo} · {dateTime(i.createdAt)}{i.requestedBy && ` · ${i.requestedBy}`}{i.encounterRef && ` · ${i.encounterRef}`}</div>
                <table className="small"><tbody>{i.lines.map(l => (
                  <tr key={l.id}><td>{l.productName}</td><td className="num">{num(l.issuedUnits)} / {num(l.qtyUnits)} {l.baseUnit}</td>
                    {pharmacy && l.pendingUnits > 0 && <td className={`num ${l.stockUnits < l.pendingUnits ? 'danger' : 'muted'}`}>stock {num(l.stockUnits)}</td>}</tr>
                ))}</tbody></table>
                {(i.status === 'Open' || i.status === 'PartlyIssued') && <div className="row end">
                  <button className="small" onClick={() => cancel(i)}>{i.status === 'PartlyIssued' ? 'Close short' : 'Cancel'}</button>
                  {pharmacy && <button className="small primary" onClick={() => setIssueFor(i)}>Issue</button>}
                </div>}
              </div>
            ))}
          </div>
        )}
      </div>
      {creating && <IndentForm onClose={saved => { setCreating(false); if (saved) reload(); }} />}
      {issueFor && <IssueForm indent={issueFor} onClose={done => { setIssueFor(null); if (done) reload(); }} />}
    </>
  );
}

function IndentForm({ onClose }: { onClose: (saved: boolean) => void }) {
  const depts = useLoad(() => hospitalApi.departments(), []);
  const [departmentId, setDepartmentId] = useState('');
  const [priority, setPriority] = useState('Routine');
  const [encounterRef, setEncounterRef] = useState('');
  const [q, setQ] = useState('');
  const dq = useDebounced(q, 200);
  const [hits, setHits] = useState<FormularyRow[]>([]);
  const [lines, setLines] = useState<{ productId: string; name: string; unit: string; qty: number }[]>([]);
  const [err, setErr] = useState<unknown>(null);
  useEffect(() => { if (depts.data?.length === 1) setDepartmentId(depts.data[0].id); }, [depts.data]);
  useEffect(() => {
    if (!dq.trim()) { setHits([]); return; }
    let live = true; hospitalApi.formulary(dq).then(r => { if (live) setHits(r.slice(0, 10)); }).catch(() => {});
    return () => { live = false; };
  }, [dq]);
  const save = async () => {
    setErr(null);
    try {
      const r = await hospitalApi.createIndent({ departmentId, priority, encounterRef: encounterRef.trim() || null, lines: lines.filter(l => l.qty > 0).map(l => ({ productId: l.productId, qtyUnits: l.qty })) });
      toast(`Indent ${r.indentNo} sent to pharmacy`); onClose(true);
    } catch (e) { setErr(e); }
  };
  return (
    <Modal title="New indent" onClose={() => onClose(false)}>
      <ErrorBanner error={err ?? depts.error} />
      <div className="grid c2">
        <Field label="Department"><select aria-label="Department" value={departmentId} onChange={e => setDepartmentId(e.target.value)}><option value="">Choose…</option>{depts.data?.filter(d => d.isActive).map(d => <option key={d.id} value={d.id}>{d.name}</option>)}</select></Field>
        <Field label="Priority"><select aria-label="Priority" value={priority} onChange={e => setPriority(e.target.value)}><option>Routine</option><option>Urgent</option></select></Field>
      </div>
      <Field label="HIS admission / encounter no. (optional, no patient names)"><input value={encounterRef} onChange={e => setEncounterRef(e.target.value)} /></Field>
      <div style={{ position: 'relative' }}>
        <input placeholder="Add formulary item…" aria-label="Add item" value={q} onChange={e => setQ(e.target.value)} style={{ width: '100%' }} />
        {hits.length > 0 && <div className="results">{hits.map(h => (
          <div key={h.productId} className="result" onMouseDown={e => {
            e.preventDefault(); setQ(''); setHits([]);
            if (!lines.some(l => l.productId === h.productId)) setLines([...lines, { productId: h.productId, name: h.productName, unit: h.baseUnit, qty: 1 }]);
          }}>{h.productName}{h.needsSecondCheck && <span className="badge warn">High-alert</span>}</div>
        ))}</div>}
      </div>
      {lines.length > 0 && <table><tbody>{lines.map((l, i) => (
        <tr key={l.productId}><td>{l.name}</td>
          <td className="num"><input className="qty" type="number" min={1} aria-label={`Qty ${l.name}`} value={l.qty} onChange={e => setLines(lines.map((x, j) => j === i ? { ...x, qty: Math.max(0, Math.floor(Number(e.target.value) || 0)) } : x))} /> {l.unit}</td>
          <td><button className="link" aria-label={`Remove ${l.name}`} onClick={() => setLines(lines.filter((_, j) => j !== i))}>✕</button></td></tr>
      ))}</tbody></table>}
      <p className="small muted">Quantities are in units (tablets, vials, ampoules). Only formulary items can be requested.</p>
      <div className="row end"><button className="primary" disabled={!departmentId || !lines.some(l => l.qty > 0)} onClick={save}>Send to pharmacy</button></div>
    </Modal>
  );
}

function IssueForm({ indent, onClose }: { indent: WardIndent; onClose: (done: boolean) => void }) {
  const pending = indent.lines.filter(l => l.pendingUnits > 0);
  const [qty, setQty] = useState<Record<string, number>>(Object.fromEntries(pending.map(l => [l.id, Math.min(l.pendingUnits, Math.floor(l.stockUnits))])));
  const [receivedBy, setReceivedBy] = useState('');
  const [err, setErr] = useState<unknown>(null);
  const save = async () => {
    setErr(null);
    try {
      const r = await hospitalApi.prepareIssue({
        departmentId: indent.departmentId, wardIndentId: indent.id, receivedBy: receivedBy.trim() || null,
        lines: pending.filter(l => (qty[l.id] ?? 0) > 0).map(l => ({ productId: l.productId, qtyUnits: qty[l.id], wardIndentLineId: l.id })),
      });
      toast(r.status === 'PendingCheck' ? `${r.issueNo} needs a second pharmacist's check before it leaves` : `${r.issueNo} issued (${money(r.costValue)})`);
      onClose(true);
    } catch (e) { setErr(e); }
  };
  return (
    <Modal title={`Issue against ${indent.indentNo} — ${indent.departmentName}`} onClose={() => onClose(false)}>
      <ErrorBanner error={err} />
      <table>
        <thead><tr><th>Item</th><th className="num">Pending</th><th className="num">In stock</th><th className="num">Issue now</th></tr></thead>
        <tbody>{pending.map(l => (
          <tr key={l.id}><td>{l.productName}</td><td className="num">{num(l.pendingUnits)}</td><td className="num">{num(l.stockUnits)}</td>
            <td className="num"><input className="qty" type="number" min={0} max={l.pendingUnits} aria-label={`Issue ${l.productName}`} value={qty[l.id] ?? 0}
              onChange={e => setQty({ ...qty, [l.id]: Math.min(l.pendingUnits, Math.max(0, Math.floor(Number(e.target.value) || 0))) })} /></td></tr>
        ))}</tbody>
      </table>
      <Field label="Handed to (name)"><input value={receivedBy} onChange={e => setReceivedBy(e.target.value)} /></Field>
      <p className="small muted">High-alert and narcotic/Schedule X items wait for a second pharmacist to verify before stock moves.</p>
      <div className="row end"><button className="primary" disabled={!pending.some(l => (qty[l.id] ?? 0) > 0)} onClick={save}>Issue</button></div>
    </Modal>
  );
}

// ---------------- Issues, second check, returns ----------------

export function WardIssuesPage() {
  const { can, session } = useAuth();
  const pharmacy = can('Owner', 'Pharmacist');
  const pendingQ = useLoad(() => hospitalApi.issues('PendingCheck'), []);
  const { data, error, reload } = useLoad(() => hospitalApi.issues(), []);
  const [open, setOpen] = useState<string | null>(null);
  const reloadAll = () => { reload(); pendingQ.reload(); };
  return (
    <>
      <PageHead title="Ward issues" sub="Medicines issued from the pharmacy store to departments, with second checks for high-alert items." />
      {pharmacy && (pendingQ.data?.length ?? 0) > 0 && <div className="panel">
        <h2>Waiting for a second check</h2>
        <div className="cards">{pendingQ.data!.map(i => (
          <div key={i.id} className="card clickable" onClick={() => setOpen(i.id)}>
            <div className="row"><b>{i.departmentName}</b><span className="spacer" /><Badge status={i.status} /></div>
            <div className="small muted">{i.issueNo} · prepared by {i.preparedBy}{i.preparedBy === session?.displayName && ' (you — another pharmacist must check)'}</div>
            <div className="small">{i.lines.map(l => `${l.productName} × ${num(l.qtyUnits)}`).join(', ')}</div>
          </div>
        ))}</div>
      </div>}
      <div className="panel">
        <ErrorBanner error={error} />
        {data?.length === 0 ? <div className="empty">No issues yet.</div> : (
          <table>
            <thead><tr><th>Issue</th><th>Department</th><th>Indent</th><th>Issued</th><th>Checked by</th><th className="num">Cost</th><th>Status</th></tr></thead>
            <tbody>{data?.map(i => <tr key={i.id} className="clickable" onClick={() => setOpen(i.id)}><td><b>{i.issueNo}</b></td><td>{i.departmentName}</td><td>{i.indentNo ?? '-'}</td>
              <td>{i.issuedAt ? dateTime(i.issuedAt) : '-'}</td><td>{i.checkedBy ?? (i.needsSecondCheck ? '—' : 'not needed')}</td><td className="num">{money(i.costValue)}</td><td><Badge status={i.status} /></td></tr>)}</tbody>
          </table>
        )}
      </div>
      {open && <IssueDetail id={open} pharmacy={pharmacy} onClose={() => { setOpen(null); reloadAll(); }} />}
    </>
  );
}

function IssueDetail({ id, pharmacy, onClose }: { id: string; pharmacy: boolean; onClose: () => void }) {
  const { data: i, error, setData } = useLoad(() => hospitalApi.issue(id), [id]);
  const [err, setErr] = useState<unknown>(null);
  const [ret, setRet] = useState(false);
  const verify = async () => {
    if (!i) return;
    if (!confirm('Confirm you have checked each item, strength and quantity against the indent?')) return;
    try { setData(await hospitalApi.verifyIssue(i.id, i.version)); toast('Checked and issued'); } catch (e) { setErr(e); }
  };
  const cancel = async () => {
    const reason = prompt('Why cancel this issue?');
    if (!reason?.trim() || !i) return;
    try { setData(await hospitalApi.cancelIssue(i.id, reason.trim())); toast('Issue cancelled'); } catch (e) { setErr(e); }
  };
  return (
    <Modal title={i ? `Issue ${i.issueNo}` : 'Issue'} onClose={onClose} wide>
      <ErrorBanner error={error ?? err} />
      {i && <div className="grid invoice">
        <div className="row"><b>{i.departmentName}</b><Badge status={i.status} /><span className="small muted">{i.indentNo && `indent ${i.indentNo} · `}prepared by {i.preparedBy} {dateTime(i.preparedAt)}{i.checkedBy && ` · checked by ${i.checkedBy}`}{i.receivedBy && ` · handed to ${i.receivedBy}`}</span></div>
        <table>
          <thead><tr><th>Item</th><th className="num">Qty</th><th>Batches</th></tr></thead>
          <tbody>{i.lines.map(l => (
            <tr key={l.id}><td>{l.productName} {l.highAlert && <span className="badge warn">High-alert</span>}</td><td className="num">{num(l.qtyUnits)} {l.baseUnit}</td>
              <td className="small">{l.batches.map(b => `${b.batchNo} (exp ${expiry(b.expiryDate)}) × ${num(b.qtyUnits)}${b.returnedUnits ? `, ${num(b.returnedUnits)} back` : ''}`).join('; ') || 'picked on release'}</td></tr>
          ))}</tbody>
        </table>
        {i.status === 'Issued' && <div className="small">Cost charged to department: <b>{money(i.costValue)}</b></div>}
        {i.cancelReason && <div className="small">Cancelled: {i.cancelReason}</div>}
        {pharmacy && <div className="row end no-print">
          {i.status === 'PendingCheck' && <><button className="danger" onClick={cancel}>Cancel</button><button className="primary" onClick={verify}>Verify &amp; issue</button></>}
          {i.status === 'Issued' && <button onClick={() => setRet(true)}>Record return from ward</button>}
          <button onClick={() => window.print()}>Print</button>
        </div>}
      </div>}
      {ret && i && <ReturnForm issue={i} onClose={done => { setRet(false); if (done) hospitalApi.issue(id).then(setData).catch(() => {}); }} />}
    </Modal>
  );
}

function ReturnForm({ issue, onClose }: { issue: WardIssue; onClose: (done: boolean) => void }) {
  const [reason, setReason] = useState<WardReturnReason>('Unused');
  const [qty, setQty] = useState<Record<string, number>>({});
  const [err, setErr] = useState<unknown>(null);
  const rows = issue.lines.flatMap(l => l.batches.map(b => ({ ...b, productName: l.productName, left: b.qtyUnits - b.returnedUnits })));
  const save = async () => {
    setErr(null);
    try {
      const r = await hospitalApi.createReturn({ wardIssueId: issue.id, reason, lines: rows.filter(b => (qty[b.id] ?? 0) > 0).map(b => ({ wardIssueBatchId: b.id, qtyUnits: qty[b.id] })) });
      toast(`Return ${r.returnNo} recorded`); onClose(true);
    } catch (e) { setErr(e); }
  };
  return (
    <Modal title={`Return against ${issue.issueNo}`} onClose={() => onClose(false)}>
      <ErrorBanner error={err} />
      <Field label="Reason"><select aria-label="Reason" value={reason} onChange={e => setReason(e.target.value as WardReturnReason)}>
        <option value="Unused">Unused — back to stock, department credited</option><option value="Expired">Expired — record only</option><option value="Damaged">Damaged — record only</option></select></Field>
      <table><tbody>{rows.map(b => (
        <tr key={b.id}><td>{b.productName}<div className="small muted">{b.batchNo} · {b.left} returnable</div></td>
          <td className="num"><input className="qty" type="number" min={0} max={b.left} disabled={b.left <= 0} aria-label={`Return ${b.productName} ${b.batchNo}`} value={qty[b.id] ?? 0}
            onChange={e => setQty({ ...qty, [b.id]: Math.min(b.left, Math.max(0, Math.floor(Number(e.target.value) || 0))) })} /></td></tr>
      ))}</tbody></table>
      <div className="row end"><button className="primary" disabled={!rows.some(b => (qty[b.id] ?? 0) > 0)} onClick={save}>Save return</button></div>
    </Modal>
  );
}

// ---------------- Formulary ----------------

export function FormularyPage() {
  const [q, setQ] = useState('');
  const dq = useDebounced(q);
  const { data, error, reload, setData } = useLoad(() => hospitalApi.formulary(dq), [dq]);
  const [adding, setAdding] = useState<{ id: string; name: string } | null>(null);
  const [err, setErr] = useState<unknown>(null);
  const set = async (productId: string, inFormulary: boolean, isHighAlert: boolean, notes?: string | null) => {
    setErr(null);
    if (data && inFormulary) setData(data.map(f => f.productId === productId ? { ...f, isHighAlert, needsSecondCheck: isHighAlert || f.schedule === 'X' || f.schedule === 'NarcoticPsychotropic' } : f));
    try { await hospitalApi.setFormulary({ productId, inFormulary, isHighAlert, notes }); } catch (e) { setErr(e); }
    reload();
  };
  return (
    <>
      <PageHead title="Formulary" sub="Items wards may indent. Mark high-alert items (insulin, potassium chloride, heparin…) to require a second pharmacist's check.">
        <input placeholder="Search formulary" value={q} onChange={e => setQ(e.target.value)} aria-label="Search formulary" />
      </PageHead>
      <div className="panel">
        <div className="row">
          <Field label="Add product"><ProductPicker value={adding} onPick={p => setAdding({ id: p.id, name: p.name })} /></Field>
          <button className="primary" disabled={!adding} onClick={async () => { if (adding) { await set(adding.id, true, false); setAdding(null); toast('Added to formulary'); } }}>Add</button>
        </div>
        <ErrorBanner error={error ?? err} />
        <table>
          <thead><tr><th>Item</th><th>Schedule</th><th className="num">Stock</th><th>High-alert</th><th>Second check</th><th /></tr></thead>
          <tbody>{data?.map(f => (
            <tr key={f.productId}><td><b>{f.productName}</b><div className="small muted">{f.composition}</div></td><td>{f.schedule === 'None' ? '-' : f.schedule}</td><td className="num">{num(f.stockUnits)} {f.baseUnit}</td>
              <td><input type="checkbox" checked={f.isHighAlert} aria-label={`High-alert ${f.productName}`} onChange={e => set(f.productId, true, e.target.checked, f.notes)} /></td>
              <td>{f.needsSecondCheck ? <span className="badge warn">Required</span> : '-'}</td>
              <td><button className="small" onClick={() => confirm(`Remove ${f.productName} from the formulary?`) && set(f.productId, false, false)}>Remove</button></td></tr>
          ))}</tbody>
        </table>
        <p className="small muted">Narcotic/psychotropic and Schedule X items always need a second check. The high-alert list should come from your pharmacy &amp; therapeutics committee.</p>
      </div>
    </>
  );
}

// ---------------- Departments ----------------

export function DepartmentsPage() {
  const { can } = useAuth();
  const { data, error, reload } = useLoad(() => hospitalApi.departments(), []);
  const users = useLoad(() => can('Owner') ? api.users() : Promise.resolve([]), []);
  const [edit, setEdit] = useState<Department | 'new' | null>(null);
  const [err, setErr] = useState<unknown>(null);
  const assign = async (userId: string, departmentId: string) => {
    setErr(null);
    try { await hospitalApi.setStaffDepartment(userId, departmentId || null); toast('Saved'); users.reload(); reload(); } catch (e) { setErr(e); }
  };
  return (
    <>
      <PageHead title="Departments" sub="Wards and departments that receive medicines; cost of issues is charged to them."><button className="primary" onClick={() => setEdit('new')}>Add department</button></PageHead>
      <div className="panel">
        <ErrorBanner error={error ?? err} />
        <table>
          <thead><tr><th>Code</th><th>Name</th><th className="num">Staff</th><th>Status</th><th /></tr></thead>
          <tbody>{data?.map(d => <tr key={d.id}><td>{d.code}</td><td>{d.name}</td><td className="num">{d.staff}</td><td>{d.isActive ? <span className="badge ok">Active</span> : <span className="badge">Off</span>}</td>
            <td><button className="small" onClick={() => setEdit(d)}>Edit</button></td></tr>)}</tbody>
        </table>
      </div>
      {can('Owner') && <div className="panel">
        <h2>Ward staff</h2>
        {(users.data ?? []).filter(u => u.role === 'WardStaff').length === 0 ? <div className="empty">Add users with the WardStaff role under Users first.</div> : (
          <table><tbody>{users.data!.filter(u => u.role === 'WardStaff').map(u => (
            <tr key={u.id}><td>{u.displayName}<div className="small muted">{u.email}</div></td>
              <td><select aria-label={`Department of ${u.displayName}`} value={u.departmentId ?? ''} onChange={e => assign(u.id, e.target.value)}><option value="">Not assigned</option>
                {data?.filter(d => d.isActive).map(d => <option key={d.id} value={d.id}>{d.name}</option>)}</select></td></tr>
          ))}</tbody></table>
        )}
      </div>}
      {edit && <DepartmentForm dept={edit === 'new' ? null : edit} onClose={saved => { setEdit(null); if (saved) reload(); }} />}
    </>
  );
}

function DepartmentForm({ dept, onClose }: { dept: Department | null; onClose: (saved: boolean) => void }) {
  const [f, setF] = useState({ name: dept?.name ?? '', code: dept?.code ?? '', isActive: dept?.isActive ?? true });
  const [err, setErr] = useState<unknown>(null);
  const save = async () => { setErr(null); try { await hospitalApi.saveDepartment(dept?.id ?? null, f); toast('Department saved'); onClose(true); } catch (e) { setErr(e); } };
  return (
    <Modal title={dept ? `Edit ${dept.name}` : 'Add department'} onClose={() => onClose(false)}>
      <ErrorBanner error={err} />
      <div className="grid c2">
        <Field label="Name"><input value={f.name} onChange={e => setF({ ...f, name: e.target.value })} autoFocus /></Field>
        <Field label="Code (≤10)"><input value={f.code} maxLength={10} onChange={e => setF({ ...f, code: e.target.value.toUpperCase() })} /></Field>
      </div>
      <label className="check"><input type="checkbox" checked={f.isActive} onChange={e => setF({ ...f, isActive: e.target.checked })} /> Active</label>
      <div className="row end"><button className="primary" disabled={!f.name.trim() || !f.code.trim()} onClick={save}>Save</button></div>
    </Modal>
  );
}

// ---------------- Consumption ----------------

export function ConsumptionPage() {
  const [from, setFrom] = useState(today().slice(0, 8) + '01');
  const [to, setTo] = useState(today());
  const { data, error } = useLoad(() => hospitalApi.consumption(from, to), [from, to]);
  return (
    <>
      <PageHead title="Department consumption" sub="Cost of medicines issued to each department, less unused returns, at purchase cost.">
        <Field label="From"><input type="date" value={from} onChange={e => setFrom(e.target.value)} /></Field>
        <Field label="To"><input type="date" value={to} onChange={e => setTo(e.target.value)} /></Field>
        <button onClick={() => window.print()}>Print</button>
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error} />
        {data && <>
          <table>
            <thead><tr><th>Department</th><th className="num">Issues</th><th className="num">Issued</th><th className="num">Returned</th><th className="num">Net cost</th><th className="num">Open indents</th><th className="num">Avg hrs to first issue</th></tr></thead>
            <tbody>{data.departments.map(d => <tr key={d.departmentId}><td>{d.departmentName}</td><td className="num">{d.issues}</td><td className="num">{money(d.issuedValue)}</td>
              <td className="num">{money(d.returnedValue)}</td><td className="num"><b>{money(d.netValue)}</b></td><td className="num">{d.openIndents || '-'}</td><td className="num">{d.avgHoursToFirstIssue ?? '-'}</td></tr>)}</tbody>
            <tfoot><tr><td><b>Total</b></td><td /><td /><td /><td className="num"><b>{money(data.totalNet)}</b></td><td /><td /></tr></tfoot>
          </table>
          {data.topProducts.length > 0 && <>
            <h3>Top items by cost</h3>
            <table className="small"><tbody>{data.topProducts.map(p => <tr key={p.productId}><td>{p.productName}</td><td className="num">{num(p.units)} {p.baseUnit}</td><td className="num">{money(p.value)}</td></tr>)}</tbody></table>
          </>}
        </>}
      </div>
    </>
  );
}
