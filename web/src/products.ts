export type Instrument={id:string;kind:string;expiry:string|null;strike:string|null;optionSide:string|null;status:string;settlementPrice:string|null};
export type Pool={asset:string;cash:string;debt:string;shares:string;reserves:string};
export type Products={index:{price:string;time:string;spot:string;perpetual:string|null}|null;priceFresh:boolean;instruments:Instrument[];pools:Pool[];amm:{btc:string;usdt:string;shares:string}};
export type Position={instrumentId:string;quantity:string;entryPrice:string;cost:string;leverage:string;realizedPnl:string};
export type Risk={equity:string;initialMargin:string;maintenanceMargin:string;transferableUsdt:string;transferableBtc:string;unrealizedPnl:string;realizedCash:string;indexPrice:string;priceFresh:boolean;restricted:boolean};
export type Loan={id:string;asset:string;collateralAsset:string;principal:string;interest:string;collateral:string;status:string;badDebt:string};
export type Portfolio={risk:Risk;positions:Position[];loans:Loan[];supplies:{asset:string;shares:string;queuedShares:string}[];liquidity:{shares:string}|null;schedules:{id:string;amount:string;budget:string|null;spent:string;hours:string;nextRun:string;paused:boolean;lastOutcome:string|null}[];events:{id:string;type:string;detail:string;time:string}[]};
