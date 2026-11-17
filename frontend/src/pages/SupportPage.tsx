import { useState } from 'react';
import { api, type Ticket } from '../api';
import { ErrorBanner, Field, PageHead, toast, useLoad } from '../ui';
import { dateTime } from '../util';

export default function SupportPage() {
  const { data, error, reload } = useLoad(() => api.tickets(), []);
  const [open, setOpen] = useState<Ticket | null>(null);
  const [form, setForm] = useState({ subject: '', body: '', priority: 'normal' });
  const [reply, setReply] = useState('');
  const [err, setErr] = useState<unknown>(null);

  const create = async () => {
    setErr(null);
    try { const t = await api.createTicket(form); setForm({ subject: '', body: '', priority: 'normal' }); toast('Ticket sent to PharmaOS support'); reload(); setOpen(t); }
    catch (e) { setErr(e); }
  };
  const send = async () => {
    if (!open) return;
    try { setOpen(await api.replyTicket(open.id, reply)); setReply(''); reload(); } catch (e) { setErr(e); }
  };

  return (
    <>
      <PageHead title="Help & support" sub="Raise a ticket with the PharmaOS team. Don't include patient details." />
      <ErrorBanner error={error ?? err} />
      <div className="grid c2" style={{ alignItems: 'start' }}>
        <div className="grid">
          <div className="panel grid">
            <h3 style={{ margin: 0 }}>New ticket</h3>
            <Field label="Subject"><input value={form.subject} onChange={e => setForm({ ...form, subject: e.target.value })} /></Field>
            <Field label="What happened?"><textarea rows={4} value={form.body} onChange={e => setForm({ ...form, body: e.target.value })} /></Field>
            <div className="row">
              <Field label="Priority"><select value={form.priority} onChange={e => setForm({ ...form, priority: e.target.value })}><option>low</option><option>normal</option><option>high</option></select></Field>
              <span className="spacer" />
              <button className="primary" style={{ alignSelf: 'end' }} disabled={!form.subject.trim() || !form.body.trim()} onClick={create}>Send</button>
            </div>
          </div>
          <div className="panel">
            <h3>Your tickets</h3>
            {data?.length === 0 ? <p className="muted small">No tickets yet.</p> : (
              <table className="small"><tbody>{data?.map(t => (
                <tr key={t.id} className={`clickable${open?.id === t.id ? ' selected' : ''}`} onClick={() => setOpen(t)}>
                  <td><b>{t.subject}</b><div className="muted">{dateTime(t.updatedAt)}</div></td>
                  <td><span className={`badge ${t.status === 'WaitingOnCustomer' ? 'warn' : t.status === 'Closed' ? '' : 'brand'}`}>{t.status === 'WaitingOnCustomer' ? 'reply needed' : t.status.toLowerCase()}</span></td>
                </tr>
              ))}</tbody></table>
            )}
          </div>
        </div>
        {open && (
          <div className="panel grid">
            <h3 style={{ margin: 0 }}>{open.subject}</h3>
            {open.messages.map((m, i) => (
              <div key={i} className={`alert ${m.fromProvider ? 'ok' : ''}`} style={{ margin: 0, background: m.fromProvider ? undefined : '#f2f5f7' }}>
                <div className="small muted">{m.authorName} · {dateTime(m.at)}</div><div style={{ whiteSpace: 'pre-wrap' }}>{m.body}</div>
              </div>
            ))}
            {open.status !== 'Closed' && <>
              <textarea rows={3} value={reply} onChange={e => setReply(e.target.value)} placeholder="Reply…" />
              <div className="row end"><button className="primary" disabled={!reply.trim()} onClick={send}>Send reply</button></div>
            </>}
          </div>
        )}
      </div>
    </>
  );
}
