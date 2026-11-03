import Decimal from 'decimal.js';
import {D, type Level} from './api';
import type {Portfolio} from './products';

export type AmmFees = {spotFee:string; futuresFee:string; optionFee:string; optionPremiumCap:string};

// A snapshot estimate only: execution still performs the authoritative atomic checks.
// Spend each level's cost, including a conservative AMM fee allowance, using decimals.
export function marketOrderMaximum({levels,kind,side,available,portfolio,symbol,strike,leverage,reduce,fees}:{
  levels:Level[];kind:string;side:string;available:string;portfolio:Portfolio|null;
  symbol:string;strike:string;leverage:string;reduce:boolean;fees:AmmFees|null;
}):string {
  if(!fees)return '0';
  const risk=portfolio?.risk,index=D(risk?.indexPrice||0);
  if(kind!=='Spot'&&(!risk?.priceFresh||index.lte(0)))return '0';
  const free=D(risk?.equity||0).minus(risk?.initialMargin||0).clamp(0,'99999999999999999999');
  const cash=D(available).clamp(0,'99999999999999999999');
  const limits:{left:ReturnType<typeof D>; cost:(price:ReturnType<typeof D>)=>ReturnType<typeof D>}[]=[];
  let cap=D('1000000000');
  const fee=(price:ReturnType<typeof D>)=>kind==='Option'
    ? Decimal.min(index.mul(fees.optionFee),price.mul(fees.optionPremiumCap))
    : price.mul(kind==='Spot'?fees.spotFee:fees.futuresFee);
  if(reduce){
    const position=D(portfolio?.positions.find(p=>p.instrumentId===symbol)?.quantity||0);
    if(position.isZero()||(side==='Buy'?position.gt(0):position.lt(0)))return '0';
    cap=position.abs();
    limits.push({left:cash,cost:fee});
  }else if(kind==='Spot'){
    const transferable=side==='Buy'?risk?.transferableUsdt:risk?.transferableBtc;
    limits.push({left:Decimal.min(cash,D(transferable??available)),cost:price=>side==='Buy'?price.plus(fee(price)):D(1)});
  }else if(kind==='Option'){
    if(side==='Buy')limits.push({left:Decimal.min(cash,D(risk?.transferableUsdt||0)),cost:price=>price.plus(fee(price))});
    else{
      const call=symbol.endsWith('-C'),cover=call?D(1):D(strike);
      if(cover.lte(0))return '0';
      limits.push({left:D(call?risk?.transferableBtc||0:risk?.transferableUsdt||0),cost:()=>cover});
      limits.push({left:cash,cost:fee});
      limits.push({left:free,cost:price=>(call?index.mul('0.9'):cover).plus(fee(price))});
    }
  }else{
    limits.push({left:cash,cost:fee});
    // Cover margin plus adverse entry-to-mark movement within the mark's 1% band.
    limits.push({left:free,cost:price=>Decimal.max(price,index.mul('1.01')).div(leverage).plus(price.minus(index).abs()).plus(index.mul('0.01')).plus(fee(price))});
  }
  let quantity=D(0);
  const sorted=[...levels].sort((a,b)=>side==='Buy'?D(a.price).cmp(b.price):D(b.price).cmp(a.price));
  for(const level of sorted){
    const price=D(level.price);if(price.lte(0))continue;
    if(kind!=='Spot'&&kind!=='Option'&&price.div(index).minus(1).abs().gt('0.1'))continue;
    const costs=limits.map(limit=>limit.cost(price));
    let take=Decimal.min(D(level.quantity),cap.minus(quantity));
    costs.forEach((cost,i)=>{if(cost.gt(0))take=Decimal.min(take,limits[i].left.div(cost));});
    take=take.toDecimalPlaces(6,Decimal.ROUND_DOWN); // ROUND_DOWN to the BTC step.
    if(take.lte(0))break;
    quantity=quantity.plus(take);
    limits.forEach((limit,i)=>{limit.left=limit.left.minus(take.mul(costs[i]));});
    if(take.lt(level.quantity)||quantity.gte(cap))break;
  }
  return quantity.toFixed(6);
}
