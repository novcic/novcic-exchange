import {useEffect,useState} from 'react';
import {api,D,fmt} from './api';
import type {Portfolio} from './products';
import {useCommand} from './hooks';
import Modal from './Modal';
export default function LeverageCard({portfolio,after}:{portfolio:Portfolio;after:()=>Promise<void>}){
 const [open,setOpen]=useState(false),[value,setValue]=useState(portfolio.leverage||'1');const command=useCommand(after);
 const [margin,setMargin]=useState<string|null>(null),[error,setError]=useState('');
 useEffect(()=>{if(!open)return;let active=true;setMargin(null);setError('');const timer=setTimeout(()=>void api<{risk:{initialMargin:string}}>('/leverage-preview',{leverage:value}).then(p=>{if(active)setMargin(p.risk.initialMargin);}).catch(e=>{if(active)setError(e.message);}),250);return()=>{active=false;clearTimeout(timer);};},[open,value]);
 const positions=portfolio.positions.filter(p=>!D(p.quantity).isZero());
 return <><section className="leverage-card"><div><strong>Cross leverage</strong><small>Unified Trading · All futures</small></div><button onClick={()=>{setValue(portfolio.leverage||'1');setOpen(true);}}>{portfolio.leverage||'1'}× <span aria-hidden="true">✎</span></button></section>{open&&<Modal title="Account cross leverage" close={()=>setOpen(false)}><p>Applies to all perpetual and expiry futures, including existing positions and open orders.</p><label>Leverage <strong>{value}×</strong><input aria-label="Account leverage" type="range" min="1" max="10" step="1" value={value} onChange={e=>setValue(e.target.value)}/></label><div className="balance-presets">{[1,2,3,5,10].map(n=><button key={n} onClick={()=>setValue(String(n))}>{n}×</button>)}</div><p>Current initial margin: <strong>{fmt(portfolio.risk.initialMargin)} USDT</strong></p><p>Proposed initial margin: <strong>{margin?fmt(margin)+' USDT':'—'}</strong></p>{error&&<p role="alert" className="error">{error}</p>}<p className="field-hint">{positions.length} active positions. Lower leverage requires more collateral. The exchange validates the entire account before applying your change.</p><button className="button full" disabled={command.busy||!margin||!!error} onClick={()=>void command.run('/leverage',{leverage:value},'Account leverage updated.').then(ok=>{if(ok)setOpen(false);})}>{command.busy?'Updating…':'Apply '+value+'× leverage'}</button></Modal>}</>;
}
