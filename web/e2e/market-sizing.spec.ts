import {test,expect} from '@playwright/test';
import {marketOrderMaximum} from '../src/orderSizing';
import type {Portfolio} from '../src/products';

const fees={spotFee:'0.001',futuresFee:'0.00055',optionFee:'0.0003',optionPremiumCap:'0.07'};
const levels=[{price:'100',quantity:'0.5',total:'0.5'},{price:'200',quantity:'2',total:'2.5'}];
const base={levels,kind:'Spot',side:'Buy',available:'250.25',portfolio:null,symbol:'BTCUSDT',strike:'0',leverage:'1',reduce:false,fees};
const portfolio:Portfolio={risk:{equity:'1000',initialMargin:'0',maintenanceMargin:'0',transferableUsdt:'1000',transferableBtc:'1',unrealizedPnl:'0',realizedCash:'0',indexPrice:'100',priceFresh:true,restricted:false},positions:[],loans:[],supplies:[],liquidity:null,liquidityRequests:[],schedules:[],events:[]};

test('market buy walks depth, includes fees and rounds down to the BTC step',()=>{
  expect(marketOrderMaximum(base)).toBe('1.500000');
  expect(marketOrderMaximum({...base,available:'0.00010009'})).toBe('0.000000');
  expect(marketOrderMaximum({...base,available:'1000000'})).toBe('2.500000');
  expect(marketOrderMaximum({...base,levels:[]})).toBe('0.000000');
  expect(marketOrderMaximum({...base,fees:null})).toBe('0');
});
test('sell and derivative sizing respect transferable collateral, fees and closing direction',()=>{
  expect(marketOrderMaximum({...base,side:'Sell',available:'0.12345678'})).toBe('0.123456');
  expect(marketOrderMaximum({...base,side:'Sell',available:'2',portfolio})).toBe('1.000000');
  expect(marketOrderMaximum({...base,kind:'Perpetual',portfolio:{...portfolio,risk:{...portfolio.risk,priceFresh:false}}})).toBe('0');
  const position={instrumentId:'BTC-PERP',quantity:'0.1',entryPrice:'100',cost:'10',leverage:'1',realizedPnl:'0'};
  const closing={...base,kind:'Perpetual',symbol:'BTC-PERP',side:'Sell',reduce:true,portfolio:{...portfolio,positions:[position]}};
  expect(marketOrderMaximum(closing)).toBe('0.100000');
  expect(marketOrderMaximum({...closing,side:'Buy'})).toBe('0');
  expect(marketOrderMaximum({...closing,available:'0'})).toBe('0.000000');
  expect(marketOrderMaximum({...base,kind:'Perpetual',symbol:'BTC-PERP',available:'0.0055',portfolio})).toBe('0.100000');
  expect(marketOrderMaximum({...base,kind:'Option',symbol:'BTC-20261106-100-C',side:'Sell',available:'0.01',portfolio})).toBe('0.333333');
});

test('market ticket offers working percentages independent of a previously edited limit price',async({page})=>{
  // Deterministic account/book snapshots; this test never submits a financial command.
  const market={bids:[{price:'90',quantity:'5',total:'5'}],asks:levels,trades:[],lastPrice:'100',high:'100',low:'100',volume:'0',change:'0'};
  await page.route('**/api/**',async route=>{
    const path=new URL(route.request().url()).pathname;
    const data:Record<string,unknown>={
      '/api/auth/refresh':{accessToken:'fixture',user:{id:'fixture-user',email:'fixture@example.com',country:'RS'}},
      '/api/config':{amm:fees},'/api/market':market,'/api/candles':[],
      '/api/products':{index:{price:'100'},priceFresh:true,instruments:[{id:'BTCUSDT',kind:'Spot',status:'Trading'}],tickers:[],optionAnalytics:[],pools:[],amm:{}},
      '/api/balances':[{kind:'Unified Trading',asset:'USDT',available:'250.25',locked:'0'},{kind:'Unified Trading',asset:'BTC',available:'0.25',locked:'0'}],
      '/api/portfolio':portfolio,'/api/orders':[],'/api/trades':[]
    };
    expect(path in data).toBeTruthy();await route.fulfill({json:data[path]});
  });
  await page.goto('/trade');await expect(page.locator('.available-row')).toContainText('250.25000000');
  await page.getByLabel('Limit price',{exact:true}).fill('999999.00');
  await page.getByRole('button',{name:'Market',exact:true}).click();
  const slider=page.getByRole('slider',{name:'Percentage of available balance'});
  await expect(slider).toBeVisible();await expect(slider).toBeEnabled();
  await page.getByRole('button',{name:'Max',exact:true}).click();await expect(page.getByLabel('Order quantity')).toHaveValue('1.500000');
  await page.getByRole('button',{name:'50%',exact:true}).click();await expect(page.getByLabel('Order quantity')).toHaveValue('0.750000');
  await slider.fill('25');await expect(page.getByLabel('Order quantity')).toHaveValue('0.375000');
  await page.getByRole('button',{name:'Sell',exact:true}).click();await page.getByRole('button',{name:'Max',exact:true}).click();
  await expect(page.getByLabel('Order quantity')).toHaveValue('0.250000');
  await page.setViewportSize({width:390,height:844});await slider.scrollIntoViewIfNeeded();await expect(slider).toBeVisible();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
});
