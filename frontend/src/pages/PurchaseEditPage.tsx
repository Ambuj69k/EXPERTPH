import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { api, type Product, type Purchase, type Supplier } from '../api';
import { RoleSets, useAuth } from '../auth';
import ProductPicker from '../components/ProductPicker';
import ImportModal, { type ImportedLine } from '../components/ImportModal';
import { ErrorBanner, Field, Modal, PageHead, StatusBadge, toast } from '../ui';
import { expiry as fmtExpiry, money, newId, num, parseExpiry, round2, today } from '../util';
import { ProductForm } from './ProductsPage';
import { SupplierForm } from './SuppliersPage';

interface Row {
  key: string; product: { id: string; name: string; packSize: number; gstRate: number } | null;
  batchNo: string; expiry: string; mrp: string; rate: string; qty: string; free: string; disc: string; gst: string;
}
const blankRow = (): Row => ({ key: newId(), product: null, batchNo: '', expiry: '', mrp: '', rate: '', qty: '', free: '0', disc: '0', gst: '' });

const calc = (r: Row) => {
  const gross = round2((Number(r.rate) || 0) * (Number(r.qty) || 0));
  const taxable = round2(gross - round2(gross * (Number(r.disc) || 0) / 100));
  const tax = round2(taxable * (Number(r.gst) || 0) / 100);
  return { taxable, tax, total: round2(taxable + tax) };
};

export default function PurchaseEditPage() {
  const { id } = useParams();
  const isNew = id === 'new';
  const navigate = useNavigate();
  const { can } = useAuth();
  const [doc, setDoc] = useState<Purchase | null>(null);
  const [suppliers, setSuppliers] = useState<Supplier[]>([]);
  const [supplierId, setSupplierId] = useState('');
  const [invoiceNo, setInvoiceNo] = useState('');
  const [invoiceDate, setInvoiceDate] = useState(today());
  const [otherCharges, setOtherCharges] = useState('0');
  const [notes, setNotes] = useState('');
  const [rows, setRows] = useState<Row[]>([blankRow()]);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const [creating, setCreating] = useState<{ rowKey: string; name: string } | null>(null);
  const [newSupplier, setNewSupplier] = useState(false);
  const [cancelReason, setCancelReason] = useState<string | null>(null);
  const [params] = useSearchParams();
  const [poId, setPoId] = useState<string | null>(null);
  const [poNo, setPoNo] = useState<string | null>(null);
  const [importing, setImporting] = useState(false);

  const editable = isNew || doc?.status === 'Draft';

  useEffect(() => { api.suppliers('', 1).then(r => setSuppliers(r.items)).catch(setError); }, []);
  // receiving against a purchase order: prefill supplier and pending quantities
  useEffect(() => {
    const po = params.get('po');
    if (!isNew || !po) return;
    api.purchaseOrder(po).then(o => {
      setPoId(o.id); setPoNo(o.poNo); setSupplierId(o.supplierId);
      setRows(o.lines.filter(l => l.pendingQtyPacks > 0).map(l => ({
        ...blankRow(), product: { id: l.productId, name: l.productName, packSize: l.packSize, gstRate: l.gstRate }, gst: String(l.gstRate),
        qty: String(l.pendingQtyPacks), rate: l.expectedRate != null ? String(l.expectedRate) : '',
      })));
    }).catch(setError);
  }, [isNew, params]);

  useEffect(() => {
    if (isNew) return;
    api.purchase(id!).then(p => {
      setDoc(p); setSupplierId(p.supplierId); setPoId(p.purchaseOrderId ?? null); setInvoiceNo(p.supplierInvoiceNo); setInvoiceDate(p.invoiceDate);
      setOtherCharges(String(p.otherCharges)); setNotes(p.notes ?? '');
      setRows(p.lines.map(l => ({
        key: newId(), product: { id: l.productId, name: l.productName ?? '', packSize: l.packSize ?? 1, gstRate: l.gstRate ?? 0 },
        batchNo: l.batchNo, expiry: fmtExpiry(l.expiryDate), mrp: String(l.mrp), rate: String(l.rate), qty: String(l.qtyPacks),
        free: String(l.freeQtyPacks), disc: String(l.discountPercent), gst: String(l.gstRate),
      })));
    }).catch(setError);
  }, [id, isNew]);

  const totals = useMemo(() => {
    const t = rows.filter(r => r.product).map(calc).reduce((a, c) => ({ taxable: a.taxable + c.taxable, tax: a.tax + c.tax }), { taxable: 0, tax: 0 });
    const raw = round2(t.taxable + t.tax + (Number(otherCharges) || 0));
    return { ...t, raw, net: Math.round(raw) };
  }, [rows, otherCharges]);

  const setRow = (key: string, patch: Partial<Row>) => setRows(rs => rs.map(r => r.key === key ? { ...r, ...patch } : r));
  const pickProduct = (key: string, p: Product) =>
    setRow(key, { product: { id: p.id, name: p.name, packSize: p.packSize, gstRate: p.gstRate }, gst: String(p.gstRate) });

  const body = () => {
    const lines = rows.filter(r => r.product).map((r, i) => {
      const exp = parseExpiry(r.expiry);
      if (!exp) throw new Error(`Line ${i + 1}: expiry must be MM/YY.`);
      return {
        productId: r.product!.id, batchNo: r.batchNo, expiryDate: exp, mrp: Number(r.mrp), rate: Number(r.rate),
        qtyPacks: Number(r.qty) || 0, freeQtyPacks: Number(r.free) || 0, discountPercent: Number(r.disc) || 0,
        gstRate: r.gst === '' ? null : Number(r.gst),
      };
    });
    if (!supplierId) throw new Error('Select a supplier.');
    if (!lines.length) throw new Error('Add at least one product line.');
    return { purchaseOrderId: poId, supplierId, supplierInvoiceNo: invoiceNo, invoiceDate, otherCharges: Number(otherCharges) || 0, notes: notes || null, lines, version: doc?.version ?? null };
  };

  const save = async (post: boolean) => {
    setError(null);
    let b;
    try { b = body(); } catch (e) { setError(e); return; }
    if (post && !confirm(`Post this purchase? ${money(totals.net)} of stock will be added and the invoice becomes read-only.`)) return;
    setBusy(true);
    try {
      let saved = isNew ? await api.createPurchase(b) : await api.updatePurchase(doc!.id, b);
      if (post) saved = await api.postPurchase(saved.id);
      toast(post ? 'Purchase posted — stock updated' : 'Draft saved');
      if (isNew) navigate(`/purchases/${saved.id}`, { replace: true });
      setDoc(saved);
    } catch (e) { setError(e); } finally { setBusy(false); }
  };

  const remove = async () => {
    if (!doc || !confirm('Delete this draft?')) return;
    try { await api.deletePurchase(doc.id); navigate('/purchases'); } catch (e) { setError(e); }
  };

  const cancelPosted = async () => {
    if (!doc || !cancelReason?.trim()) return;
    setBusy(true);
    try { setDoc(await api.cancelPurchase(doc.id, cancelReason)); setCancelReason(null); toast('Purchase cancelled'); }
    catch (e) { setError(e); } finally { setBusy(false); }
  };

  return (
    <>
      <PageHead title={isNew ? 'New purchase' : `Purchase ${doc?.supplierInvoiceNo ?? ''}`} sub={doc ? `${doc.supplierName}` : 'Enter the supplier invoice exactly as printed.'}>
        {doc && <StatusBadge status={doc.status} />}
        <button onClick={() => navigate('/purchases')}>Back</button>
      </PageHead>
      <ErrorBanner error={error} />
      {doc?.cancelReason && <div className="alert error">Cancelled: {doc.cancelReason}</div>}
      {poId && <div className="alert ok">Receiving against purchase order {poNo ?? ''}. Received paid quantities update the order.</div>}

      <div className="panel grid">
        <div className="grid c4">
          <Field label="Supplier">
            <div className="row" style={{ flexWrap: 'nowrap' }}>
              <select value={supplierId} onChange={e => setSupplierId(e.target.value)} disabled={!editable} style={{ flex: 1 }} aria-label="Supplier">
                <option value="">Select…</option>
                {suppliers.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}
              </select>
              {editable && <button type="button" className="small" onClick={() => setNewSupplier(true)} aria-label="New supplier">+</button>}
            </div>
          </Field>
          <Field label="Supplier invoice no"><input value={invoiceNo} onChange={e => setInvoiceNo(e.target.value)} disabled={!editable} /></Field>
          <Field label="Invoice date"><input type="date" value={invoiceDate} max={today()} onChange={e => setInvoiceDate(e.target.value)} disabled={!editable} /></Field>
          <Field label="Other charges (freight etc.)"><input type="number" min={0} value={otherCharges} onChange={e => setOtherCharges(e.target.value)} disabled={!editable} /></Field>
        </div>
      </div>

      <div className="panel">
        <div className="table-wrap">
          <table>
            <thead>
              <tr><th>Product</th><th>Batch</th><th>Expiry (MM/YY)</th><th className="num">MRP/pack</th><th className="num">Rate/pack</th>
                <th className="num">Qty</th><th className="num">Free</th><th className="num">Disc %</th><th className="num">GST %</th><th className="num">Amount</th><th /></tr>
            </thead>
            <tbody>
              {rows.map((r, i) => {
                const c = calc(r);
                const last = i === rows.length - 1;
                const addOnEnter = (e: React.KeyboardEvent) => { if (e.key === 'Enter' && last && editable) { e.preventDefault(); setRows(rs => [...rs, blankRow()]); } };
                return (
                  <tr key={r.key}>
                    <td style={{ minWidth: 220 }}>
                      <ProductPicker value={r.product} disabled={!editable} onPick={p => pickProduct(r.key, p)}
                        onCreate={name => setCreating({ rowKey: r.key, name })} />
                      {r.product && <div className="small muted">{r.product.packSize} units/pack</div>}
                    </td>
                    <td><input className="short" value={r.batchNo} onChange={e => setRow(r.key, { batchNo: e.target.value.toUpperCase() })} disabled={!editable} /></td>
                    <td><input className="short" value={r.expiry} placeholder="MM/YY" onChange={e => setRow(r.key, { expiry: e.target.value })} disabled={!editable}
                      style={r.expiry && !parseExpiry(r.expiry) ? { borderColor: 'var(--danger)' } : undefined} /></td>
                    <td className="num"><input className="qty" type="number" value={r.mrp} onChange={e => setRow(r.key, { mrp: e.target.value })} disabled={!editable} /></td>
                    <td className="num"><input className="qty" type="number" value={r.rate} onChange={e => setRow(r.key, { rate: e.target.value })} disabled={!editable} /></td>
                    <td className="num"><input className="qty" type="number" value={r.qty} onChange={e => setRow(r.key, { qty: e.target.value })} disabled={!editable} /></td>
                    <td className="num"><input className="qty" type="number" value={r.free} onChange={e => setRow(r.key, { free: e.target.value })} disabled={!editable} /></td>
                    <td className="num"><input className="qty" type="number" value={r.disc} onChange={e => setRow(r.key, { disc: e.target.value })} disabled={!editable} /></td>
                    <td className="num"><input className="qty" type="number" value={r.gst} onChange={e => setRow(r.key, { gst: e.target.value })} disabled={!editable} onKeyDown={addOnEnter} /></td>
                    <td className="num">{num(c.total)}</td>
                    <td>{editable && rows.length > 1 && <button className="link" onClick={() => setRows(rs => rs.filter(x => x.key !== r.key))} aria-label="Remove line">✕</button>}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        {editable && (
          <div className="row" style={{ marginTop: 8 }}>
            <button className="small" onClick={() => setRows(rs => [...rs, blankRow()])}>+ Add line</button>
            <button className="small" disabled={!supplierId} title={supplierId ? '' : 'Select the supplier first'} onClick={() => setImporting(true)}>Import from file…</button>
          </div>
        )}
        <div className="row" style={{ marginTop: 12, alignItems: 'flex-start' }}>
          <Field label="Notes"><input value={notes} onChange={e => setNotes(e.target.value)} disabled={!editable} style={{ width: 320 }} /></Field>
          <span className="spacer" />
          <div className="totals" style={{ minWidth: 240 }}>
            <span>Taxable</span><span>{money(totals.taxable)}</span>
            <span>GST</span><span>{money(totals.tax)}</span>
            <span>Other charges</span><span>{money(Number(otherCharges) || 0)}</span>
            <span>Round off</span><span>{money(round2(totals.net - totals.raw))}</span>
            <span className="grand">Net payable</span><span className="grand">{money(doc && !editable ? doc.netAmount : totals.net)}</span>
          </div>
        </div>
        <p className="small muted">Free quantity is added to stock at zero cost, lowering the effective cost per unit. Compare the net payable with the printed invoice total before posting.</p>
        <div className="row end">
          {editable && !isNew && <button className="danger" onClick={remove} disabled={busy}>Delete draft</button>}
          {editable && <button onClick={() => save(false)} disabled={busy}>Save draft</button>}
          {editable && <button className="primary" onClick={() => save(true)} disabled={busy}>Save & post to stock</button>}
          {doc?.status === 'Posted' && can(...RoleSets.salesAdmin) && <button className="danger" onClick={() => setCancelReason('')}>Cancel purchase</button>}
        </div>
      </div>

      {importing && (
        <ImportModal supplierId={supplierId} onClose={() => setImporting(false)} onApply={(lines: ImportedLine[]) => {
          setRows(rs => [...rs.filter(r => r.product), ...lines.map(l => ({
            key: newId(), product: l.product, batchNo: l.batchNo, expiry: fmtExpiry(l.expiryDate), mrp: String(l.mrp), rate: String(l.rate),
            qty: String(l.qtyPacks), free: String(l.freeQtyPacks), disc: String(l.discountPercent), gst: l.gstRate == null ? '' : String(l.gstRate),
          }))]);
          setImporting(false);
        }} />
      )}
      {creating && (
        <Modal title="New product" onClose={() => setCreating(null)}>
          <ProductForm product={null} initialName={creating.name} onSaved={p => { pickProduct(creating.rowKey, p); setCreating(null); }} />
        </Modal>
      )}
      {newSupplier && (
        <Modal title="New supplier" onClose={() => setNewSupplier(false)}>
          <SupplierForm supplier={null} onSaved={s => { setSuppliers(ss => [...ss, s]); setSupplierId(s.id); setNewSupplier(false); }} />
        </Modal>
      )}
      {cancelReason !== null && (
        <Modal title="Cancel purchase" onClose={() => setCancelReason(null)}>
          <div className="grid">
            <div className="alert warn">All received quantities will be removed from stock. This is only possible if none of it has been sold.</div>
            <Field label="Reason"><input value={cancelReason} onChange={e => setCancelReason(e.target.value)} autoFocus /></Field>
            <div className="row end"><button className="danger" disabled={busy || !cancelReason.trim()} onClick={cancelPosted}>Cancel purchase</button></div>
          </div>
        </Modal>
      )}
    </>
  );
}
