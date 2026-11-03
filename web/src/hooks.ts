import { useCallback, useEffect, useRef, useState } from 'react';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { api, ApiError } from './api';

export function useLiveRefresh(callback: () => void) {
  const ref = useRef(callback); ref.current = callback;
  const [connection, setConnection] = useState('Connecting');
  useEffect(() => {
    let disposed = false;
    const hub = new HubConnectionBuilder().withUrl('/hubs/market').withAutomaticReconnect().configureLogging(LogLevel.Error).build();
    hub.on('MarketChanged', () => ref.current());
    hub.onreconnecting(() => setConnection('Reconnecting'));
    hub.onreconnected(() => { setConnection('Live'); ref.current(); });
    hub.onclose(() => { if (!disposed) setConnection('Polling'); });
    const connect = async () => { try { await hub.start(); if (!disposed) setConnection('Live'); } catch { if (!disposed) setConnection('Polling'); } };
    void connect();
    const timer = window.setInterval(() => { ref.current(); if (hub.state === 'Disconnected') void connect(); }, 5000);
    return () => { disposed = true; clearInterval(timer); void hub.stop(); };
  }, []);
  return connection;
}
export function useCommand(after: () => void | Promise<void>) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const pending = useRef<{ signature: string; key: string } | null>(null);
  const run = useCallback(async (path: string, body: unknown, success: string) => {
    if (busy) return false;
    const signature = path + JSON.stringify(body);
    if (pending.current && pending.current.signature !== signature) {
      setError('The previous request has an unknown outcome. Retry it unchanged before submitting another request.'); return false;
    }
    pending.current ??= { signature, key: crypto.randomUUID() };
    setBusy(true); setError(''); setNotice('');
    try {
      await api(path, body, pending.current.key); pending.current = null; setNotice(success); await after(); return true;
    } catch (e) {
      if (e instanceof ApiError && e.status < 500) pending.current = null;
      setError((e as Error).message); return false;
    } finally { setBusy(false); }
  }, [busy, after]);
  return { run, busy, error, notice };
}
