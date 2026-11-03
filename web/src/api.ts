import Decimal from 'decimal.js';
Decimal.set({ precision: 60, rounding: Decimal.ROUND_DOWN });
export const D = (value: Decimal.Value = '0') => new Decimal(value);
export const fmt = (value: string | null | undefined, places = 2) => {
  if (value == null) return '—';
  const [whole, fraction] = D(value).toFixed(places).split('.');
  return whole.replace(/\B(?=(\d{3})+(?!\d))/g, ',') + (fraction == null ? '' : '.' + fraction);
};
export type User = { id: string; email: string; country: string };
export type Wallet = { kind: string; asset: string; available: string; locked: string };
export type Order = { instrumentId: string; id: string; side: string; type: string; price: string | null; quantity: string; remaining: string; reserved: string; status: string; createdAt: string; cancelReason: string | null };
export type Trade = { instrumentId: string; id: string; price: string; quantity: string; takerSide: string; createdAt: string; buyerId?: string; sellerId?: string };
export type Level = { price: string; quantity: string; count: string };
export type Market = { bids: Level[]; asks: Level[]; orders: (Order & { sequence: string })[]; trades: Trade[]; lastPrice: string | null; high: string | null; low: string | null; volume: string; change: string | null };
export type Candle = { time: string; open: string; high: string; low: string; close: string; volume: string };
let token: string | null = null;
let current: User | null = null;
const listeners = new Set<() => void>();
let refreshPromise: Promise<User | null> | null = null;
export function subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; }
export function currentUser() { return current; }
function save(data: { accessToken: string; user: User } | null) {
  token = data?.accessToken ?? null; current = data?.user ?? null; listeners.forEach(f => f());
}
export async function refresh(): Promise<User | null> {
  if (refreshPromise) return refreshPromise;
  refreshPromise = (async () => {
    const r = await fetch('/api/auth/refresh', { method: 'POST', credentials: 'same-origin' });
    if (r.ok) save(await r.json()); else if (r.status === 401) save(null); else throw new Error('Unable to restore your session. Please retry.');
    return current;
  })().finally(() => { refreshPromise = null; });
  return refreshPromise;
}
export class ApiError extends Error { constructor(message: string, public status: number) { super(message); } }
export async function api<T>(path: string, body?: unknown, key?: string, retry = true): Promise<T> {
  const r = await fetch('/api' + path, { method: body === undefined ? 'GET' : 'POST', credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: 'Bearer ' + token } : {}), ...(key ? { 'Idempotency-Key': key } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body) });
  if (r.status === 401 && retry && !path.startsWith('/auth/')) { if (await refresh()) return api(path, body, key, false); }
  if (!r.ok) {
    const data = await r.json().catch(() => ({}));
    throw new ApiError(data.error ?? (r.status === 429 ? 'Too many attempts. Please wait a minute.' : r.status === 401 ? 'Please sign in to continue.' : 'Request failed. Please retry.'), r.status);
  }
  return r.json();
}
export async function authenticate(mode: string, body: unknown) { save(await api('/auth/' + mode, body)); }
export async function logout() { await api('/auth/logout', {}); save(null); }
