import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api';
import { ErrorBanner, PageHead, Pager, StatusBadge, useLoad } from '../ui';
import { money } from '../util';

export default function PurchasesPage() {
  const navigate = useNavigate();
  const [status, setStatus] = useState('');
  const [page, setPage] = useState(1);
  const { data, error, loading } = useLoad(() => api.purchases({ status: status || undefined, page }), [status, page]);

  return (
    <>
      <PageHead title="Purchases" sub="Supplier invoices. Posting a purchase adds its batches to stock.">
        <select value={status} onChange={e => { setStatus(e.target.value); setPage(1); }} aria-label="Status filter">
          <option value="">All</option><option>Draft</option><option>Posted</option><option>Cancelled</option>
        </select>
        <button className="primary" onClick={() => navigate('/purchases/new')}>+ New purchase</button>
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error} />
        {!loading && data?.items.length === 0 ? <div className="empty">No purchases yet. Enter your first supplier invoice.</div> : (
          <div className="table-wrap">
            <table>
              <thead><tr><th>Date</th><th>Supplier</th><th>Invoice no</th><th className="num">Lines</th><th className="num">Amount</th><th>Status</th></tr></thead>
              <tbody>
                {data?.items.map(p => (
                  <tr key={p.id} className="clickable" onClick={() => navigate(`/purchases/${p.id}`)}>
                    <td>{p.invoiceDate}</td><td>{p.supplierName}</td><td><b>{p.supplierInvoiceNo}</b></td>
                    <td className="num">{p.lineCount}</td><td className="num">{money(p.netAmount)}</td><td><StatusBadge status={p.status} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {data && <Pager page={page} total={data.total} pageSize={data.pageSize} onPage={setPage} />}
      </div>
    </>
  );
}
