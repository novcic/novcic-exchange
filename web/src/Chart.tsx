import { useEffect, useRef } from 'react';
import { CandlestickSeries, ColorType, createChart, type IChartApi, type ISeriesApi, type UTCTimestamp } from 'lightweight-charts';
import type { Candle } from './api';

export default function Chart({ candles }: { candles: Candle[] }) {
  const container = useRef<HTMLDivElement>(null);
  const chart = useRef<IChartApi | null>(null);
  const series = useRef<ISeriesApi<'Candlestick'> | null>(null);
  const first = useRef(true);
  useEffect(() => {
    const element = container.current!;
    const instance = createChart(element, { autoSize: true,
      layout: { background: { type: ColorType.Solid, color: '#0D0F12' }, textColor: '#89929f', fontFamily: 'IBM Plex Mono', fontSize: 12, attributionLogo: true },
      grid: { vertLines: { color: '#191d23' }, horzLines: { color: '#191d23' } },
      rightPriceScale: { borderColor: '#272d36' }, timeScale: { borderColor: '#272d36', timeVisible: true, secondsVisible: false },
      crosshair: { vertLine: { color: '#566171' }, horzLine: { color: '#566171' } },
    });
    chart.current = instance;
    series.current = instance.addSeries(CandlestickSeries, { upColor: '#2cb889', downColor: '#e26c76', borderVisible: false, wickUpColor: '#2cb889', wickDownColor: '#e26c76', priceFormat: { type:'price', precision:2, minMove:0.01 } });
    return () => { instance.remove(); chart.current = null; series.current = null; first.current = true; };
  }, []);
  useEffect(() => {
    // Floating-point conversion exists only at the chart renderer boundary. All balances,
    // orders, calculations, form values and API values remain decimal strings/Decimal.
    series.current?.setData(candles.map(c => ({ time: Number(c.time) as UTCTimestamp, open: Number(c.open), high: Number(c.high), low: Number(c.low), close: Number(c.close) })));
    if (candles.length && first.current) { chart.current?.timeScale().fitContent(); first.current = false; }
  }, [candles]);
  return <div className="chart-wrap"><div className="chart" ref={container}/>{!candles.length && <div className="chart-empty"><span className="empty-cross">+</span><strong>Waiting for the first trade</strong><p>Candles will appear when BTC/USDT trades on Novčić.</p></div>}</div>;
}
