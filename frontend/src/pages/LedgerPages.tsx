import { useState } from 'react';
import { api, type Aging, type LedgerDoc, type Paged, type PartyBalance, type SettlementMode, type Statement } from '../api';
import { RoleSets, useAuth } from '../auth';
import { ErrorBanner, Field, Modal, PageHead, toast, useLoad } from '../ui';
import { money, num, today } from '../util';

interface LedgerConfig {
  kind: 'supplier' | 'customer';
  title: string; sub: string; partyLabel: string; payVerb: string; balanceLabel: string;
  summary: () => Promise<PartyBalance[]>;
  aging: () => Promise<Aging>;
  statement: (id: string) => Promise<Statement>;
  docs: (id: string) => Promise<Paged<LedgerDoc>>;
  create: (b: unknown) => Promise<LedgerDoc>;
  cancel: (id: string, reason: string) => Promise<LedgerDoc>;
  canPay: boolean; canCancel: boolean;
}

export function PayablesPage() {
  const { can } = useAuth();
  return <LedgerPage cfg={{
    kind: 'supplier', title: 'Payables', sub: 'What you owe suppliers: purchases − payments − credit notes.', partyLabel: 'Supplier',
    payVerb: 'Pay supplier', balanceLabel: 'You owe',
    summary: api.payables, aging: api.payablesAging, statement: api.supplierStatement, docs: id => api.supplierPayments(id),
    create: b => api.paySupplier(b), cancel: api.cancelSupplierPayment,
    canPay: can(...RoleSets.salesAdmin), canCancel: can(...RoleSets.salesAdmin),
  }} />;
}

export function ReceivablesPage() {
  const { can } = useAuth();
  return <LedgerPage cfg={{
    kind: 'customer', title: 'Customer dues', sub: 'Credit given at the counter: credit bills − returns − payments received.', partyLabel: 'Customer',
    payVerb: 'Receive payment', balanceLabel: 'Customer owes',
    summary: api.receivables, aging: api.receivablesAging, statement: api.customerStatement, docs: id => api.customerReceipts(id),
    create: b => api.receiveFromCustomer(b), cancel: api.cancelCustomerReceipt,
    canPay: can(...RoleSets.sell), canCancel: can(...RoleSets.salesAdmin),
  }} />;
}

function LedgerPage({ cfg }: { cfg: LedgerConfig }) {
  const summary = useLoad(cfg.summary, [cfg.kind]);
  const aging = useLoad(cfg.aging, [cfg.kind]);
  const [open, setOpen] = useState<PartyBalance | null>(null);
  const reload = () => { summary.reload(); aging.reload(); };

  return (
    <>
      <PageHead title={cfg.title} sub={cfg.sub} />
      <ErrorBanner error={summary.error ?? aging.error} />
      {aging.data && (
        <div className="grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(140px, 1fr))', marginBottom: 16 }}>
          {aging.data.buckets.map(b => (
            <div className="panel" key={b.label}>
              <div className="small muted">{b.label}</div>
              <div style={{ fontSize: 18, fontWeight: 700 }}>{money(b.amount)}</div>
              <div className="small muted">{b.count} doc(s)</div>
            </div>
          ))}
          <div className="panel">
            <div className="small muted">Net outstanding</div>
            <div style={{ fontSize: 18, fontWeight: 700 }}>{money(aging.data.netOutstanding)}</div>
            {aging.data.unallocatedCredit > 0 && <div className="small muted">after {money(aging.data.unallocatedCredit)} unallocated</div>}
          </div>
        </div>
      )}
      <div className="panel">
        {summary.data?.length === 0 ? <div className="empty">Nothing outstanding.</div> : (
          <div className="table-wrap">
            <table>
              <thead><tr><th>{cfg.partyLabel}</th><th className="num">Billed</th><th className="num">Paid</th><th className="num">Credits</th>
                <th className="num">Balance</th><th className="num">Overdue</th>{cfg.kind === 'customer' && <th className="num">Limit</th>}</tr></thead>
              <tbody>
                {summary.data?.map(p => (
                  <tr key={p.partyId} className="clickable" onClick={() => setOpen(p)}>
                    <td><b>{p.name}</b> {p.phone && <span className="small muted">{p.phone}</span>}</td>
                    <td className="num">{money(p.billed)}</td><td className="num">{money(p.paid)}</td><td className="num">{money(p.credits)}</td>
                    <td className="num"><b>{money(p.balance)}</b></td>
                    <td className="num">{p.overdue > 0 ? <span className="badge danger">{money(p.overdue)}</span> : '-'}</td>
                    {cfg.kind === 'customer' && <td className="num">{p.creditLimit == null ? <span className="muted">none</span> : money(p.creditLimit)}</td>}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
      {open && <PartyModal cfg={cfg} party={open} onClose={() => setOpen(null)} onChanged={reload} />}
    </>
  );
}

const MODES: SettlementMode[] = ['Cash', 'Upi', 'BankTransfer', 'Cheque', 'Card'];

function PartyModal({ cfg, party, onClose, onChanged }: { cfg: LedgerConfig; party: PartyBalance; onClose: () => void; onChanged: () => void }) {
  const st = useLoad(() => cfg.statement(party.partyId), [party.partyId]);
  const docs = useLoad(() => cfg.docs(party.partyId), [party.partyId]);
  const [paying, setPaying] = useState(false);
  const refresh = () => { st.reload(); docs.reload(); onChanged(); };

  const cancelDoc = async (d: LedgerDoc) => {
    const reason = prompt(`Cancel ${d.number}? Enter reason:`);
    if (!reason) return;
    try { await cfg.cancel(d.id, reason); toast(`${d.number} cancelled`); refresh(); } catch (e) { alert((e as Error).message); }
  };

  return (
    <Modal title={`${party.name} · statement`} onClose={onClose} wide>
      <ErrorBanner error={st.error} />
      {st.data && (
        <>
          <div className="row" style={{ marginBottom: 12 }}>
            <div><div className="small muted">{cfg.balanceLabel}</div><div style={{ fontSize: 22, fontWeight: 700 }}>{money(st.data.balance)}</div></div>
            {st.data.unallocatedCredit > 0 && <span className="badge brand">Unallocated credit {money(st.data.unallocatedCredit)}</span>}
            <span className="spacer" />
            {cfg.canPay && !paying && <button className="primary no-print" onClick={() => setPaying(true)}>{cfg.payVerb}</button>}
            <button className="no-print" onClick={() => window.print()}>Print</button>
          </div>
          {paying && <PayForm cfg={cfg} statement={st.data} onDone={() => { setPaying(false); refresh(); }} onCancel={() => setPaying(false)} />}

          <h3>Open {cfg.kind === 'supplier' ? 'invoices' : 'bills'}</h3>
          {st.data.openDocuments.length === 0 ? <p className="muted small">None.</p> : (
            <table className="small">
              <thead><tr><th>No</th><th>Date</th><th>Due</th><th className="num">Total</th><th className="num">Settled</th><th className="num">Outstanding</th></tr></thead>
              <tbody>
                {st.data.openDocuments.map(d => (
                  <tr key={d.id}><td>{d.documentNo}</td><td>{d.date}</td>
                    <td>{d.dueDate} {d.daysOverdue > 0 && <span className="badge danger">{d.daysOverdue}d overdue</span>}</td>
                    <td className="num">{num(d.total)}</td><td className="num">{num(d.settled)}</td><td className="num"><b>{num(d.outstanding)}</b></td></tr>
                ))}
              </tbody>
            </table>
          )}

          <h3 style={{ marginTop: 16 }}>Statement</h3>
          <table className="small">
            <thead><tr><th>Date</th><th>Type</th><th>No</th><th className="num">Debit</th><th className="num">Credit</th><th className="num">Balance</th></tr></thead>
            <tbody>
              {st.data.lines.map((l, i) => (
                <tr key={i}><td>{l.date}</td><td>{l.type}{l.note && <span className="muted"> · {l.note}</span>}</td><td>{l.documentNo}</td>
                  <td className="num">{l.debit ? num(l.debit) : ''}</td><td className="num">{l.credit ? num(l.credit) : ''}</td><td className="num">{num(l.balance)}</td></tr>
              ))}
            </tbody>
          </table>

          <h3 className="no-print" style={{ marginTop: 16 }}>{cfg.kind === 'supplier' ? 'Payments' : 'Receipts'}</h3>
          <table className="small no-print">
            <thead><tr><th>No</th><th>Date</th><th>Mode</th><th>Reference</th><th className="num">Amount</th><th className="num">Allocated</th><th>Status</th><th /></tr></thead>
            <tbody>
              {docs.data?.items.map(d => (
                <tr key={d.id}><td>{d.number}</td><td>{d.date}</td><td>{d.mode}</td><td>{d.reference}</td>
                  <td className="num">{num(d.amount)}</td><td className="num">{num(d.allocated)}</td>
                  <td>{d.status === 'Cancelled' ? <span className="badge danger" title={d.cancelReason ?? ''}>Cancelled</span> : <span className="badge ok">Posted</span>}</td>
                  <td>{d.status === 'Posted' && cfg.canCancel && <button className="small danger" onClick={() => cancelDoc(d)}>Cancel</button>}</td></tr>
              ))}
            </tbody>
          </table>
        </>
      )}
    </Modal>
  );
}

function PayForm({ cfg, statement, onDone, onCancel }: { cfg: LedgerConfig; statement: Statement; onDone: () => void; onCancel: () => void }) {
  const [date, setDate] = useState(today());
  const [mode, setMode] = useState<SettlementMode>('Cash');
  const [amount, setAmount] = useState(String(Math.max(0, statement.balance)));
  const [reference, setReference] = useState('');
  const [manual, setManual] = useState(false);
  const [alloc, setAlloc] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  const allocated = Object.values(alloc).reduce((s, v) => s + (Number(v) || 0), 0);
  const submit = async () => {
    setBusy(true); setError(null);
    const partyKey = cfg.kind === 'supplier' ? 'supplierId' : 'customerId';
    const dateKey = cfg.kind === 'supplier' ? 'paymentDate' : 'receiptDate';
    try {
      const doc = await cfg.create({
        [partyKey]: statement.partyId, [dateKey]: date, mode, amount: Number(amount), reference: reference || null,
        allocations: manual ? Object.entries(alloc).filter(([, v]) => Number(v) > 0).map(([documentId, v]) => ({ documentId, amount: Number(v) })) : null,
      });
      toast(`${doc.number} saved`);
      onDone();
    } catch (e) { setError(e); } finally { setBusy(false); }
  };

  return (
    <div className="panel grid no-print" style={{ marginBottom: 16 }}>
      <ErrorBanner error={error} />
      <div className="grid c4">
        <Field label="Date"><input type="date" max={today()} value={date} onChange={e => setDate(e.target.value)} /></Field>
        <Field label="Mode"><select value={mode} onChange={e => setMode(e.target.value as SettlementMode)}>{MODES.map(m => <option key={m}>{m}</option>)}</select></Field>
        <Field label="Amount"><input type="number" value={amount} onChange={e => setAmount(e.target.value)} autoFocus /></Field>
        <Field label={mode === 'Cash' ? 'Reference (optional)' : 'UTR / cheque no.'}><input value={reference} onChange={e => setReference(e.target.value)} /></Field>
      </div>
      <label className="check"><input type="checkbox" checked={manual} onChange={e => setManual(e.target.checked)} /> Choose which {cfg.kind === 'supplier' ? 'invoices' : 'bills'} this settles (otherwise oldest first)</label>
      {manual && (
        <table className="small">
          <tbody>
            {statement.openDocuments.map(d => (
              <tr key={d.id}><td>{d.documentNo}</td><td>{d.date}</td><td className="num">due {num(d.outstanding)}</td>
                <td className="num"><input className="short" type="number" value={alloc[d.id] ?? ''} placeholder="0" onChange={e => setAlloc({ ...alloc, [d.id]: e.target.value })} /></td></tr>
            ))}
          </tbody>
        </table>
      )}
      {manual && <div className="small muted">Allocated {num(allocated)} of {num(Number(amount) || 0)}; the rest stays as unallocated credit.</div>}
      <div className="row end">
        <button onClick={onCancel}>Cancel</button>
        <button className="primary" disabled={busy || !(Number(amount) > 0)} onClick={submit}>Save</button>
      </div>
    </div>
  );
}
