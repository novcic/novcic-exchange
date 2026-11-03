# Expanded platform implementation

Branch: `master`. Original `task.txt` is immutable. Existing `novcic` Compose project is preserved; expanded stack is `novcic-next` on port 8081 with a fresh database and chain.

## Acceptance matrix

| Stage | Work | Status |
| --- | --- | --- |
| 1 | Novčić branding, readable UI, wallet tabs, USD summary, contextual actions and transfer dialog | In progress |
| 1 | Versioned migrations and balanced accounting journals | In progress |
| 2 | Persistent local Ethereum, repeatable token deployment and explicit generation reset | Pending |
| 2 | Separate custody service, deposits, sweeps, withdrawals and recovery | Pending |
| 3 | Own Bybit-derived index, stale-price restrictions, cross-margin risk | Pending |
| 3 | Instrument matching, perpetuals, dated futures, options and settlement | Pending |
| 4 | Borrower-funded lending/Earn, interest and liquidations | Pending |
| 4 | Constant-product AMM and LP accounting | Pending |
| 4 | Durable DCA scheduler | Pending |
| 4 | Signed algorithmic APIs, sequenced streams and portfolio analytics | Pending |
| 5 | Feature checks, complete user journeys, concurrency, restarts and measured load | Pending |

No stage is complete until its checks pass. Keep test artifacts under `artifacts/` and record commands/results here. Use exact decimals or integer base units for monetary values; never floating-point accounting. New balances originate in confirmed local-chain token deposits. Funding is excluded from cross-margin collateral. Wallet display valuation uses local BTC/USDT last trade, assuming USDT=$1; risk prices use a separate index assembled from Bybit spot/perpetual quotes.

## Chosen product rules

- 1–10x USDT-settled BTC derivatives; one net position/instrument. BTC collateral ratio 90%, USDT 100%; maintenance margin 0.5% of notional.
- Perpetual funding every eight hours, capped ±0.05%. Weekly/monthly futures expire Friday 08:00 UTC and settle to preceding 30-minute index TWAP.
- European options, covered calls and fully collateralized puts, five 5%-spaced strikes around index rounded to $100 at listing.
- Lending initial/warning/liquidation LTV 60%/70%/80%; APR 2% + 10% × utilization. 90% lender / 10% reserve interest allocation.
- BTC/USDT CPMM, 0.3% LP fee, separate liquidity from the order book.
- Hourly/daily/weekly fixed-USDT DCA; no catch-up trades. API keys do not allow withdrawals.
- No gaming. No artificial orders or fabricated fills. Preserve continuous candle opens/previous closes.
