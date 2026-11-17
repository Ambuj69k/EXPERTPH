import { useState } from 'react';
import { api } from '../api';
import { ErrorBanner, Field, PageHead, useLoad } from '../ui';
import { expiry, money, num, today } from '../util';

export function DailySalesPage() {
  const [date, setDate] = useState(today());
  const { data: r, error } = useLoad(() => api.dailySales(date), [date]);

  return (
    <>
      <PageHead title="Daily sales" sub="Business-day summary for this store.">
        <Field label="Date"><input type="date" value={date} max={today()} onChange={e => setDate(e.target.value)} /></Field>
      </PageHead>
      <ErrorBanner error={error} />
      {r && (
        <>
          <div className="grid c4">
            <Stat label="Net sales" value={money(r.netSales)} sub={`${r.invoiceCount} bills · ${r.cancelledCount} cancelled`} />
            <Stat label="Returns" value={money(r.returns)} sub="refunded today" />
            <Stat label="Net after returns" value={money(r.netAfterReturns)} />
            <Stat label="Gross margin" value={money(r.grossMargin)} sub={`on cost ${money(r.costOfGoods)}`} />
          </div>
          <div className="grid c2" style={{ marginTop: 16 }}>
            <div className="panel">
              <h2>Collections by mode</h2>
              <table>
                <tbody>
                  {r.paymentsByMode.map(p => <tr key={p.mode}><td>{p.mode}</td><td className="num">{money(p.amount)}</td></tr>)}
                  {r.refundsByMode.map(p => <tr key={'r' + p.mode}><td>Refund · {p.mode}</td><td className="num" style={{ color: 'var(--danger)' }}>-{money(p.amount)}</td></tr>)}
                  {!r.paymentsByMode.length && !r.refundsByMode.length && <tr><td className="muted">No transactions</td><td /></tr>}
                </tbody>
              </table>
              <p className="small muted">Cash expected in drawer = Cash collections − Cash refunds. Count and compare at day end.</p>
            </div>
            <div className="panel">
              <h2>Tax summary</h2>
              <table>
                <tbody>
                  <tr><td>Gross (MRP)</td><td className="num">{money(r.grossAmount)}</td></tr>
                  <tr><td>Discount</td><td className="num">-{money(r.discountAmount)}</td></tr>
                  <tr><td>Taxable value</td><td className="num">{money(r.taxableAmount)}</td></tr>
                  <tr><td>GST</td><td className="num">{money(r.taxAmount)}</td></tr>
                </tbody>
              </table>
              <p className="small muted">
                Gross margin = (sales taxable value − returns taxable value) − (cost of units sold − cost of units returned). Excludes GST and round-off.
                Not a statutory report.
              </p>
            </div>
          </div>
        </>
      )}
    </>
  );
}

function Stat({ label, value, sub }: { label: string; value: string; sub?: string }) {
  return (
    <div className="panel">
      <div className="small muted">{label}</div>
      <div style={{ fontSize: 22, fontWeight: 700, fontVariantNumeric: 'tabular-nums' }}>{value}</div>
      {sub && <div className="small muted">{sub}</div>}
    </div>
  );
}

export function RecoveryPage() {
  const { data: r, error } = useLoad(() => api.recovery(), []);
  return (
    <>
      <PageHead title="Return recovery" sub="How much of the value sent back to suppliers actually came back as credit." />
      <ErrorBanner error={error} />
      {r && (
        <>
          <div className="grid c4">
            <Stat label="Pending with suppliers" value={money(r.pendingClaims)} sub={`${r.pendingCount} dispatched return(s)`} />
            <Stat label="Claimed (settled)" value={money(r.claimed)} />
            <Stat label="Credit received" value={money(r.accepted)} sub={`${num(r.recoveryPercent)}% recovery`} />
            <Stat label="Written off" value={money(r.writtenOff)} />
          </div>
          <div className="grid c2" style={{ marginTop: 16 }}>
            <div className="panel">
              <h2>Pending claims by age</h2>
              <table><tbody>{r.pendingAging.map(b => <tr key={b.label}><td>{b.label}</td><td className="num">{b.count}</td><td className="num">{money(b.amount)}</td></tr>)}</tbody></table>
              <p className="small muted">Age counted from dispatch date. Follow up with suppliers on anything past their usual credit-note cycle.</p>
            </div>
            <div className="panel">
              <h2>By supplier</h2>
              <table>
                <thead><tr><th>Supplier</th><th className="num">Pending</th><th className="num">Claimed</th><th className="num">Received</th><th className="num">Written off</th></tr></thead>
                <tbody>
                  {r.bySupplier.map(s => <tr key={s.supplierId}><td>{s.supplierName}</td><td className="num">{money(s.pending)}</td><td className="num">{money(s.claimed)}</td><td className="num">{money(s.accepted)}</td><td className="num">{money(s.writtenOff)}</td></tr>)}
                  {r.bySupplier.length === 0 && <tr><td className="muted" colSpan={5}>No returns dispatched yet.</td></tr>}
                </tbody>
              </table>
            </div>
          </div>
          <p className="small muted">Recovery % = credit received ÷ claim value, for settled returns only (rejected returns excluded since stock came back). Claim value = qty × cost per unit + GST.</p>
        </>
      )}
    </>
  );
}

export function RetentionPage() {
  const monthStart = today().slice(0, 8) + '01';
  const [from, setFrom] = useState(monthStart);
  const [to, setTo] = useState(today());
  const { data: r, error } = useLoad(() => api.retention(from, to), [from, to]);
  return (
    <>
      <PageHead title="Customer retention" sub="How many bills are linked to known customers, and how many of them come back.">
        <Field label="From"><input type="date" value={from} onChange={e => setFrom(e.target.value)} /></Field>
        <Field label="To"><input type="date" value={to} max={today()} onChange={e => setTo(e.target.value)} /></Field>
      </PageHead>
      <ErrorBanner error={error} />
      {r && (
        <>
          <div className="grid c4">
            <Stat label="Bills linked to a customer" value={`${num(r.identifiedSharePercent)}%`} sub={`${r.identifiedBills} of ${r.totalBills} bills`} />
            <Stat label="Customers" value={String(r.uniqueCustomers)} sub={`${r.newCustomers} new · ${r.returningCustomers} returning`} />
            <Stat label="Repeat within period" value={`${num(r.repeatRatePercent)}%`} sub={`${r.repeatWithinPeriod} with 2+ bills`} />
            <Stat label="Walk-in bills" value={String(r.totalBills - r.identifiedBills)} sub="no mobile captured" />
          </div>
          <div className="panel" style={{ marginTop: 16 }}>
            <h2>Top customers</h2>
            <table>
              <thead><tr><th>Customer</th><th>Mobile</th><th className="num">Bills</th><th className="num">Spend</th></tr></thead>
              <tbody>{r.topCustomers.map(t => <tr key={t.customerId}><td>{t.name}</td><td>{t.phone}</td><td className="num">{t.bills}</td><td className="num">{money(t.spend)}</td></tr>)}</tbody>
            </table>
          </div>
          <p className="small muted">
            Bills = posted, not cancelled, by business date. New = first-ever bill in this period; returning = bought before this period.
            Repeat rate = customers with 2+ bills ÷ customers. Spend is before returns.
          </p>
        </>
      )}
    </>
  );
}

export function NearExpiryPage() {
  const [days, setDays] = useState(90);
  const { data, error, loading } = useLoad(() => api.nearExpiry(days), [days]);
  const totalCost = data?.reduce((s, r) => s + r.valueAtCost, 0) ?? 0;

  return (
    <>
      <PageHead title="Near expiry" sub="Stock to sell first, or return to the supplier before the return window closes.">
        <Field label="Expiring within">
          <select value={days} onChange={e => setDays(Number(e.target.value))}>
            {[30, 60, 90, 120, 180].map(d => <option key={d} value={d}>{d} days</option>)}
          </select>
        </Field>
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error} />
        {data && data.length > 0 && <p style={{ marginTop: 0 }}>Exposure at cost: <b>{money(totalCost)}</b> across {data.length} batch(es).</p>}
        {!loading && data?.length === 0 ? <div className="empty">No stock expiring in this window.</div> : (
          <div className="table-wrap">
            <table>
              <thead><tr><th>Product</th><th>Batch</th><th>Expiry</th><th className="num">Days left</th><th className="num">Qty</th><th className="num">Value at cost</th><th className="num">Value at MRP</th></tr></thead>
              <tbody>
                {data?.map(r => (
                  <tr key={r.batchId}>
                    <td><b>{r.productName}</b> <span className="small muted">{r.manufacturer}</span></td>
                    <td>{r.batchNo}</td><td>{expiry(r.expiryDate)}</td>
                    <td className="num">{r.isExpired ? <span className="badge danger">expired</span> : <span className={r.daysToExpiry <= 30 ? 'badge warn' : ''}>{r.daysToExpiry}</span>}</td>
                    <td className="num">{num(r.qtyUnits)}</td><td className="num">{money(r.valueAtCost)}</td><td className="num">{money(r.valueAtMrp)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </>
  );
}
