import {D} from './api';
export default function BalanceControl({max,value,change,places=8}:{max:string;value:string;change:(v:string)=>void;places?:number}){
  const limit=D(max);
  let percent='0';try{if(limit.gt(0))percent=D(value||0).div(limit).mul(100).clamp(0,100).toFixed(0);}catch{}
  const select=(p:string)=>change(limit.mul(p).div(100).toFixed(places));
  return <div className="balance-control"><input aria-label="Percentage of available balance" type="range" min="0" max="100" step="1" value={percent} disabled={limit.lte(0)} onChange={e=>select(e.target.value)}/><div>{['0','25','50','75','100'].map(p=><button type="button" disabled={limit.lte(0)} key={p} onClick={()=>select(p)}>{p==='100'?'Max':p+'%'}</button>)}</div></div>;
}
