import { useState } from 'react';
import { mfgApi, type Genealogy, type LotNode, type ProductionBatch, type QcSample } from '../api';
import { useAuth } from '../auth';
import ProductPicker from '../components/ProductPicker';
import { ErrorBanner, Field, Modal, PageHead, toast, useDebounced, useLoad } from '../ui';
import { dateTime, expiry, money } from '../util';

const qty = (n: number) => n.toLocaleString('en-IN', { maximumFractionDigits: 3 });
const PILOT = 'Pilot module — not validated for GMP decisions (no IQ/OQ/PQ, document control or training records).';

function Badge({ status }: { status: string }) {
  const cls = ['Released', 'Approved', 'Passed'].includes(status) ? 'ok' : ['Rejected', 'Failed', 'Cancelled'].includes(status) ? 'danger'
    : ['Quarantine', 'Pending', 'Draft'].includes(status) ? 'warn' : 'brand';
  return <span className={`badge ${cls}`}>{status === 'InProcess' ? 'In process' : status}</span>;
}

/** Electronic signature: the signed-in user re-enters their password; the meaning is shown and recorded. */
export function SignModal({ title, meaning, needNote, onSign, onClose }: {
  title: string; meaning: string; needNote?: boolean; onSign: (password: string, note: string) => Promise<void>; onClose: () => void;
}) {
  const [password, setPassword] = useState('');
  const [note, setNote] = useState('');
  const [err, setErr] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const sign = async () => {
    setErr(null); setBusy(true);
    try { await onSign(password, note); onClose(); } catch (e) { setErr(e); } finally { setBusy(false); }
  };
  return (
    <Modal title={title} onClose={onClose}>
      <ErrorBanner error={err} />
      <div className="alert warn">By signing you confirm: <b>{meaning}</b></div>
      <Field label={needNote ? 'Reason (required)' : 'Comment (optional)'}><input value={note} onChange={e => setNote(e.target.value)} /></Field>
      <Field label="Your password (e-signature)"><input type="password" autoComplete="current-password" value={password} onChange={e => setPassword(e.target.value)} /></Field>
      <div className="row end"><button className="primary" disabled={busy || !password || (needNote && !note.trim())} onClick={sign}>Sign</button></div>
    </Modal>
  );
}

// ---------------- Production batches ----------------

export function ProductionPage() {
  const [status, setStatus] = useState('');
  const { data, error, reload } = useLoad(() => mfgApi.batches(status || undefined), [status]);
  const { can } = useAuth();
  const [planning, setPlanning] = useState(false);
  const [open, setOpen] = useState<string | null>(null);
  return (
    <>
      <PageHead title="Production batches" sub="Plan → issue materials (FEFO, QC-passed lots) → record output (quarantined) → QC → QA release.">
        <select value={status} onChange={e => setStatus(e.target.value)} aria-label="Status"><option value="">Active & recent</option>{['Planned', 'InProcess', 'Quarantine', 'Released', 'Rejected', 'Cancelled'].map(s => <option key={s}>{s}</option>)}</select>
        {can('Owner', 'Production') && <button className="primary" onClick={() => setPlanning(true)}>Plan batch</button>}
      </PageHead>
      <div className="alert warn">{PILOT}</div>
      <div className="panel">
        <ErrorBanner error={error} />
        {data?.length === 0 ? <div className="empty">No batches.</div> : (
          <table>
            <thead><tr><th>Batch</th><th>Product</th><th className="num">Planned</th><th className="num">Output</th><th className="num">Yield</th><th className="num">Material cost</th><th>Status</th></tr></thead>
            <tbody>{data?.map(b => <tr key={b.id} className="clickable" onClick={() => setOpen(b.id)}><td><b>{b.batchNo}</b><div className="small muted">{b.mfgDate}</div></td><td>{b.productName} <span className="small muted">v{b.recipeVersion}</span></td>
              <td className="num">{qty(b.plannedUnits)}</td><td className="num">{b.outputUnits ? qty(b.outputUnits) : '-'}</td><td className="num">{b.yieldPercent != null ? `${b.yieldPercent}%` : '-'}</td>
              <td className="num">{b.materialCost ? money(b.materialCost) : '-'}</td><td><Badge status={b.status} /></td></tr>)}</tbody>
          </table>
        )}
      </div>
      {planning && <PlanForm onClose={id => { setPlanning(false); reload(); if (id) setOpen(id); }} />}
      {open && <BatchDetail id={open} onClose={() => { setOpen(null); reload(); }} />}
    </>
  );
}

function PlanForm({ onClose }: { onClose: (id: string | null) => void }) {
  const recipes = useLoad(() => mfgApi.recipes(), []);
  const [f, setF] = useState({ recipeId: '', batchNo: '', expiryDate: '', mrp: 0, plannedUnits: 0 });
  const [err, setErr] = useState<unknown>(null);
  const r = recipes.data?.find(x => x.id === f.recipeId);
  const save = async () => { setErr(null); try { const b = await mfgApi.planBatch(f); toast(`Batch ${b.batchNo} planned`); onClose(b.id); } catch (e) { setErr(e); } };
  return (
    <Modal title="Plan a batch" onClose={() => onClose(null)}>
      <ErrorBanner error={err ?? recipes.error} />
      <Field label="Recipe (approved)"><select aria-label="Recipe" value={f.recipeId} onChange={e => { const x = recipes.data?.find(y => y.id === e.target.value); setF({ ...f, recipeId: e.target.value, plannedUnits: x?.batchSizeUnits ?? 0 }); }}>
        <option value="">Choose…</option>{recipes.data?.filter(x => x.status === 'Approved').map(x => <option key={x.id} value={x.id}>{x.productName} v{x.versionNo}</option>)}</select></Field>
      <div className="grid c2">
        <Field label="Batch no."><input value={f.batchNo} onChange={e => setF({ ...f, batchNo: e.target.value.toUpperCase() })} /></Field>
        <Field label="Planned units"><input type="number" min={1} value={f.plannedUnits || ''} onChange={e => setF({ ...f, plannedUnits: Number(e.target.value) || 0 })} /></Field>
        <Field label="Expiry"><input type="date" value={f.expiryDate} onChange={e => setF({ ...f, expiryDate: e.target.value })} /></Field>
        <Field label="MRP per pack"><input type="number" min={0} step="0.01" value={f.mrp || ''} onChange={e => setF({ ...f, mrp: Number(e.target.value) || 0 })} /></Field>
      </div>
      {r && <table className="small"><tbody>{r.lines.map(l => <tr key={l.materialProductId}><td>{l.materialName}</td><td className="num">{qty(l.qtyUnits * (f.plannedUnits || 0) / r.batchSizeUnits)} {l.baseUnit}</td></tr>)}</tbody></table>}
      <div className="row end"><button className="primary" disabled={!f.recipeId || !f.batchNo.trim() || !f.expiryDate || f.mrp <= 0 || f.plannedUnits <= 0} onClick={save}>Plan</button></div>
    </Modal>
  );
}

function BatchDetail({ id, onClose }: { id: string; onClose: () => void }) {
  const { can } = useAuth();
  const { data: b, error, setData } = useLoad(() => mfgApi.batch(id), [id]);
  const [err, setErr] = useState<unknown>(null);
  const [sign, setSign] = useState<'release' | 'reject' | null>(null);
  const act = async (fn: () => Promise<ProductionBatch>, msg: string) => { setErr(null); try { setData(await fn()); toast(msg); } catch (e) { setErr(e); } };
  const output = () => {
    const v = Number(prompt('Units produced (counted)?') ?? '');
    if (v > 0) act(() => mfgApi.recordOutput(id, v), 'Output booked into quarantine');
  };
  const prod = can('Owner', 'Production');
  return (
    <Modal title={b ? `Batch ${b.batchNo} — ${b.productName}` : 'Batch'} onClose={onClose} wide>
      <ErrorBanner error={error ?? err} />
      {b && <div className="grid invoice">
        <div className="row"><Badge status={b.status} /><span className="small muted">recipe v{b.recipeVersion} · mfg {b.mfgDate} · exp {expiry(b.expiryDate)} · MRP {money(b.mrp)}{b.createdBy && ` · planned by ${b.createdBy}`}</span></div>
        {b.decisionSignature && <div className={`alert ${b.status === 'Released' ? 'ok' : 'error'}`}>{b.decisionSignature} · {dateTime(b.decisionAt!)}{b.decisionNote && ` — ${b.decisionNote}`}</div>}
        <h3>Materials used (genealogy)</h3>
        {b.materials.length === 0 ? <div className="small muted">Not issued yet.</div> : (
          <table className="small"><thead><tr><th>Material</th><th>Lot</th><th>Exp</th><th className="num">Qty</th><th className="num">Cost/unit</th></tr></thead>
            <tbody>{b.materials.map(m => <tr key={m.batchId + m.materialProductId}><td>{m.materialName}</td><td>{m.batchNo}</td><td>{expiry(m.expiryDate)}</td><td className="num">{qty(m.qtyUnits)}</td><td className="num">{money(m.costPerUnit)}</td></tr>)}</tbody></table>
        )}
        {b.outputUnits > 0 && <div className="small">Output {qty(b.outputUnits)} units · yield {b.yieldPercent}% · material cost {money(b.materialCost)}</div>}
        {b.samples.length > 0 && <><h3>QC of finished batch</h3><SampleList samples={b.samples} /></>}
        <div className="row end no-print">
          {prod && b.status === 'Planned' && <><button className="danger" onClick={() => act(() => mfgApi.cancelBatch(id), 'Cancelled')}>Cancel</button>
            <button className="primary" onClick={() => act(() => mfgApi.issueMaterials(id), 'Materials issued')}>Issue materials</button></>}
          {prod && b.status === 'InProcess' && <button className="primary" onClick={output}>Record output</button>}
          {can('Owner', 'QaManager') && b.status === 'Quarantine' && <><button className="danger" onClick={() => setSign('reject')}>Reject</button><button className="primary" onClick={() => setSign('release')}>Release</button></>}
          <button onClick={() => window.print()}>Print</button>
        </div>
      </div>}
      {sign && b && <SignModal title={sign === 'release' ? 'Release batch' : 'Reject batch'} needNote={sign === 'reject'}
        meaning={sign === 'release' ? `I have reviewed batch ${b.batchNo} and its QC results, and release it for sale.` : `Batch ${b.batchNo} is rejected and stays blocked.`}
        onSign={async (pw, note) => { setData(await mfgApi.decide(id, sign === 'release', pw, note || null)); toast(sign === 'release' ? 'Batch released' : 'Batch rejected'); }}
        onClose={() => setSign(null)} />}
    </Modal>
  );
}

function SampleList({ samples }: { samples: QcSample[] }) {
  return (
    <table className="small"><tbody>{samples.map(s => (
      <tr key={s.id}><td>{s.sampleNo}<div className="muted">spec v{s.specVersion}</div></td><td><Badge status={s.status} /></td>
        <td>{s.results.map(r => `${r.testName}: ${r.value ?? r.text ?? ''}${r.unit ?? ''}${r.pass ? '' : ' ✗'}`).join(' · ') || '-'}</td><td>{s.testerSignature ?? ''}</td></tr>
    ))}</tbody></table>
  );
}

// ---------------- Recipes & specs ----------------

export function RecipesSpecsPage() {
  const { can } = useAuth();
  const recipes = useLoad(() => mfgApi.recipes(), []);
  const specs = useLoad(() => mfgApi.specs(), []);
  const [newRecipe, setNewRecipe] = useState(false);
  const [newSpec, setNewSpec] = useState(false);
  const [sign, setSign] = useState<{ kind: 'recipe' | 'spec'; id: string; label: string } | null>(null);
  const qa = can('Owner', 'QaManager');
  return (
    <>
      <PageHead title="Recipes & specifications" sub="Only the approved version is used. Approval needs QA's e-signature; a change means a new version." />
      <div className="alert warn">{PILOT}</div>
      <div className="panel">
        <div className="row"><h2 style={{ margin: 0 }}>Recipes (bill of materials)</h2><span className="spacer" />{can('Owner', 'Production') && <button onClick={() => setNewRecipe(true)}>New recipe version</button>}</div>
        <ErrorBanner error={recipes.error} />
        <table><tbody>{recipes.data?.map(r => (
          <tr key={r.id}><td><b>{r.productName}</b> v{r.versionNo}<div className="small muted">per {qty(r.batchSizeUnits)} units</div></td>
            <td className="small">{r.lines.map(l => `${l.materialName} ${qty(l.qtyUnits)} ${l.baseUnit}`).join(' · ')}</td>
            <td><Badge status={r.status} />{r.approvedBy && <div className="small muted">by {r.approvedBy}</div>}</td>
            <td>{qa && r.status === 'Draft' && <button className="small primary" onClick={() => setSign({ kind: 'recipe', id: r.id, label: `${r.productName} v${r.versionNo}` })}>Approve</button>}</td></tr>
        ))}</tbody></table>
      </div>
      <div className="panel">
        <div className="row"><h2 style={{ margin: 0 }}>QC specifications</h2><span className="spacer" />{can('Owner', 'QcAnalyst') && <button onClick={() => setNewSpec(true)}>New spec version</button>}</div>
        <ErrorBanner error={specs.error} />
        <table><tbody>{specs.data?.map(s => (
          <tr key={s.id}><td><b>{s.productName}</b> v{s.versionNo}</td>
            <td className="small">{s.tests.map(t => `${t.name} ${t.min != null || t.max != null ? `${t.min ?? ''}–${t.max ?? ''}${t.unit ?? ''}` : `“${t.requirement}”`}`).join(' · ')}</td>
            <td><Badge status={s.status} /></td>
            <td>{qa && s.status === 'Draft' && <button className="small primary" onClick={() => setSign({ kind: 'spec', id: s.id, label: `${s.productName} spec v${s.versionNo}` })}>Approve</button>}</td></tr>
        ))}</tbody></table>
        <p className="small muted">Material lots of products with an approved spec must pass QC before production can use them.</p>
      </div>
      {newRecipe && <RecipeForm onClose={() => { setNewRecipe(false); recipes.reload(); }} />}
      {newSpec && <SpecForm onClose={() => { setNewSpec(false); specs.reload(); }} />}
      {sign && <SignModal title={`Approve ${sign.label}`} meaning={`I approve ${sign.label} for use; the previous approved version is retired.`}
        onSign={async pw => { if (sign.kind === 'recipe') await mfgApi.approveRecipe(sign.id, pw); else await mfgApi.approveSpec(sign.id, pw); toast('Approved'); recipes.reload(); specs.reload(); }}
        onClose={() => setSign(null)} />}
    </>
  );
}

function RecipeForm({ onClose }: { onClose: () => void }) {
  const [product, setProduct] = useState<{ id: string; name: string } | null>(null);
  const [size, setSize] = useState(0);
  const [lines, setLines] = useState<{ id: string; name: string; qty: number }[]>([]);
  const [pick, setPick] = useState<{ id: string; name: string } | null>(null);
  const [err, setErr] = useState<unknown>(null);
  const save = async () => {
    setErr(null);
    try { await mfgApi.createRecipe({ productId: product!.id, batchSizeUnits: size, lines: lines.map(l => ({ materialProductId: l.id, qtyUnits: l.qty })) }); toast('Recipe drafted — QA must approve it'); onClose(); }
    catch (e) { setErr(e); }
  };
  return (
    <Modal title="New recipe version" onClose={onClose}>
      <ErrorBanner error={err} />
      <Field label="Finished product"><ProductPicker value={product} onPick={p => setProduct({ id: p.id, name: p.name })} /></Field>
      <Field label="Batch size (units)"><input type="number" min={1} value={size || ''} onChange={e => setSize(Number(e.target.value) || 0)} /></Field>
      <div className="row"><Field label="Material"><ProductPicker value={pick} onPick={p => setPick({ id: p.id, name: p.name })} /></Field>
        <button disabled={!pick} onClick={() => { if (pick && !lines.some(l => l.id === pick.id)) setLines([...lines, { ...pick, qty: 0 }]); setPick(null); }}>Add material</button></div>
      <table><tbody>{lines.map((l, i) => <tr key={l.id}><td>{l.name}</td><td className="num"><input className="qty" type="number" min={0} step="0.001" aria-label={`Qty ${l.name}`} value={l.qty || ''}
        onChange={e => setLines(lines.map((x, j) => j === i ? { ...x, qty: Number(e.target.value) || 0 } : x))} /></td><td><button className="link" onClick={() => setLines(lines.filter((_, j) => j !== i))}>✕</button></td></tr>)}</tbody></table>
      <div className="row end"><button className="primary" disabled={!product || size <= 0 || lines.length === 0 || lines.some(l => l.qty <= 0)} onClick={save}>Save draft</button></div>
    </Modal>
  );
}

function SpecForm({ onClose }: { onClose: () => void }) {
  const [product, setProduct] = useState<{ id: string; name: string } | null>(null);
  const [tests, setTests] = useState([{ name: '', unit: '', min: '', max: '', requirement: '' }]);
  const [err, setErr] = useState<unknown>(null);
  const n = (s: string) => (s.trim() === '' ? null : Number(s));
  const save = async () => {
    setErr(null);
    try {
      await mfgApi.createSpec({ productId: product!.id, tests: tests.filter(t => t.name.trim()).map(t => ({ name: t.name, unit: t.unit || null, min: n(t.min), max: n(t.max), requirement: t.requirement || null })) });
      toast('Spec drafted — QA must approve it'); onClose();
    } catch (e) { setErr(e); }
  };
  const upd = (i: number, k: string, v: string) => setTests(tests.map((t, j) => j === i ? { ...t, [k]: v } : t));
  return (
    <Modal title="New QC specification version" onClose={onClose} wide>
      <ErrorBanner error={err} />
      <Field label="Product"><ProductPicker value={product} onPick={p => setProduct({ id: p.id, name: p.name })} /></Field>
      <table>
        <thead><tr><th>Test</th><th>Unit</th><th>Min</th><th>Max</th><th>or written requirement</th></tr></thead>
        <tbody>{tests.map((t, i) => <tr key={i}>
          <td><input aria-label={`Test ${i + 1}`} value={t.name} onChange={e => upd(i, 'name', e.target.value)} /></td><td><input className="short" value={t.unit} onChange={e => upd(i, 'unit', e.target.value)} /></td>
          <td><input className="short" type="number" value={t.min} onChange={e => upd(i, 'min', e.target.value)} /></td><td><input className="short" type="number" value={t.max} onChange={e => upd(i, 'max', e.target.value)} /></td>
          <td><input value={t.requirement} onChange={e => upd(i, 'requirement', e.target.value)} /></td></tr>)}</tbody>
      </table>
      <div className="row"><button onClick={() => setTests([...tests, { name: '', unit: '', min: '', max: '', requirement: '' }])}>Add test</button><span className="spacer" />
        <button className="primary" disabled={!product || !tests.some(t => t.name.trim())} onClick={save}>Save draft</button></div>
    </Modal>
  );
}

// ---------------- QC lab ----------------

export function QcLabPage() {
  const { can } = useAuth();
  const { data, error, reload } = useLoad(() => mfgApi.samples(), []);
  const [lotQ, setLotQ] = useState('');
  const dq = useDebounced(lotQ);
  const lots = useLoad(() => dq.trim() ? mfgApi.lots(dq) : Promise.resolve([] as LotNode[]), [dq]);
  const [err, setErr] = useState<unknown>(null);
  const [enter, setEnter] = useState<QcSample | null>(null);
  const analyst = can('Owner', 'QcAnalyst');
  const sample = async (batchId: string) => { setErr(null); try { const s = await mfgApi.createSample(batchId); toast(`Sample ${s.sampleNo} logged`); setLotQ(''); reload(); } catch (e) { setErr(e); } };
  return (
    <>
      <PageHead title="QC lab" sub="Log samples of material lots and finished batches; results are signed by the analyst and cannot be edited afterwards." />
      <div className="alert warn">{PILOT}</div>
      {analyst && <div className="panel">
        <Field label="Sample a lot (batch no. or product)"><input value={lotQ} onChange={e => setLotQ(e.target.value)} aria-label="Find lot" /></Field>
        <ErrorBanner error={err} />
        {lots.data?.map(l => <div key={l.batchId} className="row"><span>{l.productName} · <b>{l.batchNo}</b> · exp {expiry(l.expiryDate)} · {qty(l.qtyUnits)} units</span><span className="spacer" /><button className="small" onClick={() => sample(l.batchId)}>Log sample</button></div>)}
      </div>}
      <div className="panel">
        <ErrorBanner error={error} />
        <table>
          <thead><tr><th>Sample</th><th>Lot</th><th>Logged</th><th>Status</th><th>Signed</th><th /></tr></thead>
          <tbody>{data?.map(s => <tr key={s.id}><td><b>{s.sampleNo}</b></td><td>{s.productName} · {s.batchNo}</td><td>{dateTime(s.createdAt)}</td><td><Badge status={s.status} /></td>
            <td className="small">{s.testerSignature ?? '-'}</td><td>{analyst && s.status === 'Pending' && <button className="small primary" onClick={() => setEnter(s)}>Enter results</button>}</td></tr>)}</tbody>
        </table>
      </div>
      {enter && <ResultsForm sample={enter} onClose={() => { setEnter(null); reload(); }} />}
    </>
  );
}

function ResultsForm({ sample, onClose }: { sample: QcSample; onClose: () => void }) {
  const [vals, setVals] = useState<Record<string, { value: string; text: string; pass: boolean }>>(Object.fromEntries(sample.tests.map(t => [t.id, { value: '', text: '', pass: true }])));
  const [signing, setSigning] = useState(false);
  const numeric = (t: QcSample['tests'][number]) => t.min != null || t.max != null;
  const ready = sample.tests.every(t => numeric(t) ? vals[t.id].value.trim() !== '' : vals[t.id].text.trim() !== '');
  const set = (id: string, p: Partial<{ value: string; text: string; pass: boolean }>) => setVals({ ...vals, [id]: { ...vals[id], ...p } });
  return (
    <Modal title={`Results — ${sample.sampleNo} (${sample.productName} ${sample.batchNo})`} onClose={onClose} wide>
      <table>
        <thead><tr><th>Test</th><th>Limit</th><th>Result</th></tr></thead>
        <tbody>{sample.tests.map(t => <tr key={t.id}><td>{t.name}</td><td className="small">{numeric(t) ? `${t.min ?? '–'} to ${t.max ?? '–'} ${t.unit ?? ''}` : t.requirement}</td>
          <td>{numeric(t)
            ? <input className="short" type="number" aria-label={`Result ${t.name}`} value={vals[t.id].value} onChange={e => set(t.id, { value: e.target.value })} />
            : <div className="row"><input aria-label={`Observation ${t.name}`} value={vals[t.id].text} onChange={e => set(t.id, { text: e.target.value })} />
              <label className="check"><input type="checkbox" checked={vals[t.id].pass} onChange={e => set(t.id, { pass: e.target.checked })} /> Complies</label></div>}</td></tr>)}</tbody>
      </table>
      <p className="small muted">Numeric results are judged against the limits automatically.</p>
      <div className="row end"><button className="primary" disabled={!ready} onClick={() => setSigning(true)}>Sign results</button></div>
      {signing && <SignModal title="Sign QC results" meaning={`These are the true results for sample ${sample.sampleNo}.`} onClose={() => setSigning(false)}
        onSign={async pw => {
          const r = await mfgApi.recordResults(sample.id, pw, sample.tests.map(t => numeric(t)
            ? { qcSpecTestId: t.id, value: Number(vals[t.id].value) } : { qcSpecTestId: t.id, text: vals[t.id].text, pass: vals[t.id].pass }));
          toast(`Sample ${r.status.toLowerCase()}`); onClose();
        }} />}
    </Modal>
  );
}

// ---------------- Genealogy ----------------

export function GenealogyPage() {
  const [q, setQ] = useState('');
  const dq = useDebounced(q);
  const lots = useLoad(() => dq.trim() ? mfgApi.lots(dq) : Promise.resolve([] as LotNode[]), [dq]);
  const [g, setG] = useState<Genealogy | null>(null);
  const [err, setErr] = useState<unknown>(null);
  const open = async (id: string) => { setErr(null); try { setG(await mfgApi.genealogy(id)); } catch (e) { setErr(e); } };
  const Nodes = ({ nodes }: { nodes: LotNode[] }) => nodes.length === 0 ? <div className="small muted">None.</div> : (
    <table className="small"><tbody>{nodes.map((n, i) => <tr key={i} className={n.batchId !== '00000000-0000-0000-0000-000000000000' ? 'clickable' : ''} onClick={() => n.batchId !== '00000000-0000-0000-0000-000000000000' && open(n.batchId)}>
      <td>{n.productName}</td><td><b>{n.batchNo}</b></td><td>exp {expiry(n.expiryDate)}</td><td className="num">{qty(n.qtyUnits)}</td><td>{n.productionStatus && <Badge status={n.productionStatus} />}</td></tr>)}</tbody></table>
  );
  return (
    <>
      <PageHead title="Batch genealogy" sub="What went into a batch, and where a material lot was used — the starting point for an investigation or recall." />
      <div className="panel">
        <Field label="Find lot"><input value={q} onChange={e => setQ(e.target.value)} aria-label="Find lot" placeholder="Batch no. or product" /></Field>
        <ErrorBanner error={err ?? lots.error} />
        {lots.data?.map(l => <div key={l.batchId} className="row clickable" onClick={() => open(l.batchId)}>{l.productName} · <b>{l.batchNo}</b> · exp {expiry(l.expiryDate)}</div>)}
      </div>
      {g && <div className="panel">
        <div className="row"><h2 style={{ margin: 0 }}>{g.productName} · {g.batchNo}</h2>{g.isBlocked ? <span className="badge danger">Blocked: {g.blockReason}</span> : <span className="badge ok">Available</span>}</div>
        <h3>Made from</h3><Nodes nodes={g.madeFrom} />
        <h3>Used in</h3><Nodes nodes={g.usedIn} />
        <h3>QC</h3>{g.samples.length ? <SampleList samples={g.samples} /> : <div className="small muted">No samples.</div>}
      </div>}
    </>
  );
}
