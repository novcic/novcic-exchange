import AssetIcon from './AssetIcon';
import {Chevron,ProductIcon} from './NavigationIcons';
import {useEffect,useState,useRef} from 'react';
import {Link,useLocation} from 'react-router-dom';
import {D,fmt} from './api';
import type {Products,Instrument,Portfolio} from './products';


export const productName=(kind:string)=>({Future:'Expiry futures',Perpetual:'Perpetual futures',Option:'Options'}[kind]||kind);
export const instrumentName=(id:string)=>id==='BTC-PERP'?'BTCUSDT':id;
const expiry=(value:string)=>new Date(value).toLocaleDateString('en-GB',{day:'numeric',month:'short',year:'numeric',timeZone:'UTC'});
export function TradeMenu(){
  const [open,setOpen]=useState(false),root=useRef<HTMLDivElement>(null),trigger=useRef<HTMLButtonElement>(null);
  const location=useLocation(),active=location.pathname==='/trade';
  const selected=new URLSearchParams(location.search).get('product')||'Spot';
  useEffect(()=>setOpen(false),[location]);
  useEffect(()=>{if(!open)return;const outside=(e:PointerEvent)=>{if(!root.current?.contains(e.target as Node))setOpen(false);};document.addEventListener('pointerdown',outside);return()=>document.removeEventListener('pointerdown',outside);},[open]);
  return <div className="trade-menu" ref={root} onBlur={e=>{if(!e.currentTarget.contains(e.relatedTarget))setOpen(false);}} onKeyDown={e=>{if(e.key==='Escape'){setOpen(false);trigger.current?.focus();}}}>
    <button ref={trigger} aria-label="Trade" className={'header-nav-control'+(active?' active':'')} aria-expanded={open} aria-controls="trade-menu-panel" onClick={()=>setOpen(!open)}><span>Trade</span><Chevron open={open}/></button>
    {open&&<div id="trade-menu-panel" className="trade-menu-panel"><span className="trade-menu-heading">Trading markets</span>{['Spot','Perpetual','Future','Option'].map(k=><Link key={k} to={'/trade?product='+k} aria-current={active&&selected===k?'page':undefined}><span className="product-menu-symbol"><ProductIcon kind={k}/></span><span className="trade-menu-copy"><strong>{productName(k)}</strong><small>{k==='Spot'?'Buy and sell assets':k==='Perpetual'?'Contracts without expiry':k==='Future'?'Trade a fixed settlement date':'Explore calls and puts'}</small></span></Link>)}</div>}
  </div>;
}
export function MarketPicker({products,kind,symbol,choose}:{products:Products|null;kind:string;symbol:string;choose:(id:string)=>void}){
  const[open,setOpen]=useState(false),[search,setSearch]=useState(''),[favoritesOnly,setFavoritesOnly]=useState(false);
  const[favorites,setFavorites]=useState<string[]>(()=>{try{return JSON.parse(localStorage.getItem('novcic-favorites')||'[]');}catch{return [];}});
  const root=useRef<HTMLDivElement>(null),trigger=useRef<HTMLButtonElement>(null),searchInput=useRef<HTMLInputElement>(null);
  const close=()=>{setOpen(false);trigger.current?.focus();};
  useEffect(()=>{if(!open)return;searchInput.current?.focus();const outside=(e:PointerEvent)=>{if(!root.current?.contains(e.target as Node))setOpen(false);};document.addEventListener('pointerdown',outside);return()=>document.removeEventListener('pointerdown',outside);},[open]);
  const items=products?.instruments.filter(i=>i.kind===kind&&i.status==='Trading'&&(!favoritesOnly||favorites.includes(i.id))&&(i.id+' Bitcoin '+(i.expiry?expiry(i.expiry):'')).toLowerCase().includes(search.toLowerCase()))||[];
  return <div className="market-picker" ref={root} onKeyDown={e=>{if(e.key==='Escape')close();if(['ArrowDown','ArrowUp'].includes(e.key)){e.preventDefault();const rows=Array.from(root.current?.querySelectorAll<HTMLButtonElement>('.market-choice')||[]);const current=rows.indexOf(document.activeElement as HTMLButtonElement);rows[(current+(e.key==='ArrowDown'?1:-1)+rows.length)%rows.length]?.focus();}}}>
    <button ref={trigger} className="market-picker-trigger" onClick={()=>setOpen(!open)} aria-label="Choose market" aria-expanded={open} aria-controls="market-popover"><span className="market-picker-label" title={instrumentName(symbol)}>{instrumentName(symbol)}</span><Chevron open={open}/></button>
    {open&&<section id="market-popover" className="market-popover" aria-label={productName(kind)+' markets'}><div className="picker-heading"><strong>{productName(kind)} markets</strong><button onClick={close} aria-label="Close market picker">{'\u00d7'}</button></div><div className="picker-search"><svg aria-hidden="true" viewBox="0 0 24 24"><circle cx="10" cy="10" r="6"/><path d="m15 15 5 5"/></svg><input ref={searchInput} aria-label="Search markets" placeholder="Search assets or expiry" value={search} onChange={e=>setSearch(e.target.value)}/></div><div className="picker-filters"><button aria-pressed={!favoritesOnly} onClick={()=>setFavoritesOnly(false)}>All markets</button><button aria-pressed={favoritesOnly} onClick={()=>setFavoritesOnly(true)}>{'\u2606'} Favorites</button></div><div className="picker-columns"><span>Market</span><span>Price / 24h</span></div><div className="market-picker-list">{items.map(i=>{const t=products?.tickers.find(t=>t.instrumentId===i.id);return <div className={'market-picker-row '+(i.id===symbol?'selected':'')} key={i.id}><button className="favorite-toggle" aria-label={(favorites.includes(i.id)?'Unfavorite ':'Favorite ')+i.id} aria-pressed={favorites.includes(i.id)} onClick={()=>{const next=favorites.includes(i.id)?favorites.filter(v=>v!==i.id):[...favorites,i.id];setFavorites(next);localStorage.setItem('novcic-favorites',JSON.stringify(next));}}>{favorites.includes(i.id)?'\u2605':'\u2606'}</button><button className="market-choice" aria-current={i.id===symbol?'true':undefined} onClick={()=>{choose(i.id);close();}}><AssetIcon asset="BTC" size={28}/><span className="market-choice-name"><strong>{instrumentName(i.id)}</strong><small>{i.expiry?expiry(i.expiry):'Bitcoin'} <span className="featured">Featured</span></small></span><span className="market-choice-price"><strong>{fmt(t?.lastPrice)}</strong><small className={D(t?.change||0).lt(0)?'negative':'positive'}>{t?.change==null?'\u2014':(D(t.change).gte(0)?'+':'')+fmt(t.change)+'%'}</small></span></button></div>;})}{!items.length&&<p className="picker-empty">{products?'No matching markets.':'Loading markets...'}</p>}</div></section>}
  </div>;
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
  return <section className="options-chain"><div className="expiry-tabs">{expiries.map(e=><button key={e} className={e===active?'active':''} onClick={()=>setSelected(e)}>{expiry(e)}<small>{Math.max(0,Math.ceil((Date.parse(e)-Date.now())/86400000))} days / 08:00 UTC</small></button>)}</div><div className="chain-toolbar"><strong>Calls</strong><span>Underlying {fmt(products?.index?.price)} USDT</span><button onClick={()=>setAll(!all)}>{all?'Around ATM':'All strikes'}</button><strong>Puts</strong></div><div className="chain-scroll"><table><thead><tr>{['Position','24h vol.','Delta','Mark / IV','Bid size','Bid','Ask','Ask size','Strike','Position','24h vol.','Delta','Mark / IV','Bid size','Bid','Ask','Ask size'].map((t,i)=><th key={i}>{t}</th>)}</tr></thead><tbody>{visible.map(strike=><tr key={strike} className={strike===nearest?'atm':''}>{cells(contracts.find(i=>i.strike===strike&&i.optionSide==='Call'))}<th>{fmt(strike)}{strike===nearest&&<small>ATM</small>}</th>{cells(contracts.find(i=>i.strike===strike&&i.optionSide==='Put'))}</tr>)}</tbody></table>{!contracts.length&&<div className="empty-state">No active options for this expiry.</div>}</div></section>;
}
