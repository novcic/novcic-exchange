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
- BTC/USDT CPMM, 0.3% LP fee, separate liquidity from the order book.
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
