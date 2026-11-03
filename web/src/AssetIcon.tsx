type AssetIconProps={asset:string;size?:number;label?:string};
const icons:Record<string,string>={BTC:'/assets/btc.svg',USDT:'/assets/usdt.svg'};
export default function AssetIcon({asset,size=32,label}:AssetIconProps){
 const src=icons[asset];
 return src?<img className="asset-icon" src={src} width={size} height={size} alt={label||''} aria-hidden={label?undefined:true} draggable={false}/>:<span className="asset-icon-fallback" aria-label={label||asset}>{asset}</span>;
}
export function AssetPairIcon(){return <span className="asset-pair-icon" aria-hidden="true"><AssetIcon asset="BTC"/><AssetIcon asset="USDT"/></span>;}
