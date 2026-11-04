import {D,fmt} from './api';
import type {Risk} from './products';

export const money=(value:string|null|undefined)=>value==null?'—':(D(value).lt(0)?'−$':'$')+fmt(D(value).abs().toString());
export function marginRate(value:string|null|undefined,required:string,risk:Risk){
  if(!risk.priceFresh)return '—';
  if(value!=null)return fmt(value)+'%';
  return D(required).gt(0)&&D(risk.marginBalance).lte(0)?'≥100%':'—';
}
export default function MarginSummary({risk}:{risk:Risk}){
  const fresh=risk.priceFresh;
  return <section className="margin-summary" aria-label="Cross-margin account">
    <h3>Cross-margin account</h3>
    <dl>
      <div><dt>Total equity</dt><dd data-testid="account-equity">{money(risk.totalEquity)}</dd></div>
      <div><dt title="Collateral-adjusted balance used for margin requirements. BTC collateral counts at 90%.">Margin balance</dt><dd data-testid="margin-balance">{fresh?money(risk.marginBalance):'—'}</dd></div>
      <div><dt>Initial margin (IM)</dt><dd>{fresh?money(risk.initialMargin):'—'}<small className={fresh&&D(risk.initialMargin).gte(risk.marginBalance)&&D(risk.initialMargin).gt(0)?'negative':''}>IMR {marginRate(risk.initialMarginRate,risk.initialMargin,risk)}</small></dd></div>
      <div><dt>Maintenance margin (MM)</dt><dd>{fresh?money(risk.maintenanceMargin):'—'}<small className={fresh&&D(risk.maintenanceMargin).gte(risk.marginBalance)&&D(risk.maintenanceMargin).gt(0)?'negative':''}>MMR {marginRate(risk.maintenanceMarginRate,risk.maintenanceMargin,risk)}</small></dd></div>
      <div className="available-margin"><dt>Available balance</dt><dd data-testid="available-margin" className={fresh&&risk.availableBalance!=null&&D(risk.availableBalance).lt(0)?'negative':''}>{fresh?money(risk.availableBalance):'—'}</dd></div>
    </dl>
  </section>;
}
