import type {ReactNode} from 'react';
import Decimal from 'decimal.js';
import {Link} from 'react-router-dom';
import {D,fmt} from './api';
import {AssetPairIcon} from './AssetIcon';
import {ArrowIcon} from './NavigationIcons';
import type {Portfolio,Products} from './products';

const utc=(value:string)=>new Date(value).toISOString().replace('T',' ').slice(0,19);
export default function LiquidityPanel({products,portfolio,feeShare,actions,busy,cancel}:{products:Products|null;portfolio:Portfolio|null;feeShare:string|null;actions:ReactNode;busy:boolean;cancel:(id:string)=>void}){
  const pool=products?.amm,holding=portfolio?.liquidity,fees=portfolio?.liquidityFees,performance=portfolio?.liquidityPerformance;
  const index=products?.priceFresh?products.index?.price:null;
  const share=pool&&D(pool.shares).gt(0)?D(holding?.shares||0).div(pool.shares):D(0);
  const value=performance?.currentValueUsd??null;
  const pnl=performance?.pnlUsd,roi=performance?.returnPercent;
  const sign=(value:string)=>D(value).gt(0)?'+':D(value).lt(0)?'−':'';
  const signedUsd=(value:string|null|undefined)=>value==null?'—':sign(value)+'$'+fmt(D(value).abs().toString());
  const rate=feeShare==null?null:D(feeShare).mul(100);
  const usd=(btc:string|undefined,usdt:string|undefined)=>btc==null||usdt==null?null:D(btc).isZero()?D(usdt).toString():index?D(btc).mul(index).plus(usdt).toString():null;
  const feeValue=usd(fees?.btc??undefined,fees?.usdt??undefined);
  const amounts=(btc:string,usdt:string)=><>{D(btc).gt(0)&&<span>{fmt(btc,8)} BTC</span>}{D(usdt).gt(0)&&<span>{fmt(usdt,8)} USDT</span>}{D(btc).isZero()&&D(usdt).isZero()&&<span>—</span>}</>;
  return <>
    <div className="product-intro"><div className="card-title"><AssetPairIcon/><h2>BTC / USDT liquidity pool</h2></div><p>Provide BTC, USDT, or both to support market-making across spot, futures, and options. {rate?`${fmt(rate.toString())}% of fees from trades with the pool goes to liquidity providers and is automatically reinvested.`:'LP execution fees are automatically reinvested in the pool.'}</p></div>
    <div className="product-cards liquidity-cards">
      <article className="product-card"><div className="card-title"><AssetPairIcon/><h2>Pool reserves</h2></div><dl>
        <dt>Net pool value · USD</dt><dd>{fmt(index?pool?.nav:null)}</dd><dt>BTC</dt><dd>{fmt(pool?.btc,8)}</dd><dt>USDT</dt><dd>{fmt(pool?.usdt,8)}</dd>
        <dt>Pool fees earned · BTC</dt><dd>{fmt(pool?.feesEarnedBtc,8)}</dd><dt>Pool fees earned · USDT</dt><dd>{fmt(pool?.feesEarned,8)}</dd>
      </dl><Link className="outline-button" to="/trade?product=Spot">Trade BTC / USDT <ArrowIcon/></Link></article>
      <article className="product-card" aria-label="Your liquidity"><h2>Your liquidity</h2><div className="product-rate">{value==null?'—':'$'+fmt(value)}<span>Estimated current value · USD</span></div><dl>
        <dt>Total P&amp;L · USD</dt><dd data-testid="lp-pnl" className={pnl==null?'':D(pnl).lt(0)?'negative':D(pnl).gt(0)?'positive':''}><strong>{signedUsd(pnl)}</strong>{roi!=null&&<span className="lp-return"> ({sign(roi)}{fmt(D(roi).abs().toFixed(2,Decimal.ROUND_HALF_UP))}%)</span>}</dd>
        <dt>Total invested · USD</dt><dd data-testid="lp-invested">{fmt(performance?.totalDepositedUsd)}</dd><dt>Total withdrawn · USD</dt><dd data-testid="lp-withdrawn">{fmt(performance?.totalWithdrawnUsd)}</dd>
        <dt>LP share price · USD</dt><dd data-testid="lp-share-price">{fmt(performance?.sharePriceUsd,4)}</dd><dt>Your LP shares</dt><dd>{fmt(holding?.shares||'0',8)}</dd><dt>Pool ownership</dt><dd>{fmt(share.mul(100).toString(),4)}%</dd>
      </dl><div className="card-actions">{actions}</div><p className="product-note">Remove liquidity to realize your share value, including reinvested fees and trading P&amp;L.</p>
        <details className="liquidity-explanation"><summary>How LP performance is calculated</summary><p>Share price can rise or fall with asset prices and the pool’s trading results. P&amp;L = current value + completed withdrawals − total invested. Reinvested fees are already included.</p><p>Completed deposits use their USD value when submitted; withdrawals use their USD value when paid. Pending and cancelled requests are excluded. Return is total P&amp;L divided by total invested, without annualizing.</p></details>
        {performance&&!performance.completeHistory&&<p className="liquidity-coverage">Historical investment values are incomplete, so total P&amp;L is unavailable.</p>}
      </article>
      <article className="product-card liquidity-fees" aria-label="Your fee earnings"><div className="card-title"><h2>Your fee earnings</h2><span className="badge">Automatically reinvested</span></div><dl>
        <dt>BTC fees earned</dt><dd data-testid="lp-fees-btc">{fmt(fees?.btc,8)} BTC</dd><dt>USDT fees earned</dt><dd data-testid="lp-fees-usdt">{fmt(fees?.usdt,8)} USDT</dd>
        <dt>Estimated value · USD</dt><dd data-testid="lp-fees-usd">{feeValue==null?'—':'$'+fmt(feeValue)}</dd>
      </dl><p className="product-note">Attributed using your ownership when each fee was earned. These earnings are included in pool share value, not an additional withdrawable balance; past withdrawals may already include some of them.</p>
        {fees?.since&&<p className="liquidity-coverage">{fees.completeHistory?'Earnings since':'Verified earnings since'} {utc(fees.since)} UTC</p>}
        {fees&&(fees.btc==null||fees.usdt==null)&&<p className="liquidity-coverage">Personal fee history is unavailable. Fees still compound in your pool shares.</p>}
        <details className="liquidity-explanation"><summary>How fees and withdrawals work</summary><p>{rate?`${fmt(rate.toString())}% of each customer fee on a pool trade is credited to the pool; ${fmt(D(100).minus(rate).toString())}% goes to the exchange.`:'The configured LP portion of each customer fee is credited to the pool.'} Your portion depends on your pool ownership at that time.{rate&&` For example, 20% pool ownership earns ${fmt(rate.mul('0.2').toString())}% of the customer fee.`}</p><p>BTC fees stay as BTC and USDT fees stay as USDT inside the pool. Unpaid closing fees are included as USDT receivables until settled. There is no separate claim step. Removing liquidity redeems shares at their then-current value, which also reflects trading gains and losses; it is not a guaranteed payout of the historical fee total.</p></details>
      </article>
    </div>
    <section className="data-panel liquidity-requests"><h2>Liquidity requests</h2><p className="liquidity-table-note">Asset amounts are exact. USD estimates use the current reference index; pending withdrawals use current pool value.</p><div className="table-scroll" role="region" aria-label="Liquidity request history" tabIndex={0}><table><thead><tr><th>Requested · UTC</th><th>Action</th><th>Amount / Asset</th><th>Est. value · USD</th><th>LP shares</th><th>Status / Progress</th><th>Actions</th></tr></thead><tbody>{portfolio?.liquidityRequests.map(r=>{
      const deposit=r.kind==='Deposit',completed=r.status==='Completed';
      const withdrawal=completed?r.received:r.status==='Pending'&&index&&pool?.nav!=null&&D(pool.shares).gt(0)?D(r.shares).mul(pool.nav).div(pool.shares).div(r.asset==='BTC'?index:1).toString():null;
      const estimate=deposit?usd(r.btc,r.usdt):withdrawal==null?null:usd(r.asset==='BTC'?withdrawal:'0',r.asset==='USDT'?withdrawal:'0');
      return <tr key={r.id}><td>{utc(r.createdAt)}{r.completedAt&&<small className="table-secondary">{r.status==='Completed'?'Completed':'Closed'} {utc(r.completedAt)}</small>}</td><td><strong>{deposit?'Add liquidity':'Remove liquidity'}</strong></td>
        <td><div className="liquidity-amounts">{deposit?amounts(r.btc,r.usdt):<span>{fmt(withdrawal,8)} {r.asset}</span>}</div><small className="table-secondary">{deposit?'Submitted assets':completed?'Received':r.status==='Pending'?'Estimated receive':'Requested payout asset'}</small></td>
        <td>{estimate==null?'—':'$'+fmt(estimate)}</td><td>{fmt(r.shares,8)}<small className="table-secondary">{deposit?completed?'Issued':'Not issued':completed?'Redeemed':'Requested'}</small></td><td><strong>{r.status}</strong><small className="table-secondary">{r.detail}</small></td><td>{r.status==='Pending'&&<button disabled={busy} onClick={()=>cancel(r.id)}>Cancel request</button>}</td></tr>;
    })}</tbody></table>{!portfolio?.liquidityRequests.length&&<div className="empty-state">No liquidity requests yet.</div>}</div></section>
  </>;
}
