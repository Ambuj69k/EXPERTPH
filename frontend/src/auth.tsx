import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react';
import { api, setUnauthorizedHandler, tokenStore, type AuthResponse, type Role, type Session } from './api';

interface AuthState {
  session: Session | null;
  loading: boolean;
  signIn: (r: AuthResponse) => void;
  signOut: () => void;
  can: (...roles: Role[]) => boolean;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(null);
  const [loading, setLoading] = useState(true);

  const signOut = useCallback(() => { tokenStore.set(null); setSession(null); }, []);
  const signIn = useCallback((r: AuthResponse) => { tokenStore.set(r.token); setSession(r.session); }, []);

  useEffect(() => {
    setUnauthorizedHandler(signOut);
    if (!tokenStore.get()) { setLoading(false); return; }
    api.me().then(setSession).catch(signOut).finally(() => setLoading(false));
  }, [signOut]);

  const can = useCallback((...roles: Role[]) => !!session && roles.includes(session.role), [session]);

  return <AuthContext.Provider value={{ session, loading, signIn, signOut, can }}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth outside AuthProvider');
  return ctx;
}

/** Mirrors server policies (server remains the source of truth). */
export const RoleSets = {
  sell: ['Owner', 'Pharmacist', 'Cashier'] as Role[],
  salesAdmin: ['Owner', 'Pharmacist'] as Role[],
  purchase: ['Owner', 'Pharmacist', 'PurchaseClerk'] as Role[],
  masterWrite: ['Owner', 'Pharmacist', 'PurchaseClerk'] as Role[],
  reports: ['Owner', 'Pharmacist', 'Accountant'] as Role[],
  accounting: ['Owner', 'Accountant'] as Role[],
  admin: ['Owner'] as Role[],
  delivery: ['Owner', 'Pharmacist', 'DeliveryAgent'] as Role[],
};
