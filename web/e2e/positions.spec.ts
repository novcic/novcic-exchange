import {test,expect,type Page} from '@playwright/test';

async function fixture(page:Page,kind='Perpetual'){
 const instrument=kind==='Option'?'BTC-20261225-50000-C':kind==='Spot'?'BTCUSDT':'BTC-PERP';
 const position={instrumentId:instrument,kind,quantity:'0.010000',entryPrice:'50000.0000000000000000',cost:'500',leverage:'5',realizedPnl:'12.34',lifecycleId:'lifecycle',version:'2',lastPrice:'50100',priceChangePercent:'0.2',markPrice:'50050',positionValue:'500.5',unrealizedPnl:'0.5',returnOnMarginPercent:'0.5',entryMargin:'100',liquidationPrice:'42000',liquidationStatus:'Approximate; other marks and collateral held constant',protection:{id:'protection',version:'1',takeProfit:'52000',stopLoss:'48000',status:'Armed',attempts:'0',detail:null}};
 const order={id:'owned',version:'1:0.02:Open',instrumentId:instrument,side:'Buy',type:'Limit',price:'49000',quantity:'0.02',remaining:'0.02',filledNotional:'0',averageExecutionPrice:null,status:'Open',createdAt:'2026-11-03T12:00:00Z'};
 const requests:{path:string;body:any;key:string|undefined}[]=[];
 await page.route('**/api/**',async route=>{
  const path=new URL(route.request().url()).pathname;
  if(['/api/position-protection','/api/amend-order','/api/stop-closing'].includes(path)){
   requests.push({path,body:route.request().postDataJSON(),key:route.request().headers()['idempotency-key']});await route.fulfill({json:{}});return;
  }
  const data:Record<string,unknown>={
   '/api/auth/refresh':{accessToken:'fixture',user:{id:'fixture',email:'fixture@example.com',username:'User14978236',avatar:'default',country:'RS'}},
   '/api/config':{amm:{spotFee:'0.001',futuresFee:'0.00055',optionFee:'0.0003',optionPremiumCap:'0.07'}},
   '/api/products':{priceFresh:true,index:{price:'50000'},instruments:[{id:instrument,kind,status:'Trading',expiry:kind==='Option'?'2026-12-25T08:00:00Z':null,strike:'50000',optionSide:'Call'}],tickers:[],optionAnalytics:[],pools:[],amm:{}},
   '/api/market':{bids:[],asks:[],trades:[],lastPrice:'50100',high:'50200',low:'49900',volume:'1',change:'0.2'},
   '/api/candles':Array.from({length:20},(_,i)=>({time:String(1793700000+i*60),open:'50000',high:'50200',low:'49900',close:'50100',volume:'1'})),
   '/api/balances':[{kind:'Unified Trading',asset:'USDT',available:'5000'},{kind:'Unified Trading',asset:'BTC',available:'1'}],
   '/api/portfolio':{leverage:'5',risk:{equity:'5000',initialMargin:'100',maintenanceMargin:'5',transferableUsdt:'4900',transferableBtc:'0.9',priceFresh:true,restricted:false,indexPrice:'50000'},positions:kind==='Spot'?[]:[position]},
   '/api/orders':[order],'/api/trades':[]
  };
  expect(path in data).toBeTruthy();await route.fulfill({json:data[path]});
 });
 await page.goto('/trade?instrument='+instrument+'&product='+kind);
 return {requests,position,order};
}

test('position metrics and OCO form use exact current versions and explicit confirmation',async({page})=>{
 const {requests}=await fixture(page);
 await page.getByRole('button',{name:'Positions',exact:true}).click();
 await expect(page.locator('.positions-table')).toContainText('Long 0.010000 BTC');
 await expect(page.locator('.positions-table')).toContainText('+0.50 USDT');
 await expect(page.locator('.positions-table')).toContainText('+0.20%');
 await expect(page.getByRole('button',{name:'Market Close',exact:true})).toBeVisible();
 await page.getByRole('button',{name:'Set TP/SL'}).click();
 await page.getByLabel('Take profit · USDT').fill('53000.01');
 expect(requests).toHaveLength(0);
 await page.getByRole('button',{name:'Confirm TP/SL',exact:true}).click();
 expect(requests[0].body).toEqual({instrument:'BTC-PERP',lifecycleId:'lifecycle',positionVersion:'2',protectionVersion:'1',takeProfit:'53000.01',stopLoss:'48000'});
 expect(requests[0].key).toMatch(/^[0-9a-f-]{36}$/);
});

test('dragging a position proposes TP and never moves its entry or submits without confirmation',async({page})=>{
 const {requests}=await fixture(page);const label=page.locator('.chart-order-label').filter({hasText:'Long ·'});
 await expect(label).toBeVisible();const box=(await label.boundingBox())!;
 await page.mouse.move(box.x+35,box.y+12);await page.mouse.down();await page.mouse.move(box.x+35,box.y-70,{steps:8});await page.mouse.up();
 await expect(page.getByRole('dialog')).toBeVisible();expect(requests).toHaveLength(0);
 expect(Number(await page.getByLabel('Take profit · USDT').inputValue())).toBeGreaterThan(50100);
 await page.getByRole('button',{name:'Close dialog'}).click();await expect(label).toContainText('50,000.00');expect(requests).toHaveLength(0);
});

test('owned limit and TP lines support keyboard editing and stale edits cannot submit',async({page})=>{
 const {requests,position}=await fixture(page);
 const limit=page.locator('.chart-order-label').filter({hasText:'Buy limit'});await limit.focus();await page.keyboard.press('Enter');
 await page.getByLabel('New limit price · USDT').fill('49123.45');await page.getByRole('button',{name:'Confirm price change'}).click();
 expect(requests[0].body).toEqual({instrument:'BTC-PERP',id:'owned',version:'1:0.02:Open',price:'49123.45'});
 await page.locator('.chart-order-label').filter({hasText:'TP ·'}).click();position.version='3';
 await expect(page.getByText('Position or protection changed. Close this dialog and review again.')).toBeVisible({timeout:12000});
 await expect(page.getByRole('button',{name:'Confirm TP/SL',exact:true})).toBeDisabled();
});

test('options retain chain default and show position/order lines in Chart, with mobile reflow',async({page})=>{
 await fixture(page,'Option');await expect(page.locator('.options-chain')).toBeVisible();
 await page.getByRole('button',{name:'Chart',exact:true}).click();await expect(page.locator('.chart-order-label').filter({hasText:'Long ·'})).toBeVisible();
 await page.setViewportSize({width:390,height:844});await expect(page.locator('.chart-order-label').filter({hasText:'Buy limit'})).toBeVisible();
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
 await page.getByRole('button',{name:'Positions',exact:true}).click();await page.getByRole('button',{name:'Set TP/SL'}).click();await expect(page.getByRole('button',{name:'Confirm TP/SL',exact:true})).toBeVisible();
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
});

test('spot chart shows owned limit orders with quantity and draggable price confirmation',async({page})=>{
 const {requests}=await fixture(page,'Spot');const line=page.locator('.chart-order-label');await expect(line).toContainText('0.020000 BTC');const box=(await line.boundingBox())!;
 await page.mouse.move(box.x+35,box.y+12);await page.mouse.down();await page.mouse.move(box.x+35,box.y-40,{steps:6});await page.mouse.up();
 await expect(page.getByRole('dialog')).toContainText('new queue priority');expect(requests).toHaveLength(0);
});

test('a market without trades shows editable owned order levels without invented candles',async({page})=>{
 await fixture(page,'Spot');await page.route('**/api/candles?*',route=>route.fulfill({json:[]}));await page.reload();
 await expect(page.getByText('Only your order levels are shown.',{exact:false})).toBeVisible();
 await expect(page.locator('.chart-order-label')).toBeVisible();await expect(page.locator('.chart-fallback-line')).toHaveCount(1);
 await page.locator('.chart-order-label').click();await expect(page.getByRole('dialog')).toBeVisible();
});
