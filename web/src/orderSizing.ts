import Decimal from 'decimal.js';
import {D} from './api';
import type {Portfolio} from './products';
export type AmmFees={spotFee:string;futuresFee:string;optionFee:string;optionPremiumCap:string};
export function estimateOrder({kind,side,price,quantity,usd,btc,portfolio,strike,optionSide,reduce,symbol,fees}:{kind:string;side:string;price:string|null;quantity:string;usd:string;btc:string;portfolio:Portfolio|null;strike:string;optionSide:string;reduce:boolean;symbol:string;fees:AmmFees|null}){
 const spot=kind==='Spot',p=D(price||0),q=D(quantity||0),risk=portfolio?.risk,lev=D(portfolio?.leverage||1),index=D(risk?.indexPrice||0);
 const feeAsset=spot&&side==='Buy'?'BTC':'USDT',free=Decimal.max(0,D(risk?.equity||0).minus(risk?.initialMargin||0));
 const cash=Decimal.max(0,Decimal.min(usd,risk?.transferableUsdt||usd)),coin=Decimal.max(0,Decimal.min(btc,risk?.transferableBtc||btc));
 if(!fees||!p.gt(0)||(kind==='Option'&&!index.gt(0)))return {maximum:'0',fee:null,net:null,cost:null,value:null,feeAsset};
 const f=spot?(side==='Buy'?D(1):p).mul(fees.spotFee):kind==='Option'?Decimal.min(index.mul(fees.optionFee),p.mul(fees.optionPremiumCap)):p.mul(fees.futuresFee);
 let max=D(0),unit=D(0);
 if(spot){unit=side==='Buy'?p:D(1);max=side==='Buy'?cash.div(p):coin;}
 else if(reduce){unit=f;const position=D(portfolio?.positions.find(x=>x.instrumentId===symbol)?.quantity||0);max=position.mul(side==='Buy'?1:-1).lt(0)?position.abs():D(0);}
 else if(kind==='Option'){unit=(side==='Buy'?p:optionSide==='Put'?D(strike):D(0)).plus(f);max=optionSide==='Call'&&side==='Sell'?coin:unit.gt(0)?cash.div(unit):D(0);if(f.gt(0))max=Decimal.min(max,cash.div(f));if(unit.gt(0))max=Decimal.min(max,free.div(unit));}
 else{unit=p.div(lev).plus(f);max=free.div(unit);if(f.gt(0))max=Decimal.min(max,cash.div(f));}
 if(!spot&&!risk?.priceFresh)max=D(0);
 const fee=q.mul(f).toDecimalPlaces(8,Decimal.ROUND_DOWN);
 return {maximum:Decimal.max(0,Decimal.min(max,'1000000000')).toFixed(6),fee:fee.toFixed(8),net:spot?q.mul(side==='Buy'?D(1):p).minus(fee).toFixed(8):'0',cost:(spot?q.mul(unit):q.mul(unit.minus(f)).toDecimalPlaces(8,Decimal.ROUND_DOWN).plus(fee)).toFixed(8),value:q.mul(p).toFixed(8),feeAsset};
}
