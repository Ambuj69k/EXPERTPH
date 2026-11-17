import { useState } from 'react';
import { api, type Supplier } from '../api';
import { ErrorBanner, Field, Modal, PageHead, Pager, toast, useDebounced, useLoad } from '../ui';

export default function SuppliersPage() {
  const [q, setQ] = useState('');
  const dq = useDebounced(q);
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState<Supplier | 'new' | null>(null);
  const { data, error, loading, reload } = useLoad(() => api.suppliers(dq, page), [dq, page]);

  return (
    <>
      <PageHead title="Suppliers" sub="Distributors and stockists you purchase from.">
        <input placeholder="Search name or GSTIN" value={q} onChange={e => { setQ(e.target.value); setPage(1); }} />
        <button className="primary" onClick={() => setEditing('new')}>+ New supplier</button>
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error} />
        {!loading && data?.items.length === 0 ? <div className="empty">No suppliers yet.</div> : (
          <div className="table-wrap">
            <table>
              <thead><tr><th>Name</th><th>GSTIN</th><th>Phone</th><th>Drug licence</th><th className="num">Credit days</th></tr></thead>
              <tbody>
                {data?.items.map(s => (
                  <tr key={s.id} className="clickable" onClick={() => setEditing(s)}>
                    <td><b>{s.name}</b> {!s.isActive && <span className="badge">inactive</span>}</td>
                    <td>{s.gstin}</td><td>{s.phone}</td><td>{s.drugLicenseNo}</td><td className="num">{s.paymentTermsDays}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {data && <Pager page={page} total={data.total} pageSize={data.pageSize} onPage={setPage} />}
      </div>
      {editing && (
        <Modal title={editing === 'new' ? 'New supplier' : `Edit ${editing.name}`} onClose={() => setEditing(null)}>
          <SupplierForm supplier={editing === 'new' ? null : editing} onSaved={() => { setEditing(null); reload(); }} />
        </Modal>
      )}
    </>
  );
}

export function SupplierForm({ supplier, onSaved }: { supplier: Supplier | null; onSaved: (s: Supplier) => void }) {
  const [f, setF] = useState({
    name: supplier?.name ?? '', gstin: supplier?.gstin ?? '', phone: supplier?.phone ?? '', email: supplier?.email ?? '',
    address: supplier?.address ?? '', drugLicenseNo: supplier?.drugLicenseNo ?? '', paymentTermsDays: String(supplier?.paymentTermsDays ?? 30),
    isActive: supplier?.isActive ?? true,
  });
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setF({ ...f, [k]: e.target.type === 'checkbox' ? e.target.checked : e.target.value });

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true); setError(null);
    const blank = (s: string) => s.trim() || null;
    const body = {
      name: f.name, gstin: blank(f.gstin), phone: blank(f.phone), email: blank(f.email), address: blank(f.address),
      drugLicenseNo: blank(f.drugLicenseNo), paymentTermsDays: Number(f.paymentTermsDays), isActive: f.isActive, version: supplier?.version ?? null,
    };
    try {
      const saved = supplier ? await api.updateSupplier(supplier.id, body) : await api.createSupplier(body);
      toast(`Saved ${saved.name}`); onSaved(saved);
    } catch (err) { setError(err); } finally { setBusy(false); }
  };

  return (
    <form className="grid" onSubmit={submit}>
      <ErrorBanner error={error} />
      <Field label="Name"><input value={f.name} onChange={set('name')} required autoFocus /></Field>
      <div className="grid c3">
        <Field label="GSTIN"><input value={f.gstin} onChange={set('gstin')} maxLength={15} /></Field>
        <Field label="Phone"><input value={f.phone} onChange={set('phone')} /></Field>
        <Field label="Email"><input type="email" value={f.email} onChange={set('email')} /></Field>
      </div>
      <Field label="Address"><input value={f.address} onChange={set('address')} /></Field>
      <div className="grid c3">
        <Field label="Drug licence no."><input value={f.drugLicenseNo} onChange={set('drugLicenseNo')} /></Field>
        <Field label="Credit days"><input type="number" min={0} max={365} value={f.paymentTermsDays} onChange={set('paymentTermsDays')} /></Field>
        <label className="check" style={{ alignSelf: 'end' }}><input type="checkbox" checked={f.isActive} onChange={set('isActive')} /> Active</label>
      </div>
      <div className="row end"><button className="primary" disabled={busy}>{busy ? 'Saving…' : 'Save supplier'}</button></div>
    </form>
  );
}
