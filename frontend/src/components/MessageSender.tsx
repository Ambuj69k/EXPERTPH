import { useEffect, useRef, useState } from 'react';
import { api, type Channel, type PreparedMessage } from '../api';
import { ErrorBanner, Modal, toast } from '../ui';

/**
 * Two-step manual send (no paid messaging API): the server checks consent and renders the message,
 * staff send it from their own WhatsApp/SMS, then confirm. Every attempt is logged.
 */
export default function MessageSender({ reminderId, channel, onClose, onDone }: { reminderId: string; channel: Channel; onClose: () => void; onDone: () => void }) {
  const [msg, setMsg] = useState<PreparedMessage | null>(null);
  const [error, setError] = useState<unknown>(null);
  const started = useRef(false); // ref survives StrictMode's double effect, so only one log entry is created

  useEffect(() => {
    if (started.current) return;
    started.current = true;
    api.notifyReminder(reminderId, channel).then(setMsg).catch(setError);
  }, [reminderId, channel]);

  const finish = async (sent: boolean) => {
    if (!msg) return;
    try {
      if (sent) await api.markSent(msg.logId); else await api.markFailed(msg.logId, 'Not delivered / customer unreachable');
      toast(sent ? 'Marked as sent' : 'Marked as failed');
      onDone();
    } catch (e) { setError(e); }
  };

  return (
    <Modal title={`Send ${channel} reminder`} onClose={onClose}>
      <ErrorBanner error={error} />
      {msg && msg.status === 'Skipped' && (
        <div className="grid">
          <div className="alert warn" style={{ margin: 0 }}>Not sent: {msg.reason}</div>
          <p className="small muted" style={{ margin: 0 }}>Record the customer's consent on their profile first. This attempt has been logged as skipped.</p>
          <div className="row end"><button onClick={onDone}>OK</button></div>
        </div>
      )}
      {msg && msg.status === 'Pending' && (
        <div className="grid">
          <div className="small muted">To {msg.recipient}</div>
          <textarea readOnly rows={4} value={msg.message} style={{ width: '100%' }} />
          <div className="row">
            {msg.clickToChatUrl
              ? <a href={msg.clickToChatUrl} target="_blank" rel="noopener noreferrer"><button className="primary">Open WhatsApp</button></a>
              : <button onClick={() => { navigator.clipboard?.writeText(msg.message); toast('Message copied'); }}>Copy text</button>}
            <span className="spacer" />
            <button className="danger" onClick={() => finish(false)}>Could not send</button>
            <button className="primary" onClick={() => finish(true)}>I've sent it</button>
          </div>
          <p className="small muted" style={{ margin: 0 }}>Messages go from your own phone, so there's no per-message charge. A WhatsApp Business API provider can be plugged in later (its fees would apply).</p>
        </div>
      )}
    </Modal>
  );
}
