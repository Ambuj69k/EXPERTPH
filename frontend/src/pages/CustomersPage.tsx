import { useState } from 'react';
import { api, type Channel, type Customer, type CustomerProfile, type Purpose, type Reminder } from '../api';
import { RoleSets, useAuth } from '../auth';
import MessageSender from '../components/MessageSender';
import ProductPicker from '../components/ProductPicker';
import { ErrorBanner, Field, Modal, PageHead, toast, useDebounced, useLoad } from '../ui';
import { dateTime, money } from '../util';
import { ReminderEdit } from './RemindersPage';

export default function CustomersPage() {
  const [q, setQ] = useState('');
  const dq = useDebounced(q);
  const { data, error, reload } = useLoad(() => api.customers(dq), [dq]);
  const [open, setOpen] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);

  return (
    <>
      <PageHead title="Customers" sub="Created automatically at billing when a mobile number is entered. Only name and mobile are stored — no medical details.">
        <input placeholder="Search mobile or name" value={q} onChange={e => setQ(e.target.value)} />
        <button className="primary" onClick={() => setCreating(true)}>+ New customer</button>
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error} />
        {data?.length === 0 ? <div className="empty">No customers found.</div> : (
          <table>
            <thead><tr><th>Name</th><th>Mobile</th><th className="num">Credit limit</th></tr></thead>
            <tbody>
              {data?.map(c => (
                <tr key={c.id} className="clickable" onClick={() => setOpen(c.id)}>
                  <td><b>{c.name}</b></td><td>{c.phone}</td>
                  <td className="num">{c.creditLimit == null ? <span className="muted">no limit</span> : c.creditLimit === 0 ? <span className="badge">no credit</span> : money(c.creditLimit)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
      {creating && <Modal title="New customer" onClose={() => setCreating(false)}><CustomerForm customer={null} onSaved={() => { setCreating(false); reload(); }} /></Modal>}
      {open && <CustomerDetail id={open} onClose={() => { setOpen(null); reload(); }} onSwitch={setOpen} />}
    </>
  );
}

function CustomerForm({ customer, onSaved }: { customer: Customer | null; onSaved: () => void }) {
  const { can } = useAuth();
  const canLimit = can(...RoleSets.salesAdmin);
  const [name, setName] = useState(customer?.name ?? '');
  const [phone, setPhone] = useState(customer?.phone ?? '');
  const [limit, setLimit] = useState(customer?.creditLimit == null ? '' : String(customer.creditLimit));
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const submit = async (e: React.FormEvent) => {
    e.preventDefault(); setBusy(true); setError(null);
    const body = { name, phone: phone || null, creditLimit: limit === '' ? null : Number(limit) };
    try {
      if (customer) await api.updateCustomer(customer.id, body); else await api.createCustomer(body);
      toast('Customer saved'); onSaved();
    } catch (err) { setError(err); } finally { setBusy(false); }
  };
  const editable = !customer || canLimit;
  return (
    <form className="grid" onSubmit={submit}>
      <ErrorBanner error={error} />
      <div className="grid c3">
        <Field label="Name"><input value={name} onChange={e => setName(e.target.value)} required disabled={!editable} /></Field>
        <Field label="Mobile"><input value={phone} onChange={e => setPhone(e.target.value)} inputMode="tel" disabled={!editable} /></Field>
        <Field label="Credit limit (blank = none, 0 = no credit)"><input type="number" min={0} value={limit} onChange={e => setLimit(e.target.value)} disabled={!canLimit} /></Field>
      </div>
      {editable && <div className="row end"><button className="primary" disabled={busy}>Save</button></div>}
    </form>
  );
}

type Tab = 'overview' | 'purchases' | 'reminders' | 'messages';

function CustomerDetail({ id, onClose, onSwitch }: { id: string; onClose: () => void; onSwitch: (id: string) => void }) {
  const profile = useLoad(() => api.customerProfile(id), [id]);
  const [tab, setTab] = useState<Tab>('overview');
  const p = profile.data;
  return (
    <Modal title={p ? `${p.name}${p.phone ? ` · ${p.phone}` : ''}` : 'Customer'} onClose={onClose} wide>
      <ErrorBanner error={profile.error} />
      {p && !p.isActive && <div className="alert warn">This record was merged into another customer. <button className="link" onClick={() => onSwitch(p.mergedIntoId!)}>Open surviving record</button></div>}
      {p && (
        <>
          <div className="row" style={{ marginBottom: 12 }}>
            {(['overview', 'purchases', 'reminders', 'messages'] as Tab[]).map(t => (
              <button key={t} className={tab === t ? 'primary small' : 'small'} onClick={() => setTab(t)}>{t[0].toUpperCase() + t.slice(1)}</button>
            ))}
          </div>
          {tab === 'overview' && <Overview p={p} onChanged={profile.reload} onMerged={onSwitch} />}
          {tab === 'purchases' && <Purchases id={id} />}
          {tab === 'reminders' && <Reminders customerId={id} hasPhone={!!p.phone} />}
          {tab === 'messages' && <Messages id={id} />}
        </>
      )}
    </Modal>
  );
}

const CONSENT_ROWS: [Channel, Purpose][] = [['WhatsApp', 'Reminders'], ['Sms', 'Reminders'], ['WhatsApp', 'Promotions'], ['Sms', 'Promotions']];

function Overview({ p, onChanged, onMerged }: { p: CustomerProfile; onChanged: () => void; onMerged: (id: string) => void }) {
  const { can } = useAuth();
  const [editing, setEditing] = useState(false);
  const [merging, setMerging] = useState(false);
  const [error, setError] = useState<unknown>(null);

  const setConsent = async (channel: Channel, purpose: Purpose, granted: boolean) => {
    const source = granted ? prompt('How did the customer give consent? (e.g. verbal at counter, signed form)', 'verbal at counter') : prompt('How did they opt out?', 'asked at counter');
    if (!source) return;
    setError(null);
    try { await api.recordConsent(p.id, { channel, purpose, granted, source }); toast(granted ? 'Consent recorded' : 'Opt-out recorded'); onChanged(); } catch (e) { setError(e); }
  };
  const adjust = async () => {
    const pts = Number(prompt('Points to add (use negative to remove):'));
    if (!pts) return;
    const reason = prompt('Reason:');
    if (!reason) return;
    try { await api.adjustPoints(p.id, pts, reason); onChanged(); } catch (e) { setError(e); }
  };

  return (
    <div className="grid">
      <ErrorBanner error={error} />
      <div className="grid c4">
        <div className="panel"><div className="small muted">Bills</div><b style={{ fontSize: 18 }}>{p.billCount}</b><div className="small muted">{p.lastPurchaseAt ? `last ${dateTime(p.lastPurchaseAt)}` : 'no purchases'}</div></div>
        <div className="panel"><div className="small muted">Spend (net of returns)</div><b style={{ fontSize: 18 }}>{money(p.totalSpend)}</b></div>
        <div className="panel"><div className="small muted">Dues</div><b style={{ fontSize: 18 }}>{money(p.balance)}</b><div className="small muted">limit {p.creditLimit == null ? 'none' : money(p.creditLimit)}</div></div>
        <div className="panel"><div className="small muted">Loyalty</div><b style={{ fontSize: 18 }}>{p.loyaltyPoints} pts</b><div className="small muted">worth {money(p.loyaltyValue)} {can('Owner') && <button className="link small" onClick={adjust}>adjust</button>}</div></div>
      </div>
      <div className="panel">
        <h3>Consent</h3>
        <table className="small">
          <tbody>
            {CONSENT_ROWS.map(([ch, pu]) => {
              const c = p.consents.find(x => x.channel === ch && x.purpose === pu);
              return (
                <tr key={ch + pu}>
                  <td>{ch} · {pu}</td>
                  <td>{c ? (c.granted ? <span className="badge ok">yes</span> : <span className="badge danger">opted out</span>) : <span className="badge">not asked</span>}</td>
                  <td className="muted">{c && `${c.source} · ${dateTime(c.at)}`}</td>
                  <td>{c?.granted ? <button className="small" onClick={() => setConsent(ch, pu, false)}>Opt out</button> : <button className="small" onClick={() => setConsent(ch, pu, true)}>Record consent</button>}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
        <p className="small muted">Ask before recording consent. An opt-out always overrides — no reminders are sent without a current "yes".</p>
      </div>
      <div className="row">
        <button onClick={() => setEditing(true)}>Edit details</button>
        {can(...RoleSets.salesAdmin) && p.isActive && <button onClick={() => setMerging(true)}>Merge a duplicate into this customer</button>}
      </div>
      {editing && <Modal title="Edit customer" onClose={() => setEditing(false)}><CustomerForm customer={{ id: p.id, name: p.name, phone: p.phone, creditLimit: p.creditLimit }} onSaved={() => { setEditing(false); onChanged(); }} /></Modal>}
      {merging && <MergeModal target={p} onClose={() => setMerging(false)} onDone={() => { setMerging(false); onChanged(); onMerged(p.id); }} />}
    </div>
  );
}

function MergeModal({ target, onClose, onDone }: { target: CustomerProfile; onClose: () => void; onDone: () => void }) {
  const [q, setQ] = useState('');
  const dq = useDebounced(q);
  const { data } = useLoad(() => dq.trim() ? api.customers(dq) : Promise.resolve([]), [dq]);
  const [error, setError] = useState<unknown>(null);
  const merge = async (c: Customer) => {
    if (!confirm(`Merge "${c.name}" (${c.phone ?? 'no mobile'}) into "${target.name}"? Bills, dues, points and reminders move over. This cannot be undone.`)) return;
    try { await api.mergeCustomers(c.id, target.id); toast('Customers merged'); onDone(); } catch (e) { setError(e); }
  };
  return (
    <Modal title={`Merge duplicate into ${target.name}`} onClose={onClose}>
      <div className="grid">
        <ErrorBanner error={error} />
        <input placeholder="Search the duplicate by name or mobile" value={q} onChange={e => setQ(e.target.value)} autoFocus />
        <table><tbody>
          {data?.filter(c => c.id !== target.id).map(c => (
            <tr key={c.id}><td>{c.name}</td><td>{c.phone}</td><td><button className="small danger" onClick={() => merge(c)}>Merge into {target.name}</button></td></tr>
          ))}
        </tbody></table>
      </div>
    </Modal>
  );
}

function Purchases({ id }: { id: string }) {
  const [page, setPage] = useState(1);
  const { data } = useLoad(() => api.customerPurchases(id, page), [id, page]);
  return (
    <div>
      <table className="small">
        <thead><tr><th>When</th><th>Bill</th><th>Items</th><th className="num">Amount</th></tr></thead>
        <tbody>
          {data?.items.map(b => (
            <tr key={b.invoiceId}><td>{dateTime(b.at)}</td><td>{b.invoiceNo} {b.status === 'Cancelled' && <span className="badge danger">cancelled</span>}</td>
              <td>{b.items.join(', ')}</td><td className="num">{money(b.netAmount)}</td></tr>
          ))}
        </tbody>
      </table>
      {data && data.total > data.pageSize && (
        <div className="row small" style={{ marginTop: 8 }}>
          <button className="small" disabled={page <= 1} onClick={() => setPage(page - 1)}>Prev</button>
          <button className="small" disabled={page * data.pageSize >= data.total} onClick={() => setPage(page + 1)}>Next</button>
        </div>
      )}
    </div>
  );
}

function Reminders({ customerId, hasPhone }: { customerId: string; hasPhone: boolean }) {
  const { data, error, reload } = useLoad(() => api.reminders({ customerId }), [customerId]);
  const [product, setProduct] = useState<{ id: string; name: string } | null>(null);
  const [every, setEvery] = useState('30');
  const [asked, setAsked] = useState(false);
  const [err, setErr] = useState<unknown>(null);
  const [editing, setEditing] = useState<Reminder | null>(null);
  const [sending, setSending] = useState<{ id: string; channel: Channel } | null>(null);

  const add = async () => {
    if (!product) return;
    setErr(null);
    try {
      await api.createReminder({ customerId, productId: product.id, intervalDays: Number(every), customerRequested: asked });
      toast('Reminder added'); setProduct(null); setAsked(false); reload();
    } catch (e) { setErr(e); }
  };

  return (
    <div className="grid">
      <ErrorBanner error={error ?? err} />
      <table className="small">
        <thead><tr><th>Product</th><th>Every</th><th>Next due</th><th>Status</th><th /></tr></thead>
        <tbody>
          {data?.map(r => (
            <tr key={r.id}><td>{r.productName}</td><td>{r.intervalDays}d</td><td>{r.nextDueDate}</td><td>{r.status}</td>
              <td className="row" style={{ flexWrap: 'nowrap' }}>
                <button className="small" disabled={!r.whatsAppConsent || r.status !== 'Active'} onClick={() => setSending({ id: r.id, channel: 'WhatsApp' })}>WhatsApp</button>
                <button className="small" onClick={() => setEditing(r)}>Edit</button>
              </td></tr>
          ))}
          {data?.length === 0 && <tr><td className="muted" colSpan={5}>No reminders.</td></tr>}
        </tbody>
      </table>
      {hasPhone ? (
        <div className="panel grid">
          <h3 style={{ margin: 0 }}>Add reminder</h3>
          <div className="row">
            <div style={{ flex: 1 }}><ProductPicker value={product} onPick={p => setProduct({ id: p.id, name: p.name })} /></div>
            <Field label="Every (days)"><input className="short" type="number" min={1} max={365} value={every} onChange={e => setEvery(e.target.value)} /></Field>
          </div>
          <label className="check"><input type="checkbox" checked={asked} onChange={e => setAsked(e.target.checked)} /> The customer asked to be reminded</label>
          <div className="row end"><button className="primary" disabled={!product || !asked} onClick={add}>Add reminder</button></div>
        </div>
      ) : <p className="small muted">Add a mobile number to set reminders.</p>}
      {editing && <ReminderEdit reminder={editing} onClose={() => setEditing(null)} onSaved={() => { setEditing(null); reload(); }} />}
      {sending && <MessageSender reminderId={sending.id} channel={sending.channel} onClose={() => setSending(null)} onDone={() => { setSending(null); reload(); }} />}
    </div>
  );
}

function Messages({ id }: { id: string }) {
  const { data } = useLoad(() => api.notifications(id), [id]);
  return (
    <table className="small">
      <thead><tr><th>When</th><th>Channel</th><th>Status</th><th>Message</th></tr></thead>
      <tbody>
        {data?.items.map(n => (
          <tr key={n.id}><td>{dateTime(n.createdAt)}</td><td>{n.channel}</td>
            <td><span className={`badge ${n.status === 'Sent' ? 'ok' : n.status === 'Skipped' || n.status === 'Failed' ? 'danger' : 'warn'}`}>{n.status}</span>{n.error && <div className="muted">{n.error}</div>}</td>
            <td>{n.message}</td></tr>
        ))}
        {data?.items.length === 0 && <tr><td className="muted" colSpan={4}>No messages.</td></tr>}
      </tbody>
    </table>
  );
}
