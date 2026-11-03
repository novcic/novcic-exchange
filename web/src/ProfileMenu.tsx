import {useEffect,useRef,useState} from 'react';
import {Link,useLocation} from 'react-router-dom';
import {logout,type User} from './api';
import {Avatar} from './Profile';
import {notify} from './Toasts';

export default function ProfileMenu({user}:{user:User}){
 const [open,setOpen]=useState(false),root=useRef<HTMLDivElement>(null),trigger=useRef<HTMLAnchorElement>(null),pointer=useRef('mouse');
 const location=useLocation();
 useEffect(()=>setOpen(false),[location.pathname]);
 useEffect(()=>{function outside(e:PointerEvent){if(!root.current?.contains(e.target as Node))setOpen(false);}document.addEventListener('pointerdown',outside);return()=>document.removeEventListener('pointerdown',outside);},[]);
 return <>
  <div className="profile-menu" ref={root}
   onPointerLeave={e=>{if(e.pointerType==='mouse')setOpen(false);}}
   onFocus={()=>{if(pointer.current!=='touch')setOpen(true);}}
   onBlur={e=>{if(!e.currentTarget.contains(e.relatedTarget))setOpen(false);}}
   onKeyDown={e=>{pointer.current='keyboard';if(e.key==='Escape'){e.preventDefault();trigger.current?.focus();setOpen(false);}if(e.key==='ArrowDown'&&e.target===trigger.current){e.preventDefault();setOpen(true);}}}>
   <Link ref={trigger} className="profile-avatar-link" to="/profile" aria-label="My profile" aria-expanded={open} aria-controls="account-menu"
    onPointerEnter={e=>{if(e.pointerType==='mouse')setOpen(true);}}
    onPointerDown={e=>{pointer.current=e.pointerType;}}
    onClick={e=>{if(pointer.current==='touch'){e.preventDefault();setOpen(v=>!v);}}}><Avatar id={user.avatar}/></Link>
   {open&&<div id="account-menu" className="profile-dropdown"><Link to="/profile" className="profile-card"><Avatar id={user.avatar}/><div><strong>{user.username}</strong><span>{user.email}</span></div></Link><nav aria-label="Account"><Link to="/api-access">My API keys</Link><Link to="/assets">My assets</Link><Link to="/portfolio">My portfolio</Link><Link to="/profile/security">Security</Link></nav><button className="profile-signout" onClick={()=>void logout().catch(e=>notify(e.message,true))}>Sign out</button></div>}
  </div>
  <Link className="my-assets-link" to="/assets">My assets</Link>
 </>;
}
