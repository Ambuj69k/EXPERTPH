import { useEffect, useState } from 'react';
import { api, type LoyaltySettings, type Settings, type Template } from '../api';
import { ErrorBanner, Field, PageHead, toast, useLoad } from '../ui';
import { useAuth } from '../auth';

export default function SettingsPage() {
  const [s, setS] = useState<Settings | null>(null);
  const [error, setError] = useState<unknown>(null);
  useEffect(() => { api.settings().then(setS).catch(setError); }, []);
  const save = async () => {
    if (!s) return;
    setError(null);
    try { setS(await api.updateSettings(s)); toast('Settings saved'); } catch (e) { setError(e); }
  };
  return (
    <>
      <PageHead title="Settings" sub="Counter controls for this pharmacy." />
      <ErrorBanner error={error} />
      {s && (
        <div className="panel grid" style={{ maxWidth: 520 }}>
          <Field label="Maximum discount a cashier can give (%)">
            <input type="number" min={0} max={100} value={s.cashierMaxDiscountPercent} onChange={e => setS({ ...s, cashierMaxDiscountPercent: Number(e.target.value) })} />
          </Field>
          <label className="check"><input type="checkbox" checked={s.requireOpenShift} onChange={e => setS({ ...s, requireOpenShift: e.target.checked })} /> Require an open cash shift before billing</label>
          <p className="small muted" style={{ margin: 0 }}>With shifts required, every bill is tied to a drawer session so cash can be reconciled per person.</p>
          <div className="row end"><button className="primary" onClick={save}>Save</button></div>
        </div>
      )}
      <AccountPanel />
      <OnlinePanel />
      <LoyaltySettingsPanel />
      <TemplatesPanel />
    </>
  );
}

function OnlinePanel() {
  const { session } = useAuth();
  const sub = useLoad(() => api.subscription(), []);
  const branches = useLoad(() => api.branches(), []);
  const b = branches.data?.find(x => x.id === session?.branchId);
  const [f, setF] = useState<{ enabled: boolean; deliveryPincodes: string; notice: string; deliveryCharge: string; freeDeliveryAbove: string; deliveryChargeGstRate: string } | null>(null);
  const [err, setErr] = useState<unknown>(null);
  useEffect(() => {
    if (b) setF({
      enabled: !!b.onlineOrdersEnabled, deliveryPincodes: b.deliveryPincodes ?? '', notice: b.onlineNotice ?? '',
      deliveryCharge: String(b.deliveryCharge ?? 0), freeDeliveryAbove: b.freeDeliveryAbove == null ? '' : String(b.freeDeliveryAbove),
      deliveryChargeGstRate: String(b.deliveryChargeGstRate ?? 18),
    });
  }, [b]);
  if (!sub.data?.current.modules.includes('online') || !f || !session) return null;
  const save = async () => {
    setErr(null);
    try {
      await api.saveOnlineSettings(session.branchId, {
        enabled: f.enabled, deliveryPincodes: f.deliveryPincodes, notice: f.notice, deliveryCharge: Number(f.deliveryCharge) || 0,
        freeDeliveryAbove: f.freeDeliveryAbove ? Number(f.freeDeliveryAbove) : null, deliveryChargeGstRate: Number(f.deliveryChargeGstRate),
      });
      toast('Online ordering settings saved'); branches.reload();
    } catch (e) { setErr(e); }
  };
  const link = `${window.location.origin}/shop/${session.branchId}`;
  return (
    <div className="panel grid" style={{ maxWidth: 720, marginTop: 16 }}>
      <h2 style={{ margin: 0 }}>Online ordering — {session.branchName}</h2>
      <ErrorBanner error={err} />
      <div className="alert warn" style={{ margin: 0 }}>Confirm that your licences and current rules allow you to accept orders online before turning this on. Only non-prescription products you mark "online" are listed; prescription items come only through pharmacist-reviewed requests.</div>
      <label className="check"><input type="checkbox" checked={f.enabled} onChange={e => setF({ ...f, enabled: e.target.checked })} /> Accept online orders at this branch</label>
      <Field label="Delivery pincodes (comma separated; empty = pickup only)"><input value={f.deliveryPincodes} onChange={e => setF({ ...f, deliveryPincodes: e.target.value })} /></Field>
      <div className="grid c3">
        <Field label="Delivery charge (₹, incl. GST)"><input type="number" min={0} value={f.deliveryCharge} onChange={e => setF({ ...f, deliveryCharge: e.target.value })} /></Field>
        <Field label="Free delivery above (₹, blank = never)"><input type="number" min={0} value={f.freeDeliveryAbove} onChange={e => setF({ ...f, freeDeliveryAbove: e.target.value })} /></Field>
        <Field label="GST on delivery %"><select value={f.deliveryChargeGstRate} onChange={e => setF({ ...f, deliveryChargeGstRate: e.target.value })}>{['0', '5', '12', '18', '28'].map(r => <option key={r}>{r}</option>)}</select></Field>
      </div>
      <p className="small muted" style={{ margin: 0 }}>Ask your CA which GST rate applies to delivery charges (it may need to follow the medicines as one composite supply).</p>
      <Field label="Notice shown to customers"><input value={f.notice} onChange={e => setF({ ...f, notice: e.target.value })} /></Field>
      {f.enabled && <div className="small">Ordering page: <a href={link} target="_blank" rel="noopener noreferrer">{link}</a></div>}
      <div className="row end"><button className="primary" onClick={save}>Save</button></div>
    </div>
  );
}

function AccountPanel() {
  const { data, error, reload } = useLoad(() => api.subscription(), []);
  const [hours, setHours] = useState('24');
  const [err, setErr] = useState<unknown>(null);
  const grant = async (h: number) => {
    setErr(null);
    try { await api.setSupportAccess(h); toast(h ? `Support can view your account (read-only) for ${h} h` : 'Support access revoked'); reload(); } catch (e) { setErr(e); }
  };
  if (!data) return <ErrorBanner error={error} />;
  const s = data.current;
  const supportOpen = s.supportAccessUntil && new Date(s.supportAccessUntil) > new Date();
  return (
    <div className="panel grid" style={{ maxWidth: 720, marginTop: 16 }}>
      <h2 style={{ margin: 0 }}>Plan & account</h2>
      <ErrorBanner error={err} />
      <div className="row">
        <b>{s.planName}</b><span className={`badge ${s.status === 'Active' ? 'ok' : s.status === 'Trial' ? 'brand' : 'danger'}`}>{s.status}</span>
        {s.readOnly && <span className="badge danger">read-only</span>}
        <span className="small muted">Branches {s.branches}/{s.maxBranches ?? '∞'} · Users {s.users}/{s.maxUsers ?? '∞'} · Modules: {s.modules.join(', ')}</span>
      </div>
      {s.notice && <div className="alert warn" style={{ margin: 0 }}>{s.notice}</div>}
      <div className="row">
        <span>Support access: <b>{supportOpen ? `until ${new Date(s.supportAccessUntil!).toLocaleString('en-IN')}` : 'off'}</b></span>
        <span className="spacer" />
        {supportOpen ? <button onClick={() => grant(0)}>Revoke</button> : <>
          <select value={hours} onChange={e => setHours(e.target.value)}>{['2', '24', '72'].map(h => <option key={h} value={h}>{h} hours</option>)}</select>
          <button onClick={() => grant(Number(hours))}>Allow support to view</button>
        </>}
      </div>
      <p className="small muted" style={{ margin: 0 }}>Support can only look (no changes) during this window, and every screen they open is recorded in your audit log.</p>
      <div className="row">
        <button onClick={() => api.downloadExport().catch(setErr)}>Download all my data (ZIP)</button>
        <span className="small muted">All records as CSV files — yours to keep, any time, even if you leave.</span>
      </div>
    </div>
  );
}

function LoyaltySettingsPanel() {
  const [s, setS] = useState<LoyaltySettings | null>(null);
  const [error, setError] = useState<unknown>(null);
  useEffect(() => { api.loyaltySettings().then(setS).catch(setError); }, []);
  const save = async () => {
    if (!s) return;
    setError(null);
    try { setS(await api.saveLoyaltySettings(s)); toast('Loyalty settings saved'); } catch (e) { setError(e); }
  };
  if (!s) return <ErrorBanner error={error} />;
  return (
    <div className="panel grid" style={{ maxWidth: 520, marginTop: 16 }}>
      <h2 style={{ margin: 0 }}>Loyalty points</h2>
      <ErrorBanner error={error} />
      <label className="check"><input type="checkbox" checked={s.enabled} onChange={e => setS({ ...s, enabled: e.target.checked })} /> Enable loyalty points</label>
      <div className="grid c3">
        <Field label="Spend per point (₹)"><input type="number" min={1} value={s.rupeesPerPoint} onChange={e => setS({ ...s, rupeesPerPoint: Number(e.target.value) })} /></Field>
        <Field label="Value of 1 point (₹)"><input type="number" min={0.01} step={0.01} value={s.pointValue} onChange={e => setS({ ...s, pointValue: Number(e.target.value) })} /></Field>
        <Field label="Min points to redeem"><input type="number" min={0} value={s.minRedeemPoints} onChange={e => setS({ ...s, minRedeemPoints: Number(e.target.value) })} /></Field>
      </div>
      <p className="small muted" style={{ margin: 0 }}>
        Effective reward = {s.rupeesPerPoint > 0 ? ((s.pointValue / s.rupeesPerPoint) * 100).toFixed(2) : 0}% of spend. Points are earned on the amount not paid with points,
        taken back on returns/cancellations. Check any rules on discounts/inducements for medicines that apply to you before enabling.
      </p>
      <div className="row end"><button className="primary" onClick={save}>Save</button></div>
    </div>
  );
}

function TemplatesPanel() {
  const { data, error, reload } = useLoad(() => api.templates(), []);
  const [edit, setEdit] = useState<Record<string, string>>({});
  const [err, setErr] = useState<unknown>(null);
  const save = async (t: Template) => {
    setErr(null);
    try { await api.saveTemplate({ key: t.key, channel: t.channel, body: edit[t.key + t.channel] ?? t.body, isActive: true }); toast('Template saved'); reload(); }
    catch (e) { setErr(e); }
  };
  return (
    <div className="panel grid" style={{ maxWidth: 720, marginTop: 16 }}>
      <h2 style={{ margin: 0 }}>Message templates</h2>
      <ErrorBanner error={error ?? err} />
      <p className="small muted" style={{ margin: 0 }}>Placeholders: {'{{customerName}} {{productName}} {{dueDate}} {{businessName}}'}. Never include diagnoses or medical details.</p>
      {data?.map(t => (
        <div key={t.key + t.channel} className="grid">
          <div className="small"><b>{t.key}</b> · {t.channel} {t.isDefault && <span className="badge">default</span>}</div>
          <textarea rows={3} value={edit[t.key + t.channel] ?? t.body} onChange={e => setEdit({ ...edit, [t.key + t.channel]: e.target.value })} />
          <div className="row end"><button className="small" onClick={() => save(t)}>Save</button></div>
        </div>
      ))}
    </div>
  );
}
