// Offline billing support: a local POS catalogue snapshot and a queue of bills made while offline.
// Bills carry their own clientRequestId, so syncing is safe to retry; the server re-checks stock and
// assigns the real invoice number. Anything the server refuses is kept in a visible "failed" list.
import { useCallback, useEffect, useRef, useState } from 'react';
import { api, ApiError, type PosProduct, type Sale } from './api';

const DB = 'pharmaos-offline';
const STORE = 'kv';

function open(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB, 1);
    req.onupgradeneeded = () => req.result.createObjectStore(STORE);
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
}
async function get<T>(key: string): Promise<T | undefined> {
  try {
    const db = await open();
    return await new Promise((resolve, reject) => {
      const r = db.transaction(STORE).objectStore(STORE).get(key);
      r.onsuccess = () => resolve(r.result as T | undefined);
      r.onerror = () => reject(r.error);
    });
  } catch { return undefined; }
}
async function set(key: string, value: unknown): Promise<void> {
  try {
    const db = await open();
    await new Promise<void>((resolve, reject) => {
      const tx = db.transaction(STORE, 'readwrite');
      tx.objectStore(STORE).put(value, key);
      tx.oncomplete = () => resolve();
      tx.onerror = () => reject(tx.error);
    });
  } catch { /* storage unavailable: offline mode simply won't persist */ }
}

export interface QueuedSale {
  request: Record<string, unknown> & { clientRequestId: string; capturedAt: string; offlineRef: string };
  display: { total: number; items: string[] };
  branchId: string;
  error?: string;
}

const catalogKey = (branchId: string) => `catalog:${branchId}`;

export async function refreshCatalog(branchId: string) {
  const items = await api.posCatalog();
  await set(catalogKey(branchId), { at: new Date().toISOString(), items });
  return items.length;
}

export async function catalogInfo(branchId: string) {
  return (await get<{ at: string; items: PosProduct[] }>(catalogKey(branchId))) ?? null;
}

/** Same matching idea as the server: barcode, name prefix, word, or composition. */
export async function searchCatalog(branchId: string, q: string): Promise<PosProduct[]> {
  const cat = await catalogInfo(branchId);
  if (!cat) return [];
  const s = q.trim().toLowerCase();
  return cat.items.filter(p => (p.barcode ?? '') === q.trim() || p.name.toLowerCase().startsWith(s) || p.name.toLowerCase().includes(' ' + s)
    || (p.composition ?? '').toLowerCase().includes(s)).slice(0, 20);
}

async function deviceRef(): Promise<string> {
  let id = await get<string>('device');
  if (!id) { id = 'D' + Math.random().toString(36).slice(2, 6).toUpperCase(); await set('device', id); }
  const seq = ((await get<number>('seq')) ?? 0) + 1;
  await set('seq', seq);
  return `${id}-${String(seq).padStart(4, '0')}`;
}

export async function enqueueSale(branchId: string, request: Record<string, unknown> & { clientRequestId: string }, display: QueuedSale['display']) {
  const offlineRef = await deviceRef();
  const queued: QueuedSale = { request: { ...request, capturedAt: new Date().toISOString(), offlineRef }, display, branchId };
  const q = (await get<QueuedSale[]>('queue')) ?? [];
  await set('queue', [...q, queued]);
  // keep the local snapshot honest: reduce what we just sold
  const cat = await catalogInfo(branchId);
  if (cat) {
    for (const l of request.lines as { productId: string; qtyUnits: number }[]) {
      const p = cat.items.find(x => x.productId === l.productId);
      if (p) p.availableUnits = Math.max(0, p.availableUnits - l.qtyUnits);
    }
    await set(catalogKey(branchId), cat);
  }
  return queued;
}

let syncing = false;
/** Push queued bills to the server, oldest first. Stops at the first network failure. */
export async function syncQueue(): Promise<{ synced: Sale[]; failed: number }> {
  if (syncing) return { synced: [], failed: 0 };
  syncing = true;
  const synced: Sale[] = [];
  try {
    let queue = (await get<QueuedSale[]>('queue')) ?? [];
    const failed = (await get<QueuedSale[]>('failed')) ?? [];
    while (queue.length) {
      const item = queue[0];
      try {
        synced.push(await api.createSale(item.request));
      } catch (e) {
        if (e instanceof ApiError && e.status === 0) break; // still offline
        failed.push({ ...item, error: e instanceof Error ? e.message : String(e) });
      }
      queue = queue.slice(1);
      await set('queue', queue);
      await set('failed', failed);
    }
    return { synced, failed: failed.length };
  } finally { syncing = false; }
}

export async function retryFailed(index: number) {
  const failed = (await get<QueuedSale[]>('failed')) ?? [];
  const [item] = failed.splice(index, 1);
  if (!item) return;
  await set('failed', failed);
  const q = (await get<QueuedSale[]>('queue')) ?? [];
  await set('queue', [...q, { ...item, error: undefined }]);
  return syncQueue();
}

export async function discardFailed(index: number) {
  const failed = (await get<QueuedSale[]>('failed')) ?? [];
  failed.splice(index, 1);
  await set('failed', failed);
}

/** Live offline status for the POS screen; syncs automatically when the connection returns. */
export function useOfflineStatus(branchId: string | undefined, onSynced?: (n: number) => void) {
  const [online, setOnline] = useState(navigator.onLine);
  const [pending, setPending] = useState(0);
  const [failed, setFailed] = useState<QueuedSale[]>([]);
  const [catalogAt, setCatalogAt] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    setPending(((await get<QueuedSale[]>('queue')) ?? []).length);
    setFailed((await get<QueuedSale[]>('failed')) ?? []);
    if (branchId) setCatalogAt((await catalogInfo(branchId))?.at ?? null);
  }, [branchId]);

  // keep the latest callback in a ref so a new inline function each render doesn't restart the effects below
  const onSyncedRef = useRef(onSynced);
  onSyncedRef.current = onSynced;
  const sync = useCallback(async () => {
    const r = await syncQueue();
    if (r.synced.length) onSyncedRef.current?.(r.synced.length);
    await refresh();
  }, [refresh]);

  useEffect(() => {
    const up = () => { setOnline(true); sync(); if (branchId) refreshCatalog(branchId).then(refresh).catch(() => {}); };
    const down = () => setOnline(false);
    window.addEventListener('online', up);
    window.addEventListener('offline', down);
    refresh();
    if (navigator.onLine && branchId) { sync(); refreshCatalog(branchId).then(refresh).catch(() => {}); }
    const t = setInterval(() => { if (navigator.onLine) sync(); }, 30000);
    return () => { window.removeEventListener('online', up); window.removeEventListener('offline', down); clearInterval(t); };
  }, [branchId, sync, refresh]);

  return { online, setOnline, pending, failed, catalogAt, refresh, sync };
}
