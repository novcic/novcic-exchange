import { useEffect, useRef, useState } from 'react';
import { CandlestickSeries, ColorType, createChart, type IChartApi, type ISeriesApi, type AutoscaleInfo, type UTCTimestamp } from 'lightweight-charts';
import { D, fmt, type Candle } from './api';

export type ChartLine={id:string;version:string;price:string;label:string;color:string;kind:'position'|'tp'|'sl'|'order';editable:boolean};
export default function Chart({ candles,lines=[],propose }: { candles: Candle[];lines?:ChartLine[];propose?:(line:ChartLine,price:string)=>void }) {
  const liveLines=useRef(lines);liveLines.current=lines;
  const [coordinates,setCoordinates]=useState<Record<string,number>>({});
  const [preview,setPreview]=useState<{id:string;price:string;y:number}|null>(null);
  const dragging=useRef<{line:ChartLine;startY:number;moved:boolean;price:string}|null>(null);
  const skipClick=useRef(false);
  const container = useRef<HTMLDivElement>(null);
  const chart = useRef<IChartApi | null>(null);
  const series = useRef<ISeriesApi<'Candlestick'> | null>(null);
  const first = useRef(true);
  // A market can have owned orders before its first trade. Show an order-only price
  // scale without inventing candles or a last-trade price.
  const fallbackRange=()=>{const prices=liveLines.current.map(l=>D(l.price));if(!prices.length)return null;const low=prices.reduce((a,b)=>a.lt(b)?a:b),high=prices.reduce((a,b)=>a.gt(b)?a:b);const pad=high.minus(low).mul('0.15').add(high.mul('0.01'));return {low:low.minus(pad),high:high.add(pad)};};
  const plotHeight=()=>Math.max(1,(container.current?.clientHeight||300)-40);
  const fallbackY=(price:string)=>{const range=fallbackRange();return range?range.high.minus(price).div(range.high.minus(range.low)).mul(plotHeight()).toNumber():null;};
  const fallbackPrice=(y:number)=>{const range=fallbackRange();return range?range.high.minus(range.high.minus(range.low).mul(D(y.toString()).div(plotHeight()))).toString():null;};
  useEffect(() => {
    const element = container.current!;
    const instance = createChart(element, { autoSize: true,
      layout: { background: { type: ColorType.Solid, color: '#ffffff' }, textColor: '#526858', fontFamily: 'IBM Plex Mono', fontSize: 12, attributionLogo: true },
      grid: { vertLines: { color: '#e2e9dd' }, horzLines: { color: '#e2e9dd' } },
      rightPriceScale: { borderColor: '#cbd8c7' }, timeScale: { borderColor: '#cbd8c7', timeVisible: true, secondsVisible: false },
      crosshair: { vertLine: { color: '#526858' }, horzLine: { color: '#526858' } },
    });
    chart.current = instance;
    series.current = instance.addSeries(CandlestickSeries, { upColor: '#176940', downColor: '#a83e42', borderVisible: false, wickUpColor: '#176940', wickDownColor: '#a83e42', priceFormat: { type:'price', precision:2, minMove:0.01 }, autoscaleInfoProvider: (original:()=>AutoscaleInfo|null) => {const range=original();if(range?.priceRange)for(const line of liveLines.current){const price=Number(line.price);range.priceRange.minValue=Math.min(range.priceRange.minValue,price);range.priceRange.maxValue=Math.max(range.priceRange.maxValue,price);}return range;} });
    return () => { instance.remove(); chart.current = null; series.current = null; first.current = true; };
  }, []);
  useEffect(() => {
    // Floating-point conversion exists only at the chart renderer boundary. All balances,
    // orders, calculations, form values and API values remain decimal strings/Decimal.
    series.current?.setData(candles.map(c => ({ time: Number(c.time) as UTCTimestamp, open: Number(c.open), high: Number(c.high), low: Number(c.low), close: Number(c.close) })));
    if (candles.length && first.current) { chart.current?.timeScale().fitContent(); first.current = false; }
  }, [candles]);
  useEffect(()=>{
    const target=series.current;if(!target)return;
    const drawn=lines.map(line=>target.createPriceLine({price:Number(line.price),color:line.color,lineWidth:1,lineStyle:line.kind==='position'?0:2,axisLabelVisible:true,title:''}));
    return()=>{for(const line of drawn)target.removePriceLine(line);};
  },[lines]);
  useEffect(()=>{
    let frame=0;
    const update=()=>{
      const next:Record<string,number>={};for(const line of liveLines.current){const y=series.current?.priceToCoordinate(Number(line.price))??fallbackY(line.price);if(y!=null)next[line.id]=y;}
      setCoordinates(old=>JSON.stringify(old)===JSON.stringify(next)?old:next);frame=requestAnimationFrame(update);
    };frame=requestAnimationFrame(update);return()=>cancelAnimationFrame(frame);
  },[]);
  const labels=lines.map(line=>({line,y:coordinates[line.id]})).filter(row=>row.y!=null).sort((a,b)=>a.y-b.y);
  // Labels can stagger while their price lines remain at exact chart coordinates.
  let previous=-30;const height=container.current?.clientHeight||300;
  const labelY=new Map<string,number>();for(const row of labels){const y=Math.max(previous+27,Math.min(height-46,Math.max(6,row.y-12)));labelY.set(row.line.id,y);previous=y;}
  return <div className="chart-wrap"><div className="chart" ref={container}/><div className="chart-overlays" aria-label="Your positions and orders">{!candles.length&&lines.map(line=><div key={line.id} className="chart-fallback-line" style={{top:coordinates[line.id],borderColor:line.color}}/>)}{lines.map(line=>coordinates[line.id]!=null&&<button key={line.id} className="chart-order-label" style={{top:labelY.get(line.id),borderColor:line.color,color:line.color}} disabled={!line.editable} title={line.label+' at '+line.price+' USDT. Drag to propose a price; click or press Enter to edit.'}
    onPointerDown={e=>{if(!line.editable)return;e.preventDefault();e.stopPropagation();e.currentTarget.setPointerCapture(e.pointerId);skipClick.current=false;dragging.current={line,startY:e.clientY,moved:false,price:line.price};}}
    onPointerMove={e=>{const drag=dragging.current;if(!drag||drag.line.id!==line.id)return;const y=e.clientY-container.current!.getBoundingClientRect().top;const price=series.current?.coordinateToPrice(y)??fallbackPrice(y);if(price==null||D(price).lte(0))return;drag.moved ||= Math.abs(e.clientY-drag.startY)>4;drag.price=D(price.toString()).toDecimalPlaces(2,4).toFixed(2);if(drag.moved)setPreview({id:line.id,price:drag.price,y});}}
    onPointerUp={e=>{const drag=dragging.current;if(!drag)return;dragging.current=null;setPreview(null);skipClick.current=drag.moved;if(e.currentTarget.hasPointerCapture(e.pointerId))e.currentTarget.releasePointerCapture(e.pointerId);if(drag.moved)propose?.(drag.line,drag.price);}}
    onPointerCancel={()=>{dragging.current=null;setPreview(null);skipClick.current=true;}}
    onKeyDown={e=>{if(e.key==='Escape'){dragging.current=null;setPreview(null);skipClick.current=true;}}}
    onClick={e=>{if(skipClick.current&&e.detail>0){skipClick.current=false;return;}skipClick.current=false;propose?.(line,line.price);}}>{line.label}<span>{fmt(line.price)}</span></button>)}{preview&&<div className="chart-drag-preview" style={{top:preview.y}}>Proposed {preview.price} USDT</div>}</div>{!candles.length && <div className="chart-empty"><span className="empty-cross">+</span><strong>Waiting for the first trade</strong><p>Candles will appear after a trade. {lines.length>0&&'Only your order levels are shown.'}</p></div>}</div>;
}
