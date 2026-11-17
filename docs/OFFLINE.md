# Offline billing — architecture decision

**Decision:** browser-based offline mode, no local server agent at the store.

| Option | Why not / why |
|---|---|
| Local server + DB at each store, two-way sync | Strong offline, but installation, upgrades, backups and conflict resolution at every store are a large support burden for a small team. Deferred. |
| **Browser offline (chosen)** | Service worker keeps the app shell; IndexedDB keeps a POS catalogue snapshot and an outbox of bills. Nothing to install. |

## What works offline
- Opening the billing screen (after it has been loaded once online in a production build).
- Searching products from the last catalogue snapshot (refreshed on POS open and when the connection returns).
- Cash / UPI / card bills. **Credit and loyalty payments are blocked offline** because balances must be checked on the server.

## Numbering
- Offline bills get a provisional reference `<device>-<seq>` (e.g. `DK3F-0007`) printed on a **provisional receipt**.
- The real invoice number is assigned by the server when the bill syncs (so invoice numbers stay gap-free and unique per branch).
- The server stores `CapturedOffline = true`, the provisional reference, and the original capture time as the invoice time.
- **Needs CA review:** whether a provisional receipt is acceptable until the tax invoice is issued, and how to handle bills that never sync.

## Sync and conflicts
- Each bill carries its `clientRequestId`; sync is retried safely (the server returns the existing invoice for a repeated id).
- Bills must sync within **72 hours** of capture; the server rejects older or future-dated captures.
- Stock is re-checked at sync. If it was sold elsewhere meanwhile (or the batch was blocked), the bill moves to a visible **"failed to sync"** list on the POS; staff retry after fixing stock or discard and re-bill/refund the customer.
- Multiple devices offline at once can oversell the same stock; the first to sync wins, later ones fail visibly. Nothing is silently dropped.

## Not covered
- Purchases, returns, stock counts and other back-office screens require a connection.
- Testing note: the offline flow has build/type checks and server-side tests for late sync; it has **not** yet been exercised in a real browser with the network switched off.
