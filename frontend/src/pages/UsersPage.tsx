import { useState } from 'react';
import { api, type Role } from '../api';
import { useAuth } from '../auth';
import { ErrorBanner, Field, Modal, PageHead, toast, useLoad } from '../ui';

const ROLE_HELP: Record<Role, string> = {
  Owner: 'Everything, including users and back-dated cancellations',
  Pharmacist: 'Billing, returns, cancellations, purchases, reports',
  Cashier: 'Billing only, with a discount limit',
  PurchaseClerk: 'Purchases, suppliers and products',
  Accountant: 'Books, statements and reports; no billing or stock',
  DeliveryAgent: 'Only their assigned online-order deliveries',
};

export default function UsersPage() {
  const { session } = useAuth();
  const { data, error, reload } = useLoad(() => api.users(), []);
  const [adding, setAdding] = useState(false);
  const branches = useLoad(() => api.branches(), []);
  const assign = async (id: string, branchId: string) => {
    if (!confirm('Move this user to the selected branch? They will need to sign in again.')) return;
    try { await api.assignUserBranch(id, branchId); toast('Branch updated'); reload(); } catch (e) { alert((e as Error).message); }
  };

  const toggle = async (id: string, active: boolean) => {
    try { await api.setUserActive(id, active); reload(); } catch (e) { alert((e as Error).message); }
  };

  return (
    <>
      <PageHead title="Users" sub="Staff who can sign in to this pharmacy.">
        <button className="primary" onClick={() => setAdding(true)}>+ Add user</button>
      </PageHead>
      <div className="panel">
        <ErrorBanner error={error} />
        <table>
          <thead><tr><th>Name</th><th>Email</th><th>Role</th><th>Branch</th><th>Status</th><th /></tr></thead>
          <tbody>
            {data?.map(u => (
              <tr key={u.id}>
                <td><b>{u.displayName}</b></td><td>{u.email}</td>
                <td>{u.role}<div className="small muted">{ROLE_HELP[u.role]}</div></td>
                <td>{u.role === 'Owner' || u.role === 'Accountant' ? <span className="small muted">all branches</span> : (
                  <select value={u.defaultBranchId} onChange={e => assign(u.id, e.target.value)} aria-label={`Branch for ${u.displayName}`}>
                    {branches.data?.map(b => <option key={b.id} value={b.id}>{b.name}</option>)}
                  </select>)}</td>
                <td>{u.isActive ? <span className="badge ok">active</span> : <span className="badge">inactive</span>}</td>
                <td>{u.id !== session?.userId && <div className="row" style={{ flexWrap: 'nowrap' }}>
                  <button className="small" onClick={() => toggle(u.id, !u.isActive)}>{u.isActive ? 'Deactivate' : 'Activate'}</button>
                  <button className="small" onClick={() => { const p = prompt(`Temporary password for ${u.displayName} (min 8). Share it privately; they should change it after signing in.`); if (p) api.resetUserPassword(u.id, p).then(() => toast('Password reset')).catch(e => alert((e as Error).message)); }}>Reset password</button>
                </div>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {adding && <Modal title="Add user" onClose={() => setAdding(false)}><UserForm onDone={() => { setAdding(false); reload(); }} /></Modal>}
    </>
  );
}

function UserForm({ onDone }: { onDone: () => void }) {
  const [f, setF] = useState({ displayName: '', email: '', password: '', role: 'Cashier' as Role });
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const submit = async (e: React.FormEvent) => {
    e.preventDefault(); setBusy(true); setError(null);
    try { await api.createUser(f); toast(`Added ${f.email}`); onDone(); } catch (err) { setError(err); } finally { setBusy(false); }
  };
  return (
    <form className="grid" onSubmit={submit}>
      <ErrorBanner error={error} />
      <Field label="Name"><input value={f.displayName} onChange={e => setF({ ...f, displayName: e.target.value })} required autoFocus /></Field>
      <Field label="Email"><input type="email" value={f.email} onChange={e => setF({ ...f, email: e.target.value })} required /></Field>
      <Field label="Temporary password (min 8)"><input type="password" value={f.password} minLength={8} onChange={e => setF({ ...f, password: e.target.value })} required /></Field>
      <Field label="Role">
        <select value={f.role} onChange={e => setF({ ...f, role: e.target.value as Role })}>
          {(Object.keys(ROLE_HELP) as Role[]).map(r => <option key={r} value={r}>{r} — {ROLE_HELP[r]}</option>)}
        </select>
      </Field>
      <div className="row end"><button className="primary" disabled={busy}>Add user</button></div>
    </form>
  );
}
