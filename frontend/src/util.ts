const inr = new Intl.NumberFormat('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export const money = (n: number | null | undefined) => `₹${inr.format(n ?? 0)}`;
export const num = (n: number | null | undefined) => inr.format(n ?? 0);

/** "2027-03-31" → "03/27" (how expiry is printed on packs). */
export const expiry = (iso: string) => { const [y, m] = iso.split('-'); return `${m}/${y.slice(2)}`; };

export const dateTime = (iso: string) =>
  new Date(iso).toLocaleString('en-IN', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' });

/** Today's date in the browser's local time zone, as yyyy-MM-dd. */
export const today = () => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
};

/** 25 base units of a 10-pack → "2 Strip + 5 Tab". */
export const packs = (units: number, packSize: number, packUnit: string, baseUnit: string) => {
  if (packSize <= 1) return `${units} ${baseUnit}`;
  const full = Math.floor(units / packSize);
  const loose = units - full * packSize;
  return [full ? `${full} ${packUnit}` : '', loose ? `${loose} ${baseUnit}` : ''].filter(Boolean).join(' + ') || `0 ${baseUnit}`;
};

/** Parse "MM/YY" or "MM/YYYY" to an ISO date (first of month; server normalises to month end). */
export const parseExpiry = (s: string): string | null => {
  const m = /^\s*(\d{1,2})\s*[/-]\s*(\d{2}|\d{4})\s*$/.exec(s);
  if (!m) return null;
  const month = Number(m[1]);
  if (month < 1 || month > 12) return null;
  const year = m[2].length === 2 ? 2000 + Number(m[2]) : Number(m[2]);
  return `${year}-${String(month).padStart(2, '0')}-01`;
};

export const round2 = (n: number) => Math.round((n + Number.EPSILON) * 100) / 100;
const round4 = (n: number) => Math.round((n + Number.EPSILON) * 10000) / 10000;

/** Same rule as the server (Pricing.Line): gross at MRP, base = branch price if below MRP, extra discount % on the base. */
export const priceLine = (qtyUnits: number, mrpPerPack: number, packSize: number, branchPricePerPack: number | null | undefined, extraDiscountPercent: number) => {
  const gross = round2(qtyUnits * round4(mrpPerPack / packSize));
  const base = branchPricePerPack != null && branchPricePerPack < mrpPerPack ? round2(qtyUnits * round4(branchPricePerPack / packSize)) : gross;
  const net = round2(base - round2(base * extraDiscountPercent / 100));
  return { gross, net, discount: round2(gross - net) };
};

export const newId = () => crypto.randomUUID();
