import { useEffect, useState } from 'react';
import { NavLink, Navigate, Route, Routes, useNavigate, useParams } from 'react-router-dom';
import { isMfaPending, platformApi, platformTokenStore, setPlatformUnauthorizedHandler, tokenStore, type SubscriptionInput, type Ticket } from '../api';
import { MfaPanel } from './AccountPage';
import { ErrorBanner, Field, PageHead, StatusBadge, ToastHost, toast, useLoad } from '../ui';
import { dateTime, money, today } from '../util';

/** SaaS provider console — separate login and token from any pharmacy account. */
export default function PlatformApp() {
  const [authed, setAuthed] = useState(!!platformTokenStore.get());
  useEffect(() => { setPlatformUnauthorizedHandler(() => { platformTokenStore.set(null); setAuthed(false); }); }, []);
  if (!authed) return <PlatformLogin onDone={() => setAuthed(true)} />;
  return (
    <div className="shell">
      <nav className="side" aria-label="Provider">
        <div className="brand">PharmaOS</div>
        <div className="tenant">Provider console</div>
        <NavLink to="/platform" end>Dashboard</NavLink>
        <NavLink to="/platform/tenants">Customers</NavLink>
        <NavLink to="/platform/billing">Overdue invoices</NavLink>
        <NavLink to="/platform/tickets">Support tickets</NavLink>
        <div className="foot"><button className="small" onClick={() => { platformTokenStore.set(null); setAuthed(false); }}>Sign out</button></div>
      </nav>
      <main className="main">
        <Routes>
          <Route index element={<Dashboard />} />
          <Route path="tenants" element={<Tenants />} />
          <Route path="tenants/:id" element={<TenantPage />} />
          <Route path="billing" element={<Overdue />} />
          <Route path="tickets" element={<Tickets />} />
          <Route path="*" element={<Navigate to="/platform" replace />} />
        </Routes>
      </main>
      <ToastHost />
    </div>
  );
}

function PlatformLogin({ onDone }: { onDone: () => void }) {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [mfaToken, setMfaToken] = useState<string | null>(null);
  const [code, setCode] = useState('');
  const submit = async (e: React.FormEvent) => {
    e.preventDefault(); setError(null);
    try {
      if (mfaToken) { const r = await platformApi.loginMfa(mfaToken, code); platformTokenStore.set(r.token); onDone(); return; }
      const r = await platformApi.login(email, password);
      if (isMfaPending(r)) setMfaToken(r.mfaToken); else { platformTokenStore.set(r.token); onDone(); }
    } catch (err) { setError(err); }
  };
  return (
    <div className="auth">
      <form className="panel grid" onSubmit={submit}>
        <div><h1>Provider console</h1><p className="muted" style={{ margin: 0 }}>For PharmaOS staff only. Pharmacy users sign in at the main page.</p></div>
        <ErrorBanner error={error} />
        {mfaToken
          ? <Field label="6-digit code"><input value={code} inputMode="numeric" autoFocus onChange={e => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))} /></Field>
          : <>
            <Field label="Email"><input type="email" value={email} onChange={e => setEmail(e.target.value)} required autoFocus /></Field>
            <Field label="Password"><input type="password" value={password} onChange={e => setPassword(e.target.value)} required /></Field>
          </>}
        <button className="primary" disabled={!!mfaToken && code.length !== 6}>{mfaToken ? 'Verify' : 'Sign in'}</button>
      </form>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string | number }) {
  return <div className="panel"><div className="small muted">{label}</div><div style={{ fontSize: 22, fontWeight: 700 }}>{value}</div></div>;
}

function Dashboard() {
  const { data: d, error } = useLoad(() => platformApi.dashboard(), []);
  return (
    <>
      <PageHead title="Dashboard" sub="All pharmacy customers on the platform." />
      <ErrorBanner error={error} />
      {d && <>
        <div className="grid c4">
          <Stat label="Customers" value={d.tenants} /><Stat label="Paying (active)" value={d.active} />
          <Stat label="On trial" value={d.trials} /><Stat label="Trial expired" value={d.trialsExpired} />
          <Stat label="Past due" value={d.pastDue} /><Stat label="Suspended" value={d.suspended} />
          <Stat label="Overdue amount" value={money(d.overdueAmount)} /><Stat label="Open tickets" value={d.openTickets} />
        </div>
        <div className="panel" style={{ marginTop: 16 }}>
          <h2>Plans</h2>
          <table><thead><tr><th>Plan</th><th className="num">Monthly</th><th className="num">Branches</th><th className="num">Users</th><th>Modules</th></tr></thead>
            <tbody>{d.plans.map(p => <tr key={p.code}><td><b>{p.name}</b><div className="small muted">{p.priceNote}</div></td>
              <td className="num">{p.monthlyPriceHypothesis != null ? money(p.monthlyPriceHypothesis) : 'quote'}</td>
              <td className="num">{p.maxBranches ?? '∞'}</td><td className="num">{p.maxUsers ?? '∞'}</td><td className="small">{p.modules.join(', ')}</td></tr>)}</tbody></table>
          <p className="small muted">Prices are hypotheses for validation, not published prices. Third-party charges (messaging, payment gateway, etc.) are separate.</p>
        </div>
        <p className="small muted">Bills issued across all customers in the last 24 h: {d.billsLast24h}</p>
        <div style={{ maxWidth: 520, marginTop: 16 }}><MfaPanel load={platformApi.mfaStatus} setup={platformApi.mfaSetup} enable={platformApi.mfaEnable} /></div>
      </>}
    </>
  );
}

function Tenants() {
  const navigate = useNavigate();
  const { data, error } = useLoad(() => platformApi.tenants(), []);
  return (
    <>
      <PageHead title="Customers" />
      <div className="panel">
        <ErrorBanner error={error} />
        <div className="table-wrap">
          <table>
            <thead><tr><th>Pharmacy</th><th>Plan</th><th>Status</th><th className="num">Branches</th><th className="num">Users</th><th className="num">Bills (30d)</th><th>Last bill</th><th className="num">Overdue</th><th>Migration</th></tr></thead>
            <tbody>{data?.map(t => (
              <tr key={t.id} className="clickable" onClick={() => navigate(`/platform/tenants/${t.id}`)}>
                <td><b>{t.name}</b> {!t.isActive && <span className="badge danger">{t.deletedAt ? 'deleted' : 'suspended'}</span>}{t.deletionScheduledAt && !t.deletedAt && <span className="badge warn">deletion scheduled</span>}</td>
                <td>{t.planCode}</td><td><StatusBadge status={t.status} />{t.status === 'Trial' && t.trialEndsAt && <div className="small muted">ends {t.trialEndsAt.slice(0, 10)}</div>}</td>
                <td className="num">{t.branches}</td><td className="num">{t.users}</td><td className="num">{t.billsLast30Days}</td>
                <td className="small">{t.lastBillAt ? dateTime(t.lastBillAt) : <span className="muted">never</span>}</td>
                <td className="num">{t.overdue ? <span className="badge danger">{money(t.overdue)}</span> : '-'}</td><td className="small">{t.migration}</td>
              </tr>
            ))}</tbody>
          </table>
        </div>
      </div>
    </>
  );
}

const MODULES = ['crm', 'purchasing', 'accounting', 'multistore'];

function TenantPage() {
  const { id } = useParams();
  const { data: d, error, reload, setData } = useLoad(() => platformApi.tenant(id!), [id]);
  const [sub, setSub] = useState<SubscriptionInput | null>(null);
  const [inv, setInv] = useState({ periodFrom: today(), periodTo: today(), amount: '', dueDate: today() });
  const [err, setErr] = useState<unknown>(null);
  useEffect(() => { if (d) setSub(d.subscription); }, [d]);
  const act = async (fn: () => Promise<unknown>, msg: string) => { setErr(null); try { await fn(); toast(msg); reload(); } catch (e) { setErr(e); } };

  const supportSession = async () => {
    setErr(null);
    try {
      const s = await platformApi.supportSession(id!);
      tokenStore.set(s.token); // read-only tenant view in a new tab
      window.open('/', '_blank', 'noopener');
      toast('Support view opened in a new tab (read-only, audited)');
    } catch (e) { setErr(e); }
  };

  if (!d || !sub) return <ErrorBanner error={error} />;
  const t = d.tenant;
  return (
    <>
      <PageHead title={t.name} sub={`Customer since ${t.createdAt.slice(0, 10)} · ${t.branches} branch(es) · ${t.users} user(s) · ${t.billsLast30Days} bills in 30 days`}>
        {t.isActive ? <button className="danger" onClick={() => confirm('Suspend this customer? All their sessions stop immediately.') && act(() => platformApi.setActive(t.id, false), 'Suspended')}>Suspend</button>
          : !t.deletedAt && <button onClick={() => act(() => platformApi.setActive(t.id, true), 'Reactivated')}>Reactivate</button>}
        <button onClick={supportSession} disabled={!d.supportAccessUntil || new Date(d.supportAccessUntil) < new Date()}
          title={d.supportAccessUntil ? '' : 'Owner has not granted access'}>Open support view</button>
      </PageHead>
      <ErrorBanner error={err} />
      <div className="grid c2" style={{ alignItems: 'start' }}>
        <div className="panel grid">
          <h2 style={{ margin: 0 }}>Subscription</h2>
          <div className="grid c2">
            <Field label="Plan"><select value={sub.planCode} onChange={e => setSub({ ...sub, planCode: e.target.value })}>{['single', 'chain', 'enterprise'].map(p => <option key={p}>{p}</option>)}</select></Field>
            <Field label="Status"><select value={sub.status} onChange={e => setSub({ ...sub, status: e.target.value })}>{['Trial', 'Active', 'PastDue', 'Cancelled'].map(s => <option key={s}>{s}</option>)}</select></Field>
            <Field label="Trial ends"><input type="date" value={sub.trialEndsAt?.slice(0, 10) ?? ''} onChange={e => setSub({ ...sub, trialEndsAt: e.target.value ? `${e.target.value}T23:59:59Z` : null })} /></Field>
            <Field label="Migration"><select value={sub.migration} onChange={e => setSub({ ...sub, migration: e.target.value })}>{['NotStarted', 'InProgress', 'Completed', 'NotNeeded'].map(s => <option key={s}>{s}</option>)}</select></Field>
            <Field label="Extra modules (feature flags)"><input value={sub.extraModules ?? ''} placeholder={MODULES.join(',')} onChange={e => setSub({ ...sub, extraModules: e.target.value || null })} /></Field>
            <Field label="Disabled modules"><input value={sub.disabledModules ?? ''} onChange={e => setSub({ ...sub, disabledModules: e.target.value || null })} /></Field>
            <Field label="Max branches override"><input type="number" value={sub.maxBranchesOverride ?? ''} onChange={e => setSub({ ...sub, maxBranchesOverride: e.target.value ? Number(e.target.value) : null })} /></Field>
            <Field label="Max users override"><input type="number" value={sub.maxUsersOverride ?? ''} onChange={e => setSub({ ...sub, maxUsersOverride: e.target.value ? Number(e.target.value) : null })} /></Field>
          </div>
          <Field label="Internal notes"><textarea rows={3} value={sub.notes ?? ''} onChange={e => setSub({ ...sub, notes: e.target.value })} /></Field>
          <div className="row"><span className="small muted">Effective modules: {d.modules.join(', ') || 'core only'}</span><span className="spacer" />
            <button className="primary" onClick={() => act(async () => setData(await platformApi.updateSubscription(t.id, sub)), 'Subscription saved')}>Save</button></div>
        </div>
        <div className="panel grid">
          <h2 style={{ margin: 0 }}>Invoices</h2>
          <table className="small"><tbody>{d.invoices.map(i => (
            <tr key={i.id}><td>{i.invoiceNo}<div className="muted">{i.periodFrom} → {i.periodTo}</div></td><td className="num">{money(i.amount)}</td>
              <td><StatusBadge status={i.status === 'Due' ? (i.daysOverdue ? 'Cancelled' : 'Draft') : 'Posted'} /> {i.status}{i.daysOverdue > 0 && ` · ${i.daysOverdue}d overdue`}{i.paymentReference && <div className="muted">{i.paymentReference}</div>}</td>
              <td>{i.status === 'Due' && <div className="row" style={{ flexWrap: 'nowrap' }}>
                <button className="small" onClick={() => { const r = prompt('Payment reference (UTR / cheque no.)'); if (r) act(() => platformApi.markPaid(i.id, r), 'Marked paid'); }}>Paid</button>
                <button className="small" onClick={() => confirm('Void this invoice?') && act(() => platformApi.voidInvoice(i.id), 'Voided')}>Void</button></div>}</td></tr>
          ))}</tbody></table>
          <div className="grid c4">
            <Field label="From"><input type="date" value={inv.periodFrom} onChange={e => setInv({ ...inv, periodFrom: e.target.value })} /></Field>
            <Field label="To"><input type="date" value={inv.periodTo} onChange={e => setInv({ ...inv, periodTo: e.target.value })} /></Field>
            <Field label="Amount"><input type="number" value={inv.amount} onChange={e => setInv({ ...inv, amount: e.target.value })} /></Field>
            <Field label="Due"><input type="date" value={inv.dueDate} onChange={e => setInv({ ...inv, dueDate: e.target.value })} /></Field>
          </div>
          <div className="row end"><button disabled={!(Number(inv.amount) > 0)} onClick={() => act(() => platformApi.createInvoice(t.id, { ...inv, amount: Number(inv.amount) }), 'Invoice created')}>Create invoice</button></div>
          <p className="small muted" style={{ margin: 0 }}>No payment gateway is connected; record payments received by bank/UPI here.</p>
        </div>
      </div>
      <div className="grid c2" style={{ alignItems: 'start', marginTop: 16 }}>
        <div className="panel">
          <h2>Support & audit</h2>
          <p className="small">Support access granted until: <b>{d.supportAccessUntil ? dateTime(d.supportAccessUntil) : 'not granted'}</b></p>
          <table className="small"><tbody>{d.recentAudit.map((a, i) => <tr key={i}><td>{dateTime(a.at)}</td><td>{a.action}</td><td className="muted">{a.summary}</td></tr>)}</tbody></table>
        </div>
        <div className="panel grid">
          <h2 style={{ margin: 0 }}>Offboarding</h2>
          {t.deletedAt ? <div className="alert error">Data purged on {dateTime(t.deletedAt)}.</div> : t.deletionScheduledAt ? <>
            <div className="alert warn" style={{ margin: 0 }}>Deletion scheduled for {dateTime(t.deletionScheduledAt)}. The account is read-only; the customer can still export data.</div>
            <div className="row">
              <button onClick={() => act(() => platformApi.cancelDeletion(t.id), 'Deletion cancelled')}>Cancel deletion</button>
              <button className="danger" disabled={new Date(t.deletionScheduledAt) > new Date()} onClick={() => {
                if (prompt(`Type the customer name to permanently delete all their data:\n${t.name}`) === t.name) act(() => platformApi.purge(t.id), 'Data purged');
              }}>Purge data now</button>
            </div>
          </> : <div className="row">
            <button className="danger" onClick={() => { const d2 = prompt('Schedule deletion in how many days? (0-90; customer should export first)', '30'); if (d2 !== null) act(() => platformApi.scheduleDeletion(t.id, Number(d2)), 'Deletion scheduled'); }}>Schedule deletion</button>
          </div>}
          <p className="small muted" style={{ margin: 0 }}>Purge permanently removes all business data. Provider invoices are retained as our own records. Confirm retention obligations before purging.</p>
        </div>
      </div>
    </>
  );
}

function Overdue() {
  const { data, error } = useLoad(() => platformApi.overdue(), []);
  const navigate = useNavigate();
  return (
    <>
      <PageHead title="Overdue invoices" sub="Dunning list: due invoices past their due date." />
      <div className="panel">
        <ErrorBanner error={error} />
        {data?.length === 0 ? <div className="empty">Nothing overdue.</div> : (
          <table><thead><tr><th>Customer</th><th>Invoice</th><th>Due</th><th className="num">Days overdue</th><th className="num">Amount</th></tr></thead>
            <tbody>{data?.map(i => <tr key={i.id} className="clickable" onClick={() => navigate(`/platform/tenants/${i.tenantId}`)}>
              <td>{i.tenantName}</td><td>{i.invoiceNo}</td><td>{i.dueDate}</td><td className="num"><span className="badge danger">{i.daysOverdue}</span></td><td className="num">{money(i.amount)}</td></tr>)}</tbody></table>
        )}
      </div>
    </>
  );
}

function Tickets() {
  const { data, error, reload } = useLoad(() => platformApi.tickets(), []);
  const [open, setOpen] = useState<Ticket | null>(null);
  const [reply, setReply] = useState('');
  const send = async (status: string) => {
    if (!open) return;
    try { setOpen(await platformApi.reply(open.id, reply, status)); setReply(''); toast('Sent'); reload(); } catch (e) { alert((e as Error).message); }
  };
  return (
    <>
      <PageHead title="Support tickets" sub="Open tickets from all customers, oldest activity first." />
      <ErrorBanner error={error} />
      <div className="grid c2" style={{ alignItems: 'start' }}>
        <div className="panel">
          {data?.length === 0 ? <div className="empty">No open tickets.</div> : (
            <table><tbody>{data?.map(t => (
              <tr key={t.id} className={`clickable${open?.id === t.id ? ' selected' : ''}`} onClick={() => setOpen(t)}>
                <td><b>{t.subject}</b><div className="small muted">{t.tenantName} · {dateTime(t.updatedAt)}</div></td>
                <td>{t.priority === 'high' && <span className="badge danger">high</span>}</td><td><span className="badge">{t.status}</span></td></tr>
            ))}</tbody></table>
          )}
        </div>
        {open && (
          <div className="panel grid">
            <h3 style={{ margin: 0 }}>{open.subject} <span className="small muted">· {open.tenantName}</span></h3>
            {open.messages.map((m, i) => <div key={i} className={`alert ${m.fromProvider ? 'ok' : ''}`} style={{ margin: 0, background: m.fromProvider ? undefined : '#f2f5f7' }}>
              <div className="small muted">{m.authorName} · {dateTime(m.at)}</div><div style={{ whiteSpace: 'pre-wrap' }}>{m.body}</div></div>)}
            <textarea rows={3} value={reply} onChange={e => setReply(e.target.value)} placeholder="Reply…" />
            <div className="row end">
              <button onClick={() => send('Closed')}>Close</button>
              <button className="primary" disabled={!reply.trim()} onClick={() => send('WaitingOnCustomer')}>Reply & wait on customer</button>
            </div>
          </div>
        )}
      </div>
    </>
  );
}
