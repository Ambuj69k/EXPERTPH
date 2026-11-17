import { useState } from 'react';
import { api, type Channel, type Reminder } from '../api';
import MessageSender from '../components/MessageSender';
import { ErrorBanner, Field, Modal, PageHead, toast, useLoad } from '../ui';
import { dateTime } from '../util';

export default function RemindersPage() {
  const [days, setDays] = useState(7);
  const { data, error, reload } = useLoad(() => api.reminders({ dueWithinDays: days, status: 'Active' }), [days]);
  const [sending, setSending] = useState<{ id: string; channel: Channel } | null>(null);
  const [editing, setEditing] = useState<Reminder | null>(null);

  return (
    <>
      <PageHead title="Refill reminders" sub="Only reminders customers asked for. Messages are sent only where the customer has consented.">
        <Field label="Due within">
          <select value={days} onChange={e => setDays(Number(e.target.value))}>{[0, 3, 7, 15, 30].map(d => <option key={d} value={d}>{d === 0 ? 'today / overdue' : `${d} days`}</option>)}</select>
        </Field>
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error} />
        {data?.length === 0 ? <div className="empty">Nothing due. Add reminders from a customer's profile.</div> : (
          <table>
            <thead><tr><th>Customer</th><th>Product</th><th>Due</th><th className="num">Every</th><th>Last contacted</th><th /></tr></thead>
            <tbody>
              {data?.map(r => (
                <tr key={r.id}>
                  <td><b>{r.customerName}</b> <span className="small muted">{r.customerPhone}</span></td>
                  <td>{r.productName}</td>
                  <td>{r.nextDueDate} {r.daysUntilDue < 0 ? <span className="badge danger">{-r.daysUntilDue}d overdue</span> : r.daysUntilDue === 0 ? <span className="badge warn">today</span> : <span className="small muted">in {r.daysUntilDue}d</span>}</td>
                  <td className="num">{r.intervalDays}d</td>
                  <td className="small">{r.lastContactedAt ? dateTime(r.lastContactedAt) : '-'}</td>
                  <td className="row" style={{ flexWrap: 'nowrap' }}>
                    <button className="small" disabled={!r.whatsAppConsent} title={r.whatsAppConsent ? '' : 'No WhatsApp consent'} onClick={() => setSending({ id: r.id, channel: 'WhatsApp' })}>WhatsApp</button>
                    <button className="small" disabled={!r.smsConsent} title={r.smsConsent ? '' : 'No SMS consent'} onClick={() => setSending({ id: r.id, channel: 'Sms' })}>SMS</button>
                    <button className="small" onClick={() => setEditing(r)}>Edit</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
      {sending && <MessageSender reminderId={sending.id} channel={sending.channel} onClose={() => setSending(null)} onDone={() => { setSending(null); reload(); }} />}
      {editing && <ReminderEdit reminder={editing} onClose={() => setEditing(null)} onSaved={() => { setEditing(null); reload(); }} />}
    </>
  );
}

export function ReminderEdit({ reminder, onClose, onSaved }: { reminder: Reminder; onClose: () => void; onSaved: () => void }) {
  const [f, setF] = useState({ intervalDays: String(reminder.intervalDays), nextDueDate: reminder.nextDueDate, status: reminder.status, notes: reminder.notes ?? '' });
  const [error, setError] = useState<unknown>(null);
  const save = async () => {
    setError(null);
    try { await api.updateReminder(reminder.id, { ...f, intervalDays: Number(f.intervalDays) }); toast('Reminder updated'); onSaved(); } catch (e) { setError(e); }
  };
  return (
    <Modal title={`${reminder.productName} · ${reminder.customerName}`} onClose={onClose}>
      <div className="grid">
        <ErrorBanner error={error} />
        <div className="grid c3">
          <Field label="Every (days)"><input type="number" min={1} max={365} value={f.intervalDays} onChange={e => setF({ ...f, intervalDays: e.target.value })} /></Field>
          <Field label="Next due"><input type="date" value={f.nextDueDate} onChange={e => setF({ ...f, nextDueDate: e.target.value })} /></Field>
          <Field label="Status"><select value={f.status} onChange={e => setF({ ...f, status: e.target.value as Reminder['status'] })}><option>Active</option><option>Paused</option><option>Cancelled</option></select></Field>
        </div>
        <Field label="Notes (no medical details)"><input value={f.notes} onChange={e => setF({ ...f, notes: e.target.value })} /></Field>
        <div className="row end"><button className="primary" onClick={save}>Save</button></div>
      </div>
    </Modal>
  );
}
