// Run against the local demo: node scripts/verify-recovery.mjs
// Creates a funded test account and verifies durable reservations/idempotency
// across a real matching-service restart. No credentials are written or logged.
import { execFileSync } from 'node:child_process';
import assert from 'node:assert/strict';
const base = process.env.BASE_URL ?? 'http://localhost:8080';
let token;
async function call(path, body, key) {
  const response = await fetch(base + '/api' + path, {
    method: body === undefined ? 'GET' : 'POST',
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: 'Bearer ' + token } : {}), ...(key ? { 'Idempotency-Key': key } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  assert.equal(response.status, 200, `${path}: ${await response.clone().text()}`);
  return response.json();
}
token = (await call('/auth/register', { email: `recovery-${Date.now()}@example.com`, password: 'RecoveryTest123!', country: 'GB' })).accessToken;
await call('/faucet', {}, crypto.randomUUID());
await call('/transfer', { asset: 'BTC', from: 'Funding', to: 'Unified Trading', amount: '2' }, crypto.randomUUID());
const key = crypto.randomUUID();
const request = { side: 'Sell', type: 'Limit', quantity: '1', price: '987654321.00' };
const order = await call('/orders', request, key);
assert.equal(order.status, 'Open');
const before = await call('/balances');
const btc = before.find(w => w.kind === 'Unified Trading' && w.asset === 'BTC');
assert.equal(btc.available, '1.00000000'); assert.equal(btc.locked, '1.00000000');
execFileSync('docker', ['compose', 'restart', 'matching'], { stdio: 'pipe' });
let healthy = false;
for (let attempt = 0; attempt < 40; attempt++) {
  try { if ((await fetch(base + '/api/health')).ok) { healthy = true; break; } } catch { /* startup */ }
  await new Promise(resolve => setTimeout(resolve, 500));
}
assert.ok(healthy, 'Matching service must become healthy after restart.');
const after = await call('/balances');
const sort = data => data.sort((a,b) => (a.kind+a.asset).localeCompare(b.kind+b.asset));
assert.deepEqual(sort(after), sort(before));
assert.equal((await call('/orders', request, key)).id, order.id);
assert.equal((await call('/orders')).filter(o => o.id === order.id).length, 1);
await call(`/orders/${order.id}/cancel`, {}, crypto.randomUUID());
const released = (await call('/balances')).find(w => w.kind === 'Unified Trading' && w.asset === 'BTC');
assert.equal(released.available, '2.00000000'); assert.equal(released.locked, '0.00000000');
console.log('PASS: matching-service restart preserves balances, reservations, order identity and idempotent receipts; cancellation releases funds.');
