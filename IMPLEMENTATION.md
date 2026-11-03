# Expanded platform implementation

Branch: `master`. Existing `novcic` Compose project is preserved; expanded stack is `novcic-next` on port 8081 with a separate database and chain.

## Acceptance matrix

| Stage | Work | Status |
| --- | --- | --- |
| 1 | Novčić branding, readable UI, wallet tabs, USD summary, contextual actions and transfer dialog | Verified |
| 1 | Versioned migrations and balanced accounting journals | Verified |
| 2 | Persistent local Ethereum, repeatable token deployment and explicit generation reset | Verified |
| 2 | Separate custody service, deposits, sweeps, withdrawals and recovery | Verified |
| 3 | Own Bybit-derived index, stale-price restrictions, cross-margin risk | Verified |
| 3 | Instrument matching, perpetuals, dated futures, options and settlement | Verified |
| 4 | Borrower-funded lending/Earn, interest and liquidations | Verified |
| 4 | Constant-product AMM and LP accounting | Verified |
| 4 | Durable DCA scheduler | Verified |
| 4 | Signed algorithmic APIs, sequenced snapshot notifications and portfolio analytics | Verified |
| 5 | Feature checks, complete user journeys, concurrency, restarts and measured load | Verified |

No stage is complete until its checks pass. Keep test artifacts under `artifacts/` and record commands/results here. Use exact decimals or integer base units for monetary values; never floating-point accounting. New balances originate in confirmed local-chain token deposits. Funding is excluded from cross-margin collateral. Wallet display valuation uses local BTC/USDT last trade, assuming USDT=$1; risk prices use a separate index assembled from Bybit spot/perpetual quotes.

## Chosen product rules

- 1–10x USDT-settled BTC derivatives; one net position/instrument. BTC collateral ratio 90%, USDT 100%; maintenance margin 0.5% of notional.
- Perpetual funding every eight hours, capped ±0.05%. Weekly/monthly futures expire Friday 08:00 UTC and settle to preceding 30-minute index TWAP.
- European options, covered calls and fully collateralized puts, five 5%-spaced strikes around index rounded to $100 at listing.
- Lending initial/warning/liquidation LTV 60%/70%/80%; APR 2% + 10% × utilization. 90% lender / 10% reserve interest allocation.
- BTC/USDT shared LP capital: funded spot CPMM ladders and collateralized derivative CFMM quotes in the same books; 80% of AMM execution fees accrue to LPs.
- Hourly/daily/weekly fixed-USDT DCA; no catch-up trades. API keys do not allow withdrawals.
- No gaming. No artificial orders or fabricated fills. Preserve continuous candle opens/previous closes.

## Verification record — 2026-11-03

- `dotnet test tests/Novcic.Tests/Novcic.Tests.csproj`: **42 passed**, zero failures. Includes exact spot settlement, concurrency/replay, fractional cost basis, covered-option restrictions, stale feeds, funded-profit limits, lending interest carry, redemption queues, AMM invariants/slippage, DCA execution, derivative/loan liquidation against real liquidity, capped funding, futures/options delivery, missing-TWAP deferral, and migration preservation of existing balances.
- `npm run test:e2e --prefix web`: **7 passed** in 13.7 seconds. Fresh local-chain-funded accounts; registration/country/session, assets and transfer/deposit dialogs, order placement/cancellation and product switching, Earn/redemption/DCA/portfolio, API key lifecycle, mobile navigation/Escape, and lost transfer response followed by reload and an exactly-once retry.
- `node chain/platform-verify.mjs`: actual BTC and USDT funding/deposits, independent books for all product types, reduce-only closes, lending and AMM journeys, signed API replay/clock/scope/revocation checks, durable snapshot revisions. Latest bounded read sample: **50 requests, concurrency 5; p50 3.48 ms, p95 9.69 ms, max 13.23 ms**. This is a local read-path measurement, not an exchange-wide throughput claim.
- `node chain/recovery-verify.mjs`: mining paused with a withdrawal broadcast and unconfirmed; custody/matching restarted; replay retained the same transaction hash; resumed mining produced exactly one payout. Chain restart retained token state, block history and balances; custody remained unquarantined.
- `node chain/setup.mjs`: rerun reported **“Verified existing chain and token contracts. No redeployment.”** Explicit reset was exercised on the isolated verification generation.
- Frontend production builds and container builds passed. Desktop and 390 px mobile screens inspected through Playwright; screenshots are in ignored `artifacts/ui/`.
- Git history and index contain no local task instruction file. Generated environment, chain state, test credentials, backups and screenshots remain ignored.

## Issues found and corrected during verification

- Anvil's volume was initially owned by root, preventing the unprivileged node from persisting state. The first chain-restart test exposed state loss and correctly quarantined custody. All five affected accounts were created by the verification scripts. The isolated database/public manifest were backed up in ignored `artifacts/pre-persistence-repair/`; deployment credentials stayed in their original configuration. A storage initializer, historical-state persistence, SIGINT shutdown, and a setup writeability/state-file check fixed the cause. A new verification generation passed the complete journey and restart checks.
- Browser mobile navigation was covered by trading tabs because styles loaded in the wrong order. Corrected the order and verified actual taps at 390 px.
- Local funding and exchange payouts shared a treasury signer but originally allocated nonces separately. Both now use the durable custody queue.
- Realized P&L was initially convertible to wallet money without a funded clearing pool. Payouts now use collected USDT only; liabilities remain explicit, and collateral conversion uses real guarded spot liquidity.
- Spot requests now reject derivative-only flags, preventing a reduce-only flag from bypassing cross-margin checks.

## Operational boundaries

The implemented rules are documented in `README.md`. Liquidation requires actual counterparties/pool liquidity; uncollected obligations and corresponding profits can remain pending. Funding/expiry need adequate reference coverage, and the reference feed currently has one provider with two market contributions. The writer lock is global. The measured load result covers market reads only. Public-chain custody, an external insurance capitalization mechanism, and production throughput qualification are not claimed by these checks.

## Trading workspace and integrated liquidity verification - 2026-11-03

Implemented on master without resetting the database or local chain. The pre-migration database backup is in ignored artifacts/pre-integrated-liquidity.dump. The additive IntegratedLiquidity migration preserves existing LP shares and moves existing reserves into a dedicated internal maker wallet on its first atomic worker cycle.

- Public depth contains only price, summed quantity, and cumulative quantity. Individual orders, owner IDs, counts, and queue positions are excluded. Private order management is unchanged.
- Global Trade menu replaces product tabs; perpetual display symbols use BTCUSDT with stable internal IDs. Searchable market pickers, expiry navigation, option chain, decimal-safe balance controls, modal styling, price prefilling, and desktop viewport layout are implemented.
- Queued contributions are escrowed, balanced through actual counterparty fills, then activated atomically with the strategy update. Conversion tolerances are bounded by the trading quantity increment and 0.01% of contribution value. Withdrawals settle in full in the selected token when risk-adjusted collateral permits; cancellation releases unprocessed commitments. No slippage cap or completion-time guarantee is implied.
- Funded AMM orders execute through the normal matching paths for spot, perpetuals, expiry futures, and covered options. Strategy orders are post-only, refreshed under the global financial lock, and expire for execution after three seconds. Small redemptions do not require unrelated positions to close; remaining collateral and the reserve buffer must still be sufficient.
- Fee allocation, NAV, pending conversion wallets, option values, and risk use decimal arithmetic. Bybit volatility is refreshed every 20 seconds and expires after 60 seconds. Interpolation stays within available strike/expiry coverage. Zero-carry Black-Scholes is the local option-pricing model; unavailable IV suppresses affected quotes rather than substituting invented volatility.
- Lending liquidation now uses actual order-book fills after reserve migration. DCA budgets include AMM fees. AMM rule failures defer that cycle without blocking other lifecycle work.

Validation: 52 backend tests passed, including conservation, queue replay/cancellation, chosen-token withdrawal, partial-capital withdrawal with remaining positions, actual AMM fills on all product kinds, stale-quote rejection, integrated loan liquidation, migration preservation, decimal option pricing, IV coverage, and aggregate depth. Seven authenticated browser journeys and six workspace checks passed. Workspace sizes checked: 1366x768, 1440x900, 1920x1080, and 390px mobile; option quote clicks retain side and a valid price. Screenshots are under ignored artifacts/ui.

The updated platform verification passed actual local-chain funding, transfers, all trading products, Earn/loans, queued liquidity, DCA, and signed API replay/scope checks. Its fixed-price and fixed-share assumptions were corrected because active AMM liquidity competes on price and lending share NAV accrues interest. Latest 50-request read sample at concurrency five: p50 4.01ms, p95 7.08ms, max 7.85ms; not a production throughput claim.

Container NuGet downloads encountered EOF/connectivity failures. Matching/API were instead published with the already-restored local dependencies, then packaged into the same .NET 10 runtime image using ignored artifacts/runtime-* build contexts. Frontend production container builds passed normally. Only matching/API/web services were replaced; database, custody, and blockchain volumes were preserved.

Current scope remains the supported BTC/USDT pair. Pool records carry pair identity for subsequent asset expansion; listing other base assets also requires extending exchange-wide asset and instrument handling. Queues may remain pending without suitable liquidity, including amounts below the trading increment. Unleveraged LP capital still bears inventory and derivative losses; the collateral controls do not promise principal protection.

## Meadow design, repository split and public API docs - 2026-11-03

The complete checkout, Git history, ignored configuration, artifacts and chain state now live under `NovcicExchange/`. The parent is a workspace containing that checkout and the independent `docs/` repository. Both use master. Docker project names are explicit (`novcic` and `novcic-next`); the database and chain volumes were preserved. No task instruction file exists in tracked history.

The detailed BRANDING.md defines the light Meadow identity, typography, tokens, spacing, icons, financial components, accessibility, responsive behavior, examples and maintenance. Shared versioned JSON/CSS tokens and a restrained botanical SVG are synchronized into docs with checksums. The exchange now has semantic light surfaces, a light chart, aligned market identity/statistics, compact anchored market selection with search/favorites/icons, a mobile picker sheet, responsive asset cards, clearer dialogs and an API documentation link. Refresh callbacks coalesce overlapping requests and public product discovery shares a short cache.

Derivative AMM quotes now split the same allocation across five levels per side (configurable 1-20), with separate curve segments and merged tick collisions. Unchanged quote IDs, sequences and creation times retain priority; the additive QuoteValidation migration provides a separate revalidation time for stale-execution checks. Money remains decimal and all quote replacement/reservation work remains within the existing atomic financial transaction. Public config now identifies user-to-user versus configured AMM fees.

The separate Docusaurus site contains 115 pages: integration guides, 41 REST operation references including retired/disabled routes, and 58 request/response schemas. It includes OpenAPI 3.0, HMAC examples in JavaScript/Python, SignalR semantics, permission matrices, decimal/precision rules, lifecycle/queue behavior, error limits, local network details, local search, changelog and maintenance instructions. Contract/source hashes and brand checksums are checked by CI. Public docs were committed separately as `6e65a0a` and pushed to novcic/docs master; GitHub Pages publication is verified as part of release completion.

Verification: 53 backend tests pass, including the new derivative depth-budget/priority conservation test. Six workspace browser checks pass at 1366x768, 1440x900, 1920x1080 and 390px. Seven authenticated journeys pass after making the API-key fixture name unique for repeatability; final mobile asset and transfer checks were rerun after card layout changes. Manual keyboard checks confirm arrow navigation, Escape/focus restoration and five live perpetual levels per side. Documentation search/navigation and 390px reflow pass, and the production site builds with broken-link errors enabled. Fourteen live REST response shapes match the contract; both published signing examples authenticate successfully with a temporary Read key that is revoked afterward.

The full platform journey passes local-chain deposits/transfers, spot/perpetual/expiry/options matching and closes, Earn/loan/collateral/repayment/redemption, LP queues, DCA and HMAC replay/scope/idempotency/revocation. A separate exact-precision custody journey passes confirmed token deposits, duplicate withdrawal intent, actual on-chain payout and final balance/reserve reconciliation. Latest bounded market-read sample: 50 requests, concurrency five, p50 8.14ms, p95 11.78ms, max 13.41ms. This remains a local read sample, not production throughput qualification.
