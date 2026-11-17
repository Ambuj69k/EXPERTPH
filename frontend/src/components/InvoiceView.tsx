import type { Sale } from '../api';
import { dateTime, expiry, money, num, packs } from '../util';

/** Printable retail invoice. Layout is a starting point — invoice content requirements must be verified with a CA. */
export default function InvoiceView({ sale }: { sale: Sale }) {
  const h = sale.header;
  const taxByRate = Object.values(sale.lines.reduce<Record<string, { rate: number; taxable: number; tax: number }>>((acc, l) => {
    const k = String(l.gstRate);
    acc[k] ??= { rate: l.gstRate, taxable: 0, tax: 0 };
    acc[k].taxable += l.taxableAmount; acc[k].tax += l.taxAmount;
    return acc;
  }, {}));
  if (sale.deliveryCharge) {
    const d = taxByRate.find(t => t.rate === sale.deliveryChargeGstRate);
    if (d) { d.taxable += sale.deliveryChargeTaxable ?? 0; d.tax += sale.deliveryChargeTax ?? 0; }
    else taxByRate.push({ rate: sale.deliveryChargeGstRate ?? 0, taxable: sale.deliveryChargeTaxable ?? 0, tax: sale.deliveryChargeTax ?? 0 });
  }

  return (
    <div className="invoice">
      <div style={{ textAlign: 'center', marginBottom: 8 }}>
        <div style={{ fontSize: 16, fontWeight: 700 }}>{h.businessName}</div>
        <div>{h.branchName}{h.address ? `, ${h.address}` : ''}</div>
        <div className="small">
          {h.gstin && <>GSTIN: {h.gstin} · </>}{h.drugLicenseNo && <>DL: {h.drugLicenseNo} · </>}{h.phone && <>Ph: {h.phone}</>}
        </div>
        <div style={{ fontWeight: 600, marginTop: 4 }}>TAX INVOICE {sale.status === 'Cancelled' && <span className="badge danger">CANCELLED</span>}</div>
      </div>
      <div className="row small" style={{ justifyContent: 'space-between', marginBottom: 6 }}>
        <div>Bill: <b>{sale.invoiceNo}</b><br />{dateTime(sale.invoiceAt)}</div>
        <div style={{ textAlign: 'right' }}>
          {sale.customerName && <>Patient: {sale.customerName}{sale.customerPhone ? ` (${sale.customerPhone})` : ''}<br /></>}
          {sale.doctorName && <>Dr.: {sale.doctorName}</>}
        </div>
      </div>
      <table>
        <thead>
          <tr><th>#</th><th>Item</th><th>Batch</th><th>Exp</th><th className="num">Qty</th><th className="num">MRP/u</th><th className="num">Disc</th><th className="num">GST%</th><th className="num">Amount</th></tr>
        </thead>
        <tbody>
          {sale.lines.map(l => (
            <tr key={l.id}>
              <td>{l.lineNo}</td>
              <td>{l.productName}{l.schedule !== 'None' && <span className="badge warn" style={{ marginLeft: 4 }}>{l.schedule}</span>}{l.hsnCode && <div className="small muted">HSN {l.hsnCode}</div>}</td>
              <td>{l.batchNo}</td><td>{expiry(l.expiryDate)}</td>
              <td className="num">{packs(l.qtyUnits, l.packSize, l.packUnit, l.baseUnit)}</td>
              <td className="num">{num(l.mrpPerUnit)}</td>
              <td className="num">{l.discountAmount ? num(l.discountAmount) : '-'}</td>
              <td className="num">{l.gstRate}</td>
              <td className="num">{num(l.netAmount)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <div className="row" style={{ justifyContent: 'space-between', alignItems: 'flex-start', marginTop: 8 }}>
        <table style={{ width: 'auto' }} className="small">
          <thead><tr><th>GST%</th><th className="num">Taxable</th><th className="num">CGST</th><th className="num">SGST</th></tr></thead>
          <tbody>
            {taxByRate.map(t => (
              <tr key={t.rate}><td>{t.rate}</td><td className="num">{num(t.taxable)}</td><td className="num">{num(t.tax / 2)}</td><td className="num">{num(t.tax / 2)}</td></tr>
            ))}
          </tbody>
        </table>
        <div className="totals" style={{ minWidth: 220 }}>
          <span>Gross</span><span>{money(sale.grossAmount)}</span>
          <span>Discount</span><span>-{money(sale.discountAmount)}</span>
          {!!sale.deliveryCharge && <><span>Delivery (incl. {sale.deliveryChargeGstRate}% GST)</span><span>{money(sale.deliveryCharge)}</span></>}
          <span>Round off</span><span>{money(sale.roundOff)}</span>
          <span className="grand">Total</span><span className="grand">{money(sale.netAmount)}</span>
          <span className="small muted">Paid</span>
          <span className="small muted">{sale.payments.map(p => `${p.mode} ${num(p.amount)}`).join(', ')}</span>
        </div>
      </div>
      <p className="small muted" style={{ marginTop: 10 }}>Prices are inclusive of GST. CGST/SGST shown assumes intra-state supply.</p>
    </div>
  );
}
