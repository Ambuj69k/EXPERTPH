import { useEffect, useRef, useState } from 'react';
import { api, type Product } from '../api';
import { useDebounced } from '../ui';

/** Type-ahead product selector. Shows "+ Create" when nothing matches and onCreate is provided. */
export default function ProductPicker({ value, onPick, onCreate, autoFocus, disabled }: {
  value: { id: string; name: string } | null;
  onPick: (p: Product) => void;
  onCreate?: (name: string) => void;
  autoFocus?: boolean;
  disabled?: boolean;
}) {
  const [text, setText] = useState(value?.name ?? '');
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState<Product[]>([]);
  const [active, setActive] = useState(0);
  const dq = useDebounced(text, 200);
  const ref = useRef<HTMLInputElement>(null);

  useEffect(() => { setText(value?.name ?? ''); }, [value?.id, value?.name]);
  useEffect(() => {
    if (!open || !dq.trim() || dq === value?.name) { setItems([]); return; }
    let live = true;
    api.products(dq).then(r => { if (live) { setItems(r.items.slice(0, 12)); setActive(0); } }).catch(() => {});
    return () => { live = false; };
  }, [dq, open, value?.name]);

  const pick = (p: Product) => { onPick(p); setText(p.name); setOpen(false); };
  const showCreate = !!onCreate && dq.trim().length > 1 && items.length === 0 && dq !== value?.name;

  return (
    <div style={{ position: 'relative', minWidth: 200 }}>
      <input ref={ref} value={text} disabled={disabled} autoFocus={autoFocus} placeholder="Search product" style={{ width: '100%' }}
        onChange={e => { setText(e.target.value); setOpen(true); }}
        onFocus={() => setOpen(true)} onBlur={() => setTimeout(() => setOpen(false), 150)}
        onKeyDown={e => {
          if (e.key === 'ArrowDown') { e.preventDefault(); setActive(a => Math.min(a + 1, items.length - 1)); }
          else if (e.key === 'ArrowUp') { e.preventDefault(); setActive(a => Math.max(0, a - 1)); }
          else if (e.key === 'Enter' && open && items[active]) { e.preventDefault(); pick(items[active]); }
          else if (e.key === 'Enter' && showCreate) { e.preventDefault(); onCreate!(dq.trim()); setOpen(false); }
        }} aria-label="Product" />
      {open && (items.length > 0 || showCreate) && (
        <div className="results" style={{ minWidth: 320 }}>
          {items.map((p, i) => (
            <div key={p.id} className={`result${i === active ? ' active' : ''}`} onMouseDown={e => { e.preventDefault(); pick(p); }}>
              <div><b>{p.name}</b><div className="meta">{p.composition} · {p.manufacturer}</div></div>
              <div className="meta">{p.packSize} {p.baseUnit}/{p.packUnit}</div>
            </div>
          ))}
          {showCreate && (
            <div className="result active" onMouseDown={e => { e.preventDefault(); onCreate!(dq.trim()); setOpen(false); }}>
              + Create product “{dq.trim()}”
            </div>
          )}
        </div>
      )}
    </div>
  );
}
