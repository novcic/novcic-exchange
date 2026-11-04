import {Link} from 'react-router-dom';
import AssetIcon from './AssetIcon';
import {ArrowIcon} from './NavigationIcons';
import {D,fmt} from './api';
import {money} from './MarginSummary';
import type {AccountAsset} from './products';

export default function AssetBalances({assets,tab,query,hideZero,open}:{assets:AccountAsset[];tab:string;query:string;hideZero:boolean;open:(action:string,asset:string)=>void}){
  const trading=tab==='Unified Trading';
  const negative=(value:string|null)=>value!=null&&D(value).lt(0)?'negative':'';
  const rows=assets.filter(a=>a.kind===tab&&a.asset.toLowerCase().includes(query.toLowerCase())&&(!hideZero||[a.walletBalance,a.equity,a.unrealizedPnl,a.realizedCash].some(v=>v!=null&&!D(v).isZero())||a.equity==null));
  return <div className="table-scroll"><table className="asset-table"><thead><tr>
    <th>Asset</th>{trading&&<th>Equity</th>}<th>Wallet balance</th>{trading&&<th>Unrealized P&amp;L</th>}<th>{trading?'Transferable':'Available'}</th><th>{trading?'Equity':'Wallet value'} · USD</th><th>Actions</th>
  </tr></thead><tbody>{rows.map(a=><tr key={a.asset} data-testid={`${tab}-${a.asset}`}>
    <td><div className="asset-name"><AssetIcon asset={a.asset}/><div><strong>{a.asset}</strong><span>{a.asset==='BTC'?'Bitcoin':'Tether USD'}</span></div></div></td>
    {trading&&<td data-label="Equity" data-testid="equity" className={negative(a.equity)}><span>{fmt(a.equity,8)}{!D(a.realizedCash).isZero()&&<small className="table-secondary">Unsettled cash {fmt(a.realizedCash,8)}</small>}</span></td>}
    <td data-label="Wallet balance" data-testid="total"><span>{fmt(a.walletBalance,8)}<small className="table-secondary" data-testid="locked">Reserved {fmt(a.locked,8)}</small></span></td>
    {trading&&<td data-label="Unrealized P&L" data-testid="unrealized-pnl" className={negative(a.unrealizedPnl)}>{fmt(a.unrealizedPnl,8)}</td>}
    <td data-label={trading?'Transferable':'Available'} data-testid="available">{fmt(a.transferable,8)}</td><td data-label="Value · USD" className={negative(a.usdValue)}>{money(a.usdValue)}</td>
    <td><div className="row-actions">{!trading&&<><button onClick={()=>open('Deposit',a.asset)}>Deposit</button><button onClick={()=>open('Withdraw',a.asset)}>Withdraw</button></>}<button onClick={()=>open('Transfer',a.asset)}>Transfer</button><Link to={'/trade?side='+(a.asset==='BTC'?'Sell':'Buy')}>Trade <ArrowIcon/></Link></div></td>
  </tr>)}</tbody></table>{!rows.length&&<div className="empty-state">No matching assets.</div>}</div>;
}
