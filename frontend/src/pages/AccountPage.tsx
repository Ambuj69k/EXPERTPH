import { useState } from 'react';
import { api } from '../api';
import { useAuth } from '../auth';
import { ErrorBanner, Field, PageHead, toast, useLoad } from '../ui';

/** Any signed-in staff member: change password and manage two-step sign-in. */
export default function AccountPage() {
  const { session } = useAuth();
  return (
    <>
      <PageHead title="My account" sub={`${session?.displayName} · ${session?.email} · ${session?.role}`} />
      <div className="grid c2" style={{ alignItems: 'start' }}>
        <MfaPanel load={api.mfaStatus} setup={api.mfaSetup} enable={api.mfaEnable} disable={api.mfaDisable} />
        <PasswordPanel />
      </div>
    </>
  );
}

export function MfaPanel({ load, setup, enable, disable }: {
  load: () => Promise<{ enabled: boolean }>; setup: () => Promise<{ secret: string; otpAuthUri: string }>;
  enable: (code: string) => Promise<void>; disable?: (password: string) => Promise<void>;
}) {
  const status = useLoad(load, []);
  const [pending, setPending] = useState<{ secret: string; otpAuthUri: string } | null>(null);
  const [code, setCode] = useState('');
  const [error, setError] = useState<unknown>(null);
  const run = async (fn: () => Promise<unknown>, msg: string) => { setError(null); try { await fn(); toast(msg); status.reload(); } catch (e) { setError(e); } };

  return (
    <div className="panel grid">
      <h2 style={{ margin: 0 }}>Two-step sign-in</h2>
      <ErrorBanner error={error ?? status.error} />
      {status.data?.enabled ? <>
        <div className="alert ok" style={{ margin: 0 }}>On — sign-in asks for a code from your authenticator app.</div>
        {disable && <button onClick={() => { const p = prompt('Enter your password to turn two-step sign-in off'); if (p) run(() => disable(p), 'Two-step sign-in turned off'); }}>Turn off</button>}
      </> : pending ? <>
        <p className="small" style={{ margin: 0 }}>1. In Google Authenticator, Microsoft Authenticator or similar, add an account using this key:</p>
        <code style={{ fontSize: 16, wordBreak: 'break-all', background: '#f2f5f7', padding: 8, borderRadius: 6 }}>{pending.secret.match(/.{1,4}/g)?.join(' ')}</code>
        <p className="small muted" style={{ margin: 0 }}>On a phone you can also open: <a href={pending.otpAuthUri}>authenticator link</a></p>
        <Field label="2. Enter the 6-digit code it shows"><input value={code} inputMode="numeric" onChange={e => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))} /></Field>
        <button className="primary" disabled={code.length !== 6} onClick={() => run(async () => { await enable(code); setPending(null); setCode(''); }, 'Two-step sign-in is on')}>Turn on</button>
      </> : <>
        <p style={{ margin: 0 }}>Protect your account with a code from your phone in addition to your password. Strongly recommended for owners and accountants.</p>
        <button className="primary" onClick={async () => { setError(null); try { setPending(await setup()); } catch (e) { setError(e); } }}>Set up</button>
      </>}
    </div>
  );
}

function PasswordPanel() {
  const [f, setF] = useState({ current: '', next: '', confirm: '' });
  const [error, setError] = useState<unknown>(null);
  const save = async () => {
    setError(null);
    if (f.next !== f.confirm) { setError(new Error('New passwords do not match.')); return; }
    try { await api.changePassword(f.current, f.next); setF({ current: '', next: '', confirm: '' }); toast('Password changed'); } catch (e) { setError(e); }
  };
  return (
    <div className="panel grid">
      <h2 style={{ margin: 0 }}>Change password</h2>
      <ErrorBanner error={error} />
      <Field label="Current password"><input type="password" autoComplete="current-password" value={f.current} onChange={e => setF({ ...f, current: e.target.value })} /></Field>
      <Field label="New password (min 8)"><input type="password" autoComplete="new-password" value={f.next} onChange={e => setF({ ...f, next: e.target.value })} /></Field>
      <Field label="Repeat new password"><input type="password" autoComplete="new-password" value={f.confirm} onChange={e => setF({ ...f, confirm: e.target.value })} /></Field>
      <button className="primary" disabled={!f.current || f.next.length < 8} onClick={save}>Change password</button>
    </div>
  );
}
