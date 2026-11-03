import {useEffect,useState} from 'react';
import {Link,useLocation} from 'react-router-dom';
import {D,fmt} from './api';
import type {Products,Instrument,Portfolio} from './products';
import Modal from './Modal';

export const productName=(kind:string)=>({Future:'Expiry futures',Perpetual:'Perpetual futures',Option:'Options'}[kind]||kind);
export const instrumentName=(id:string)=>id==='BTC-PERP'?'BTCUSDT':id;
const expiry=(value:string)=>new Date(value).toLocaleDateString('en-GB',{day:'numeric',month:'short',year:'numeric',timeZone:'UTC'});
export function TradeMenu(){
  const[open,setOpen]=useState(false);const location=useLocation();
  useEffect(()=>setOpen(false),[location]);
  return <div className="trade-menu" onBlur={e=>{if(!e.currentTarget.contains(e.relatedTarget))setOpen(false);}} onKeyDown={e=>{if(e.key==='Escape')setOpen(false);}}><button aria-label="Trade" className={location.pathname==='/trade'?'active':''} aria-expanded={open} onClick={()=>setOpen(!open)}>Trade <span>⌄</span></button>{open&&<div className="trade-menu-panel">{['Spot','Perpetual','Future','Option'].map(k=><Link key={k} to={'/trade?product='+k}>{productName(k)}<small>{k==='Spot'?'Buy and sell assets':k==='Perpetual'?'Contracts without expiry':k==='Future'?'Trade a fixed settlement date':'Explore calls and puts'}</small></Link>)}</div>}</div>;
}
export function MarketPicker({products,kind,symbol,choose}:{products:Products|null;kind:string;symbol:string;choose:(id:string)=>void}){
  const[open,setOpen]=useState(false),[search,setSearch]=useState('');
  return <><button className="market-picker-trigger" onClick={()=>{setSearch('');setOpen(true);}} aria-label="Choose market">{instrumentName(symbol)} <span>⌄</span></button>{open&&<Modal title={productName(kind)+' markets'} close={()=>setOpen(false)}><input aria-label="Search markets" autoFocus placeholder="Search asset or expiry" value={search} onChange={e=>setSearch(e.target.value)}/><div className="market-picker-list">{products?.instruments.filter(i=>i.kind===kind&&i.status==='Trading'&&(i.id+' Bitcoin '+(i.expiry?expiry(i.expiry):'')).toLowerCase().includes(search.toLowerCase())).map(i=>{const t=products.tickers.find(t=>t.instrumentId===i.id);return <button key={i.id} onClick={()=>{choose(i.id);setOpen(false);}}><span><strong>{instrumentName(i.id)}</strong><small>Bitcoin {i.expiry?'· '+expiry(i.expiry)+' UTC':''}</small></span><span><strong>{fmt(t?.lastPrice)}</strong><small className={D(t?.change||0).lt(0)?'negative':'positive'}>{t?.change==null?'—':fmt(t.change)+'%'}</small></span><span className="featured">Featured</span></button>;})}</div></Modal>}</>;
}
export function OptionsChain({products,portfolio,symbol,choose}:{products:Products|null;portfolio:Portfolio|null;symbol:string;choose:(id:string,side:string,price:string)=>void}){
  const instruments=products?.instruments.filter(i=>i.kind==='Option'&&i.status==='Trading')||[];
  const expiries=[...new Set(instruments.map(i=>i.expiry!))].sort();
  const[selected,setSelected]=useState(''),[all,setAll]=useState(false);
  const active=selected||instruments.find(i=>i.id===symbol)?.expiry||expiries[0];
  const contracts=instruments.filter(i=>i.expiry===active);
  const strikes=[...new Set(contracts.map(i=>i.strike!))].sort((a,b)=>D(a).cmp(b));
  const index=D(products?.index?.price||0);const nearest=[...strikes].sort((a,b)=>D(a).minus(index).abs().cmp(D(b).minus(index).abs()))[0];
  const visible=all?strikes:strikes.filter((_,i)=>Math.abs(i-strikes.indexOf(nearest))<=3);
  function cells(i:Instrument|undefined){const analytics=products?.optionAnalytics.find(a=>a.instrumentId===i?.id);const t=products?.tickers.find(t=>t.instrumentId===i?.id);return <><td>{fmt(portfolio?.positions.find(p=>p.instrumentId===i?.id)?.quantity||'0',4)}</td><td>{fmt(t?.volume,4)}</td><td>{fmt(analytics?.delta,4)}</td><td>{fmt(analytics?.mark)}<small>{analytics?.iv?fmt(D(analytics.iv).mul(100).toString())+'% IV':'IV unavailable'}</small></td><td>{fmt(t?.bid?.quantity,4)}</td><td><button className="positive" disabled={!i||!t?.bid} onClick={()=>i&&t?.bid&&choose(i.id,'Sell',t.bid.price)}>{fmt(t?.bid?.price)}</button></td><td><button className="negative" disabled={!i||!t?.ask} onClick={()=>i&&t?.ask&&choose(i.id,'Buy',t.ask.price)}>{fmt(t?.ask?.price)}</button></td><td>{fmt(t?.ask?.quantity,4)}</td></>;}
  return <section className="options-chain"><div className="expiry-tabs">{expiries.map(e=><button key={e} className={e===active?'active':''} onClick={()=>setSelected(e)}>{expiry(e)}<small>{Math.max(0,Math.ceil((Date.parse(e)-Date.now())/86400000))} days · 08:00 UTC</small></button>)}</div><div className="chain-toolbar"><strong>Calls</strong><span>Underlying {fmt(products?.index?.price)} USDT</span><button onClick={()=>setAll(!all)}>{all?'Around ATM':'All strikes'}</button><strong>Puts</strong></div><div className="chain-scroll"><table><thead><tr>{['Position','24h vol.','Delta','Mark / IV','Bid size','Bid','Ask','Ask size','Strike','Position','24h vol.','Delta','Mark / IV','Bid size','Bid','Ask','Ask size'].map((t,i)=><th key={i}>{t}</th>)}</tr></thead><tbody>{visible.map(strike=><tr key={strike} className={strike===nearest?'atm':''}>{cells(contracts.find(i=>i.strike===strike&&i.optionSide==='Call'))}<th>{fmt(strike)}{strike===nearest&&<small>ATM</small>}</th>{cells(contracts.find(i=>i.strike===strike&&i.optionSide==='Put'))}</tr>)}</tbody></table>{!contracts.length&&<div className="empty-state">No active options for this expiry.</div>}</div></section>;
}
