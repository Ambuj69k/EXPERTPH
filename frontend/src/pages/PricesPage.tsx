import { useState } from 'react';
import { api } from '../api';
import { RoleSets, useAuth } from '../auth';
import { ErrorBanner, PageHead, toast, useDebounced, useLoad } from '../ui';
import { dateTime, money } from '../util';

/** This branch's selling prices. Bills always charge min(price, batch MRP) and show the saving against MRP. */
export default function PricesPage() {
  const { session, can } = useAuth();
  const editable = can(...RoleSets.salesAdmin);
  const [q, setQ] = useState('');
  const dq = useDebounced(q);
  const [only, setOnly] = useState(false);
  const { data, error, reload } = useLoad(() => api.branchPrices(dq, only), [dq, only]);
  const [edit, setEdit] = useState<Record<string, string>>({});
  const [err, setErr] = useState<unknown>(null);

  const save = async (productId: string, value: string) => {
    setErr(null);
    try { await api.setBranchPrice(productId, value.trim() === '' ? null : Number(value)); toast(value.trim() === '' ? 'Price removed — MRP applies' : 'Price saved'); reload(); setEdit(e => { const n = { ...e }; delete n[productId]; return n; }); }
    catch (e) { setErr(e); }
  };

  return (
    <>
      <PageHead title="Branch prices" sub={`Selling prices at ${session?.branchName}. Leave blank to sell at MRP. A price above a batch's MRP is ignored for that batch.`}>
        <input placeholder="Search product" value={q} onChange={e => setQ(e.target.value)} />
        <label className="check"><input type="checkbox" checked={only} onChange={e => setOnly(e.target.checked)} /> Only priced items</label>
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error ?? err} />
        <table>
          <thead><tr><th>Product</th><th className="num">Current MRP</th><th className="num">Selling price / pack</th><th className="num">Below MRP</th><th>Updated</th><th /></tr></thead>
          <tbody>
            {data?.map(r => {
              const v = edit[r.productId] ?? (r.pricePerPack == null ? '' : String(r.pricePerPack));
              const changed = edit[r.productId] !== undefined;
              return (
                <tr key={r.productId}>
                  <td><b>{r.productName}</b> <span className="small muted">{r.packSize}/{r.packUnit}</span></td>
                  <td className="num">{r.currentMrp != null ? money(r.currentMrp) : <span className="muted">no stock</span>}</td>
                  <td className="num">{editable
                    ? <input className="short" type="number" min={0} step="0.01" placeholder="MRP" value={v} onChange={e => setEdit({ ...edit, [r.productId]: e.target.value })} aria-label={`Price for ${r.productName}`} />
                    : r.pricePerPack != null ? money(r.pricePerPack) : '—'}</td>
                  <td className="num">{r.discountVsMrpPercent != null ? (r.discountVsMrpPercent > 0 ? `${r.discountVsMrpPercent}%` : <span className="badge warn">≥ MRP (MRP applies)</span>) : ''}</td>
                  <td className="small">{r.updatedAt ? dateTime(r.updatedAt) : ''}</td>
                  <td>{editable && changed && <button className="small primary" onClick={() => save(r.productId, v)}>Save</button>}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </>
  );
}
