import { useEffect, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { api, D, fmt, type Market, type Wallet } from './api';
import { useCommand, useLiveRefresh } from './hooks';
import Modal from './Modal';
import BalanceControl from './BalanceControl';
import CommandFeedback from './CommandFeedback';
import type {Portfolio} from './products';
type ChainConfig = { network: string; chainId: string; confirmations: string; generation: string; tokens: Record<string, { address: string; decimals: number }> };
type Movement = { id: string; kind: string; asset: string; amount: string; address: string; status: string; hash: string | null; createdAt: string; block: string | null; error: string | null };
export default function Assets() {
  const [wallets, setWallets] = useState<Wallet[]>([]), [ledger, setLedger] = useState<any[]>([]), [market, setMarket] = useState<Market | null>(null);
  const [movements, setMovements] = useState<Movement[]>([]), [chain, setChain] = useState<ChainConfig | null>(null);
  const [networkError,setNetworkError]=useState(''),[copyNotice,setCopyNotice]=useState('');
  const [portfolio,setPortfolio]=useState<Portfolio|null>(null);
  const [tab, setTab] = useState(localStorage.getItem('wallet-tab') || 'Funding');
  const [modal, setModal] = useState(''), [asset, setAsset] = useState('USDT'), [from, setFrom] = useState('Funding'), [amount, setAmount] = useState('');
  const [address, setAddress] = useState(''), [depositAddress, setDepositAddress] = useState(''), [confirm, setConfirm] = useState(false);
  const [error, setError] = useState(''), [loaded, setLoaded] = useState(false), [query, setQuery] = useState(''), [hideZero, setHideZero] = useState(false), [activity, setActivity] = useState('Transactions');
  const load = async () => {
    try {
      const [w,l,m,p] = await Promise.all([api<Wallet[]>('/balances'), api<any[]>('/ledger'), api<Market>('/market'),api<Portfolio>('/portfolio')]);
      setWallets(w); setLedger(l); setMarket(m);setPortfolio(p); setError(''); setLoaded(true);
      try {const[c,h]=await Promise.all([api<ChainConfig>('/network'),api<Movement[]>('/custody/history')]);setChain(c);setMovements(h);setNetworkError('');}catch{setNetworkError('Network service is unavailable. Balances and internal transfers remain available.');}
    } catch(e) { setError((e as Error).message); }
  };
  useEffect(() => { void load(); }, []);
  const connection = useLiveRefresh(() => void load());
  const command = useCommand(load);
  const to = from === 'Funding' ? 'Unified Trading' : 'Funding';
  const balance = (kind: string, coin: string) => wallets.find(w => w.kind === kind && w.asset === coin) ?? { available:'0', locked:'0' };
  const total = (kind: string, coin: string) => D(balance(kind,coin).available).plus(balance(kind,coin).locked);
  const value = (kind: string) => total(kind,'USDT').plus(market?.lastPrice ? total(kind,'BTC').mul(market.lastPrice) : 0);
  const selected = balance(modal === 'Transfer' ? from : 'Funding',asset);
  const transferable=modal==='Transfer'&&from==='Unified Trading'?(asset==='USDT'?portfolio?.risk.transferableUsdt:portfolio?.risk.transferableBtc)??'0':selected.available;
  const unpriced = !market?.lastPrice && total(tab,'BTC').gt(0);
  const copy=async(text:string)=>{try{await navigator.clipboard.writeText(text);setCopyNotice('Copied to clipboard.');}catch{setCopyNotice('Copy failed. Select and copy the displayed value.');}};
  function open(action: string, coin = 'USDT') {
    setAsset(coin); setAmount(''); setAddress(''); setConfirm(false); setFrom(tab); setModal(action); setDepositAddress('');
    if (action === 'Deposit') void api<{ address: string }>('/custody/address', {}).then(x => setDepositAddress(x.address)).catch(e => setError(e.message));
  }
  async function submit(e: FormEvent) {
    e.preventDefault();
    if (modal === 'Withdraw' && !confirm) { setConfirm(true); return; }
    const success = await command.run(modal === 'Transfer' ? '/transfer' : '/withdraw', modal === 'Transfer' ? { asset, from, to, amount } : { asset, amount, address }, modal === 'Transfer' ? 'Transfer completed.' : 'Withdrawal submitted. Track confirmation below.');
    if(success) setModal('');
  }
  return <main className="assets-page"><div className="page-heading"><div><div className="eyebrow">ACCOUNT OVERVIEW</div><h1>Your assets</h1><p className="muted">A clear view of your balances and capital at work.</p></div><div className="heading-actions"><button className="outline-button" onClick={() => open('Transfer')}>⇄ Transfer</button><Link className="button" to="/trade">Open trading terminal ↗</Link></div></div>
    {error && <p className="error" role="alert">{error} <button className="text-button" onClick={() => void load()}>Retry</button></p>}
    <section className="balance-overview"><div><span className="eyebrow">TOTAL ESTIMATED VALUE</span><div className="balance-amount">{loaded ? '$'+fmt(value('Funding').plus(value('Unified Trading')).toString()) : '…'} <span>USD</span></div><p className="muted">{market?.lastPrice ? `BTC at $${fmt(market.lastPrice)} · Last exchange trade · USDT = $1` : 'USDT = $1 · BTC awaiting its first exchange price'}</p></div><div className="balance-breakdown">{['Funding','Unified Trading'].map(k => <button key={k} onClick={() => { setTab(k); localStorage.setItem('wallet-tab', k); }}><span>{k}</span><strong>${fmt(value(k).toString())}</strong></button>)}</div></section>
    <div className="wallet-tabs" role="tablist" aria-label="Wallets">{['Funding','Unified Trading'].map(k => <button role="tab" aria-selected={tab === k} key={k} onClick={() => { setTab(k); localStorage.setItem('wallet-tab',k); }}>{k}<span>{k === 'Funding' ? 'Hold & move' : 'Trade & invest'}</span></button>)}</div>
    <section className="wallet-panel"><div className="asset-toolbar"><div><h2>{tab}</h2><span className="muted">{unpriced ? 'BTC is excluded from USD totals until a price is available.' : 'Available + reserved assets are included in total value.'}</span></div><div className="asset-filters"><input aria-label="Search assets" placeholder="Search assets" value={query} onChange={e => setQuery(e.target.value)}/><label className="checkbox"><input type="checkbox" checked={hideZero} onChange={e => setHideZero(e.target.checked)}/>Hide zero balances</label></div></div>
    <div className="table-scroll"><table className="asset-table"><thead><tr><th>Asset</th><th>Total balance</th><th>Available</th><th>Reserved</th><th>Estimated USD</th><th>Actions</th></tr></thead><tbody>{['BTC','USDT'].filter(a => a.toLowerCase().includes(query.toLowerCase()) && (!hideZero || total(tab,a).gt(0))).map(a => { const b=balance(tab,a); return <tr key={a} data-testid={`${tab}-${a}`}><td><div className="asset-name"><span className={'asset-symbol '+a.toLowerCase()}>{a === 'BTC' ? '₿':'₮'}</span><div><strong>{a}</strong><span>{a === 'BTC' ? 'Bitcoin':'Tether USD'}</span></div></div></td><td data-testid="total">{fmt(total(tab,a).toString(),8)}</td><td data-testid="available">{fmt(b.available,8)}</td><td data-testid="locked">{fmt(b.locked,8)}</td><td>{a === 'BTC' && !market?.lastPrice ? 'Unpriced' : '$'+fmt(total(tab,a).mul(a === 'BTC' ? market!.lastPrice! : '1').toString())}</td><td><div className="row-actions">{tab === 'Funding' && <><button onClick={() => open('Deposit',a)}>Deposit</button><button onClick={() => open('Withdraw',a)}>Withdraw</button></>}<button onClick={() => open('Transfer',a)}>Transfer</button><Link to={'/trade?side='+(a === 'BTC' ? 'Sell':'Buy')}>Trade ↗</Link></div></td></tr>; })}</tbody></table></div></section>
    <section className="ledger-panel"><div className="history-tabs">{['Transactions','Balance activity'].map(a => <button key={a} className={a === activity ? 'active':''} onClick={() => setActivity(a)}>{a}</button>)}</div><div className="table-scroll"><table><thead><tr>{(activity === 'Transactions' ? ['Time (UTC)','Type','Asset','Amount','Status','Transaction'] : ['Time (UTC)','Activity','Wallet','Asset','Available change','Reserved change']).map(h => <th key={h}>{h}</th>)}</tr></thead><tbody>{activity === 'Transactions' ? movements.map(t => <tr key={t.id}><td>{utc(t.createdAt)}</td><td>{t.kind}</td><td>{t.asset}</td><td>{fmt(t.amount,8)}</td><td><span className={'status-tag '+t.status.toLowerCase()} title={t.error ?? ''}>{t.status}</span></td><td><button className="text-button mono" title={t.hash ?? ''} onClick={() => { if(t.hash) void copy(t.hash); }}>{t.hash ? t.hash.slice(0,10)+'… Copy':'—'}</button></td></tr>) : ledger.map(l => <tr key={l.id}><td>{utc(l.createdAt)}</td><td>{l.reason}</td><td>{l.wallet}</td><td>{l.asset}</td><td>{fmt(l.availableDelta,8)}</td><td>{fmt(l.lockedDelta,8)}</td></tr>)}</tbody></table>{!(activity === 'Transactions' ? movements : ledger).length && <div className="empty-state">No activity yet. Deposit assets to get started.</div>}</div></section>
    <CommandFeedback command={command}/>{networkError&&<p className="risk-banner">{networkError}</p>}{copyNotice&&<p className="notice" role="status">{copyNotice}</p>}<div className="page-status"><span>{connection}</span><span>{chain?.network ?? 'Connecting to network'}</span></div>
    {modal && <Modal title={modal === 'Transfer' ? 'Transfer assets' : modal+' '+asset} close={() => { if(!command.busy) setModal(''); }}>
    {modal === 'Deposit' ? <><p>Send <strong>{asset}</strong> on <strong>{chain?.network}</strong> to your Funding address.</p><label>Deposit address<div className="input-action"><input aria-label="Deposit address" readOnly value={depositAddress || 'Preparing address…'}/><button type="button" className="text-button" disabled={!depositAddress} onClick={() => void copy(depositAddress)}>Copy</button></div></label><div className="network-details"><span>Network</span><strong>{chain?.network}</strong><span>Token contract</span><code>{chain?.tokens[asset]?.address}</code><span>Confirmations</span><strong>{chain?.confirmations}</strong></div><p className="field-hint">Only the selected token contract on this network is supported. Deposits arrive in Funding after confirmation.</p></> : <form onSubmit={submit}>
      <label>Asset<select aria-label="Asset" value={asset} disabled={confirm} onChange={e => setAsset(e.target.value)}><option>BTC</option><option>USDT</option></select></label>
      {modal === 'Transfer' ? <div className="form-row"><label>From<select aria-label="From wallet" value={from} onChange={e => setFrom(e.target.value)}><option>Funding</option><option>Unified Trading</option></select></label><button type="button" aria-label="Swap transfer direction" onClick={() => setFrom(to)}>⇄</button><label>To<input readOnly value={to}/></label></div> : <><p className="network-label">{chain?.network} · Funding wallet · Fee: 0 {asset}</p><label>Destination address<input aria-label="Destination address" required pattern="0x[0-9a-fA-F]{40}" value={address} readOnly={confirm} onChange={e => setAddress(e.target.value)}/></label></>}
      <label>Amount<div className="input-action"><input aria-label="Transfer amount" inputMode="decimal" required pattern="[0-9]+(\.[0-9]{1,8})?" readOnly={confirm} value={amount} placeholder="0.00" onChange={e => setAmount(e.target.value)}/><button type="button" className="text-button" disabled={confirm} onClick={() => setAmount(D(transferable).toFixed(modal === "Withdraw" && asset === "USDT" ? 6 : 8))}>Max</button></div></label>{!confirm&&<BalanceControl max={transferable} value={amount} change={setAmount} places={modal==='Withdraw'&&asset==='USDT'?6:8}/>}<p className="field-hint">Available to transfer: {fmt(transferable,8)} {asset}</p>
      {confirm && <p className="notice">Confirm sending {amount} {asset} to {address} on {chain?.network}. Network transactions cannot be cancelled once broadcast.</p>}
      <CommandFeedback command={command}/><button className="button full" disabled={command.busy || !loaded || (modal === "Withdraw" && !!networkError)}>{command.busy ? 'Processing…' : modal === 'Transfer' ? 'Transfer funds' : confirm ? 'Confirm withdrawal' : 'Review withdrawal'}</button>{confirm && <button type="button" className="text-button" onClick={() => setConfirm(false)}>Edit withdrawal</button>}
    </form>}</Modal>}
  </main>;
}
function utc(s: string) { return new Date(s).toISOString().replace('T',' ').slice(0,19); }
