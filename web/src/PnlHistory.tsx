import {useEffect,useRef,useState} from 'react';
import {api,D,fmt} from './api';
import {useLiveRefresh} from './hooks';
import {instrumentName,productName} from './MarketNavigation';
import {signed} from './PositionControls';
import type {Products} from './products';
type Row={id:string;instrumentId:string;kind:string;direction:string;quantity:string;averageEntry:string;averageExit:string;grossPnl:string;openingFee:string;closingFee:string;funding:string;netPnl:string;openedAt:string;closedAt:string;durationSeconds:string};
type Page={items:Row[];nextCursor:string|null};
const stamp=(v:string)=>new Date(v).toISOString().replace('T',' ').slice(0,19);
const duration=(s:string)=>{const n=BigInt(s);return `${n/86400n}d ${n%86400n/3600n}h ${n%3600n/60n}m ${n%60n}s`;};
export default function PnlHistory({products}:{products:Products|null}){
 const [product,setProduct]=useState(''),[instrument,setInstrument]=useState(''),[from,setFrom]=useState(''),[to,setTo]=useState('');
 const [pages,setPages]=useState<Page[]>([]),[error,setError]=useState(''),[busy,setBusy]=useState(false);
 const mounted=useRef(false),generation=useRef(0),loading=useRef(false),snapshot=useRef(pages);snapshot.current=pages;
 const filters=new URLSearchParams(Object.fromEntries(Object.entries({product,instrument,from,to}).filter(([,v])=>v)));
 const query=filters.toString();
 const fetchPage=(cursor?:string)=>api<Page>('/pnl-history?'+query+(cursor?'&cursor='+encodeURIComponent(cursor):''));
 const rerun=useRef<()=>void>(()=>{}),pending=useRef(false);
 const refresh=async()=>{
  if(!mounted.current)return;if(loading.current){pending.current=true;return;}loading.current=true;const current=generation.current;
  try{const next:Page[]=[];for(let i=0;i<Math.max(1,snapshot.current.length);i++){const page=await fetchPage(i?next[i-1].nextCursor!:undefined);next.push(page);if(!page.nextCursor)break;}if(current===generation.current){setPages(next);setError('');}}
  catch(e){if(current===generation.current)setError((e as Error).message);}finally{loading.current=false;if(pending.current){pending.current=false;rerun.current();}}
 };
 rerun.current=()=>void refresh();
 useEffect(()=>{mounted.current=true;generation.current++;setPages([]);snapshot.current=[];void refresh();return()=>{mounted.current=false;generation.current++;};},[query]);
 useLiveRefresh(refresh);
 const more=async()=>{const cursor=pages.at(-1)?.nextCursor;if(!cursor||loading.current)return;loading.current=true;setBusy(true);const current=generation.current;try{const page=await fetchPage(cursor);if(current===generation.current){setPages(old=>[...old,page]);setError('');}}catch(e){if(current===generation.current)setError((e as Error).message);}finally{loading.current=false;setBusy(false);if(pending.current){pending.current=false;rerun.current();}}};
 return <div className="pnl-history"><div className="history-filters"><label>Product<select value={product} onChange={e=>{setProduct(e.target.value);setInstrument('');}}><option value="">All derivatives</option>{['Perpetual','Future','Option'].map(k=><option key={k} value={k}>{productName(k)}</option>)}</select></label><label>Instrument<select value={instrument} onChange={e=>setInstrument(e.target.value)}><option value="">All instruments</option>{products?.instruments.filter(i=>i.kind!=='Spot'&&(!product||i.kind===product)).map(i=><option key={i.id} value={i.id}>{instrumentName(i.id)}</option>)}</select></label><label>Closed from · UTC<input type="date" value={from} onChange={e=>setFrom(e.target.value)}/></label><label>Closed through · UTC<input type="date" value={to} onChange={e=>setTo(e.target.value)}/></label></div>
 {error&&<p className="error" role="alert">{error}</p>}<table className="pnl-table"><thead><tr>{['Instrument','Avg entry · USDT','Avg exit · USDT','Qty · BTC','Final P&L · USDT','Opening fee · USDT','Closing fee · USDT','Open time · UTC','Close time · UTC','Duration'].map(t=><th key={t}>{t}</th>)}</tr></thead><tbody>{pages.flatMap(p=>p.items).map(r=><tr key={r.id}><td><strong>{instrumentName(r.instrumentId)}</strong><small className="table-secondary">{productName(r.kind)} · {r.direction}</small></td><td>{fmt(r.averageEntry)}</td><td>{fmt(r.averageExit)}</td><td>{fmt(r.quantity,6)}</td><td><strong className={D(r.netPnl).gte(0)?'positive':'negative'}>{signed(r.netPnl,8)}</strong><details><summary>Breakdown</summary><div>Trading {signed(r.grossPnl,8)}<br/>Fees −{fmt(D(r.openingFee).plus(r.closingFee).toString(),8)}<br/>Funding {signed(r.funding,8)}</div></details></td><td>{fmt(r.openingFee,8)}</td><td>{fmt(r.closingFee,8)}</td><td>{stamp(r.openedAt)}</td><td>{stamp(r.closedAt)}</td><td>{duration(r.durationSeconds)}</td></tr>)}</tbody></table>
 {!pages.some(p=>p.items.length)&&<div className="empty-state">No verified closed positions for these filters.</div>}<div className="pnl-history-footer"><p>Only positions with complete accounting are shown. Final P&amp;L includes execution fees and funding; cash availability may differ.</p>{pages.at(-1)?.nextCursor&&<button disabled={busy} onClick={()=>void more()}>{busy?'Loading…':'Load more'}</button>}</div></div>;
}
