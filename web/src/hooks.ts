import {notify} from './Toasts';
import { useCallback, useEffect, useRef, useState } from 'react';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { api, ApiError, currentUser } from './api';

export function useLiveRefresh(callback: () => void | Promise<void>) {
  const ref = useRef(callback); ref.current = callback;
  const [connection, setConnection] = useState('Connecting');
  useEffect(() => {
    let disposed = false, running = false, queued = false;
    const update = async () => {
      if(disposed)return;
      if(running){queued=true;return;}
      running=true;
      try{do{queued=false;await ref.current();}while(queued&&!disposed);}finally{running=false;}
    };
    const hub = new HubConnectionBuilder().withUrl('/hubs/market').withAutomaticReconnect().configureLogging(LogLevel.Error).build();
    hub.on('MarketChanged', () => void update());
    hub.onreconnecting(() => setConnection('Reconnecting'));
    hub.onreconnected(() => { setConnection('Live'); void update(); });
    hub.onclose(() => { if (!disposed) setConnection('Polling'); });
    const connect = async () => { try { await hub.start(); if (!disposed) setConnection('Live'); } catch { if (!disposed) setConnection('Polling'); } };
    void connect();
    const timer = window.setInterval(() => { void update(); if (hub.state === 'Disconnected') void connect(); }, 5000);
    return () => { disposed = true; clearInterval(timer); void hub.stop(); };
  }, []);
  return connection;
}
export function useCommand(after: () => void | Promise<void>) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const inFlight = useRef(false);
  const [canRetry,setCanRetry]=useState(()=>!!sessionStorage.getItem('novcic-pending:'+(currentUser()?.id||'anonymous')));
  type Pending={signature:string;key:string;path:string;body:unknown;success:string};
  const storage=()=> 'novcic-pending:'+ (currentUser()?.id||'anonymous');
  const read=():Pending|null=>{try{return JSON.parse(sessionStorage.getItem(storage())||'null');}catch{return null;}};
  const run = useCallback(async (path: string, body: unknown, success: string) => {
    if (inFlight.current) return false;
    const signature = path + JSON.stringify(body);
    let pending=read();
    if (pending && pending.signature !== signature) {
      setCanRetry(true);setError('The previous request has an unknown outcome. Retry it unchanged before submitting another request.'); notify('A previous request needs reconciliation.',true,()=>void run(pending!.path,pending!.body,pending!.success)); return false;
    }
    pending ??= { signature, key: crypto.randomUUID(),path,body,success };
    sessionStorage.setItem(storage(),JSON.stringify(pending));
    inFlight.current=true;setBusy(true); setError(''); setNotice('');setCanRetry(false);
    try {
      const result=await api<{cancelled?:string}>(path, body, pending.key); sessionStorage.removeItem(storage()); setNotice(success); notify(path==='/cancel-all'?`${result.cancelled} orders cancelled.`:success); try{await after();}catch{notify("Completed. Refresh the page to see updated data.",true);} return true;
    } catch (e) {
      if (e instanceof ApiError && e.status < 500)sessionStorage.removeItem(storage());
      setCanRetry(!!read());
      setError((e as Error).message); notify((e as Error).message,true,read()?()=>void run(pending!.path,pending!.body,pending!.success):undefined); return false;
    } finally { inFlight.current=false;setBusy(false); }
  }, [after]);
  const retry=async()=>{const pending=read();return pending?run(pending.path,pending.body,pending.success):false;};
  return { run, retry, canRetry, busy, error, notice };
}
