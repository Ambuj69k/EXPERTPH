import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { ApiError } from './api';

export function ErrorBanner({ error }: { error: unknown }) {
  if (!error) return null;
  const msg = error instanceof ApiError || error instanceof Error ? error.message : String(error);
  return <div className="alert error" role="alert">{msg}</div>;
}

export function Modal({ title, onClose, children, wide }: { title: string; onClose: () => void; children: ReactNode; wide?: boolean }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);
  return (
    <div className="backdrop" onMouseDown={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className={`modal${wide ? ' wide' : ''}`} role="dialog" aria-modal="true" aria-label={title}>
        <div className="row no-print" style={{ marginBottom: 12 }}>
          <h2 style={{ margin: 0 }}>{title}</h2>
          <span className="spacer" />
          <button className="link" onClick={onClose} aria-label="Close">✕</button>
        </div>
        {children}
      </div>
    </div>
  );
}

export function Field({ label, children }: { label: string; children: ReactNode }) {
  return <label className="field"><span>{label}</span>{children}</label>;
}

export function PageHead({ title, sub, children }: { title: string; sub?: string; children?: ReactNode }) {
  return (
    <div className="page-head">
      <div><h1>{title}</h1>{sub && <p>{sub}</p>}</div>
      <div className="row">{children}</div>
    </div>
  );
}

export function Pager({ page, total, pageSize, onPage }: { page: number; total: number; pageSize: number; onPage: (p: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  if (pages <= 1) return <div className="small muted" style={{ marginTop: 8 }}>{total} record(s)</div>;
  return (
    <div className="row small" style={{ marginTop: 8 }}>
      <span className="muted">{total} records</span><span className="spacer" />
      <button className="small" disabled={page <= 1} onClick={() => onPage(page - 1)}>Prev</button>
      <span>Page {page} / {pages}</span>
      <button className="small" disabled={page >= pages} onClick={() => onPage(page + 1)}>Next</button>
    </div>
  );
}

/** Load data with loading/error state; `reload` re-runs the loader. */
export function useLoad<T>(loader: () => Promise<T>, deps: unknown[]) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [loading, setLoading] = useState(true);
  const seq = useRef(0);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const run = useCallback(loader, deps);
  const reload = useCallback(() => {
    const my = ++seq.current;
    setLoading(true);
    run().then(d => { if (my === seq.current) { setData(d); setError(null); } })
      .catch(e => { if (my === seq.current) setError(e); })
      .finally(() => { if (my === seq.current) setLoading(false); });
  }, [run]);
  useEffect(reload, [reload]);
  return { data, error, loading, reload, setData };
}

export function useDebounced<T>(value: T, ms = 250) {
  const [v, setV] = useState(value);
  useEffect(() => { const t = setTimeout(() => setV(value), ms); return () => clearTimeout(t); }, [value, ms]);
  return v;
}

let toastSetter: ((m: string | null) => void) | null = null;
export const toast = (m: string) => toastSetter?.(m);
export function ToastHost() {
  const [msg, setMsg] = useState<string | null>(null);
  useEffect(() => { toastSetter = setMsg; return () => { toastSetter = null; }; }, []);
  useEffect(() => { if (!msg) return; const t = setTimeout(() => setMsg(null), 3000); return () => clearTimeout(t); }, [msg]);
  return msg ? <div className="toast" role="status">{msg}</div> : null;
}

export function StatusBadge({ status }: { status: string }) {
  const cls = status === 'Posted' ? 'ok' : status === 'Cancelled' ? 'danger' : status === 'Draft' ? 'warn' : '';
  return <span className={`badge ${cls}`}>{status}</span>;
}
