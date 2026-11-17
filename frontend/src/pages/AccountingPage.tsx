import { useState } from 'react';
import { api, type AccountType, type LedgerAccount, type StatementRow } from '../api';
import { useAuth } from '../auth';
import { ErrorBanner, Field, Modal, PageHead, toast, useLoad } from '../ui';
import { money, today } from '../util';

type Tab = 'overview' | 'tb' | 'pl' | 'bs' | 'ledger' | 'journals' | 'gst' | 'accounts' | 'close';
const TABS: [Tab, string][] = [
  ['overview', 'Overview'], ['tb', 'Trial balance'], ['pl', 'Profit & loss'], ['bs', 'Balance sheet'], ['ledger', 'Ledgers & cash book'],
  ['journals', 'Journals'], ['gst', 'GST summary'], ['accounts', 'Chart of accounts'], ['close', 'Close & export'],
];
const monthStart = () => today().slice(0, 8) + '01';

export default function AccountingPage() {
  const [tab, setTab] = useState<Tab>('overview');
  return (
    <>
      <PageHead title="Accounts" sub="Double-entry books generated automatically from bills, purchases, returns and payments." />
      <div className="row no-print" style={{ marginBottom: 12 }}>
        {TABS.map(([t, label]) => <button key={t} className={tab === t ? 'primary small' : 'small'} onClick={() => setTab(t)}>{label}</button>)}
      </div>
      {tab === 'overview' && <Overview />}
      {tab === 'tb' && <TrialBalanceTab />}
      {tab === 'pl' && <PlTab />}
      {tab === 'bs' && <BsTab />}
      {tab === 'ledger' && <LedgerTab />}
      {tab === 'journals' && <JournalsTab />}
      {tab === 'gst' && <GstTab />}
      {tab === 'accounts' && <AccountsTab />}
      {tab === 'close' && <CloseTab />}
    </>
  );
}

function Range({ from, to, setFrom, setTo }: { from: string; to: string; setFrom: (v: string) => void; setTo: (v: string) => void }) {
  return (
    <div className="row no-print" style={{ marginBottom: 12 }}>
      <Field label="From"><input type="date" value={from} onChange={e => setFrom(e.target.value)} /></Field>
      <Field label="To"><input type="date" value={to} max={today()} onChange={e => setTo(e.target.value)} /></Field>
      <span className="spacer" /><button onClick={() => window.print()}>Print</button>
    </div>
  );
}

function Overview() {
  const sync = useLoad(() => api.accSync(), []);
  const checks = useLoad(() => api.controlChecks(), [sync.data?.at]);
  return (
    <div className="grid">
      <ErrorBanner error={sync.error ?? checks.error} />
      {sync.data && <div className="alert ok" style={{ margin: 0 }}>Books are up to date{sync.data.entriesCreated ? ` — ${sync.data.entriesCreated} new entries posted just now` : ''}.</div>}
      <div className="panel">
        <h2>Control checks</h2>
        <table>
          <thead><tr><th>Check</th><th className="num">Books</th><th className="num">Operations</th><th className="num">Difference</th><th /></tr></thead>
          <tbody>
            {checks.data?.map(c => (
              <tr key={c.name}><td><b>{c.name}</b><div className="small muted">{c.note}</div></td>
                <td className="num">{money(c.ledger)}</td><td className="num">{money(c.subLedger)}</td>
                <td className="num">{c.difference === 0 ? <span className="badge ok">matched</span> : <span className="badge danger">{money(c.difference)}</span>}</td><td /></tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="small muted">
        Posting rules are engineering defaults (perpetual inventory, GST input/output, UPI/card into a clearing account) and must be reviewed by your CA.
        Card/UPI settlements: record a journal Dr Bank, Dr Bank Charges & MDR, Cr Card/UPI Clearing when money reaches the bank.
      </p>
    </div>
  );
}

function TrialBalanceTab() {
  const [asOf, setAsOf] = useState(today());
  const { data: tb, error } = useLoad(() => api.trialBalance(asOf), [asOf]);
  return (
    <div className="panel">
      <div className="row no-print"><Field label="As of"><input type="date" value={asOf} max={today()} onChange={e => setAsOf(e.target.value)} /></Field><span className="spacer" /><button onClick={() => window.print()}>Print</button></div>
      <ErrorBanner error={error} />
      {tb && (
        <table style={{ marginTop: 12 }}>
          <thead><tr><th>Code</th><th>Account</th><th>Type</th><th className="num">Debit</th><th className="num">Credit</th></tr></thead>
          <tbody>
            {tb.rows.map(r => <tr key={r.accountId}><td>{r.code}</td><td>{r.name}</td><td className="small muted">{r.type}</td><td className="num">{r.debit ? money(r.debit) : ''}</td><td className="num">{r.credit ? money(r.credit) : ''}</td></tr>)}
            <tr><td /><td><b>Total</b></td><td>{tb.balanced ? <span className="badge ok">balanced</span> : <span className="badge danger">NOT balanced</span>}</td><td className="num"><b>{money(tb.totalDebit)}</b></td><td className="num"><b>{money(tb.totalCredit)}</b></td></tr>
          </tbody>
        </table>
      )}
    </div>
  );
}

function Section({ title, rows, total }: { title: string; rows: StatementRow[]; total: number }) {
  return (
    <>
      <tr><td colSpan={2}><b>{title}</b></td></tr>
      {rows.map(r => <tr key={r.code}><td style={{ paddingLeft: 20 }}>{r.code} {r.name}</td><td className="num">{money(r.amount)}</td></tr>)}
      <tr><td style={{ paddingLeft: 20 }}><i>Total {title.toLowerCase()}</i></td><td className="num"><b>{money(total)}</b></td></tr>
    </>
  );
}

function PlTab() {
  const [from, setFrom] = useState(monthStart());
  const [to, setTo] = useState(today());
  const { data: p, error } = useLoad(() => api.profitAndLoss(from, to), [from, to]);
  return (
    <div className="panel" style={{ maxWidth: 720 }}>
      <Range from={from} to={to} setFrom={setFrom} setTo={setTo} />
      <ErrorBanner error={error} />
      {p && <table><tbody>
        <Section title="Income" rows={p.income} total={p.totalIncome} />
        <Section title="Expenses" rows={p.expenses} total={p.totalExpenses} />
        <tr><td><b>Net {p.netProfit >= 0 ? 'profit' : 'loss'}</b></td><td className="num" style={{ fontSize: 18 }}><b style={{ color: p.netProfit >= 0 ? 'var(--ok)' : 'var(--danger)' }}>{money(p.netProfit)}</b></td></tr>
      </tbody></table>}
      <p className="small muted">Sales returns appear as negative income. Not a statutory financial statement.</p>
    </div>
  );
}

function BsTab() {
  const [asOf, setAsOf] = useState(today());
  const { data: b, error } = useLoad(() => api.balanceSheet(asOf), [asOf]);
  return (
    <div className="panel" style={{ maxWidth: 720 }}>
      <div className="row no-print"><Field label="As of"><input type="date" value={asOf} max={today()} onChange={e => setAsOf(e.target.value)} /></Field><span className="spacer" /><button onClick={() => window.print()}>Print</button></div>
      <ErrorBanner error={error} />
      {b && <table style={{ marginTop: 12 }}><tbody>
        <Section title="Assets" rows={b.assets} total={b.totalAssets} />
        <Section title="Liabilities" rows={b.liabilities} total={b.totalLiabilities} />
        <Section title="Equity" rows={[...b.equity, { code: '', name: 'Profit / loss to date', amount: b.currentEarnings }]} total={b.totalEquity} />
        <tr><td><b>Liabilities + equity</b></td><td className="num"><b>{money(b.totalLiabilities + b.totalEquity)}</b> {b.balanced ? <span className="badge ok">balanced</span> : <span className="badge danger">mismatch</span>}</td></tr>
      </tbody></table>}
    </div>
  );
}

function LedgerTab() {
  const accounts = useLoad(() => api.accounts(), []);
  const [accountId, setAccountId] = useState('');
  const [from, setFrom] = useState(monthStart());
  const [to, setTo] = useState(today());
  const cashId = accounts.data?.find(a => a.systemKey === 'CASH')?.id ?? '';
  const id = accountId || cashId;
  const { data: l, error } = useLoad(() => id ? api.accountLedger(id, from, to) : Promise.resolve(null), [id, from, to]);
  return (
    <div className="panel">
      <div className="row no-print">
        <Field label="Account">
          <select value={id} onChange={e => setAccountId(e.target.value)}>
            {accounts.data?.map(a => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}
          </select>
        </Field>
      </div>
      <Range from={from} to={to} setFrom={setFrom} setTo={setTo} />
      <ErrorBanner error={error} />
      {l && (
        <table className="small">
          <thead><tr><th>Date</th><th>Voucher</th><th>Particulars</th><th className="num">Debit</th><th className="num">Credit</th><th className="num">Balance</th></tr></thead>
          <tbody>
            <tr><td>{l.from}</td><td /><td><i>Opening balance</i></td><td /><td /><td className="num">{money(l.opening)}</td></tr>
            {l.rows.map((r, i) => <tr key={i}><td>{r.date}</td><td>{r.entryNo}</td><td>{r.narration}</td><td className="num">{r.debit ? money(r.debit) : ''}</td><td className="num">{r.credit ? money(r.credit) : ''}</td><td className="num">{money(r.balance)}</td></tr>)}
            <tr><td>{l.to}</td><td /><td><b>Closing balance</b></td><td /><td /><td className="num"><b>{money(l.closing)}</b></td></tr>
          </tbody>
        </table>
      )}
    </div>
  );
}

function JournalsTab() {
  const [from, setFrom] = useState(monthStart());
  const [to, setTo] = useState(today());
  const [source, setSource] = useState('');
  const { data, error, reload } = useLoad(() => api.journals(from, to, source || undefined), [from, to, source]);
  const [adding, setAdding] = useState(false);
  const reverse = async (id: string) => {
    if (!confirm('Post a reversal of this journal dated today?')) return;
    try { await api.reverseJournal(id); toast('Reversed'); reload(); } catch (e) { alert((e as Error).message); }
  };
  return (
    <div className="panel">
      <div className="row no-print">
        <Field label="Source"><select value={source} onChange={e => setSource(e.target.value)}>
          <option value="">All</option>{['SalesInvoice', 'SalesReturn', 'PurchaseInvoice', 'PurchaseReturn', 'CustomerReceipt', 'SupplierPayment', 'StockAdjustment', 'StockCount', 'CashMovement', 'CashShift', 'Manual'].map(s => <option key={s}>{s}</option>)}
        </select></Field>
        <span className="spacer" />
        <button className="primary" onClick={() => setAdding(true)}>+ Expense / journal</button>
      </div>
      <Range from={from} to={to} setFrom={setFrom} setTo={setTo} />
      <ErrorBanner error={error} />
      <table className="small">
        <thead><tr><th>Date</th><th>Voucher</th><th>Narration</th><th>Lines</th><th className="num">Amount</th><th /></tr></thead>
        <tbody>
          {data?.map(j => (
            <tr key={j.id}>
              <td>{j.date}</td><td>{j.entryNo}<div className="muted">{j.sourceType} · {j.event}</div></td><td>{j.narration}</td>
              <td>{j.lines.map((l, i) => <div key={i}>{l.debit ? 'Dr' : 'Cr'} {l.accountName} {money(l.debit || l.credit)}</div>)}</td>
              <td className="num">{money(j.total)}</td>
              <td>{j.sourceType === 'Manual' && j.event === 'manual' && <button className="small" onClick={() => reverse(j.id)}>Reverse</button>}</td>
            </tr>
          ))}
          {data?.length === 0 && <tr><td colSpan={6} className="muted">No entries.</td></tr>}
        </tbody>
      </table>
      {adding && <JournalForm onClose={() => setAdding(false)} onSaved={() => { setAdding(false); reload(); }} />}
    </div>
  );
}

function JournalForm({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const accounts = useLoad(() => api.accounts(), []);
  const usable = (accounts.data ?? []).filter(a => a.isActive && !['AR', 'AP', 'INVENTORY', 'CLAIMS'].includes(a.systemKey ?? ''));
  const [mode, setMode] = useState<'expense' | 'journal'>('expense');
  const [date, setDate] = useState(today());
  const [narration, setNarration] = useState('');
  const [exp, setExp] = useState({ expenseId: '', paidFromId: '', amount: '' });
  const [lines, setLines] = useState([{ accountId: '', debit: '', credit: '' }, { accountId: '', debit: '', credit: '' }]);
  const [error, setError] = useState<unknown>(null);
  const dr = lines.reduce((s, l) => s + (Number(l.debit) || 0), 0);
  const cr = lines.reduce((s, l) => s + (Number(l.credit) || 0), 0);
  const save = async () => {
    setError(null);
    const body = mode === 'expense'
      ? { date, narration, lines: [{ accountId: exp.expenseId, debit: Number(exp.amount), credit: 0 }, { accountId: exp.paidFromId, debit: 0, credit: Number(exp.amount) }] }
      : { date, narration, lines: lines.filter(l => l.accountId).map(l => ({ accountId: l.accountId, debit: Number(l.debit) || 0, credit: Number(l.credit) || 0 })) };
    try { await api.createJournal(body); toast('Journal posted'); onSaved(); } catch (e) { setError(e); }
  };
  const opts = (filter?: (a: LedgerAccount) => boolean) => usable.filter(filter ?? (() => true)).map(a => <option key={a.id} value={a.id}>{a.code} {a.name}</option>);
  return (
    <Modal title="New entry" onClose={onClose}>
      <div className="grid">
        <ErrorBanner error={error} />
        <div className="row">
          <button className={mode === 'expense' ? 'primary small' : 'small'} onClick={() => setMode('expense')}>Expense</button>
          <button className={mode === 'journal' ? 'primary small' : 'small'} onClick={() => setMode('journal')}>General journal</button>
        </div>
        <div className="grid c2">
          <Field label="Date"><input type="date" value={date} max={today()} onChange={e => setDate(e.target.value)} /></Field>
          <Field label="Narration"><input value={narration} onChange={e => setNarration(e.target.value)} placeholder="e.g. November rent" /></Field>
        </div>
        {mode === 'expense' ? (
          <div className="grid c3">
            <Field label="Expense account"><select value={exp.expenseId} onChange={e => setExp({ ...exp, expenseId: e.target.value })}><option value="">Choose…</option>{opts(a => a.type === 'Expense')}</select></Field>
            <Field label="Paid from"><select value={exp.paidFromId} onChange={e => setExp({ ...exp, paidFromId: e.target.value })}><option value="">Choose…</option>{opts(a => ['CASH', 'BANK', 'CLEARING'].includes(a.systemKey ?? '') || (a.type === 'Asset' && !a.systemKey))}</select></Field>
            <Field label="Amount"><input type="number" value={exp.amount} onChange={e => setExp({ ...exp, amount: e.target.value })} /></Field>
          </div>
        ) : (
          <>
            <table className="small"><tbody>
              {lines.map((l, i) => (
                <tr key={i}>
                  <td><select value={l.accountId} onChange={e => setLines(ls => ls.map((x, j) => j === i ? { ...x, accountId: e.target.value } : x))}><option value="">Account…</option>{opts()}</select></td>
                  <td><input className="short" type="number" placeholder="Debit" value={l.debit} onChange={e => setLines(ls => ls.map((x, j) => j === i ? { ...x, debit: e.target.value, credit: '' } : x))} /></td>
                  <td><input className="short" type="number" placeholder="Credit" value={l.credit} onChange={e => setLines(ls => ls.map((x, j) => j === i ? { ...x, credit: e.target.value, debit: '' } : x))} /></td>
                </tr>
              ))}
            </tbody></table>
            <div className="row small"><button className="small" onClick={() => setLines([...lines, { accountId: '', debit: '', credit: '' }])}>+ Line</button><span className="spacer" />
              <span style={{ color: dr === cr && dr > 0 ? 'var(--ok)' : 'var(--danger)' }}>Dr {money(dr)} · Cr {money(cr)}</span></div>
          </>
        )}
        <p className="small muted" style={{ margin: 0 }}>Receivables, payables, inventory and supplier claims are maintained from documents and can't be used here.</p>
        <div className="row end"><button className="primary" disabled={!narration.trim()} onClick={save}>Post</button></div>
      </div>
    </Modal>
  );
}

function GstTab() {
  const [from, setFrom] = useState(monthStart());
  const [to, setTo] = useState(today());
  const { data: g, error } = useLoad(() => api.gstSummary(from, to), [from, to]);
  return (
    <div className="panel" style={{ maxWidth: 820 }}>
      <Range from={from} to={to} setFrom={setFrom} setTo={setTo} />
      <ErrorBanner error={error} />
      {g && (
        <div className="grid c2">
          <div><h3>Outward (sales − returns)</h3>
            <table className="small"><thead><tr><th>Rate</th><th className="num">Taxable</th><th className="num">Tax</th></tr></thead>
              <tbody>{g.outwardSupplies.map(r => <tr key={r.rate}><td>{r.rate}%</td><td className="num">{money(r.taxable)}</td><td className="num">{money(r.tax)}</td></tr>)}</tbody></table>
            <b>Output tax {money(g.outputTax)}</b></div>
          <div><h3>Inward (purchases)</h3>
            <table className="small"><thead><tr><th>Rate</th><th className="num">Taxable</th><th className="num">Tax</th></tr></thead>
              <tbody>{g.inwardSupplies.map(r => <tr key={r.rate}><td>{r.rate}%</td><td className="num">{money(r.taxable)}</td><td className="num">{money(r.tax)}</td></tr>)}</tbody></table>
            <div>Less on returns to suppliers {money(g.returnsToSupplierTax)}</div>
            <b>Input tax {money(g.inputTax - g.returnsToSupplierTax)}</b></div>
        </div>
      )}
      {g && <div className="totals" style={{ maxWidth: 320, marginTop: 12 }}><span className="grand">Net (output − input)</span><span className="grand">{money(g.netTax)}</span></div>}
      <div className="alert warn" style={{ marginTop: 12 }}>
        Informational only — not a GST return. Place of supply, exempt/nil-rated items, ITC eligibility, reverse charge and e-invoicing are not evaluated.
        Your CA must prepare filings from the source documents.
      </div>
    </div>
  );
}

const TYPES: AccountType[] = ['Asset', 'Liability', 'Equity', 'Income', 'Expense'];

function AccountsTab() {
  const { data, error, reload } = useLoad(() => api.accounts(), []);
  const [edit, setEdit] = useState<LedgerAccount | 'new' | null>(null);
  return (
    <div className="panel">
      <div className="row"><span className="spacer" /><button className="primary" onClick={() => setEdit('new')}>+ Account</button></div>
      <ErrorBanner error={error} />
      <table>
        <thead><tr><th>Code</th><th>Name</th><th>Type</th><th /></tr></thead>
        <tbody>
          {data?.map(a => (
            <tr key={a.id} className="clickable" onClick={() => setEdit(a)}>
              <td>{a.code}</td><td>{a.name} {!a.isActive && <span className="badge">inactive</span>}</td><td>{a.type}</td>
              <td>{a.systemKey && <span className="badge brand" title="Used by automatic postings">system</span>}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {edit && <AccountForm account={edit === 'new' ? null : edit} onClose={() => setEdit(null)} onSaved={() => { setEdit(null); reload(); }} />}
    </div>
  );
}

function AccountForm({ account, onClose, onSaved }: { account: LedgerAccount | null; onClose: () => void; onSaved: () => void }) {
  const [f, setF] = useState({ code: account?.code ?? '', name: account?.name ?? '', type: account?.type ?? 'Expense' as AccountType, isActive: account?.isActive ?? true });
  const [error, setError] = useState<unknown>(null);
  const save = async () => { setError(null); try { await api.saveAccount(account?.id ?? null, f); toast('Account saved'); onSaved(); } catch (e) { setError(e); } };
  return (
    <Modal title={account ? `Edit ${account.name}` : 'New account'} onClose={onClose}>
      <div className="grid">
        <ErrorBanner error={error} />
        <div className="grid c3">
          <Field label="Code"><input value={f.code} onChange={e => setF({ ...f, code: e.target.value })} /></Field>
          <Field label="Name"><input value={f.name} onChange={e => setF({ ...f, name: e.target.value })} /></Field>
          <Field label="Type"><select value={f.type} disabled={!!account?.systemKey} onChange={e => setF({ ...f, type: e.target.value as AccountType })}>{TYPES.map(t => <option key={t}>{t}</option>)}</select></Field>
        </div>
        {!account?.systemKey && <label className="check"><input type="checkbox" checked={f.isActive} onChange={e => setF({ ...f, isActive: e.target.checked })} /> Active</label>}
        <div className="row end"><button className="primary" onClick={save}>Save</button></div>
      </div>
    </Modal>
  );
}

function CloseTab() {
  const { can } = useAuth();
  const lock = useLoad(() => api.lockDate(), []);
  const [upTo, setUpTo] = useState('');
  const [from, setFrom] = useState(monthStart());
  const [to, setTo] = useState(today());
  const [error, setError] = useState<unknown>(null);
  const close = async () => {
    if (!confirm(`Close books up to ${upTo}? No entries can be dated on or before this date afterwards.`)) return;
    setError(null);
    try { await api.closeBooks(upTo); toast('Books closed'); lock.reload(); } catch (e) { setError(e); }
  };
  const reopen = async () => {
    if (!confirm('Reopen the closed period? This is audited.')) return;
    try { await api.reopenBooks(null); toast('Period reopened'); lock.reload(); } catch (e) { setError(e); }
  };
  return (
    <div className="grid" style={{ maxWidth: 640 }}>
      <ErrorBanner error={error ?? lock.error} />
      <div className="panel grid">
        <h2 style={{ margin: 0 }}>Period close</h2>
        <div>Books closed up to: <b>{lock.data?.lockedUntil ?? 'not closed'}</b></div>
        {can('Owner') ? <>
          <div className="row"><Field label="Close up to (inclusive)"><input type="date" max={today()} value={upTo} onChange={e => setUpTo(e.target.value)} /></Field>
            <button className="primary" style={{ alignSelf: 'end' }} disabled={!upTo} onClick={close}>Close books</button>
            {lock.data?.lockedUntil && <button style={{ alignSelf: 'end' }} onClick={reopen}>Reopen</button>}</div>
          <p className="small muted" style={{ margin: 0 }}>Closing checks that the trial balance balances. Later documents for a closed period are posted on the first open day.</p>
        </> : <p className="small muted">Only the owner can close or reopen books.</p>}
      </div>
      <div className="panel grid">
        <h2 style={{ margin: 0 }}>Export for your accountant</h2>
        <Range from={from} to={to} setFrom={setFrom} setTo={setTo} />
        <div className="row">
          <button className="primary" onClick={() => api.downloadJournalCsv(from, to).catch(setError)}>Download journal CSV</button>
          <button onClick={() => api.downloadTallyXml(from, to).catch(setError)}>Download for Tally (XML)</button>
        </div>
        <p className="small muted" style={{ margin: 0 }}>Tally file: ledger masters + journal vouchers (Gateway → Import → Vouchers). Not yet tested against a live Tally — have your CA import into a test company first and check the ledger groups.</p>
        <p className="small muted" style={{ margin: 0 }}>One row per debit/credit line with account codes — can be mapped into Tally or other software by your accountant.</p>
      </div>
    </div>
  );
}
