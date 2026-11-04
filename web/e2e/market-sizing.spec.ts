import {test,expect} from '@playwright/test';
const fees={spotFee:'0.001',futuresFee:'0.00055',optionFee:'0.0003',optionPremiumCap:'0.07'};
const levels=[{price:'100',quantity:'0.5',total:'0.5'},{price:'200',quantity:'2',total:'2.5'}];
const portfolio={leverage:'1',risk:{equity:'272.75',marginBalance:'272.75',totalEquity:'275.25',availableBalance:'272.75',initialMargin:'0',maintenanceMargin:'0',initialMarginRate:'0',maintenanceMarginRate:'0',transferableUsdt:'250.25',transferableBtc:'0.25',priceFresh:true},positions:[],events:[]};

test('reduce-only Max uses position size even with zero USDT and negative available margin',async({page})=>{
  await page.route('**/hubs/**',r=>r.abort());
  await page.route('**/api/**',async route=>{
    const path=new URL(route.request().url()).pathname;
    const data:Record<string,unknown>={
      '/api/auth/refresh':{accessToken:'fixture',user:{id:'fixture-user',email:'fixture@example.com',country:'RS'}},
      '/api/config':{amm:fees},'/api/market':{bids:[],asks:[],trades:[],lastPrice:'50000',markPrice:'50000'},'/api/candles':[],
      '/api/products':{index:{price:'50000'},priceFresh:true,instruments:[{id:'BTC-PERP',kind:'Perpetual',status:'Trading'}],tickers:[],optionAnalytics:[],pools:[],amm:{}},
      '/api/balances':[{kind:'Unified Trading',asset:'USDT',available:'0',locked:'0'}],
      '/api/portfolio':{...portfolio,risk:{...portfolio.risk,equity:'-100',availableBalance:'-200',initialMargin:'100',transferableUsdt:'0',indexPrice:'50000'},positions:[{instrumentId:'BTC-PERP',kind:'Perpetual',quantity:'0.15',entryPrice:'50000',leverage:'10',cost:'7500',reserved:'0',realizedPnl:'0',protection:null,lifecycleId:'fixture',version:'1'}]},'/api/orders':[],'/api/trades':[]
    };
    expect(path in data).toBeTruthy();await route.fulfill({json:data[path]});
  });
  await page.goto('/trade?product=Perpetual');await page.getByRole('button',{name:'Sell / Short',exact:true}).click();await page.getByLabel('Reduce only',{exact:true}).check();
  await page.getByRole('button',{name:'Market',exact:true}).click();await page.getByRole('button',{name:'Max',exact:true}).click();
  await expect(page.getByLabel('Order quantity')).toHaveValue('0.150000');await expect(page.locator('.order-summary')).toContainText('4.12500000 USDT');
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
    expect(path).not.toBe('/api/order-preview');
    expect(path in data).toBeTruthy();await route.fulfill({json:data[path]});
  });
  await page.goto('/trade');await expect(page.locator('.available-row')).toContainText('250.25000000');
  await page.getByLabel('Limit price',{exact:true}).fill('999999.00');
  await page.getByRole('button',{name:'Market',exact:true}).click();
  const slider=page.getByRole('slider',{name:'Percentage of available balance'});
  await expect(slider).toBeVisible();await expect(slider).toBeEnabled();
  await page.getByRole('button',{name:'Max',exact:true}).click();await expect(page.getByLabel('Order quantity')).toHaveValue('2.502500');
  await expect(slider).toBeEnabled();await page.getByRole('button',{name:'50%',exact:true}).click();await expect(page.getByLabel('Order quantity')).toHaveValue('1.251250');
  await expect(slider).toBeEnabled();await slider.fill('25');await expect(page.getByLabel('Order quantity')).toHaveValue('0.625625');
  await expect(page.locator('.order-summary')).toContainText('0.00062562 BTC');
  await expect(page.locator('.order-summary')).toContainText('62.56 USDT');
  await page.getByRole('button',{name:'Sell',exact:true}).click();await page.getByRole('button',{name:'Max',exact:true}).click();
  await expect(page.getByLabel('Order quantity')).toHaveValue('0.250000');
  await page.setViewportSize({width:390,height:844});await slider.scrollIntoViewIfNeeded();await expect(slider).toBeVisible();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
});
