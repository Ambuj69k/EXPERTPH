import { useState } from 'react';
import { api, ApiError, type ImportPreview, type ImportRow } from '../api';
import { ErrorBanner, Modal, toast } from '../ui';
import { expiry, num } from '../util';
import ProductPicker from './ProductPicker';

export interface ImportedLine {
  product: { id: string; name: string; packSize: number; gstRate: number };
  batchNo: string; expiryDate: string; mrp: number; rate: number; qtyPacks: number; freeQtyPacks: number; discountPercent: number; gstRate: number | null;
}

/**
 * CSV invoice import with mandatory human review. Nothing is saved to stock here: confirmed rows are
 * copied into the purchase draft, which the user still checks and posts.
 */
export default function ImportModal({ supplierId, onClose, onApply }: { supplierId: string; onClose: () => void; onApply: (lines: ImportedLine[]) => void }) {
  const [csv, setCsv] = useState('');
  const [preview, setPreview] = useState<ImportPreview | null>(null);
  const [chosen, setChosen] = useState<Record<number, { id: string; name: string; packSize: number } | null>>({});
  const [include, setInclude] = useState<Record<number, boolean>>({});
  const [remember, setRemember] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);

  const loadFile = (file: File) => { const r = new FileReader(); r.onload = () => setCsv(String(r.result ?? '')); r.readAsText(file); };

  const runPreview = async () => {
    setBusy(true); setError(null);
    try {
      const p = await api.importPreview({ supplierId, csvText: csv, saveMapping: true });
      setPreview(p);
      setChosen(Object.fromEntries(p.rows.map(r => [r.rowNo, r.productId ? { id: r.productId, name: r.productName ?? '', packSize: 1 } : null])));
      setInclude(Object.fromEntries(p.rows.map(r => [r.rowNo, true])));
    } catch (e) { setError(e); } finally { setBusy(false); }
  };

  const tryPdf = async () => {
    setError(null);
    try {
      const res = await fetch(`/api/v1/purchases/import/extract?supplierId=${supplierId}`, { method: 'POST', headers: { Authorization: `Bearer ${localStorage.getItem('pharmaos.token') ?? ''}` } });
      const body = await res.json().catch(() => ({}));
      if (!res.ok) throw new ApiError(res.status, body.code ?? '', body.detail ?? 'Not available');
    } catch (e) { setError(e); }
  };

  const rowsReady = preview?.rows.filter(r => include[r.rowNo]) ?? [];
  const blocking = rowsReady.filter(r => !chosen[r.rowNo] || !r.batchNo || !r.expiryDate || !r.mrp || r.rate == null);

  const apply = async () => {
    if (!preview) return;
    setBusy(true); setError(null);
    try {
      const products = await Promise.all(rowsReady.map(r => api.products(chosen[r.rowNo]!.name).then(p => p.items.find(x => x.id === chosen[r.rowNo]!.id))));
      if (remember) {
        await api.saveItemMap(supplierId, rowsReady.filter(r => r.matchedBy !== 'supplier code').map(r => ({ supplierItemCode: r.itemCode ?? null, supplierItemName: r.itemName, productId: chosen[r.rowNo]!.id })));
      }
      onApply(rowsReady.map((r, i) => {
        const p = products[i]!;
        return {
          product: { id: p.id, name: p.name, packSize: p.packSize, gstRate: p.gstRate }, batchNo: r.batchNo!, expiryDate: r.expiryDate!, mrp: r.mrp!, rate: r.rate!,
          qtyPacks: r.qtyPacks, freeQtyPacks: r.freeQtyPacks, discountPercent: r.discountPercent, gstRate: r.gstRate ?? p.gstRate,
        };
      }));
      toast(`${rowsReady.length} line(s) added to the draft — check them before posting`);
    } catch (e) { setError(e); } finally { setBusy(false); }
  };

  return (
    <Modal title="Import supplier invoice" onClose={onClose} wide>
      <div className="grid">
        <ErrorBanner error={error} />
        {!preview && <>
          <p className="small muted" style={{ margin: 0 }}>Upload the CSV/Excel-exported file from your distributor, or paste it. Columns are detected from the header row and remembered per supplier.</p>
          <div className="row">
            <input type="file" accept=".csv,text/csv,text/plain" onChange={e => e.target.files?.[0] && loadFile(e.target.files[0])} />
            <span className="spacer" />
            <button onClick={tryPdf}>PDF / photo…</button>
          </div>
          <textarea rows={8} value={csv} onChange={e => setCsv(e.target.value)} placeholder="Item Code,Item Name,Batch,Expiry,Qty,Free,MRP,Rate,Disc %,GST %" style={{ width: '100%', fontFamily: 'monospace' }} />
          <div className="row end"><button className="primary" disabled={busy || !csv.trim()} onClick={runPreview}>Preview</button></div>
        </>}
        {preview && <>
          <div className="row small">
            <span className="badge ok">{preview.matched} matched</span><span className="badge warn">{preview.unmatched} need a product</span>
            <span className="muted">Columns: name = {preview.mapping.itemName}, qty = {preview.mapping.qty}{preview.mapping.rate && `, rate = ${preview.mapping.rate}`}</span>
          </div>
          <div className="table-wrap">
            <table className="small">
              <thead><tr><th /><th>Supplier item</th><th>Our product</th><th>Batch</th><th>Exp</th><th className="num">Qty+Free</th><th className="num">MRP</th><th className="num">Rate</th><th className="num">Disc</th><th>Check</th></tr></thead>
              <tbody>
                {preview.rows.map((r: ImportRow) => (
                  <tr key={r.rowNo} className={include[r.rowNo] ? '' : 'muted'}>
                    <td><input type="checkbox" checked={!!include[r.rowNo]} onChange={e => setInclude({ ...include, [r.rowNo]: e.target.checked })} /></td>
                    <td>{r.itemName}{r.itemCode && <div className="muted">{r.itemCode}</div>}</td>
                    <td style={{ minWidth: 200 }}>
                      {r.suggestions.length > 0 && !chosen[r.rowNo] && (
                        <select onChange={e => { const s = r.suggestions.find(x => x.productId === e.target.value); if (s) setChosen({ ...chosen, [r.rowNo]: { id: s.productId, name: s.name, packSize: s.packSize } }); }} defaultValue="">
                          <option value="">Suggestions…</option>{r.suggestions.map(s => <option key={s.productId} value={s.productId}>{s.name}</option>)}
                        </select>
                      )}
                      <ProductPicker value={chosen[r.rowNo] ?? null} onPick={p => setChosen({ ...chosen, [r.rowNo]: { id: p.id, name: p.name, packSize: p.packSize } })} />
                      {r.matchedBy && chosen[r.rowNo]?.id === r.productId && <div className="muted">matched by {r.matchedBy}</div>}
                    </td>
                    <td>{r.batchNo}</td><td>{r.expiryDate ? expiry(r.expiryDate) : '-'}</td>
                    <td className="num">{num(r.qtyPacks)}{r.freeQtyPacks ? ` + ${num(r.freeQtyPacks)}` : ''}</td>
                    <td className="num">{r.mrp ?? '-'}</td><td className="num">{r.rate ?? '-'}</td><td className="num">{r.discountPercent}%</td>
                    <td>{r.issues.filter(i => !(i.startsWith('Product not matched') && chosen[r.rowNo])).map(i => <div key={i} style={{ color: 'var(--danger)' }}>{i}</div>)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <label className="check"><input type="checkbox" checked={remember} onChange={e => setRemember(e.target.checked)} /> Remember these product matches for this supplier</label>
          {blocking.length > 0 && <div className="alert warn" style={{ margin: 0 }}>{blocking.length} selected row(s) still need a product, batch, expiry, MRP or rate.</div>}
          <div className="row end">
            <button onClick={() => setPreview(null)}>Back</button>
            <button className="primary" disabled={busy || rowsReady.length === 0 || blocking.length > 0} onClick={apply}>Add {rowsReady.length} line(s) to purchase</button>
          </div>
        </>}
      </div>
    </Modal>
  );
}
