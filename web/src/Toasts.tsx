import {useEffect,useState,useRef} from 'react';
import {createPortal} from 'react-dom';
type Toast={id:number;text:string;error:boolean;retry?:()=>void};
let serial=0;const listeners=new Set<(t:Toast)=>void>();
export function notify(text:string,error=false,retry?:()=>void){const t={id:++serial,text,error,retry};listeners.forEach(fn=>fn(t));}
function Item({item,close}:{item:Toast;close:()=>void}){
  const [paused,setPaused]=useState(false);
  useEffect(()=>{if(paused||item.retry)return;const timer=setTimeout(close,item.error?8000:4000);return()=>clearTimeout(timer);},[paused,item,close]);
  return <div className={'toast '+(item.error?'toast-error':'toast-success')} role={item.error?'alert':'status'} onMouseEnter={()=>setPaused(true)} onMouseLeave={()=>setPaused(false)} onFocus={()=>setPaused(true)} onBlur={()=>setPaused(false)}><span aria-hidden="true">{item.error?'!':'✓'}</span><div>{item.text}{item.retry&&<button onClick={()=>{item.retry?.();close();}}>Retry original request</button>}</div><button aria-label="Dismiss notification" onClick={close}>×</button></div>;
}
export default function Toasts(){
 const ref=useRef<HTMLElement>(null);
 const [items,setItems]=useState<Toast[]>([]);
 useEffect(()=>{const receive=(t:Toast)=>setItems(old=>[...old.filter(i=>i.text!==t.text),t]);listeners.add(receive);return()=>{listeners.delete(receive);};},[]);
 useEffect(()=>{if(items.length)ref.current?.showPopover();else ref.current?.hidePopover();},[items.length]);
 return createPortal(<aside ref={ref} popover="manual" className="toast-stack" aria-label="Notifications">{items.map(item=><Item key={item.id} item={item} close={()=>setItems(old=>old.filter(i=>i.id!==item.id))}/>)}</aside>,document.body);
}
