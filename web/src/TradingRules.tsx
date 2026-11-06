import {useEffect,useState} from 'react';
import {Link} from 'react-router-dom';
import {api,D} from './api';
import AssetIcon from './AssetIcon';
import {ArrowIcon} from './NavigationIcons';
import type {AmmFees} from './orderSizing';

type TradingConfig={priceTick:string;quantityStep:string;amm:AmmFees};
const topics=[['collateral','Collateral ratios'],['margin','Margin & balances'],['liquidation','Liquidation'],['prices','Index & mark prices'],['orders','Order execution'],['fees','Trading fees'],['settlement','Funding & settlement']];
const percent=(value:string|undefined)=>value==null?'—':D(value).mul(100).toString()+'%';

export default function TradingRules(){
  const [config,setConfig]=useState<TradingConfig|null>(null),[error,setError]=useState(''),[attempt,setAttempt]=useState(0);
  useEffect(()=>{const previous=document.title;document.title='Trading Rules · Novčić';return()=>{document.title=previous;};},[]);
  useEffect(()=>{
    let active=true;setError('');
    void api<TradingConfig>('/config').then(value=>{if(active)setConfig(value);}).catch(()=>{if(active)setError('Current fee rates and order increments could not be loaded.');});
    return()=>{active=false;};
  },[attempt]);
  return <main className="trading-rules-page">
    <div className="page-heading"><div><div className="eyebrow">NOVČIĆ / MARKET INFORMATION</div><h1>Trading Rules</h1><p className="muted">Collateral, margin and execution rules for spot, perpetuals, expiry futures and options.</p></div><Link className="outline-button" to="/trade?product=Perpetual">Open trading terminal <ArrowIcon/></Link></div>
    <div className="rules-layout">
      <nav className="rules-nav" aria-label="Trading rules topics"><span>On this page</span>{topics.map(([id,label])=><a key={id} href={'#'+id}>{label}</a>)}</nav>
      <div className="rules-content">
        <section id="collateral" className="rules-section" aria-labelledby="collateral-title">
          <div className="rules-section-heading"><span>01</span><h2 id="collateral-title">Collateral value ratios</h2></div>
          <p>Cross margin uses eligible assets in Unified Trading. The collateral value ratio determines how much of each asset’s value counts toward your margin balance.</p>
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Collateral ratios table"><table><thead><tr><th>Asset</th><th>Collateral value ratio</th><th>Valuation</th></tr></thead><tbody>
            <tr><td><span className="rules-asset"><AssetIcon asset="USDT"/>USDT</span></td><td className="rules-number">100%</td><td>1 USDT = $1</td></tr>
            <tr><td><span className="rules-asset"><AssetIcon asset="BTC"/>BTC</span></td><td className="rules-number">90%</td><td>BTC reference index × 90%</td></tr>
          </tbody></table></div>
          <p>BTC worth $1,000 contributes $900 before other account adjustments. Funding balances are excluded. Reserved assets and BTC committed to covered calls cannot also support new futures exposure.</p>
        </section>

        <section id="margin" className="rules-section" aria-labelledby="margin-title">
          <div className="rules-section-heading"><span>02</span><h2 id="margin-title">Margin & balances</h2></div>
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Margin requirements table"><table><thead><tr><th>Parameter</th><th>Rule</th></tr></thead><tbody>
            <tr><th scope="row">Margin mode</th><td>Cross margin across Unified Trading. Futures leverage is account-wide, from 1× to 10×.</td></tr>
            <tr><th scope="row">Total equity</th><td>Wallet value + unsettled realized cash + unrealized futures P&amp;L + option value, before collateral ratios.</td></tr>
            <tr><th scope="row">Margin balance</th><td>Eligible collateral after ratios and reservations, adjusted for realized cash, marked P&amp;L and option coverage.</td></tr>
            <tr><th scope="row">Available balance</th><td>Margin balance − initial margin. A negative value shows a margin deficit; order quantities and transferable amounts stay nonnegative.</td></tr>
            <tr><th scope="row">Initial margin (IM)</th><td>Futures position: absolute BTC size × mark price ÷ leverage. Opening futures orders use remaining size × index price ÷ leverage, plus estimated execution fees. Reduce-only orders reserve no new margin or fee capacity. Options have separate coverage requirements.</td></tr>
            <tr><th scope="row">Maintenance margin (MM)</th><td>Futures positions: absolute BTC size × mark price × 0.5%.</td></tr>
            <tr><th scope="row">Initial margin rate (IMR)</th><td>IM ÷ margin balance × 100%. New exposure must leave IMR at or below 100% and meet collateral and fee requirements.</td></tr>
            <tr><th scope="row">Maintenance margin rate (MMR)</th><td>MM ÷ margin balance × 100%. Liquidation starts at 100% or higher when references are fresh.</td></tr>
          </tbody></table></div>
          <div className="rules-example"><strong>Available balance example</strong><span>$529.21 margin balance − $602.19 IM = <b>−$72.98</b></span></div>
          <p>A losing USDT perpetual can produce negative USDT equity with a zero USDT wallet balance. BTC collateral may still keep total account equity positive. Transferable amounts account for current collateral commitments and can be lower than wallet balances.</p>
          <p>IMR above 100% prevents additional exposure; it does not alone trigger liquidation. With positive requirements and a zero or negative margin balance, the account rates display ≥100% instead of a negative percentage. Missing price references display an em dash.</p>
        </section>

        <section id="liquidation" className="rules-section" aria-labelledby="liquidation-title">
          <div className="rules-section-heading"><span>03</span><h2 id="liquidation-title">Liquidation rules</h2></div>
          <ol className="rules-steps">
            <li><strong>Assess account risk.</strong> Futures liquidation is triggered when margin balance reaches or falls below maintenance margin. A negative margin balance or insufficient option coverage can also restrict the account.</li>
            <li><strong>Cancel orders that can increase exposure.</strong> Open orders without reduce-only protection are cancelled to release commitments.</li>
            <li><strong>Attempt partial reductions.</strong> Each intervention attempts to close 25% of each remaining futures position, rounded up to the quantity step, through reduce-only market orders. The account is checked again on subsequent cycles.</li>
          </ol>
          <p>Liquidation requires fresh reference prices and executable liquidity. An attempt can be partially filled or remain unfilled. Remaining exposure is still subject to price movements.</p>
          <p>The position’s displayed liquidation price is an approximate <strong>mark-price</strong> threshold, holding other marks and collateral values constant. Actual liquidation depends on account risk, not whether the last trade crosses that estimate.</p>
        </section>

        <section id="prices" className="rules-section" aria-labelledby="prices-title">
          <div className="rules-section-heading"><span>04</span><h2 id="prices-title">Index price & mark price</h2></div>
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Price calculations table"><table><thead><tr><th>Price</th><th>Calculation</th><th>Used for</th></tr></thead><tbody>
            <tr><th scope="row">Last-traded price</th><td>The latest completed trade on the selected Novčić market.</td><td>Candles, price movement and TP/SL triggers.</td></tr>
            <tr><th scope="row">Reference index</th><td>The average of valid Bybit BTC/USDT spot and perpetual midquotes. Each midquote is (best bid + best ask) ÷ 2. Valid spot alone is used if the perpetual contribution is unavailable or divergent.</td><td>BTC collateral valuation, price bands, order margin and settlement reference.</td></tr>
            <tr><th scope="row">Futures mark</th><td>The simple average of this contract’s local trade prices over the last 150 seconds, bounded to 99–101% of the index. Without trades in that window, the mark equals the index.</td><td>Unrealized futures P&amp;L, position margin and liquidation risk.</td></tr>
            <tr><th scope="row">Option mark</th><td>A Black–Scholes value using the reference index, strike, time to expiry and supported Bybit implied volatility, with zero carry. Volatility is interpolated within supported strikes and expiries.</td><td>Option valuation and quoting. Unsupported or stale volatility makes the displayed mark unavailable.</td></tr>
          </tbody></table></div>
          <details><summary>Reference validation and freshness</summary><ul>
            <li>Index updates are attempted once per second. The required index must be no more than five seconds old.</li>
            <li>A source quote is rejected if its bid is nonpositive, its ask is below its bid, or its spread exceeds 1% of the bid.</li>
            <li>The perpetual contribution must be fresh and within 2% of the spot midquote. A valid spot quote is required to publish an index.</li>
            <li>Option volatility observations must be no more than 60 seconds old; unsupported strikes or expiries are not extrapolated.</li>
          </ul></details>
          <p>When the index is stale, derivative order placement—including reduce-only orders—and liquidation attempts wait for a fresh reference. You can still cancel orders and transfer collateral into Unified Trading. A last trade can differ substantially from the mark because the mark remains bounded to the reference index.</p>
        </section>

        <section id="orders" className="rules-section" aria-labelledby="orders-title">
          <div className="rules-section-heading"><span>05</span><h2 id="orders-title">Order execution & position protection</h2></div>
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Order parameters table"><table><thead><tr><th>Parameter</th><th>Rule</th></tr></thead><tbody>
            <tr><th scope="row">Price increment</th><td>{config?D(config.priceTick).toString()+' USDT':'—'}</td></tr>
            <tr><th scope="row">Quantity increment</th><td>{config?D(config.quantityStep).toFixed(6)+' BTC':'—'}</td></tr>
            <tr><th scope="row">Futures price limits</th><td>Buy limits must be at or below 110% of the reference index; sell limits must be at or above 90%. Lower buys and higher sells are allowed. Orders beyond their own side’s limit are rejected without changing the requested price.</td></tr>
            <tr><th scope="row">Futures execution limits</th><td>Buys cannot fill above 110% of the current reference; sells cannot fill below 90%, including market and reduce-only orders. Matching stops at that limit and cancels an unfilled market remainder. Valid passive orders stay on the book; resting orders that breach their own side’s limit after the reference moves are cancelled when encountered.</td></tr>
            <tr><th scope="row">Matching</th><td>Best price first, then time priority. Orders execute against actual resting liquidity; self-trades are prevented.</td></tr>
            <tr><th scope="row">Market orders</th><td>Fill against eligible resting orders. Any unfilled remainder is cancelled. The last trade and order preview are not guaranteed execution prices.</td></tr>
            <tr><th scope="row">Reduce-only</th><td>Must reduce the existing position and cannot exceed its current size. A market close is one attempt; it can leave a remaining position.</td></tr>
            <tr><th scope="row">TP / SL</th><td>Triggered by the last trade. The first reached leg cancels the other and starts repeated reduce-only market close attempts until the position closes or automatic closing is stopped.</td></tr>
            <tr><th scope="row">Limit amendments</th><td>Replace the remaining order quantity at the new price with new time priority. A crossing amendment can execute immediately.</td></tr>
          </tbody></table></div>
          <p>Execution fees apply to actual fills. Opening futures and options orders require USDT for fees even when BTC provides margin collateral. Reduce-only closes use available USDT first; any unpaid fee remains in signed USDT settlement and does not block the close.</p>
        </section>

        <section id="fees" className="rules-section" aria-labelledby="fees-title">
          <div className="rules-section-heading"><span>06</span><h2 id="fees-title">Trading fees</h2></div>
          <p>Current rates from the exchange configuration. Customer maker and taker fills use the same rates.</p>
          {error&&<div className="rules-load-error" role="status">{error} <button className="text-button" onClick={()=>setAttempt(n=>n+1)}>Retry</button></div>}
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Execution fees table"><table><thead><tr><th>Product</th><th>Execution fee</th><th>Fee asset</th></tr></thead><tbody>
            <tr><th scope="row">Spot</th><td data-testid="rules-spot-fee">{percent(config?.amm.spotFee)} of the received asset</td><td>BTC for buys; USDT for sells</td></tr>
            <tr><th scope="row">Perpetual & expiry futures</th><td data-testid="rules-futures-fee">{percent(config?.amm.futuresFee)} of executed notional</td><td>USDT</td></tr>
            <tr><th scope="row">Options</th><td>{percent(config?.amm.optionFee)} of underlying index notional, capped at {percent(config?.amm.optionPremiumCap)} of executed premium</td><td>USDT</td></tr>
          </tbody></table></div>
        </section>

        <section id="settlement" className="rules-section" aria-labelledby="settlement-title">
          <div className="rules-section-heading"><span>07</span><h2 id="settlement-title">Funding, options & expiry settlement</h2></div>
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Funding and settlement table"><table><thead><tr><th>Parameter</th><th>Rule</th></tr></thead><tbody>
            <tr><th scope="row">Settlement asset</th><td>USDT for futures P&amp;L, perpetual funding, and option premiums and settlement.</td></tr>
            <tr><th scope="row">Perpetual funding</th><td>Eight-hour intervals at 00:00, 08:00 and 16:00 UTC. The rate is the previous interval’s average local trade price ÷ time-weighted index − 1, capped at ±0.05%.</td></tr>
            <tr><th scope="row">Funding payment</th><td>Uses position ownership at the interval boundary. A positive rate means longs pay shorts; a negative rate reverses the payment. Missing index-window coverage or no trades produces a zero rate.</td></tr>
            <tr><th scope="row">Expiry</th><td>Weekly and monthly contracts expire on Friday at 08:00 UTC. The monthly expiry is the last Friday. Each instrument shows its specific expiry.</td></tr>
            <tr><th scope="row">Delivery price</th><td>The time-weighted local index over the preceding 30 minutes. If downtime leaves missing coverage or a gap over five seconds, recovery uses the average of all 30 closed one-minute Bybit BTCUSDT index candles from that same window. This historical fallback differs from the local index; the settlement records its source.</td></tr>
            <tr><th scope="row">Recovery after downtime</th><td>Expired contracts stop trading and await settlement while historical data is recovered automatically. Incomplete data is retried; positions and collateral remain reserved. Pool quotes resume after its expired positions settle and live pricing is available. Settlement does not use the restart-time price.</td></tr>
            <tr><th scope="row">Option coverage</th><td>European exercise at expiry. Longs pay the premium; short calls require BTC cover and short puts require the full strike value in USDT. Cover cannot be reused for new exposure.</td></tr>
            <tr><th scope="row">Realized cash</th><td>Profits can remain unsettled until corresponding obligations fund the clearing pool. A positive equity or cash claim does not guarantee immediately transferable USDT.</td></tr>
          </tbody></table></div>
        </section>
        <p className="rules-footer">These are Novčić’s current trading rules. For API details, see the <a href="https://novcic.github.io/docs/guides/risk/" target="_blank" rel="noopener noreferrer">risk and settlement guide <ArrowIcon/></a>.</p>
      </div>
    </div>
  </main>;
}
