import { useEffect, useState } from 'react';
import { api, type Branch, type Product, type StockRow } from '../api';
import { useAuth } from '../auth';
import ProductPicker from '../components/ProductPicker';
import { ErrorBanner, Field, Modal, PageHead, StatusBadge, toast, useLoad } from '../ui';
import { dateTime, expiry, money, num, today } from '../util';

// ---------------- Branches ----------------

export function BranchesPage() {
  const { data, error, reload } = useLoad(() => api.branches(), []);
  const [edit, setEdit] = useState<Branch | 'new' | null>(null);
  return (
    <>
      <PageHead title="Branches" sub="Stores and warehouses of this business. Products, suppliers and customers are shared; stock and numbering are per branch.">
        <button className="primary" onClick={() => setEdit('new')}>+ Branch</button>
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error} />
        <table>
          <thead><tr><th>Code</th><th>Name</th><th>Type</th><th>GSTIN</th><th>Drug licence</th><th>Status</th></tr></thead>
          <tbody>
            {data?.map(b => (
              <tr key={b.id} className="clickable" onClick={() => setEdit(b)}>
                <td><b>{b.code}</b></td><td>{b.name}</td><td>{b.isWarehouse ? <span className="badge brand">warehouse</span> : 'store'}</td>
                <td>{b.gstin}</td><td>{b.drugLicenseNo}{b.drugLicenseValidTo && <div className="small muted">valid to {b.drugLicenseValidTo}</div>}</td>
                <td>{b.isActive ? <span className="badge ok">active</span> : <span className="badge">inactive</span>}</td>
              </tr>
            ))}
          </tbody>
        </table>
        <p className="small muted">Licence details are stored for reference; PharmaOS does not verify them. Each store needs its own licences as applicable.</p>
      </div>
      {edit && <BranchForm branch={edit === 'new' ? null : edit} onClose={() => setEdit(null)} onSaved={() => { setEdit(null); reload(); }} />}
    </>
  );
}

function BranchForm({ branch, onClose, onSaved }: { branch: Branch | null; onClose: () => void; onSaved: () => void }) {
  const [f, setF] = useState({
    name: branch?.name ?? '', code: branch?.code ?? '', address: branch?.address ?? '', phone: branch?.phone ?? '', gstin: branch?.gstin ?? '',
    stateCode: branch?.stateCode ?? '', drugLicenseNo: branch?.drugLicenseNo ?? '', drugLicenseValidTo: branch?.drugLicenseValidTo ?? '',
    isWarehouse: branch?.isWarehouse ?? false, isActive: branch?.isActive ?? true,
  });
  const [error, setError] = useState<unknown>(null);
  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement>) => setF({ ...f, [k]: e.target.type === 'checkbox' ? e.target.checked : e.target.value });
  const save = async () => {
    setError(null);
    try { await api.saveBranch(branch?.id ?? null, { ...f, drugLicenseValidTo: f.drugLicenseValidTo || null, gstin: f.gstin || null }); toast('Branch saved'); onSaved(); }
    catch (e) { setError(e); }
  };
  return (
    <Modal title={branch ? `Edit ${branch.name}` : 'New branch'} onClose={onClose}>
      <div className="grid">
        <ErrorBanner error={error} />
        <div className="grid c3">
          <Field label="Name"><input value={f.name} onChange={set('name')} /></Field>
          <Field label="Code (≤4, used in bill numbers)"><input value={f.code} maxLength={4} onChange={set('code')} /></Field>
          <Field label="GST state code"><input value={f.stateCode} maxLength={2} onChange={set('stateCode')} /></Field>
        </div>
        <Field label="Address"><input value={f.address} onChange={set('address')} /></Field>
        <div className="grid c4">
          <Field label="Phone"><input value={f.phone} onChange={set('phone')} /></Field>
          <Field label="GSTIN"><input value={f.gstin} maxLength={15} onChange={set('gstin')} /></Field>
          <Field label="Drug licence no."><input value={f.drugLicenseNo} onChange={set('drugLicenseNo')} /></Field>
          <Field label="Licence valid to"><input type="date" value={f.drugLicenseValidTo} onChange={set('drugLicenseValidTo')} /></Field>
        </div>
        <div className="row">
          <label className="check"><input type="checkbox" checked={f.isWarehouse} onChange={set('isWarehouse')} /> Warehouse (no counter billing)</label>
          <label className="check"><input type="checkbox" checked={f.isActive} onChange={set('isActive')} /> Active</label>
        </div>
        <div className="row end"><button className="primary" onClick={save}>Save</button></div>
      </div>
    </Modal>
  );
}

/** Sidebar control: owner/accountant can switch the branch they are working in. */
export function BranchSwitcher() {
  const { session, signIn, can } = useAuth();
  const [branches, setBranches] = useState<Branch[]>([]);
  useEffect(() => { if (can('Owner', 'Accountant')) api.branches().then(setBranches).catch(() => {}); }, [can, session?.branchId]);
  if (branches.filter(b => b.isActive).length < 2) return null;
  const change = async (id: string) => {
    try { signIn(await api.switchBranch(id)); toast('Switched branch'); } catch (e) { alert((e as Error).message); }
  };
  return (
    <select value={session?.branchId} onChange={e => change(e.target.value)} aria-label="Working branch" style={{ margin: '0 10px 10px', background: '#1b3940', color: '#fff', border: '1px solid #2c5059' }}>
      {branches.filter(b => b.isActive).map(b => <option key={b.id} value={b.id}>{b.name}{b.isWarehouse ? ' (WH)' : ''}</option>)}
    </select>
  );
}

// ---------------- Transfers ----------------

export function TransfersPage() {
  const { data, error, reload } = useLoad(() => api.transfers(), []);
  const [open, setOpen] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  return (
    <>
      <PageHead title="Stock transfers" sub="Move stock between your own branches. Stock is in transit until the other branch confirms what arrived.">
        <button className="primary" onClick={() => setCreating(true)}>+ New transfer</button>
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error} />
        {data?.length === 0 ? <div className="empty">No transfers yet.</div> : (
          <table>
            <thead><tr><th>Transfer</th><th /><th>From</th><th>To</th><th>Created</th><th className="num">Lines</th><th>Status</th></tr></thead>
            <tbody>
              {data?.map(t => (
                <tr key={t.id} className="clickable" onClick={() => setOpen(t.id)}>
                  <td><b>{t.transferNo}</b></td><td>{t.direction === 'in' ? <span className="badge brand">incoming</span> : <span className="badge">outgoing</span>}</td>
                  <td>{t.fromBranch}</td><td>{t.toBranch}</td><td>{dateTime(t.createdAt)}</td><td className="num">{t.lineCount}</td>
                  <td>{t.status === 'Dispatched' ? <span className="badge warn">in transit</span> : <StatusBadge status={t.status} />}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
      {creating && <NewTransfer onClose={() => setCreating(false)} onCreated={id => { setCreating(false); reload(); setOpen(id); }} />}
      {open && <TransferDetail id={open} onClose={() => { setOpen(null); reload(); }} />}
    </>
  );
}

function NewTransfer({ onClose, onCreated }: { onClose: () => void; onCreated: (id: string) => void }) {
  const { session } = useAuth();
  const dir = useLoad(() => api.branchDirectory(), []);
  const [to, setTo] = useState('');
  const [product, setProduct] = useState<Product | null>(null);
  const [stock, setStock] = useState<StockRow | null>(null);
  const [lines, setLines] = useState<{ batchId: string; label: string; qtyUnits: number; max: number }[]>([]);
  const [notes, setNotes] = useState('');
  const [error, setError] = useState<unknown>(null);

  const pick = async (p: Product) => {
    setProduct(p);
    const s = await api.stock({ q: p.name, inStock: true });
    setStock(s.items.find(i => i.productId === p.id) ?? null);
  };
  const add = (batchId: string, label: string, max: number) => {
    if (lines.some(l => l.batchId === batchId)) return;
    setLines([...lines, { batchId, label, qtyUnits: max, max }]);
  };
  const save = async () => {
    setError(null);
    try { const t = await api.createTransfer({ toBranchId: to, notes, lines: lines.map(l => ({ batchId: l.batchId, qtyUnits: l.qtyUnits })) }); toast(`${t.transferNo} created`); onCreated(t.id); }
    catch (e) { setError(e); }
  };
  return (
    <Modal title="New transfer" onClose={onClose} wide>
      <div className="grid">
        <ErrorBanner error={error ?? dir.error} />
        <div className="grid c2">
          <Field label="From"><input value={session?.branchName ?? ''} disabled /></Field>
          <Field label="To branch"><select value={to} onChange={e => setTo(e.target.value)}><option value="">Choose…</option>
            {dir.data?.filter(b => b.id !== session?.branchId).map(b => <option key={b.id} value={b.id}>{b.name}{b.isWarehouse ? ' (warehouse)' : ''}</option>)}</select></Field>
        </div>
        <ProductPicker value={product ? { id: product.id, name: product.name } : null} onPick={pick} />
        {stock && (
          <table className="small"><tbody>
            {stock.batches.filter(b => b.qtyUnits > 0).map(b => (
              <tr key={b.batchId}><td>{b.batchNo}</td><td>exp {expiry(b.expiryDate)} {b.isExpired && <span className="badge danger">expired</span>}</td>
                <td className="num">{num(b.qtyUnits)} {stock.baseUnit}</td><td><button className="small" onClick={() => add(b.batchId, `${stock.name} · ${b.batchNo}`, b.qtyUnits)}>Add</button></td></tr>
            ))}
          </tbody></table>
        )}
        {lines.length > 0 && (
          <table>
            <thead><tr><th>Item</th><th className="num">Qty (units)</th><th /></tr></thead>
            <tbody>{lines.map(l => (
              <tr key={l.batchId}><td>{l.label}</td>
                <td className="num"><input className="qty" type="number" min={1} max={l.max} value={l.qtyUnits} onChange={e => setLines(ls => ls.map(x => x.batchId === l.batchId ? { ...x, qtyUnits: Math.min(l.max, Math.max(1, Math.floor(Number(e.target.value) || 1))) } : x))} /></td>
                <td><button className="link" onClick={() => setLines(ls => ls.filter(x => x.batchId !== l.batchId))}>✕</button></td></tr>
            ))}</tbody>
          </table>
        )}
        <Field label="Notes"><input value={notes} onChange={e => setNotes(e.target.value)} /></Field>
        <div className="row end"><button className="primary" disabled={!to || lines.length === 0} onClick={save}>Create draft</button></div>
      </div>
    </Modal>
  );
}

function TransferDetail({ id, onClose }: { id: string; onClose: () => void }) {
  const { session } = useAuth();
  const { data: t, error, setData } = useLoad(() => api.transfer(id), [id]);
  const [recv, setRecv] = useState<Record<string, string>>({});
  const [notes, setNotes] = useState('');
  const [err, setErr] = useState<unknown>(null);
  const act = async (fn: () => Promise<unknown>, msg: string) => {
    setErr(null);
    try { const r = await fn(); if (r && typeof r === 'object') setData(r as never); toast(msg); } catch (e) { setErr(e); }
  };
  const canReceive = t?.status === 'Dispatched' && t.toBranchId === session?.branchId;
  return (
    <Modal title={t ? `Transfer ${t.transferNo}` : 'Transfer'} onClose={onClose} wide>
      <ErrorBanner error={error ?? err} />
      {t && (
        <div className="grid invoice">
          <div className="row"><b>{t.fromBranch}</b> → <b>{t.toBranch}</b>{t.status === 'Dispatched' ? <span className="badge warn">in transit</span> : <StatusBadge status={t.status} />}
            <span className="small muted">value {money(t.value)}{t.dispatchedAt && ` · sent ${dateTime(t.dispatchedAt)}`}{t.receivedAt && ` · received ${dateTime(t.receivedAt)}`}</span></div>
          <table>
            <thead><tr><th>Product</th><th>Batch</th><th>Exp</th><th className="num">Sent</th><th className="num">Received</th><th className="num">Short</th></tr></thead>
            <tbody>{t.lines.map(l => (
              <tr key={l.id}><td>{l.productName}</td><td>{l.batchNo}</td><td>{expiry(l.expiryDate)}</td><td className="num">{num(l.qtyUnits)}</td>
                <td className="num">{canReceive
                  ? <input className="qty" type="number" min={0} max={l.qtyUnits} value={recv[l.id] ?? String(l.qtyUnits)} onChange={e => setRecv({ ...recv, [l.id]: e.target.value })} />
                  : l.receivedQtyUnits ?? '-'}</td>
                <td className="num">{l.shortageUnits ? <span className="badge danger">{num(l.shortageUnits)}</span> : '-'}</td></tr>
            ))}</tbody>
          </table>
          {t.shortageValue > 0 && <div className="alert warn" style={{ margin: 0 }}>Shortage written off: {money(t.shortageValue)} — {t.receiveNotes}</div>}
          {canReceive && <Field label="Notes (required if anything is short)"><input value={notes} onChange={e => setNotes(e.target.value)} /></Field>}
          <div className="row end no-print">
            {t.status === 'Draft' && t.fromBranchId === session?.branchId && <>
              <button className="danger" onClick={() => act(async () => { await api.cancelTransfer(id); onClose(); }, 'Cancelled')}>Cancel</button>
              <button className="primary" onClick={() => confirm('Dispatch now? Stock leaves this branch.') && act(() => api.dispatchTransfer(id), 'Dispatched')}>Dispatch</button>
            </>}
            {canReceive && <button className="primary" onClick={() => act(() => api.receiveTransfer(id, { notes, lines: t.lines.map(l => ({ lineId: l.id, receivedQtyUnits: Number(recv[l.id] ?? l.qtyUnits) })) }), 'Received into stock')}>Confirm receipt</button>}
            {t.status === 'Dispatched' && !canReceive && <span className="small muted">Waiting for {t.toBranch} to confirm receipt.</span>}
            <button onClick={() => window.print()}>Print</button>
          </div>
        </div>
      )}
    </Modal>
  );
}

// ---------------- Indents ----------------

export function IndentsPage() {
  const { session } = useAuth();
  const { data, error, reload } = useLoad(() => api.indents(), []);
  const dir = useLoad(() => api.branchDirectory(), []);
  const [creating, setCreating] = useState(false);
  const [supplier, setSupplier] = useState('');
  const [lines, setLines] = useState<{ product: Product; qty: number }[]>([]);
  const [err, setErr] = useState<unknown>(null);
  const act = async (fn: () => Promise<unknown>, msg: string) => { setErr(null); try { await fn(); toast(msg); reload(); } catch (e) { setErr(e); } };
  const raise = () => act(async () => {
    await api.createIndent({ supplyingBranchId: supplier, lines: lines.map(l => ({ productId: l.product.id, qtyUnits: l.qty })) });
    setCreating(false); setLines([]);
  }, 'Indent sent');
  return (
    <>
      <PageHead title="Indents" sub="Ask the warehouse or another store for stock. The supplying branch turns it into a transfer.">
        <button className="primary" onClick={() => setCreating(true)}>+ Raise indent</button>
      </PageHead>
      <ErrorBanner error={error ?? err} />
      <div className="panel">
        {data?.length === 0 ? <div className="empty">No indents.</div> : (
          <table>
            <thead><tr><th>Indent</th><th>From</th><th>To</th><th>Items</th><th>Status</th><th /></tr></thead>
            <tbody>{data?.map(i => (
              <tr key={i.id}>
                <td><b>{i.indentNo}</b><div className="small muted">{dateTime(i.createdAt)}</div></td><td>{i.requestingBranch}</td><td>{i.supplyingBranch}</td>
                <td className="small">{i.lines.map(l => <div key={l.productId}>{l.productName} × {num(l.qtyUnits)} {i.status === 'Requested' && i.direction === 'received' && <span className="muted">(have {num(l.availableAtSupplier)})</span>}</div>)}</td>
                <td><StatusBadge status={i.status} />{i.rejectReason && <div className="small muted">{i.rejectReason}</div>}</td>
                <td>{i.status === 'Requested' && i.supplyingBranchId === session?.branchId && <div className="row" style={{ flexWrap: 'nowrap' }}>
                  <button className="small primary" onClick={() => act(() => api.fulfilIndent(i.id), 'Transfer draft created — dispatch it from Transfers')}>Fulfil</button>
                  <button className="small" onClick={() => { const r = prompt('Reason for rejecting?'); if (r) act(() => api.rejectIndent(i.id, r), 'Rejected'); }}>Reject</button>
                </div>}</td>
              </tr>
            ))}</tbody>
          </table>
        )}
      </div>
      {creating && (
        <Modal title="Raise indent" onClose={() => setCreating(false)}>
          <div className="grid">
            <Field label="Ask"><select value={supplier} onChange={e => setSupplier(e.target.value)}><option value="">Choose branch/warehouse…</option>
              {dir.data?.filter(b => b.id !== session?.branchId).map(b => <option key={b.id} value={b.id}>{b.name}{b.isWarehouse ? ' (warehouse)' : ''}</option>)}</select></Field>
            <ProductPicker value={null} onPick={p => !lines.some(l => l.product.id === p.id) && setLines([...lines, { product: p, qty: p.packSize }])} />
            <table className="small"><tbody>{lines.map(l => (
              <tr key={l.product.id}><td>{l.product.name}</td>
                <td className="num"><input className="qty" type="number" min={1} value={l.qty} onChange={e => setLines(ls => ls.map(x => x.product.id === l.product.id ? { ...x, qty: Math.max(1, Math.floor(Number(e.target.value) || 1)) } : x))} /> {l.product.baseUnit}</td>
                <td><button className="link" onClick={() => setLines(ls => ls.filter(x => x.product.id !== l.product.id))}>✕</button></td></tr>
            ))}</tbody></table>
            <div className="row end"><button className="primary" disabled={!supplier || lines.length === 0} onClick={raise}>Send indent</button></div>
          </div>
        </Modal>
      )}
    </>
  );
}

// ---------------- Branch comparison ----------------

export function BranchComparisonPage() {
  const [from, setFrom] = useState(today().slice(0, 8) + '01');
  const [to, setTo] = useState(today());
  const { data, error } = useLoad(() => api.branchComparison(from, to), [from, to]);
  const cols: [keyof NonNullable<typeof data>['total'], string, boolean][] = [
    ['bills', 'Bills', false], ['netSales', 'Net sales', true], ['returns', 'Returns', true], ['grossMargin', 'Gross margin', true], ['marginPercent', 'Margin %', false],
    ['stockValue', 'Stock (cost)', true], ['nearExpiryValue', 'Expiring ≤90d', true], ['customerDues', 'Customer dues', true], ['inTransitOut', 'In transit out', true],
  ];
  return (
    <>
      <PageHead title="Branch comparison" sub="All branches side by side.">
        <Field label="From"><input type="date" value={from} onChange={e => setFrom(e.target.value)} /></Field>
        <Field label="To"><input type="date" value={to} max={today()} onChange={e => setTo(e.target.value)} /></Field>
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error} />
        {data && (
          <div className="table-wrap">
            <table>
              <thead><tr><th>Branch</th>{cols.map(([k, l]) => <th key={k} className="num">{l}</th>)}</tr></thead>
              <tbody>
                {[...data.rows, data.total].map(r => (
                  <tr key={r.branchId} style={r.branchId === data.total.branchId ? { fontWeight: 700 } : undefined}>
                    <td>{r.branch} {r.isWarehouse && <span className="badge brand">WH</span>}</td>
                    {cols.map(([k, , isMoney]) => <td key={k} className="num">{isMoney ? money(r[k] as number) : k === 'marginPercent' ? `${num(r[k] as number)}%` : String(r[k])}</td>)}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <p className="small muted">Gross margin = (sales − returns, ex-GST) − cost of goods sold net of returns. Stock and expiring stock at current batch cost. Customer dues are all-time, not period.</p>
      </div>
    </>
  );
}
