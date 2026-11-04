import {useState} from 'react';
import {Link} from 'react-router-dom';
import {D,fmt,type Order} from './api';
import type {Position} from './products';
import {instrumentName,productName} from './MarketNavigation';
import {useCommand} from './hooks';
import Feedback from './CommandFeedback';
import Modal from './Modal';

export const signed=(v:string|null|undefined,places=2)=>v==null?'—':(D(v).gte(0)?'+':'')+fmt(v,places);
const tone=(v:string|null|undefined)=>v==null?'':D(v).gte(0)?'positive':'negative';
// Persistence returns scale-eight decimals. Validate value precision, not the
// serialized zero padding; never round a genuinely off-tick price into a new intent.
const validPrice=(value:string)=>/^\d+(\.\d+)?$/.test(value)&&D(value).gt(0)&&D(value).decimalPlaces()<=2;
const priceInput=(value:string|null|undefined)=>value&&validPrice(value)?D(value).toFixed(2):value||'';
export type PositionEdit={position:Position;leg?:'TakeProfit'|'StopLoss';price?:string};

export function Positions({positions,edit,after}:{positions:Position[];edit:(p:PositionEdit)=>void;after:()=>Promise<void>}){
  const command=useCommand(after);
  return <><table className="positions-table"><thead><tr><th>Instrument / Size</th><th>Entry / Last / Mark</th><th>Unrealized / Realized P&amp;L</th><th>Liquidation price · Mark</th><th>TP / SL</th><th>Actions</th></tr></thead><tbody>{positions.map(p=><tr key={p.lifecycleId}>
    <td><Link to={'/trade?instrument='+encodeURIComponent(p.instrumentId)}>{instrumentName(p.instrumentId)}</Link><small className="table-secondary">{productName(p.kind)} · {p.leverage}×</small><strong className={D(p.quantity).gt(0)?'positive':'negative'}>{D(p.quantity).gt(0)?'Long':'Short'} {fmt(D(p.quantity).abs().toString(),6)} BTC</strong><small className="table-secondary">Value {fmt(p.positionValue)} USDT</small></td>
    <td><strong>Entry {fmt(p.entryPrice)}</strong><small className="table-secondary">Last {fmt(p.lastPrice)}</small><small className="table-secondary">Mark {fmt(p.markPrice)}</small><span className={tone(p.priceChangePercent)}>({signed(p.priceChangePercent)}%)</span></td>
    <td title="Mark-based P&L before fees and funding. Return uses remaining entry margin (options: premium for longs, full cover for shorts)."><strong className={tone(p.unrealizedPnl)}>Unrealized {signed(p.unrealizedPnl)} USDT</strong><small className={'table-secondary '+tone(p.realizedPnl)}>Realized {signed(p.realizedPnl)} USDT</small><small className={'table-secondary '+tone(p.returnOnMarginPercent)}>{signed(p.returnOnMarginPercent)}% ROI</small></td>
    <td title="Approximate cross-margin liquidation price; other marks and collateral held constant.">{p.liquidationPrice?fmt(p.liquidationPrice):'-'}</td>
    <td><div className="protection-summary"><span>TP {p.protection?.status==='Armed'||p.protection?.status==='Closing'&&p.protection.triggerLeg==='Take profit'?fmt(p.protection.takeProfit):'-'}</span><span>SL {p.protection?.status==='Armed'||p.protection?.status==='Closing'&&p.protection.triggerLeg==='Stop loss'?fmt(p.protection.stopLoss):'-'}</span>{p.protection?.status==='Closing'?<><small>Closing position</small><button disabled={command.busy} onClick={()=>void command.run('/stop-closing',{instrument:p.instrumentId,id:p.protection!.id,version:p.protection!.version},'Automatic closing stopped.')}>Stop closing</button></>:<button onClick={()=>edit({position:p})}>Set TP/SL</button>}</div></td>
    <td><div className="position-actions"><button className="text-button" disabled={command.busy} onClick={()=>void command.run('/orders',{side:D(p.quantity).gt(0)?'Sell':'Buy',type:'Market',quantity:D(p.quantity).abs().toFixed(6),price:null,instrument:p.instrumentId,reduceOnly:true},'Market close attempted. Check the remaining position.')}>Market Close</button></div></td>
  </tr>)}</tbody></table><p className="position-footnote">P&amp;L uses mark price; TP/SL uses last trade. Liquidation is approximate: other marks and collateral values are held constant. Actual cross-account risk may change.</p><Feedback command={command}/></>;
}

export function ProtectionDialog({draft,current,after,close}:{draft:PositionEdit;current:Position|undefined;after:()=>Promise<void>;close:()=>void}){
  const p=draft.position;
  const active=p.protection?.status==='Armed';
  const [tp,setTp]=useState(()=>priceInput(draft.leg==='TakeProfit'?draft.price:active?p.protection?.takeProfit:null));
  const [sl,setSl]=useState(()=>priceInput(draft.leg==='StopLoss'?draft.price:active?p.protection?.stopLoss:null));
  const command=useCommand(after);
  const stale=!current||current.lifecycleId!==p.lifecycleId||current.version!==p.version||(current.protection?.version||'0')!==(p.protection?.version||'0');
  const last=current?.lastPrice;const direction=D(p.quantity).gt(0)?1:-1;
  let invalid='';
  for(const [name,value,sign] of [['Take profit',tp,1],['Stop loss',sl,-1]] as const){
    if(!value)continue;
    if(!validPrice(value)){invalid=name+' must be a positive price in increments of 0.01 USDT.';break;}
    if(last&&D(value).minus(last).mul(direction*sign).lte(0)){invalid=name+' is on the wrong side of the last traded price.';break;}
  }
  return <Modal title="Position TP/SL" close={close}><div className="protection-context"><strong>{instrumentName(p.instrumentId)} · {D(p.quantity).gt(0)?'Long':'Short'}</strong><span>{fmt(D(p.quantity).abs().toString(),6)} BTC · Entire position</span><span>Entry {fmt(p.entryPrice)} · Last {fmt(last)} USDT</span></div><form onSubmit={e=>{e.preventDefault();if(!stale&&!invalid)void command.run('/position-protection',{instrument:p.instrumentId,lifecycleId:p.lifecycleId,positionVersion:p.version,protectionVersion:p.protection?.version||'0',takeProfit:priceInput(tp)||null,stopLoss:priceInput(sl)||null},tp||sl?'TP/SL updated.':'TP/SL removed.').then(ok=>{if(ok)close();});}}>
    <label>Take profit · USDT<input autoFocus inputMode="decimal" placeholder="Optional trigger price" value={tp} onChange={e=>setTp(e.target.value)} onBlur={()=>setTp(priceInput(tp))}/></label>
    <label>Stop loss · USDT<input inputMode="decimal" placeholder="Optional trigger price" value={sl} onChange={e=>setSl(e.target.value)} onBlur={()=>setSl(priceInput(sl))}/></label>
    <p className="field-hint">Last trade triggers the first reached leg and cancels its sibling. Automatic closing stays active until flat or stopped, waiting and retrying when liquidity is unavailable. Fees apply to actual fills; execution price is not guaranteed.</p>
    <p className="field-hint">Adding to this direction increases the quantity to close. Reaching zero or reversing ends this protection. You can stop automatic closing from Positions.</p>
    {stale&&<p className="error" role="alert">Position or protection changed. Close this dialog and review again.</p>}{invalid&&<p className="error" role="alert">{invalid}</p>}
    <button className="button full" disabled={command.busy||stale||!!invalid||!last}>Confirm {tp||sl?'TP/SL':'removal'}</button><Feedback command={command}/>
  </form></Modal>;
}

export function AmendDialog({order,price,current,after,close}:{order:Order;price:string;current:Order|undefined;after:()=>Promise<void>;close:()=>void}){
  const [value,setValue]=useState(()=>priceInput(price));const command=useCommand(after);
  const stale=!current||current.version!==order.version||!['Open','PartiallyFilled'].includes(current.status);
  const valid=validPrice(value)&&!D(value).eq(order.price||0);
  return <Modal title="Amend limit price" close={close}><form onSubmit={e=>{e.preventDefault();if(stale||!valid)return;void command.run('/amend-order',{instrument:order.instrumentId,id:order.id,version:order.version,price:priceInput(value)},'Limit order amended.').then(ok=>{if(ok)close();});}}><div className="protection-context"><strong>{order.side} {instrumentName(order.instrumentId)}</strong><span>Remaining {fmt(order.remaining,6)} BTC</span><span>Current limit {fmt(order.price)} USDT</span></div><label>New limit price · USDT<input autoFocus inputMode="decimal" required value={value} onChange={e=>setValue(e.target.value)} onBlur={()=>setValue(priceInput(value))}/></label><p className="field-hint">The remaining quantity gets a new order and new queue priority. Existing fills stay final. A crossing price can execute immediately; balances, margin and fees are checked atomically.</p>{stale&&<p className="error" role="alert">Order changed. Close this dialog and review again.</p>}<button className="button full" disabled={!valid||stale||command.busy}>Confirm price change</button><Feedback command={command}/></form></Modal>;
}
