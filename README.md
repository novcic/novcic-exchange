# Novcic Exchange

An English-language BTC/USDT spot exchange with a React terminal, ASP.NET Core API,
separate matching service, Entity Framework Core, and PostgreSQL.

## Run locally

Install Docker with Compose and start its Linux engine, then run:

```sh
docker compose up -d --build
```

Open **http://localhost:8080**. Compose waits for database, matching, and API health.
Register an account, choose a country, and use **Add test funds** on Assets. Each
click adds 100 BTC and 1,000,000 USDT to Funding. Transfer assets into Unified
Trading before placing orders. To create liquidity, register another account in
a separate browser profile or private window and place opposing orders.

The book starts empty. There are no artificial orders, external prices, deposits,
or withdrawals. Charts start at the first trade. Green/red indicate trading data;
blue is the primary interface accent. All displayed times are UTC.

```sh
docker compose ps
docker compose logs --tail 100 api matching
docker compose stop
```

PostgreSQL data persists in the `postgres-data` volume. The database is exposed
only on loopback port 54329 for local tooling. Matching and API have no host ports.
Copy `.env.example` to `.env` to override local settings. `DEMO_MODE=false` disables
the faucet server-side and uses Secure refresh cookies; that mode requires HTTPS
at the reverse proxy. Use fresh signing/internal keys and a database password
before hosting. The checked-in defaults are only for a loopback demo.

## Accounting and execution

- C# `decimal` and PostgreSQL `numeric(28,8)` hold all monetary amounts. API decimal
  fields are strings. Browser forms/state retain strings and use `decimal.js` with
  60-digit precision for arithmetic. Only the chart drawing adapter converts
  prices into the plotting library's floating-point format; it never supplies
  values for orders or accounting.
- BTC order step: 0.000001. USDT price tick: 0.01. Transfer precision: 8 decimals.
  Individual order amounts/prices are positive and at most 1,000,000,000.
  Trading fees are zero. Quantities are BTC, not derivatives contracts.
- Resting orders execute by best price, then durable order sequence. Execution
  occurs at the maker's price. Limit remainders rest; market remainders cancel.
  Self-trade prevention cancels the incoming remainder without changing the maker.
- Buys reserve USDT; sells reserve BTC. Execution releases buy price improvements.
  Cancellation releases the remaining reservation. Only available funds transfer.
- Matching owns the `exchange` schema; API authentication owns `auth`. For this MVP,
  both schemas share a PostgreSQL database and a common EF model. Matching creates
  the initial schema. `EnsureCreated` supports fresh installations; future schema
  changes require introducing versioned migrations rather than recreating data.
- Every financial command takes a UUID `Idempotency-Key`. Its fingerprint, response,
  orders, trades, wallet changes, and ledger entries commit in one transaction.
  A PostgreSQL transaction advisory lock serializes commands across instances.
  Retries with the same key return the original response; changed payloads reject.
  Client forms retain the key after an ambiguous failure. No cross-service
  balance-update callback or in-memory book is necessary for recovery.
- Each trade receives its sequence in execution order within that transaction.
  Market snapshots use repeatable-read isolation. API relays committed revision
  notifications through SignalR every 500 ms; clients fetch authorized data and
  also reconcile every five seconds. Notifications contain no account details.
- Candle intervals: 1m, 5m, 15m, 1h. Open equals the previous close. Empty intervals
  carry that price as O=H=L=C with zero volume, including the current interval.
  Each response returns up to 500 candles.

This is a single-market MVP: its global writer lock and database-backed book favor
correctness. It has not been qualified for production exchange throughput or
real-money custody. No derivatives, margin, lending, AMM, Earn, or gaming is included.

## Authentication and API

Access JWTs expire after ten minutes and stay in browser memory. Refresh tokens
rotate, are hashed in PostgreSQL, and use HttpOnly/SameSite=Strict cookies.
Refresh-token reuse revokes the token family; logout revokes its refresh family.
Already-issued access JWTs remain valid until expiration. Passwords use ASP.NET
Identity hashing. Registration/login/refresh are rate limited. The internal
service validates a separate key and receives user identity only from the API.

Public endpoints: `GET /api/config`, `/api/health`, `/api/market`, and
`/api/candles?interval=1m`. Auth endpoints: `POST /api/auth/register`, `/login`,
`/refresh`, `/logout`; `GET /api/auth/me`. Authenticated endpoints:

| Endpoint | Purpose |
| --- | --- |
| `GET /api/balances` | Available and reserved BTC/USDT in both wallets |
| `GET /api/orders`, `/api/trades`, `/api/ledger` | Latest personal activity |
| `POST /api/faucet` | Demo credit; body `{}` |
| `POST /api/transfer` | `{ "asset":"USDT", "from":"Funding", "to":"Unified Trading", "amount":"1000" }` |
| `POST /api/orders` | `{ "side":"Buy", "type":"Limit", "quantity":"0.1", "price":"50000.00" }` |
| `POST /api/orders/{id}/cancel` | Cancel own resting order; body `{}` |

For Market orders, `price` is null. Authenticated calls use `Authorization: Bearer
<accessToken>`. All financial POST requests require a UUID `Idempotency-Key`.

## Verification

With .NET 10 SDK and the Compose database running, create a disposable test database:

```sh
docker compose exec -T db createdb -U novcic novcic_tests
dotnet test Novcic.sln
```

Tests default to the local demo database credentials. Set `TEST_DB` to a connection
string if you changed them. Tests refuse a database whose name does not end in
`_tests`; exchange tables in that test database are truncated per test.

The 18 backend checks cover conservation, ledger reconciliation, 13 BTC trading
gain, fractional settlement, FIFO and price priority, concurrent spending,
concurrent duplicates, cancellation, self-trades, persistence, and candle gaps.

With Node 22 and Microsoft Edge installed, run the browser tests against Compose:

```sh
cd web
npm ci
npm run build
npm run test:e2e
```

Playwright uses two isolated users and real orders, verifies registration through
login, credits, transfers, 10 BTC buy → 2 BTC sell → 5 BTC buy, final 13 BTC and
348,000 USDT, cancellation, refresh rotation/replay rejection, and mobile layout.
It creates test accounts in the running demo; use a disposable stack for repeated
runs. Screenshots go to `artifacts/`; failure traces go to `web/test-results/`.

For a real service-restart check, run `node scripts/verify-recovery.mjs` from the
repository root. It verifies a resting order, its reserved balance, and its retry
receipt across `docker compose restart matching`, then cancels the order and
checks that the exact reservation is released.

Verified locally: all 18 backend tests, both Playwright suites, and the real
matching-service restart check pass. Desktop and 390px mobile terminal screenshots
were inspected. Direct navigation and reload of `/assets` are covered.
