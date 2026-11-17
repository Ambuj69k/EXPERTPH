import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { api, ApiError, isMfaPending } from '../api';
import { useAuth } from '../auth';
import { ErrorBanner, Field } from '../ui';

export function LoginPage() {
  const { signIn } = useAuth();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const [mfaToken, setMfaToken] = useState<string | null>(null);
  const [code, setCode] = useState('');

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true); setError(null);
    try {
      if (mfaToken) { signIn(await api.loginMfa(mfaToken, code)); return; }
      const r = await api.login(email, password);
      if (isMfaPending(r)) setMfaToken(r.mfaToken); else signIn(r);
    } catch (err) {
      setError(err);
      if (err instanceof ApiError && err.code === 'mfa_expired') { setMfaToken(null); setCode(''); }
    } finally { setBusy(false); }
  };

  return (
    <div className="auth">
      <form className="panel grid" onSubmit={submit}>
        <div><h1>PharmaOS</h1><p className="muted" style={{ margin: 0 }}>Sign in to your pharmacy</p></div>
        <ErrorBanner error={error} />
        {mfaToken ? (
          <Field label="6-digit code from your authenticator app">
            <input value={code} onChange={e => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))} inputMode="numeric" autoComplete="one-time-code" autoFocus required />
          </Field>
        ) : <>
          <Field label="Email"><input type="email" autoComplete="username" value={email} onChange={e => setEmail(e.target.value)} required autoFocus /></Field>
          <Field label="Password"><input type="password" autoComplete="current-password" value={password} onChange={e => setPassword(e.target.value)} required /></Field>
        </>}
        <button className="primary" disabled={busy || (!!mfaToken && code.length !== 6)}>{busy ? 'Signing in…' : mfaToken ? 'Verify' : 'Sign in'}</button>
        <p className="small muted" style={{ margin: 0 }}>New pharmacy? <Link to="/register">Create an account</Link></p>
      </form>
    </div>
  );
}

export function RegisterPage() {
  const { signIn } = useAuth();
  const [f, setF] = useState({
    businessName: '', branchName: 'Main Store', branchCode: 'MAIN', stateCode: '', gstin: '', drugLicenseNo: '',
    ownerName: '', email: '', password: '',
  });
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement>) => setF({ ...f, [k]: e.target.value });

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true); setError(null);
    try {
      signIn(await api.register({ ...f, gstin: f.gstin || null, drugLicenseNo: f.drugLicenseNo || null }));
    } catch (err) { setError(err); } finally { setBusy(false); }
  };

  return (
    <div className="auth">
      <form className="panel grid" onSubmit={submit}>
        <div><h1>Set up your pharmacy</h1><p className="muted" style={{ margin: 0 }}>You can add more users after setup.</p></div>
        <ErrorBanner error={error} />
        <Field label="Business name"><input value={f.businessName} onChange={set('businessName')} required autoFocus /></Field>
        <div className="grid c3">
          <Field label="Store name"><input value={f.branchName} onChange={set('branchName')} required /></Field>
          <Field label="Store code (≤4)"><input value={f.branchCode} onChange={set('branchCode')} maxLength={4} required /></Field>
          <Field label="GST state code"><input value={f.stateCode} onChange={set('stateCode')} placeholder="27" maxLength={2} required /></Field>
        </div>
        <div className="grid c2">
          <Field label="GSTIN (optional)"><input value={f.gstin} onChange={set('gstin')} maxLength={15} /></Field>
          <Field label="Drug licence no. (optional)"><input value={f.drugLicenseNo} onChange={set('drugLicenseNo')} /></Field>
        </div>
        <Field label="Your name"><input value={f.ownerName} onChange={set('ownerName')} required /></Field>
        <div className="grid c2">
          <Field label="Email"><input type="email" value={f.email} onChange={set('email')} required /></Field>
          <Field label="Password (min 8)"><input type="password" value={f.password} onChange={set('password')} minLength={8} required /></Field>
        </div>
        <p className="small muted" style={{ margin: 0 }}>
          Licence and GST details are stored as records only; PharmaOS does not verify them with authorities.
        </p>
        <button className="primary" disabled={busy}>{busy ? 'Creating…' : 'Create account'}</button>
        <p className="small muted" style={{ margin: 0 }}>Already registered? <Link to="/">Sign in</Link></p>
      </form>
    </div>
  );
}
