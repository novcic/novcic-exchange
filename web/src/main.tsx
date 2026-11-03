import {ArrowIcon} from './NavigationIcons';
import Profile from './Profile';
import ProfileMenu from './ProfileMenu';
import Toasts from './Toasts';
import React, { createContext, useContext, useEffect, useState, useSyncExternalStore, type FormEvent } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter, Link, NavLink, Navigate, Route, Routes, useNavigate } from 'react-router-dom';
import countries from 'i18n-iso-countries';
import english from 'i18n-iso-countries/langs/en.json';
import '@fontsource/inter/latin-400.css';
import '@fontsource/inter/latin-500.css';
import '@fontsource/inter/latin-600.css';
import '@fontsource/inter/latin-700.css';
import '@fontsource/ibm-plex-mono/latin-600.css';
import '@fontsource/ibm-plex-mono/latin-700.css';
import '@fontsource/ibm-plex-mono/latin-400.css';
import 'flag-icons/css/flag-icons.min.css';
import { api, authenticate, currentUser, D, fmt, logout, refresh, subscribe, type Candle, type Market, type Order, type Trade, type Wallet } from './api';
import { useCommand, useLiveRefresh } from './hooks';
import Chart from './Chart';
import Assets from './Assets';
import Terminal from './TradingTerminal';
import {TradeMenu} from './MarketNavigation';
import Invest from './Invest';
import PortfolioPage from './PortfolioPage';
import ApiAccess from './ApiAccess';
import './tokens.css';
import './layout.css';
import './meadow.css';
countries.registerLocale(english);
const countryList = Object.entries(countries.getNames('en', { select: 'official' })).sort((a, b) => a[1].localeCompare(b[1]));
function App() {
  const user = useSyncExternalStore(subscribe, currentUser);
  const [ready, setReady] = useState(false); const [startupError, setStartupError] = useState('');
  const [signoutError, setSignoutError] = useState('');
  const boot = () => { setStartupError(''); void refresh()
    .then(() => setReady(true)).catch(e => setStartupError(e.message)); };
  useEffect(boot, []);
  if (!ready) return <main className="startup"><Logo/><p role="status">{startupError || 'Connecting to Novčić…'}</p>{startupError && <button onClick={boot}>Retry connection</button>}</main>;
  return <BrowserRouter><Toasts/><header className="header"><Link to="/" aria-label="Novčić home"><Logo/></Link><nav className="primary-nav" aria-label="Main navigation"><TradeMenu/><NavLink className="header-nav-control" to="/invest">Earn & invest</NavLink></nav><div className="header-right">{user ? <ProfileMenu user={user}/> : <><Link to="/login" className="login-link">Sign in</Link><Link to="/register" className="button small">Create account</Link></>}</div></header>
    {signoutError && <div className="global-error" role="alert">{signoutError}</div>}
    <Routes><Route path="/profile/*" element={user?<Profile/>:<Navigate to="/login" replace/>}/><Route path="/" element={<Landing/>}/><Route path="/register" element={user ? <Navigate to="/assets" replace/> : <Auth register/>}/><Route path="/login" element={user ? <Navigate to="/assets" replace/> : <Auth/>}/><Route path="/assets" element={user ? <Assets/> : <Navigate to="/login" replace/>}/><Route path="/trade" element={<Terminal/>}/><Route path="/invest" element={<Invest/>}/><Route path="/portfolio" element={user ? <PortfolioPage/> : <Navigate to="/login" replace/>}/><Route path="/api-access" element={user ? <ApiAccess/> : <Navigate to="/login" replace/>}/><Route path="*" element={<Navigate to="/" replace/>}/></Routes>
  </BrowserRouter>;
}
function Logo() { return <span className="logo"><span className="logo-mark">N</span>novčić<span className="logo-dot">.</span></span>; }
function Landing() {
  return <><main className="landing"><div className="eyebrow">DIGITAL ASSET EXCHANGE / BTC · USDT</div><section className="intro"><div><h1>A clear view.<br/>A precise execution.</h1><p>Spot and derivatives. Funding and trading accounts. A clear view of every order, position, and asset.</p><div className="actions"><Link className="button" to={currentUser() ? '/assets' : '/register'}>Open your account <ArrowIcon/></Link><Link className="outline-button" to="/trade">Explore the terminal</Link></div></div><div className="market-spec"><div className="spec-top"><span>01 / SPOT MARKET</span><span className="badge">BTC/USDT</span></div><div className="pair-display">BTC<span>/ USDT</span></div><div className="spec-row"><span>Order execution</span><strong>Price · Time priority</strong></div><div className="spec-row"><span>Order types</span><strong>Limit / Market</strong></div><div className="spec-row"><span>Trading fee</span><strong>0.10% spot</strong></div><div className="spec-row"><span>Settlement</span><strong>Atomic</strong></div></div></section><section className="principles"><article><span className="index">01</span><h2>Every order visible.</h2><p>Real participant orders. Aggregated price levels. A transparent view of available liquidity.</p></article><article><span className="index">02</span><h2>Your assets, organized.</h2><p>Keep Funding separate from Unified Trading. Transfer available balances when you are ready to trade.</p></article><article><span className="index">03</span><h2>Built for precision.</h2><p>Exact decimal accounting, reserved order funds, and a complete record of every balance movement.</p></article></section></main><footer><Logo/><span>Precision in every transaction.</span><span>BTC/USDT · Spot</span></footer></>;
}
function CountrySelect({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const [search, setSearch] = useState(''); const [open, setOpen] = useState(false);
  const selected = countryList.find(([code]) => code === value);
  return <div className="country-picker"><label id="country-label">Country of residence</label><button type="button" className="country-toggle" aria-labelledby="country-label" aria-expanded={open} onClick={() => setOpen(!open)}>{selected ? <><span className={'fi fi-' + value.toLowerCase()}/>{selected[1]}</> : 'Select your country'}<span className="chevron">⌄</span></button>{open && <div className="country-menu"><input autoFocus aria-label="Search countries" placeholder="Search country or code" value={search} onChange={e => setSearch(e.target.value)} onKeyDown={e => { if (e.key === 'Escape') setOpen(false); }}/><div role="listbox" aria-label="Countries">{countryList.filter(([code, name]) => (name + ' ' + code).toLowerCase().includes(search.toLowerCase())).map(([code, name]) => <button type="button" role="option" aria-selected={value === code} key={code} onClick={() => { onChange(code); setOpen(false); setSearch(''); }}><span className={'fi fi-' + code.toLowerCase()}/>{name}<span className="muted">{code}</span></button>)}</div></div>}</div>;
}
function Auth({ register = false }) {
  const [email, setEmail] = useState(''); const [password, setPassword] = useState(''); const [country, setCountry] = useState('');
  const [error, setError] = useState(''); const [busy, setBusy] = useState(false); const navigate = useNavigate();
  async function submit(e: FormEvent) {
    e.preventDefault(); setError(''); if (register && !country) { setError('Select your country of residence.'); return; } setBusy(true);
    try { await authenticate(register ? 'register' : 'login', { email, password, ...(register ? { country } : {}) }); navigate('/assets'); }
    catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  }
  return <main className="auth-layout"><section className="auth-aside"><div className="eyebrow">YOUR ACCESS TO DIGITAL MARKETS</div><h1>Clarity.<br/>Control.<br/>Execution.</h1><p>Your markets. Your capital. One clear view.</p><div className="auth-aside-bottom">NOVČIĆ / DIGITAL ASSET EXCHANGE</div></section><section className="auth-panel"><div className="eyebrow">{register ? 'GET STARTED' : 'WELCOME BACK'}</div><h1>{register ? 'Create your account' : 'Sign in to Novčić'}</h1><p className="muted">{register ? 'Set up your account to access the exchange.' : 'Access your wallets and trading terminal.'}</p><form onSubmit={submit}><label>Email address<input type="email" name="email" autoComplete="email" maxLength={254} required placeholder="you@example.com" value={email} onChange={e => setEmail(e.target.value)}/></label><label>Password<input type="password" name="password" autoComplete={register ? 'new-password' : 'current-password'} minLength={register ? 10 : undefined} maxLength={128} required placeholder={register ? 'At least 10 characters' : 'Your password'} value={password} onChange={e => setPassword(e.target.value)}/></label>{register && <><p className="field-hint">Use 10–128 characters, including a letter and a number.</p><CountrySelect value={country} onChange={setCountry}/></>}{error && <p role="alert" className="error">{error}</p>}<button className="button full" disabled={busy}>{busy ? 'Please wait…' : register ? 'Create account' : 'Sign in'}<ArrowIcon direction="right"/></button></form><p className="auth-switch">{register ? 'Already registered?' : 'New to Novčić?'} <Link to={register ? '/login' : '/register'}>{register ? 'Sign in' : 'Create an account'}</Link></p></section></main>;
}
function Feedback({ error, notice }: { error: string; notice: string }) { return <>{error && <p className="error" role="alert">{error}</p>}{notice && <p className="notice" role="status">{notice}</p>}</>; }
function time(value: string) { return new Date(value).toISOString().replace('T', ' ').slice(0, 19); }
createRoot(document.getElementById('root')!).render(<App/>);
