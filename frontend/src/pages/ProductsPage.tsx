import { useState } from 'react';
import { api, type Product, type Schedule } from '../api';
import { RoleSets, useAuth } from '../auth';
import { ErrorBanner, Field, Modal, PageHead, Pager, toast, useDebounced, useLoad } from '../ui';

export default function ProductsPage() {
  const { can } = useAuth();
  const [q, setQ] = useState('');
  const dq = useDebounced(q);
  const [page, setPage] = useState(1);
  const [inactive, setInactive] = useState(false);
  const [editing, setEditing] = useState<Product | 'new' | null>(null);
  const { data, error, loading, reload } = useLoad(() => api.products(dq, page, inactive), [dq, page, inactive]);
  const writable = can(...RoleSets.masterWrite);

  return (
    <>
      <PageHead title="Products" sub="Medicine master. Stock is tracked in base units (e.g. tablets).">
        <input placeholder="Search name, composition, barcode" value={q} onChange={e => { setQ(e.target.value); setPage(1); }} style={{ width: 260 }} />
        <label className="check"><input type="checkbox" checked={inactive} onChange={e => setInactive(e.target.checked)} /> Show inactive</label>
        {writable && <button className="primary" onClick={() => setEditing('new')}>+ New product</button>}
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error} />
        {!loading && data?.items.length === 0 ? (
          <div className="empty">{dq ? 'No match.' : 'No products yet. Add your first product or enter a purchase.'}</div>
        ) : (
          <div className="table-wrap">
            <table>
              <thead><tr><th>Name</th><th>Composition</th><th>Manufacturer</th><th>Pack</th><th className="num">GST %</th><th>HSN</th><th>Schedule</th><th /></tr></thead>
              <tbody>
                {data?.items.map(p => (
                  <tr key={p.id} className={writable ? 'clickable' : ''} onClick={() => writable && setEditing(p)}>
                    <td><b>{p.name}</b> {!p.isActive && <span className="badge">inactive</span>}</td>
                    <td className="small">{p.composition}</td><td>{p.manufacturer}</td>
                    <td>{p.packSize} {p.baseUnit}/{p.packUnit}{!p.allowLooseSale && <span className="small muted"> · packs only</span>}</td>
                    <td className="num">{p.gstRate}</td><td>{p.hsnCode}</td>
                    <td>{p.schedule !== 'None' && <span className="badge warn">{p.schedule}</span>}</td>
                    <td>{p.barcode && <span className="small muted">{p.barcode}</span>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {data && <Pager page={page} total={data.total} pageSize={data.pageSize} onPage={setPage} />}
      </div>
      {editing && (
        <Modal title={editing === 'new' ? 'New product' : `Edit ${editing.name}`} onClose={() => setEditing(null)}>
          <ProductForm product={editing === 'new' ? null : editing} onSaved={() => { setEditing(null); reload(); }} />
        </Modal>
      )}
    </>
  );
}

const SCHEDULES: Schedule[] = ['None', 'G', 'H', 'H1', 'X', 'NarcoticPsychotropic'];

export function ProductForm({ product, onSaved, initialName }: { product: Product | null; onSaved: (p: Product) => void; initialName?: string }) {
  const [f, setF] = useState({
    name: product?.name ?? initialName ?? '', composition: product?.composition ?? '', manufacturer: product?.manufacturer ?? '',
    hsnCode: product?.hsnCode ?? '3004', gstRate: String(product?.gstRate ?? 12), packSize: String(product?.packSize ?? 10),
    packUnit: product?.packUnit ?? 'Strip', baseUnit: product?.baseUnit ?? 'Tab', allowLooseSale: product?.allowLooseSale ?? true,
    schedule: product?.schedule ?? 'None' as Schedule, barcode: product?.barcode ?? '', reorderLevelUnits: String(product?.reorderLevelUnits ?? 0),
    isActive: product?.isActive ?? true, onlineListed: product?.onlineListed ?? false,
  });
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) =>
    setF({ ...f, [k]: e.target.type === 'checkbox' ? (e.target as HTMLInputElement).checked : e.target.value });

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true); setError(null);
    const body = {
      ...f, composition: f.composition || null, manufacturer: f.manufacturer || null, hsnCode: f.hsnCode || null, barcode: f.barcode || null,
      gstRate: Number(f.gstRate), packSize: Number(f.packSize), reorderLevelUnits: Number(f.reorderLevelUnits), version: product?.version ?? null,
    };
    try {
      const saved = product ? await api.updateProduct(product.id, body) : await api.createProduct(body);
      toast(`Saved ${saved.name}`);
      onSaved(saved);
    } catch (err) { setError(err); } finally { setBusy(false); }
  };

  return (
    <form className="grid" onSubmit={submit}>
      <ErrorBanner error={error} />
      <div className="grid c2">
        <Field label="Brand name"><input value={f.name} onChange={set('name')} required autoFocus /></Field>
        <Field label="Composition / generic"><input value={f.composition} onChange={set('composition')} placeholder="Paracetamol 500mg" /></Field>
      </div>
      <div className="grid c3">
        <Field label="Manufacturer"><input value={f.manufacturer} onChange={set('manufacturer')} /></Field>
        <Field label="HSN"><input value={f.hsnCode} onChange={set('hsnCode')} maxLength={8} /></Field>
        <Field label="GST %">
          <select value={f.gstRate} onChange={set('gstRate')}>{[0, 5, 12, 18, 28].map(r => <option key={r}>{r}</option>)}</select>
        </Field>
      </div>
      <div className="grid c4">
        <Field label="Pack size"><input type="number" min={1} value={f.packSize} onChange={set('packSize')} required /></Field>
        <Field label="Pack unit"><input value={f.packUnit} onChange={set('packUnit')} /></Field>
        <Field label="Base unit"><input value={f.baseUnit} onChange={set('baseUnit')} /></Field>
        <Field label="Reorder level (units)"><input type="number" min={0} value={f.reorderLevelUnits} onChange={set('reorderLevelUnits')} /></Field>
      </div>
      <div className="grid c3">
        <Field label="Schedule">
          <select value={f.schedule} onChange={set('schedule')}>{SCHEDULES.map(s => <option key={s}>{s}</option>)}</select>
        </Field>
        <Field label="Barcode"><input value={f.barcode} onChange={set('barcode')} /></Field>
        <div className="grid" style={{ alignContent: 'end' }}>
          <label className="check"><input type="checkbox" checked={f.allowLooseSale} onChange={set('allowLooseSale')} /> Allow loose sale</label>
          <label className="check"><input type="checkbox" checked={f.isActive} onChange={set('isActive')} /> Active</label>
          <label className="check" title="Prescription (scheduled) medicines are never listed publicly"><input type="checkbox" checked={f.onlineListed && f.schedule === 'None'} disabled={f.schedule !== 'None'} onChange={set('onlineListed')} /> List on online ordering page</label>
        </div>
      </div>
      <p className="small muted" style={{ margin: 0 }}>Verify GST rate, HSN and schedule against current rules — these drive billing and record requirements.</p>
      <div className="row end"><button className="primary" disabled={busy}>{busy ? 'Saving…' : 'Save product'}</button></div>
    </form>
  );
}
