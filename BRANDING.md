# Novčić branding and interface specification

Version: 1.1.0 - Meadow. Applies to the exchange and public developer documentation.

## Identity

Novčić should feel clear, grounded, and close to nature. Warm daylight, meadow paths, sage leaves, and forest-green ink inform the visual language. It is a financial workspace first: decorative nature imagery never obscures figures or competes with decisions. Use one light theme. Do not add a dark-mode toggle or follow system dark mode.

Use the Serbian spelling **Novčić** in prose and **novčić.** in the wordmark. Keep code identifiers and URLs ASCII where necessary. Keep the existing recognizable N mark; do not replace asset icons with leaves. The brand voice is direct, calm, and specific. Explain what happened and what the user can do next. Avoid hype, guaranteed returns, invented market activity, and technical implementation details in ordinary user flows.

## Color system

`branding/tokens.json` is the machine-readable source; `branding/tokens.css` is generated. Both repositories use identical versions and checksums.

| Role | Color | Use |
| --- | --- | --- |
| Canvas | #f6f8f2 | Warm meadow background |
| Surface | #ffffff | Data panels, cards, dialogs |
| Soft surface | #edf3e8 | Grouped controls, botanical illustrations |
| Hover | #e2eddb | Interactive row hover, selected neutral areas |
| Primary text | #203c2d | Forest ink; headings and values |
| Muted text | #526858 | Supporting labels; never disabled-opacity body text |
| Border | #cbd8c7 | Panel separation and form outlines |
| Primary | #326b42 | Navigation selection and primary actions |
| Primary hover | #255333 | Hover/pressed action |
| On primary | #ffffff | Text on solid primary buttons |
| Positive / buy | #176940 | Gains, buy actions, bids |
| Negative / sell | #a83e42 | Losses, sell actions, asks |
| Warning | #805519 | Delays, pending settlement, risk notices |
| Focus | #397c50 | Visible 2px focus outline, 3px offset |
| Chart grid | #e2e9dd | Low-contrast chart guides |

A green navigation button means an action, not a positive financial outcome. Profit/loss must also use signs, labels, or direction indicators. Bids and asks must have column/section labels. Never use red/green alone to communicate status. Body text must meet WCAG AA 4.5:1 contrast; large text and meaningful graphics 3:1. Borders may be subtle except where needed to identify input controls.

## Typography and numbers

Inter is the shared body/UI family; IBM Plex Mono is reserved for financial values, identifiers, and code. Use tabular numerals. Body and form text: 15-16px, 1.5 line height. Data tables: 13-14px, minimum 12px only for secondary dense metadata. Page titles: 28-36px, weight 600. Panel headings: 16-18px, weight 600. Use sentence case. Avoid wide all-caps labels outside tiny category badges.

Right-align comparable quantities and prices. Keep units in column headers or input suffixes. Do not wrap a number away from its unit. Use the instrument tick/step precision, not arbitrary trailing zeroes. Preserve full precision for editing and API values. Display unavailable values as an em dash, never zero. Format signed changes explicitly. Timestamps must identify UTC when that is the source timezone.

## Geometry and density

Spacing scale: 4, 8, 12, 16, 20, 24, 32, 40, 48px. Controls use 8px corners, panels 12px, dialogs 16px. Prefer a one-pixel border to a shadow. Floating popovers may use a soft green-tinted shadow; do not glow. Standard buttons/inputs: 40-44px high. Touch targets: at least 44px. Desktop data rows: 36-44px. Avoid enormous empty cards and squeezed label highlights.

Align market identity, price, and statistics on a deliberate grid. The asset icon centers against the two-line name/product block. A market name and its product label are not separate inline elements fighting for a baseline. No negative margins for alignment. Desktop terminal shell: 64px header, compact market summary, flexible trading workspace, visible activity tabs. On short screens allow panel scrolling rather than clipping controls or making typography microscopic.

## Icons and botanical artwork

Use a consistent 20px line-icon style with 1.75px strokes, round caps, and an accessible name on icon-only controls. Use recognizable coin symbols inside restrained token-colored circles. Decorative SVG leaves and meadow contours use soft sage/ochre fills and sparse forest-green outlines. Mark them aria-hidden. No raster photos behind the terminal, neon gradients, glass panels, floating particles, or continuous animations.

Botanical graphics belong in the sign-in introduction, landing page, documentation introduction, and selected empty states. The order book, trading ticket, chart, and API code blocks remain plain and focused. Small icons may distinguish search, favorites, products, documentation, and wallet actions.

## Navigation and market picker

The header contains the logo, Trade menu, Earn & invest, Assets, Portfolio, and API access. Show the current area with an understated green underline or soft fill. Preserve predictable keyboard order. Trade lists Spot, Perpetual futures, Expiry futures, and Options.

Desktop market picker: anchored below the selected instrument; approximately 360-420px wide, never wider than the viewport. Open cached rows immediately. Search and Favorites are above a scrollable list. Each compact row has an asset icon, symbol/name, right-aligned price/change, and optional discreet Featured badge. The selected row has an accessible selected state. Escape/outside click dismiss and restore trigger focus. Arrow keys move between rows; Enter selects. On mobile use a bottom sheet with a visible title, close action, safe-area spacing, and the same content. Do not darken the whole desktop trading surface with a blocking market-selection dialog.

## Financial components

Order book: aggregated price levels only; price, quantity, cumulative quantity. Depth fills are subtle, with opaque text above them. No individual-order identities or counts. Preserve best-price ordering and a clear spread/last-trade separator.

Trading ticket: buy/sell segment, order type, labeled inputs, balance controls, fees/value summary, and one primary submit action. Prefill untouched price fields only. Never change a user's price because live data refreshed. Sliders supplement editable exact amounts; Max respects available funds and margin. Submission must show progress, result, and a safe retry path for unknown outcomes.

Options chain: calls left, strikes center, puts right. Expiry buttons stay compact; ATM uses a sage highlight and explicit text. Quote buttons identify buy/sell intent. Long chains scroll inside their panel with sticky headings. Mobile may scroll the data table horizontally without making the entire page overflow.

Cards and tables: use consistent padding and headers; keep actions adjacent to the object they affect. Empty states explain the missing condition and offer a relevant action. Skeletons only indicate loading; do not fabricate values. Keep existing data visible during refresh and label stale data.

Dialogs: clear title, close button, field labels, helper text, inline validation, and a bottom action. Max width 560px for ordinary forms; max height 90dvh with internal scrolling. Respect focus trapping, Escape, and reduced-motion preferences. Destructive or financial actions retain their existing validation and idempotency behavior.

## Responsive and interaction rules

At 1366 x 768 and larger, terminal history tabs and principal controls must remain visible. At widths below 1100px rearrange panels without overlap; at 700px and below stack into a deliberate mobile flow. At 390px no document-level horizontal overflow. Verify 200% zoom: controls remain reachable and content reflows. Tables may scroll horizontally inside labeled containers.

Hover/focus transitions: 100-140ms; sheet/popover appearance: at most 160ms. No delayed input feedback, bouncing elements, or animation of changing account balances. Honor prefers-reduced-motion. Refreshes must preserve focus, selection, scroll position, and edits. Coalesce overlapping network work rather than blanking the page on every update.

## Documentation

Use the same light canvas, forest ink, sage selected navigation, type scale, button shape, and nature graphics. Desktop docs have a compact header, searchable left navigation, readable article column (approximately 75 characters), and a secondary on-page outline. Code uses a light sage background with high-contrast syntax. Endpoint method badges distinguish GET/POST with text, not only color. Warnings explain concrete integration limitations without alarmist decoration.

Every reference page contains authentication, parameters, response structure, example, errors, and lifecycle notes. Do not present unavailable environments or APIs as implemented. Public examples contain only placeholders or deterministic test data. External brand references inform behavior; do not copy their logos or claim protocol compatibility.

## Approved and rejected examples

Approved: forest-green Transfer button on a white wallet card; compact BTC row with coin icon and aligned numbers; sage selected expiry; botanical line art beside an introduction; clearly labeled red negative P&L.

Rejected: an oversized centered modal for switching instruments; black panels left inside a light page; tiny hover rectangles against text; dark chart canvas; pale gray explanatory text; generic blue CTA buttons; leaf artwork behind a financial table; cosmetic reformatting that changes monetary values.

## Maintenance and acceptance checklist

1. Update tokens and this file together; increment their version when the design contract changes.
2. Run the branding synchronization tool and copy its checked release into the docs repository. Never hand-edit generated token CSS.
3. Verify contrast, numerical alignment, icon consistency, focus and error states.
4. Inspect desktop, mobile, and 200% zoom across trading, assets, invest, portfolio, authentication, API access, and docs.
5. Verify pointer and keyboard flows; preserve financial validation and decimal-safe calculations.
6. CI checks the branding release checksums. API changes require the contract export, endpoint documentation, examples, and changelog to accompany the exchange release.

## Trading controls and feedback

Tab labels have at least 16px horizontal and 12px vertical padding, with 44px touch targets. Hover and selection use the entire padded target. History tables use 14px Inter with tabular figures; average execution prices exclude fees and unfilled orders display an em dash.

Account-wide futures leverage lives in a separate card above the ticket. Its dialog shows current and proposed initial margin. Tickets show estimated execution fee with its asset, net spot receipt or derivative order cost, and a backend-sized percentage control.

Routine results use bottom-right notifications: success 4 seconds, errors 8 seconds, paused while hovered or focused. Notifications remain visible above dialogs, are dismissible, and use accessible status/alert announcements. Ambiguous requests retain a persistent safe-retry action. Field validation stays with its input.
