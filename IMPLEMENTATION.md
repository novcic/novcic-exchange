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

Publication completed at https://novcic.github.io/docs/ . Initial Pages provisioning exceeded the default ten-minute deployment timeout while `updating_pages`; increasing the window and retrying succeeded (docs commit `c551173`, Actions run `41670653217`). The public page, local search and downloadable 41-operation OpenAPI contract were checked in the browser. The exchange changes remain committed locally on master; the independent docs master is pushed. A 683x384 viewport check representing 200% reflow from 1366x768 also retained reachable controls without horizontal page overflow.

## Market-order balance controls - 2026-11-03

Removed the limit-only condition that hid percentage controls on market orders. Market sizing walks the current opposing aggregated depth using decimal arithmetic, reserves configured AMM fee allowance, rounds down to the BTC quantity step, and respects available/transferable balances, derivative collateral and reduce-only direction/size. An edited limit price does not affect market sizing. Empty depth or unavailable configuration disables sizing, and fee configuration is retried on refresh. Quantity remains manually editable and backend atomic validation remains authoritative when prices or liquidity change. Zero-capacity shortcuts are disabled alongside the slider.

Validation: frontend production build and three focused Playwright tests pass: exact multi-level purchase cost/fees/rounding, sell/derivative/covered-option/closing constraints, and working market Max/50%/slider controls across desktop/mobile with mocked snapshots and no submitted orders. The updated web container is deployed at port 8081.


## Execution fees, account leverage and trading controls (2026-11-03)

Implemented customer maker/taker fees on every fill: spot 0.10% in the received asset, futures 0.055% USDT, options 0.03% underlying notional capped at 7% premium. Fees split 80/20 pool/exchange only on AMM counterparties; other customer fees go to the exchange. Fees are recorded per trade/asset and commit with settlement and receipts. BTC LP fee income is tracked separately; final USDT LP redemptions return untradable BTC dust in kind. DCA spends its USDT budget without an obsolete input-fee allowance.

Account leverage is persisted and applies atomically to all futures positions/open orders. Insufficient-margin changes roll back. The additive migration backfills all-fill order notional and initializes account leverage from existing active futures maxima. Historical trade fees remain nullable rather than reconstructed inaccurately. Zero-net balance reservations/releases remain in the internal ledger but are filtered before the public activity limit.

Added rollback-only order and leverage previews. Max executes the same accounting path in savepoints, including fees, rounding, liquidity and self-trade prevention. No order, trade, wallet, receipt or revision persists from preview; identity sequences can have gaps. Live test-account previews measured roughly 1.1-1.4 seconds, so clients debounce and refresh every five seconds. This global-lock implementation prioritizes correctness and should be replaced with an isolated snapshot evaluator before high-throughput deployment. Market changes still require submission revalidation.

UI: padded history/account tabs; all-fill average execution price; clear partial-cancellation status; account-wide leverage card/modal with margin preview; account-wide Cancel all confirmation; accessible bottom-right toasts above dialogs and durable ambiguous-request retry; fee/net-receipt/cost estimates and backend-sized sliders. Shared Meadow branding updated to 1.1. API contract and independent docs synchronized (44 operations, 62 schemas).

Verification: 63 backend tests passed, including fee conservation/idempotency, spot and futures Max boundaries, next-lot rejection, rollback-only preview, self-trade sizing and leverage rollback. Seven responsive workspace browser checks (including the effective 200% desktop-zoom viewport) and two focused slider/control journeys passed. Docs signing tests (2) and production build passed. Live migrated orders report averages, public ledger excludes zero-net rows, and spot/perpetual preview endpoints returned successful estimates. Visual checks caught and corrected Windows Unicode encoding damage. Backup: ignored artifacts/pre-execution-fees.dump. Matching/API/web deployed to novcic-next without resetting database or chain volumes. NuGet vulnerability-feed lookup remains unavailable in the local environment; compilation and tests use restored dependencies.

Documentation release bb1481f published successfully via GitHub Pages run 41678052695; the live OpenAPI contract exposes version 1.1.0 and all three new operations. Final UI preview errors are shown beside the quantity controls and empty/invalid quantities do not display misleading fee estimates. Focused browser checks passed again after that adjustment.


## Profiles, scoped API keys and immediate estimates (2026-11-03)

Implemented on master. Existing accounts receive unique random User######## usernames and the default avatar through the additive ProfilesAndKeyProducts migration. Registration retries username collisions against a case-insensitive unique index. Profiles support username changes, nine bundled Meadow avatars and an optional private birth date. The header now contains Trade/Earn on the left and the profile menu/My assets on the right; Profile and Security share a responsive category sidebar.

Password and email changes require the current password. Email changes take effect immediately without verification. Both changes atomically increment a security version, revoke refresh sessions and all API keys, and invalidate existing JWTs on their next request. Login, refresh, security changes and key creation serialize their relevant authentication changes. Birth dates and security operations require browser JWT authentication.

My API keys now has a documentation action and a compact creation modal with Read-Only/Read-Write access and Spot/Perpetual/Expiry/Options permissions. Secrets are shown once with explicit save/copy actions. HMAC authorization enforces product selection for preview, placement, cancellation and filtered trading history/positions; cancel-all only cancels selected products. Account-wide leverage requires both futures product permissions. Shared account balances remain readable; transfers, withdrawals and account changes remain unavailable to keys. Existing keys retain all four trading products; new requests that omit products default to Spot.

Order estimates now use the latest actual instrument trade, with an explicitly labeled index/theoretical fallback only when no trade exists. Quantity changes and slider movement update value, estimated fee and net receipt/cost synchronously with decimal arithmetic. Market Max does not inspect or cap against resting liquidity. The order-preview API uses a repeatable-read snapshot and the same formulas, replacing its prior matching simulation and global writer lock. Leverage preview still uses rollback-only validation. Actual submission and per-fill fee settlement remain atomic and authoritative, with fees assessed at actual execution prices; estimates do not guarantee that Max fills at a later price. Option fees retain their underlying-notional basis and premium cap.

Verification: 65 backend tests passed, including product-scoped cancel-all and last-trade estimates with an empty book. Seven responsive workspace checks and two focused sizing/control tests passed. The new live profile/security journey passed random unique names, case-insensitive collisions, avatar/birth-date validation, eight HMAC scope/product combinations, immediate JWT/session/key revocation, profile persistence, key creation/secret/revocation through the UI, and the 390px menu. Visual inspection corrected inherited radio/checkbox layout and verified the profile and API modal. Documentation signing tests (2), contract/source checks and the 131-page production build passed (48 operations, 66 schemas, contract/Meadow 1.2).

Matching/API/web are deployed to novcic-next at port 8081. Database and chain state were preserved; the pre-migration backup is ignored artifacts/pre-profiles.dump. Local restored .NET dependencies were used because NuGet vulnerability metadata remains unreachable. The requested Conventional Commits preference was added only to the global C:/Users/kolya5544/.codex/AGENTS.md, not repository instructions. Documentation commit 040058f was pushed separately to novcic/docs master.

Documentation deployment 41685421837 completed successfully. The live OpenAPI document reports version 1.2.0 and includes profile/security routes, key products and estimate reference fields. Public profile guidance is available at https://novcic.github.io/docs/guides/profile .


## Navigation affordances (2026-11-03)

Replaced font-based disclosure arrows in Trade, account and market controls with a shared SVG chevron, centered independently of typography and rotated on expansion. Primary entries have baseline underlines, generous padding and a vertical divider. The avatar and account toggle share one outlined control while preserving separate profile-link and menu actions with 44px targets. Market selection has an underlined trigger rather than a tight rounded highlight. Trading product menu rows now have line icons, descriptions, row separators and current-product state; the menu anchors to its trigger across widths and restores focus on Escape.

Updated the shared Meadow specification/tokens to 1.2.1 and applied matching navbar underlines in the separate docs repository. Frontend and documentation production builds passed, along with all seven existing workspace checks (desktop sizes, 390px and effective 200% viewport). Manual browser inspection verified desktop/mobile alignment, anchored Trade menu, account toggle/hover, outside dismissal and no page overflow. The rebuilt web container is deployed on port 8081; financial services and persisted state are unchanged.


## Standalone profile avatar (2026-11-03)

Removed the separate account chevron, rectangular grouping, divider and underline. The header now shows only a 40px circular avatar with a 44px hit area. Desktop hover/keyboard focus opens the account menu and clicking the avatar opens Profile. Touch taps toggle the menu; its identity card opens Profile. Escape restores avatar focus. Updated the existing profile journey for the removed toggle and synchronized Meadow branding 1.2.2.

Frontend build and the existing live profile integration journey passed. Browser checks confirmed the 44px control, absence of the old button, hover opening, and actual touch-context tap/menu/profile navigation at 390px. The updated web container is deployed locally; the documentation branding checks passed.


## Typography, local asset artwork and text repair (2026-11-03)

Kept Inter and strengthened the visual hierarchy: real bundled 600-weight navigation/headings/actions/labels and 700-weight page titles and wallet/portfolio summaries. Financial figures retain IBM Plex Mono, now with real 600/700 faces for emphasized values; dense book rows remain regular. Documentation uses matching local 500/600/700 Inter faces instead of assigning a single semibold file to all bold weights. Shared Meadow branding is version 1.3.0.

Added a reusable AssetIcon and paired pool icons backed by locally stored BTC and USDT SVGs from Bitcoin.org and Tether's media assets. Original logo geometry/colors are preserved; Bitcoin's emblem is extracted from its wordmark, and metadata/style indirection is stripped. Sources and the upstream Bitcoin.org MIT license are retained alongside the files. SVGs contain no scripts, external resources or raster images. Decorative images avoid duplicate accessible asset labels.

Corrected four literal damaged question-mark strings in the liquidity view: pool-value separator, trade-link arrow, withdrawal asset description and unavailable-value fallback. Decorative action/transfer arrows across the exchange now use a shared SVG component. A strict UTF-8 audit of 273 tracked text files in both repositories found no remaining replacement characters or common mojibake patterns; a TypeScript syntax-tree inspection found no remaining standalone/question-mark separator strings in UI copy. Query syntax and financial logic were preserved.

Validation: frontend and documentation production builds passed; all seven existing workspace checks passed. Browser inspection of nine routes across 1366px desktop, 390px mobile and 683px effective 200% reflow found no page overflow or broken local icons, and confirmed title weights, actual bold font loading and the corrected liquidity link destination. The rapid reload sweep initially reached the existing authentication rate limit; it was completed through normal client-side navigation after the limit reset. Updated web deployed locally with database and chain state preserved. Documentation commit bfd8665 is pushed separately; the exchange release also pushes the previously unpushed local commits using a normal fast-forward.


## Position analytics, OCO protection and chart trading - 2026-11-03

Implemented on master with additive PositionProtection migration. Existing position lifecycle IDs are backfilled individually; option analytics rebuild exact weighted remaining basis from full execution history without altering wallets or cash settlement. Portfolio preserves existing position fields and flat rows, adding lifecycle/version, last and mark prices, unrealized USDT/return on margin, position value, approximate cross-margin liquidation and current protection. Financial values remain decimal/JSON strings.

Position protection is full-position, last-trade triggered OCO for perpetuals, expiry futures and options. Each matching trade evaluates triggers atomically, preserving transient crossings. Durable closing jobs use reduce-only market attempts with a 100 ms target, no overlapping/catch-up execution, shared financial locking and ordinary fee/risk rules. Same-direction additions are included; flat/reversal/expiry stop the old lifecycle. Users may keep trading and can stop closing. Manual Market Close remains a one-shot order. Retry history is grouped by closingJobId; all active orders are returned independently of the 200 closed-order history limit.

Owned limit prices can be amended atomically using an opaque canonical version token. The remainder receives new priority/ID; existing fills remain final. Failure rolls back cancellation and replacement. New mutation routes enforce product-specific Trade-key permissions and idempotency. Charts show precise position/order sizes and TP/SL; dragging proposes a confirmation, with table and keyboard alternatives. Options default to Chain with a Chart switch. Order-only scales support markets without trades and do not synthesize candles.

Verification: 76 backend tests passed (including OCO crossing/retrace, long/short triggers, option cost reconstruction, partial retry/restart contexts, stale references, explicit stop, lifecycle reversal/reopen/expiry, concurrent workers, amendment rollback/replay/version stability, conservation and migrations). Six focused browser tests cover analytics, exact confirmation payloads, dragging, stale edits, keyboard edits, mobile options and no-candle markets. Existing sizing/fee and trading-control tests passed. The deployed HMAC/profile test verifies Read/Trade permissions across all products for all three new commands. Fourteen live response schemas and both signing examples pass; temporary API keys are revoked.

A live no-position job exposed starvation with try-lock polling under active AMM work. The worker now joins the shared advisory-lock queue with one awaited attempt at a time. The same live fixture then completed under load without any trade/fund movement; its temporary row was removed. The 100 ms cadence remains a target subject to matching/lock capacity, available liquidity and reference/risk checks, not a hard real-time guarantee.

Matching/API/web deployed to novcic-next at port 8081; database and local chain volumes preserved. Pre-migration backup: ignored artifacts/pre-position-protection.dump. Public API 1.3.0 (51 routes), expanded trading/risk guides and Meadow branding 1.4.0 are synchronized to the independent docs repository. Documentation signing tests and production builds pass. Local restored .NET dependencies were used because NuGet vulnerability metadata is unreachable.


## Persisted price input precision fix - 2026-11-03

TP/SL and limit-amendment dialogs now normalize exact, tick-aligned API values to two decimal places when opened, blurred or submitted. Validation checks decimal value precision rather than counting serialized trailing zeros. Thus 77645.39000000 loads/submits as 77645.39; 77645.39100000 remains unchanged and is rejected. Arithmetic stays decimal; no price is silently rounded into a different intent. Both saved values and chart-origin edits use the same normalization.

The reported failure was reproduced against the previous deployed build. Fixtures now use the database's eight-decimal serialization, and two regression tests cover saved/pasted prices, exact outgoing values, chart reopening and off-tick rejection. All eight position/chart browser tests and the production web build pass. The web-only fix is deployed at port 8081; API, balances, chain state and public contract are unchanged.
