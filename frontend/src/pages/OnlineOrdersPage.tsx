import { useState } from 'react';
import { api, type OnlineOrder, type SettlementMode } from '../api';
import { RoleSets, useAuth } from '../auth';
import ProductPicker from '../components/ProductPicker';
import { ErrorBanner, Field, Modal, PageHead, toast, useLoad } from '../ui';
import { dateTime, money, today } from '../util';

const TABS: [string, string][] = [['Submitted', 'New'], ['NeedsCustomerApproval', 'Awaiting customer'], ['Accepted', 'To pack'], ['Packed', 'To dispatch'],
  ['ReadyForPickup', 'Ready for pickup'], ['OutForDelivery', 'Out for delivery'], ['DeliveryFailed', 'Failed delivery'], ['', 'All']];
const RX = ['H', 'H1', 'X', 'NarcoticPsychotropic'];

export default function OnlineOrdersPage() {
  const { session } = useAuth();
  const [tab, setTab] = useState('Submitted');
  const { data, error, reload } = useLoad(() => api.onlineOrders(tab || undefined), [tab]);
  const [open, setOpen] = useState<string | null>(null);
  const link = session ? `${window.location.origin}/shop/${session.branchId}` : '';
  return (
    <>
      <PageHead title="Online orders" sub="Every order is reviewed by a pharmacist before anything is billed or packed.">
        <button className="small" onClick={() => { navigator.clipboard?.writeText(link); toast('Ordering page link copied'); }}>Copy ordering link</button>
      </PageHead>
      <div className="row" style={{ marginBottom: 12 }}>{TABS.map(([k, l]) => <button key={k} className={tab === k ? 'primary small' : 'small'} onClick={() => setTab(k)}>{l}</button>)}</div>
      <div className="panel">
        <ErrorBanner error={error} />
        {data?.length === 0 ? <div className="empty">Nothing here.</div> : (
          <table>
            <thead><tr><th>Order</th><th>Customer</th><th>Type</th><th>Items</th><th>Updated</th><th>Status</th></tr></thead>
            <tbody>{data?.map(o => (
              <tr key={o.id} className="clickable" onClick={() => setOpen(o.id)}>
                <td><b>{o.orderNo}</b></td><td>{o.customerName}<div className="small muted">{o.customerPhone}</div></td>
                <td>{o.fulfilment}{o.pincode && <div className="small muted">{o.pincode}</div>}</td>
                <td className="small">{o.lines.length} item(s){o.requestText && <div className="badge warn">Rx request</div>}{o.files.length > 0 && <span className="badge brand">{o.files.length} file(s)</span>}</td>
                <td className="small">{dateTime(o.updatedAt)}</td><td><span className="badge">{o.status}</span></td>
              </tr>
            ))}</tbody>
          </table>
        )}
      </div>
      <Collections />
      {open && <OrderModal id={open} onClose={() => { setOpen(null); reload(); }} />}
    </>
  );
}

function Collections() {
  const { can } = useAuth();
  const { data } = useLoad(() => can(...RoleSets.reports) ? api.onlineCollections(today()) : Promise.resolve([]), []);
  if (!data?.length) return null;
  return (
    <div className="panel" style={{ marginTop: 16 }}>
      <h3>Collected today for online orders</h3>
      <table className="small"><tbody>{data.map((r, i) => <tr key={i}><td>{r.orderNo}</td><td>{r.agentName ?? 'store'}</td><td>{r.mode}</td><td className="num">{money(r.amount)}</td></tr>)}</tbody></table>
      <p className="small muted">Reconcile cash collected by each delivery agent against this list.</p>
    </div>
  );
}

function OrderModal({ id, onClose }: { id: string; onClose: () => void }) {
  const { data: o, error, setData } = useLoad(() => api.onlineOrder(id), [id]);
  const agents = useLoad(() => api.deliveryAgents(), []);
  const [qty, setQty] = useState<Record<string, string>>({});
  const [added, setAdded] = useState<{ id: string; name: string; qty: string }[]>([]);
  const [notes, setNotes] = useState('');
  const [prescriber, setPrescriber] = useState('');
  const [agent, setAgent] = useState('');
  const [mode, setMode] = useState<SettlementMode>('Cash');
  const [err, setErr] = useState<unknown>(null);

  const act = async (fn: () => Promise<OnlineOrder>, msg: string) => { setErr(null); try { setData(await fn()); toast(msg); } catch (e) { setErr(e); } };
  if (!o) return <Modal title="Order" onClose={onClose}><ErrorBanner error={error} /></Modal>;

  const review = (accept: boolean) => {
    if (!accept) {
      const reason = prompt('Reason shown to the customer:');
      if (!reason) return;
      return act(() => api.reviewOrder(o.id, { accept: false, lines: [], rejectReason: reason }), 'Order rejected');
    }
    const lines = [...o.lines.map(l => ({ productId: l.productId, approvedQtyUnits: Number(qty[l.productId] ?? l.requestedQtyUnits) || 0 })),
      ...added.map(a => ({ productId: a.id, approvedQtyUnits: Number(a.qty) || 0 }))];
    return act(() => api.reviewOrder(o.id, { accept: true, lines, notes: notes || null, prescriberName: prescriber || null }), 'Review saved');
  };

  return (
    <Modal title={`Online order ${o.orderNo}`} onClose={onClose} wide>
      <div className="grid">
        <ErrorBanner error={err} />
        <div className="row"><b>{o.customerName}</b><span>{o.customerPhone}</span><span className="badge">{o.fulfilment}</span><span className="badge brand">{o.status}</span>
          {o.invoiceNo && <span className="small">Bill {o.invoiceNo} · due {money(o.amountDue)}</span>}</div>
        {o.address && <div className="small">{o.address} — {o.pincode}</div>}
        {o.customerNotes && <div className="small">Customer note: {o.customerNotes}</div>}
        {o.requestText && <div className="alert warn" style={{ margin: 0 }}>Prescription request: {o.requestText}</div>}
        {o.files.length > 0 && <div className="row small">Prescriptions: {o.files.map(f => <button key={f.id} className="small" onClick={() => api.openOrderFile(o.id, f.id).catch(setErr)}>{f.fileName}</button>)}
          <span className="muted">(viewing is logged)</span></div>}
        <table className="small">
          <thead><tr><th>Item</th><th className="num">Asked</th><th className="num">In stock</th><th className="num">Approve</th></tr></thead>
          <tbody>
            {o.lines.map(l => (
              <tr key={l.productId}><td>{l.productName} {RX.includes(l.schedule) && <span className="badge warn">{l.schedule}</span>}</td>
                <td className="num">{l.requestedQtyUnits}</td><td className="num">{l.availableUnits}</td>
                <td className="num">{o.status === 'Submitted'
                  ? <input className="qty" type="number" min={0} value={qty[l.productId] ?? String(l.requestedQtyUnits)} onChange={e => setQty({ ...qty, [l.productId]: e.target.value })} />
                  : l.approvedQtyUnits ?? '-'}</td></tr>
            ))}
            {added.map(a => <tr key={a.id}><td>{a.name} <span className="badge brand">added</span></td><td /><td /><td className="num">
              <input className="qty" type="number" min={0} value={a.qty} onChange={e => setAdded(x => x.map(y => y.id === a.id ? { ...y, qty: e.target.value } : y))} /></td></tr>)}
          </tbody>
        </table>
        {o.status === 'Submitted' && <>
          <div className="row"><span className="small">Add from prescription:</span><div style={{ flex: 1 }}><ProductPicker value={null} onPick={p => setAdded(x => x.some(y => y.id === p.id) ? x : [...x, { id: p.id, name: p.name, qty: String(p.packSize) }])} /></div></div>
          <div className="grid c2">
            <Field label="Prescriber (required for prescription medicines)"><input value={prescriber} onChange={e => setPrescriber(e.target.value)} /></Field>
            <Field label="Note to customer"><input value={notes} onChange={e => setNotes(e.target.value)} /></Field>
          </div>
          <p className="small muted" style={{ margin: 0 }}>Check the prescription is valid and current before approving prescription medicines. Any change from what the customer asked needs their approval.</p>
          <div className="row end"><button className="danger" onClick={() => review(false)}>Reject</button><button className="primary" onClick={() => review(true)}>Approve</button></div>
        </>}
        <div className="row end">
          {o.status === 'Accepted' && <button className="primary" onClick={() => act(() => api.packOrder(o.id), 'Packed and billed')}>Pack & bill</button>}
          {(o.status === 'Packed' || o.status === 'DeliveryFailed') && <>
            <select value={agent} onChange={e => setAgent(e.target.value)}><option value="">Delivery agent…</option>{agents.data?.map(a => <option key={a.id} value={a.id}>{a.displayName}</option>)}</select>
            <button className="primary" disabled={!agent} onClick={() => act(() => api.assignOrder(o.id, agent), 'Out for delivery')}>Dispatch</button>
          </>}
          {o.status === 'ReadyForPickup' && <>
            <select value={mode} onChange={e => setMode(e.target.value as SettlementMode)}><option>Cash</option><option>Upi</option><option>Card</option></select>
            <button className="primary" onClick={() => act(() => api.completeOrder(o.id, { mode }), 'Handed over')}>Customer collected & paid</button>
          </>}
          {!['Completed', 'Cancelled', 'Rejected', 'OutForDelivery'].includes(o.status) &&
            <button className="danger" onClick={() => { const r = prompt('Reason for cancelling?'); if (r) act(() => api.cancelOrder(o.id, r), 'Cancelled'); }}>Cancel order</button>}
        </div>
        {o.deliveryAgentName && <div className="small muted">Agent: {o.deliveryAgentName} · attempts {o.deliveryAttempts}{o.deliveryFailureReason && ` · last failure: ${o.deliveryFailureReason}`}</div>}
      </div>
    </Modal>
  );
}

/** Delivery agent's screen: only their active deliveries. */
export function DeliveriesPage() {
  const { data, error, reload } = useLoad(() => api.myDeliveries(), []);
  const [code, setCode] = useState<Record<string, string>>({});
  const [mode, setMode] = useState<Record<string, SettlementMode>>({});
  const [err, setErr] = useState<unknown>(null);
  const act = async (fn: () => Promise<unknown>, msg: string) => { setErr(null); try { await fn(); toast(msg); reload(); } catch (e) { setErr(e); } };
  return (
    <>
      <PageHead title="My deliveries" sub="Ask the customer for the 4-digit handover code shown on their order page." />
      <ErrorBanner error={error ?? err} />
      {data?.length === 0 && <div className="panel empty">No deliveries assigned.</div>}
      <div className="grid">
        {data?.map(o => (
          <div key={o.id} className="panel grid">
            <div className="row"><b>{o.orderNo}</b><span>{o.customerName}</span>{o.customerPhone && <a href={`tel:${o.customerPhone}`}>{o.customerPhone}</a>}</div>
            <div>{o.address} — {o.pincode}</div>
            <div style={{ fontSize: 18 }}>Collect: <b>{money(o.amountDue)}</b></div>
            <div className="row">
              <input className="short" placeholder="Code" maxLength={4} inputMode="numeric" value={code[o.id] ?? ''} onChange={e => setCode({ ...code, [o.id]: e.target.value })} />
              <select value={mode[o.id] ?? 'Cash'} onChange={e => setMode({ ...mode, [o.id]: e.target.value as SettlementMode })}><option>Cash</option><option>Upi</option></select>
              <button className="primary" disabled={(code[o.id] ?? '').length !== 4} onClick={() => act(() => api.completeOrder(o.id, { handoverCode: code[o.id], mode: mode[o.id] ?? 'Cash' }), 'Delivered')}>Delivered</button>
              <button className="danger" onClick={() => { const r = prompt('Why could it not be delivered?'); if (r) act(() => api.failOrder(o.id, r), 'Marked failed'); }}>Couldn't deliver</button>
            </div>
          </div>
        ))}
      </div>
    </>
  );
}
