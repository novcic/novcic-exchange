# Novčić Exchange

Digital-asset exchange with a React terminal, ASP.NET Core API, a separate matching authority and custody worker, PostgreSQL accounting, and a persistent local Ethereum network.

This checkout now lives in `NovcicExchange/`. Run exchange commands from this directory. The sibling `../docs/` is an independent repository on `master`, published at [novcic.github.io/docs](https://novcic.github.io/docs/). Both repositories follow [BRANDING.md](BRANDING.md), the detailed Meadow design specification.

After public API or branding changes, run `python scripts/sync-docs.py` and `python scripts/check-contract.py`, then regenerate and validate the sibling docs. See `../docs/docs/maintenance.md` for the release workflow. Default derivative AMM depth is five levels per side, controlled by `Amm:DerivativeLevels` (1–20); the levels divide the existing collateral budget.

## Run the expanded exchange

Requirements: Docker with its Linux engine running, Node.js 22+, and .NET 10 SDK for backend development/tests.

```sh
npm ci --prefix chain
node chain/setup.mjs
```

Open **http://127.0.0.1:8081**. Register, open **Assets → Deposit**, and copy the Funding address. Fund it with local tokens:

```sh
node chain/setup.mjs fund YOUR_DEPOSIT_ADDRESS BTC 1
node chain/setup.mjs fund YOUR_DEPOSIT_ADDRESS USDT 50000
```

The funding helper queues a real ERC-20 transfer through the custody nonce allocator. Deposits credit Funding after three confirmations. Transfer funds to Unified Trading to trade or invest. Create another account to provide opposing orders; the exchange never fabricates liquidity.

The setup script verifies the existing chain identity and contracts on every run and reuses them. Deployment metadata lives in ignored `chain/state/deployment.json`; generated secrets live only in ignored `.env.next`. PostgreSQL uses loopback port 54330 and Ethereum RPC uses 8545. `novcic-next` owns separate database and chain volumes. The original `novcic` stack on port 8080 is preserved; do not rebuild it against the expanded schema.

```sh
docker compose --env-file .env.next -f compose.next.yaml -p novcic-next ps
docker compose --env-file .env.next -f compose.next.yaml -p novcic-next logs --tail 100 matching custody
docker compose --env-file .env.next -f compose.next.yaml -p novcic-next stop
```

Only an explicit reset creates a new chain generation and clears the expanded database:

```sh
node chain/setup.mjs reset --confirm-reset-next
```

Anvil runs as its unprivileged user. A one-shot storage initializer grants that user access to the chain volume. State is saved every second with historical states preserved, and SIGINT requests a final dump on shutdown. Setup fails if the state file is absent or unwritable. Back up the database, chain volume, and the existing protected configuration together for operational recovery.

## Products and account experience

- **Assets:** Funding/Unified Trading tabs, local-price USD wallet summary, search/hide-zero controls, contextual Trade/Deposit/Withdraw/Transfer actions, and a separate transfer dialog. Transfer Max respects margin requirements. On-chain activity and accounting entries have separate views.
- **Trading:** instrument-specific charts, price/time-priority order books, limit/market orders, partial fills, self-trade prevention, order/trade history, positions, and reduce-only closes. Spot, BTC perpetual, weekly/monthly expiry futures, and European call/put options share the terminal.
- **Cross margin:** Unified Trading USDT at 100% and BTC at 90%; Funding is excluded. Futures support 1–10× leverage and 0.5% maintenance margin. Covered calls reserve BTC and puts reserve their full strike value in USDT. Additional commitments cannot reuse that collateral.
- **Reference prices:** a local index combines Bybit BTC/USDT spot and perpetual midquotes, rejecting stale or divergent contributions. Derivative marks use recent local execution prices bounded to ±1% of the index. New derivative limit prices are bounded to ±10%. Risk-increasing actions stop when reference prices are older than five seconds.
- **Perpetual funding:** eight-hour boundaries, capped at ±0.05%. Funding uses position ownership at the boundary and a fully covered reference-price window; an incomplete window records a zero-rate interval rather than extrapolating a price.
- **Delivery:** weekly/monthly Friday 08:00 UTC expiry. Futures and European options settle once to the preceding 30-minute index TWAP. The TWAP uses elapsed time, not sample count; missing coverage or a gap over five seconds defers delivery.
- **Risk interventions:** cancel risk-increasing orders and attempt partial derivative reductions against actual resting liquidity. Uncollected realized obligations remain liabilities. Unencumbered BTC can be converted against actual spot bids within the collateral price guard. Profit withdrawals use only USDT actually collected in the clearing pool.
- **Earn and loans:** borrower-funded pools, variable APR `2% + 10% × utilization`, 90% supplier/10% reserve interest allocation, precise fractional-interest carry, queued redemptions, repayment, additional collateral, and actual-liquidity liquidation. Initial/warning/liquidation LTV is 60%/70%/80%. Losses beyond pool reserves reduce supplier NAV.
- **Liquidity:** BTC/USDT LP capital supplies funded spot CPMM and collateralized derivative CFMM orders in the actual books. Public books expose aggregated levels only; private orders remain private. Deposits accept either/both tokens and queue for equal-value conversion and share issuance. Withdrawals queue for a selected token and sufficient released collateral; they settle all at once at execution value, without a slippage cap. Pending requests can be cancelled; already executed conversions are not reversed. Requests may wait indefinitely for counterparties.
- **AMM risk and fees:** no borrowing/LP leverage, 1x futures collateral, covered options, 20% reserve buffer. 80% of execution fees accrue to LP share value and 20% to exchange revenue. Default fees are spot 0.10%, futures 0.055%, options 0.03% underlying notional capped at 7% of premium. Matching-service `Amm__SpotFee`, `Amm__FuturesFee`, `Amm__OptionFee`, `Amm__OptionPremiumCap`, and `Amm__LpShare` configure decimal fractions. Rates are validated at startup.
- **Options workspace:** expiry navigation and a calls/strikes/puts chain with depth, marks, IV, delta, and private positions. Decimal Black-Scholes uses zero carry and live Bybit IV interpolated within supported strikes/expiries; it does not extrapolate. Missing/stale IV pauses affected AMM quotes. This is our pricing model, not Bybit's proprietary strategy.
- **Recurring buys:** hourly/daily/weekly USDT budgets, persisted schedules, pause/resume and explicit run outcomes. Missed runs do not catch up, and buys use actual spot liquidity.
- **Portfolio:** wallet allocation, margin health, unsettled realized cash, capital committed to products, and risk-event history. Wallet USD estimates use the last local spot trade with USDT=$1; an unpriced BTC balance is explicitly excluded. Margin valuation uses the separate reference index.

Product terminology and account organization reference [Bybit's Unified Trading Account documentation](https://www.bybit.com/en/help-center/article/Introduction-to-Bybit-Unified-Trading-Account) and [liquidation rules](https://www.bybit.com/en/help-center/article/UTA-Trading-Rules-Liquidation-Process). Market data uses the official [V5 tickers endpoint](https://bybit-exchange.github.io/docs/v5/market/tickers). Novčić's product rules are specified above; they are not an assertion of identical Bybit behavior.

## Accounting and recovery

Monetary values use C# `decimal` and PostgreSQL `numeric(28,8)`. Position cost is stored exactly; average entry prices are informational `numeric(28,16)`. Sub-atomic interest carry uses `numeric(28,20)`. Ethereum token amounts use integer base units (BTC 8 decimals, USDT 6). JSON financial fields are decimal strings. Browser forms retain strings and use `decimal.js`; only chart coordinates convert to the plotting library's floating-point format.

Every financial command requires a UUID `Idempotency-Key`. The request fingerprint, original response, orders, fills, balances, journal entries, and revision commit in one database transaction under a PostgreSQL advisory lock. Identical retries return the original result; changing the payload rejects. Available/reserved movements are double-entry journaled and nonnegative balances are enforced. Versioned EF migrations preserve existing rows. The browser persists ambiguous requests in session storage so the original command can be reconciled after a reload.

Custody creates encrypted per-user deposit keys and scans only the configured token contracts. Deposit identifiers are unique by generation/transaction/log. A single custody lock serializes durable nonces for withdrawals, sweeps, gas funding, and local funding. Signed transaction bytes and their hash are stored before broadcast. Unknown outcomes retain the reservation and rebroadcast the same bytes. Only a confirmed success or revert completes/releases a withdrawal. A chain/checkpoint mismatch quarantines custody instead of crediting or paying on a different chain.

The clearing pool does not manufacture spendable USDT from a positive P&L entry. Outstanding profits can wait for realized obligations to be collected. Derivative reductions and loan liquidations can remain pending when actual order-book/pool liquidity is unavailable. The current reference feed uses two markets from one provider, and the global writer lock prioritizes atomic accounting over throughput. These are concrete operational boundaries of this implementation.

## API and streams

JWT access tokens stay in browser memory. Rotating refresh tokens use HttpOnly/SameSite cookies and hashed database records. API keys are created/revoked through the signed-in browser, expire after 90 days, and have **Read** or **Trade** scopes. Their secret is shown once and stored encrypted. Neither scope permits withdrawals, internal transfers, investment commands, or key management.

| Endpoint | Purpose |
| --- | --- |
| `GET /api/market?instrument=BTCUSDT` | Consistent market snapshot with durable revision |
| `GET /api/candles?instrument=BTCUSDT&interval=1m` | Continuous local-trade candles |
| `GET /api/products`, `/api/network` | Instruments, reference prices, pools, supported chain/tokens |
| `GET /api/balances`, `/api/orders`, `/api/trades`, `/api/ledger` | Private balances and activity |
| `GET /api/portfolio`, `/api/custody/history` | Positions, margin, investments, chain activity |
| `POST /api/custody/address`, `/api/withdraw`, `/api/transfer` | Funding and wallet operations |
| `POST /api/orders`, `/api/orders/{id}/cancel`, `/api/cancel-all` | Order management |
| `POST /api/supply`, `/api/redeem`, `/api/borrow`, `/api/repay`, `/api/add-collateral` | Lending operations |
| `POST /api/add-liquidity`, `/api/remove-liquidity`, `/api/swap` | Pool operations |
| `POST /api/dca`, `/api/toggle-dca`, `/api/settle-cash` | Schedules and realized-cash settlement |
| `GET/POST /api/keys`, `POST /api/keys/{id}/revoke` | Interactive-session key management |

An order body is `{ "side":"Buy", "type":"Limit", "quantity":"0.01", "price":"77000.00", "instrument":"BTC-PERP", "leverage":"2", "reduceOnly":false }`. For market orders, price is null. Spot defaults to `BTCUSDT`. Internal perpetual ID `BTC-PERP` remains stable for compatibility; its public display symbol is `BTCUSDT`, scoped by product category.

Signed requests send `X-Api-Key`, `X-Api-Timestamp` (Unix milliseconds), `X-Api-Nonce` (unique UUID), and `X-Api-Signature` (hex HMAC-SHA256). The signed string joins these six fields with `\n`: timestamp, nonce, uppercase HTTP method, exact path/query including `/api`, idempotency key (empty for GET), and exact raw JSON body (empty for GET). Clock tolerance is 30 seconds; nonces cannot be replayed. Retry an uncertain write with a new nonce but the original idempotency key and body. See `scripts/api-example.mjs` for a signed balance request using environment variables.

SignalR `/hubs/market` emits `MarketChanged` objects containing a durable `revision` string. These are public invalidations, not order-book deltas. Fetch a fresh snapshot on reconnect, duplicate/out-of-order notifications, or gaps; never derive balances from public notifications. Private reads still require authentication.

## Verification

```sh
npm ci --prefix web
dotnet test tests/Novcic.Tests/Novcic.Tests.csproj
node chain/verify.mjs
node chain/platform-verify.mjs
node chain/recovery-verify.mjs
npm run test:e2e --prefix web
```

Backend tests require disposable databases ending in `_tests`; they cover conservation, atomic concurrency, replay, exact accounting, margin, lending, AMM, DCA, funding, delivery, liquidation, and migrations. `TEST_DB` and `PRODUCT_TEST_DB` override the loopback test connections. The browser suite creates fresh accounts and funds them through real local-chain transactions. Recovery verification pauses local mining temporarily and restarts only expanded-stack services, then restores one-second mining. Test results, temporary test credentials, screenshots, and measurements are ignored under `artifacts/`; see `IMPLEMENTATION.md` for verified results.

### Queued liquidity API

`POST /api/liquidity-deposit` takes `{ "btc":"0.01", "usdt":"0" }`; `POST /api/liquidity-withdraw` takes `{ "shares":"1", "asset":"USDT" }`; `POST /api/liquidity-cancel` takes `{ "id":"request-uuid" }`. All require authentication and an idempotency key. `/api/portfolio` includes the user's request history. Legacy immediate add/remove/swap endpoints return HTTP 410. Conversion and redemption execution shares the same locked ledger and matching transactions as trading.

`/api/market` exposes only `bids` and `asks` of `{price,quantity,total}`; `total` is cumulative base quantity from best price outward. No individual orders, owner IDs, queue positions, or counts are included. `/api/products` batches tickers and option analytics for pickers and chains. Pool actor identifiers are internal.
