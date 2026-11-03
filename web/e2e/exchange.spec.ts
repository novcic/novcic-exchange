import { test, expect, type Page } from '@playwright/test';
import Decimal from 'decimal.js';

const password = 'ExchangeTest123!';
async function register(page: Page, email: string) {
  await page.goto('/register');
  await page.getByLabel('Email address').fill(email);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Country of residence' }).click();
  await page.getByRole('textbox', { name: 'Search countries' }).fill('United Kingdom');
  await page.getByRole('option', { name: 'United Kingdom GB' }).click();
  await page.getByRole('button', { name: /^Create account/ }).click();
  await expect(page.getByRole('heading', { name: 'Your assets' })).toBeVisible();
}
async function credit(page: Page) {
  await page.getByRole('button', { name: /^Add test funds/ }).click();
  await expect(page.getByRole('status')).toContainText('100 BTC');
}
async function transfer(page: Page, asset: string, amount: string) {
  await page.getByLabel('Asset', { exact: true }).selectOption(asset);
  await page.getByRole('textbox', { name: 'Transfer amount' }).fill(amount);
  await page.getByRole('button', { name: 'Transfer funds', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('Transfer completed.');
  await expect(page.getByRole('textbox', { name: 'Transfer amount' })).toHaveValue('');
}
async function place(page: Page, side: string, type: string, quantity: string, price?: string) {
  const panel = page.locator('.order-panel');
  await panel.getByRole('button', { name: side, exact: true }).click();
  await panel.getByRole('button', { name: type, exact: true }).click();
  if (price) await page.getByRole('textbox', { name: 'Limit price' }).fill(price);
  await page.getByRole('textbox', { name: 'Order quantity' }).fill(quantity);
  await panel.getByRole('button', { name: new RegExp('^' + side + ' BTC') }).click();
  await expect(panel.getByRole('status')).toContainText('Order accepted.');
  await expect(panel.getByRole('button', { name: new RegExp('^' + side + ' BTC') })).toBeEnabled();
}

test('two users: registration, funding, transfers, real liquidity, 13 BTC, cancellation, session restoration', async ({ browser }) => {
  const a = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
  const b = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
  const alice = await a.newPage(); const bob = await b.newPage();
  const errors: string[] = []; for (const p of [alice, bob]) p.on('pageerror', e => errors.push(e.message));
  const email = `alice-${Date.now()}@example.com`;
  await register(alice, email); await credit(alice); await credit(alice);
  await expect(alice.getByTestId('Funding-BTC').getByTestId('available')).toHaveText('200.00000000');
  await transfer(alice, 'USDT', '1000000');
  await expect(alice.getByTestId('Unified Trading-BTC').getByTestId('available')).toHaveText('0.00000000');
  await register(bob, `bob-${Date.now()}@example.com`); await credit(bob);
  await transfer(bob, 'BTC', '100'); await transfer(bob, 'USDT', '1000000');
  await alice.goto('/trade'); await bob.goto('/trade');
  await expect(alice.locator('.connection')).toContainText('Live');
  await place(bob, 'Sell', 'Limit', '10', '50000');
  await expect(alice.locator('.asks')).toContainText('10.000000');
  await alice.getByRole('button', { name: 'Individual orders', exact: true }).click();
  await expect(alice.locator('.asks')).toContainText('50,000.00');
  await alice.getByRole('button', { name: 'Price levels', exact: true }).click();
  await place(alice, 'Buy', 'Limit', '10', '50000');
  await place(bob, 'Buy', 'Limit', '2', '49000');
  await expect(alice.locator('.bids')).toContainText('49,000.00');
  await place(alice, 'Sell', 'Market', '2');
  await place(bob, 'Sell', 'Limit', '5', '50000');
  await expect(alice.locator('.asks')).toContainText('5.000000');
  await place(alice, 'Buy', 'Limit', '5', '50000');
  await alice.getByRole('button', { name: 'Trade history', exact: true }).click();
  await expect(alice.locator('.history-panel tbody tr')).toHaveCount(3);
  await expect(alice.locator('.chart-empty')).toHaveCount(0);
  await alice.screenshot({ path: '../artifacts/terminal-desktop.png', fullPage: true });
  // Check decimal-string wire values before any rendering conversion.
  const candleResponse = await alice.request.get('/api/candles?interval=1m'); const candles = await candleResponse.json();
  for (let i = 0; i < candles.length; i++) { expect(typeof candles[i].close).toBe('string'); if (i) expect(new Decimal(candles[i].open).eq(candles[i-1].close)).toBeTruthy(); }
  await alice.goto('/assets');
  await expect(alice.getByTestId('Unified Trading-BTC').getByTestId('available')).toHaveText('13.00000000');
  await expect(alice.getByTestId('Unified Trading-USDT').getByTestId('available')).toHaveText('348,000.00000000');
  await expect(alice.getByTestId('Unified Trading-USDT').getByTestId('locked')).toHaveText('0.00000000');
  await alice.reload(); await expect(alice.getByRole('heading', { name: 'Your assets' })).toBeVisible();
  await alice.goto('/trade'); await place(alice, 'Buy', 'Limit', '1', '100');
  await alice.getByRole('button', { name: /^Open orders/ }).click();
  await expect(alice.locator('.history-panel tbody tr')).toHaveCount(1);
  await alice.locator('.history-panel').getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(alice.locator('.history-panel tbody tr')).toHaveCount(0);
  await alice.goto('/assets'); await expect(alice.getByTestId('Unified Trading-USDT').getByTestId('available')).toHaveText('348,000.00000000');
  await alice.screenshot({ path: '../artifacts/assets-desktop.png', fullPage: true });
  await alice.setViewportSize({ width: 390, height: 844 }); await alice.goto('/trade');
  await expect(alice.getByRole('heading', { name: 'BTC/USDT', exact: true })).toBeVisible();
  expect(await alice.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
  await alice.screenshot({ path: '../artifacts/terminal-mobile.png', fullPage: true });
  await alice.getByRole('button', { name: 'Sign out', exact: true }).click();
  await alice.goto('/assets'); await expect(alice).toHaveURL(/\/login$/);
  await alice.getByLabel('Email address').fill(email); await alice.getByLabel('Password', { exact: true }).fill(password);
  await alice.getByRole('button', { name: /^Sign in/ }).click();
  await expect(alice.getByTestId('Unified Trading-BTC').getByTestId('available')).toHaveText('13.00000000');
  expect(errors).toEqual([]); await a.close(); await b.close();
});

test('auth refresh rotates cookies, rejects reuse, and private APIs require JWT', async ({ request }) => {
  expect((await request.get('/api/balances')).status()).toBe(401);
  const response = await request.post('/api/auth/register', { data: { email: `auth-${Date.now()}@example.com`, password, country: 'GB' } });
  expect(response.status()).toBe(200);
  const session = await response.json();
  const original = (await request.storageState()).cookies.find(c => c.name === 'novcic_refresh')!;
  expect(original.httpOnly).toBeTruthy(); expect(original.sameSite).toBe('Strict');
  const refresh = await request.post('/api/auth/refresh'); expect(refresh.status()).toBe(200);
  const rotated = (await request.storageState()).cookies.find(c => c.name === 'novcic_refresh')!;
  expect(rotated.value).not.toBe(original.value);
  const replay = await request.post('/api/auth/refresh', { headers: { Cookie: `novcic_refresh=${original.value}` } }); expect(replay.status()).toBe(401);
  const descendant = await request.post('/api/auth/refresh', { headers: { Cookie: `novcic_refresh=${rotated.value}` } }); expect(descendant.status()).toBe(401);
  const before = await request.post('/api/faucet', { data: {}, headers: { Authorization: `Bearer ${session.accessToken}`, 'Idempotency-Key': crypto.randomUUID() } }); expect(before.status()).toBe(200);
  const crossOrigin = await request.post('/api/auth/logout', { headers: { Origin: 'https://elsewhere.example' }, data: {} }); expect(crossOrigin.status()).toBe(403);
  const badCountry = await request.post('/api/auth/register', { data: { email: `invalid-${Date.now()}@example.com`, password, country: 'ZZ' } }); expect(badCountry.status()).toBe(400);
});
