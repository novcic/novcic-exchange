import {test,expect,type Page} from '@playwright/test';

async function fixture(page:Page,kind='Perpetual'){
 const instrument=kind==='Option'?'BTC-20261225-50000-C':kind==='Spot'?'BTCUSDT':'BTC-PERP';
 const position={instrumentId:instrument,kind,quantity:'0.010000',entryPrice:'50000.0000000000000000',cost:'500',leverage:'5',realizedPnl:'12.34',lifecycleId:'lifecycle',version:'2',lastPrice:'50100',priceChangePercent:'0.2',markPrice:'50050',positionValue:'500.5',unrealizedPnl:'0.5',returnOnMarginPercent:'0.5',entryMargin:'100',liquidationPrice:'42000',liquidationStatus:'Approximate; other marks and collateral held constant',protection:{id:'protection',version:'1',takeProfit:'52000.00000000',stopLoss:'48000.00000000',status:'Armed',attempts:'0',detail:null}};
 const order={id:'owned',version:'1:0.02:Open',instrumentId:instrument,side:'Buy',type:'Limit',price:'49000.00000000',quantity:'0.02',remaining:'0.02',filledNotional:'0',averageExecutionPrice:null,status:'Open',createdAt:'2026-11-03T12:00:00Z'};
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
   '/api/market':{bids:[],asks:[],trades:[],lastPrice:'50100',markPrice:'50050',high:'50200',low:'49900',volume:'1',change:'0.2'},
   '/api/candles':Array.from({length:20},(_,i)=>({time:String(1793700000+i*60),open:'50000',high:'50200',low:'49900',close:'50100',volume:'1'})),
   '/api/balances':[{kind:'Unified Trading',asset:'USDT',available:'5000'},{kind:'Unified Trading',asset:'BTC',available:'1'}],
   '/api/portfolio':{leverage:'5',risk:{totalEquity:'5000',marginBalance:'5000',availableBalance:'4900',initialMarginRate:'2',maintenanceMarginRate:'0.1',equity:'5000',initialMargin:'100',maintenanceMargin:'5',transferableUsdt:'4900',transferableBtc:'0.9',priceFresh:true,restricted:false,indexPrice:'50000'},positions:kind==='Spot'?[]:[position]},
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
 expect(requests[0].body).toEqual({instrument:'BTC-PERP',lifecycleId:'lifecycle',positionVersion:'2',protectionVersion:'1',takeProfit:'53000.01',stopLoss:'48000.00'});
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

for(const scenario of [
 {name:'losing short',quantity:'-0.077339',entry:'72566.8854224905933617',target:'81741.13',leg:'Take profit',field:'takeProfit'},
 {name:'profitable short',quantity:'-0.077339',entry:'90000',target:'85000',leg:'Stop loss',field:'stopLoss'},
 {name:'losing long',quantity:'0.077339',entry:'90000',target:'85000',leg:'Take profit',field:'takeProfit'},
 {name:'profitable long',quantity:'0.077339',entry:'72566.8854224905933617',target:'81741.13',leg:'Stop loss',field:'stopLoss'}
])test(`position drag uses last trade for a ${scenario.name}, even between entry and last`,async({page})=>{
 const {requests,position,order}=await fixture(page);
 position.quantity=scenario.quantity;position.entryPrice=scenario.entry;position.lastPrice='83832.23';position.protection.status='Cancelled';order.price=scenario.target;
 await page.route('**/api/market?*',route=>route.fulfill({json:{bids:[],asks:[],trades:[],lastPrice:position.lastPrice,markPrice:'50050',high:'84000',low:'83000',volume:'1',change:'0.2'}}));
 await page.route('**/api/candles?*',route=>route.fulfill({json:Array.from({length:20},(_,i)=>({time:String(1793700000+i*60),open:'83500',high:'84000',low:'83000',close:position.lastPrice,volume:'1'}))}));
 await page.reload();
 const label=page.locator('.chart-order-label').filter({hasText:scenario.quantity.startsWith('-')?'Short ·':'Long ·'});
 const target=page.locator('.chart-order-label').filter({hasText:'Buy limit'});
 await expect(label).toBeVisible();await expect(target).toBeVisible();
 const start=(await label.boundingBox())!,end=(await target.boundingBox())!;
 await page.mouse.move(start.x+35,start.y+12);await page.mouse.down();await page.mouse.move(start.x+35,end.y+12,{steps:8});
 const preview=(await page.locator('.chart-drag-preview').innerText()).match(/Proposed ([0-9.]+) USDT/)![1];
 await page.mouse.up();
 expect(Number(preview)).toBeGreaterThan(Math.min(Number(scenario.entry),Number(position.lastPrice)));
 expect(Number(preview)).toBeLessThan(Math.max(Number(scenario.entry),Number(position.lastPrice)));
 await expect(page.getByLabel(scenario.leg+' · USDT')).toHaveValue(preview);
 await expect(page.getByLabel((scenario.leg==='Take profit'?'Stop loss':'Take profit')+' · USDT')).toHaveValue('');
 expect(requests).toHaveLength(0);await expect(page.getByRole('button',{name:'Confirm TP/SL',exact:true})).toBeEnabled();
 await page.getByRole('button',{name:'Confirm TP/SL',exact:true}).click();
 expect(requests).toHaveLength(1);expect(requests[0].body).toEqual({instrument:'BTC-PERP',lifecycleId:'lifecycle',positionVersion:'2',protectionVersion:'1',takeProfit:null,stopLoss:null,[scenario.field]:preview});
 await expect(label).toHaveAttribute('title',new RegExp(' at '+scenario.entry.replaceAll('.','\\.')+' USDT'));
});

for(const leg of ['TP','SL'])test(`dragging an existing ${leg} across last trade keeps its leg and validates it`,async({page})=>{
 const {requests}=await fixture(page);
 const label=page.locator('.chart-order-label').filter({hasText:leg+' ·'}),target=page.locator('.chart-order-label').filter({hasText:(leg==='TP'?'SL':'TP')+' ·'});
 await expect(label).toBeVisible();await expect(target).toBeVisible();const start=(await label.boundingBox())!,end=(await target.boundingBox())!;
 await page.mouse.move(start.x+35,start.y+12);await page.mouse.down();await page.mouse.move(start.x+35,end.y+12,{steps:8});
 const preview=(await page.locator('.chart-drag-preview').innerText()).match(/Proposed ([0-9.]+) USDT/)![1];await page.mouse.up();
 await expect(page.getByLabel((leg==='TP'?'Take profit':'Stop loss')+' · USDT')).toHaveValue(preview);
 await expect(page.getByRole('alert')).toHaveText((leg==='TP'?'Take profit':'Stop loss')+' is on the wrong side of the last traded price.');
 await expect(page.getByRole('button',{name:'Confirm TP/SL',exact:true})).toBeDisabled();expect(requests).toHaveLength(0);
});

test('position drag without a last trade opens an unclassified protection form',async({page})=>{
 const {requests,position}=await fixture(page);(position as any).lastPrice=null;position.protection.status='Cancelled';await page.reload();
 const label=page.locator('.chart-order-label').filter({hasText:'Long ·'});await expect(label).toBeVisible();const start=(await label.boundingBox())!;
 await page.mouse.move(start.x+35,start.y+12);await page.mouse.down();await page.mouse.move(start.x+35,start.y-70,{steps:8});await page.mouse.up();
 await expect(page.getByRole('dialog')).toBeVisible();await expect(page.getByLabel('Take profit · USDT')).toHaveValue('');await expect(page.getByLabel('Stop loss · USDT')).toHaveValue('');expect(requests).toHaveLength(0);
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

test('saved decimal prices reopen without false precision errors and preserve their exact values',async({page})=>{
 const {requests,position}=await fixture(page);
 position.lastPrice='80000';position.protection.takeProfit='82000.01000000';position.protection.stopLoss='77645.39000000';await page.reload();
 await page.getByRole('button',{name:'Positions',exact:true}).click();await page.getByRole('button',{name:'Set TP/SL'}).click();
 await expect(page.getByLabel('Take profit · USDT')).toHaveValue('82000.01');
 await expect(page.getByLabel('Stop loss · USDT')).toHaveValue('77645.39');
 await expect(page.getByRole('button',{name:'Confirm TP/SL',exact:true})).toBeEnabled();
 await page.getByRole('button',{name:'Confirm TP/SL',exact:true}).click();
 expect(requests[0].body).toMatchObject({takeProfit:'82000.01',stopLoss:'77645.39'});
 await page.locator('.chart-order-label').filter({hasText:'SL ·'}).click();
 await expect(page.getByLabel('Stop loss · USDT')).toHaveValue('77645.39');
 await page.getByLabel('Stop loss · USDT').fill('77645.39100000');
 await expect(page.getByRole('button',{name:'Confirm TP/SL',exact:true})).toBeDisabled();
 await page.getByLabel('Stop loss · USDT').blur();
 await expect(page.getByLabel('Stop loss · USDT')).toHaveValue('77645.39100000');
 expect(requests).toHaveLength(1);
 await page.getByLabel('Stop loss · USDT').fill('77645.39000000');
 await expect(page.getByRole('button',{name:'Confirm TP/SL',exact:true})).toBeEnabled();
 await page.getByRole('button',{name:'Confirm TP/SL',exact:true}).click();
 expect(requests[1].body.stopLoss).toBe('77645.39');
});

test('limit amendment normalizes saved and pasted trailing zeros without rounding off-tick prices',async({page})=>{
 const {requests}=await fixture(page,'Spot');await page.getByRole('button',{name:'Edit',exact:true}).click();
 const input=page.getByLabel('New limit price · USDT');await expect(input).toHaveValue('49000.00');
 await expect(page.getByRole('button',{name:'Confirm price change'})).toBeDisabled();
 await input.fill('49123.45100000');await input.blur();await expect(input).toHaveValue('49123.45100000');
 await expect(page.getByRole('button',{name:'Confirm price change'})).toBeDisabled();
 await input.fill('49123.45000000');await expect(page.getByRole('button',{name:'Confirm price change'})).toBeEnabled();
 await page.getByRole('button',{name:'Confirm price change'}).click();
 expect(requests[0].body.price).toBe('49123.45');
});

test('positions use concise labels and separate protection from market close',async({page})=>{
 const {position}=await fixture(page);(position as any).liquidationPrice=null;await page.reload();
 await page.getByRole('button',{name:'Positions',exact:true}).click();const table=page.locator('.positions-table');
 await expect(table.getByRole('columnheader',{name:'Liquidation price · Mark',exact:true})).toBeVisible();
 await expect(table.locator('tbody td').nth(3)).toHaveText('-');
 await expect(table).toContainText('Entry 50,000.00');await expect(table).toContainText('Last 50,100.00');await expect(table).toContainText('(+0.20%)');
 await expect(table).toContainText('Unrealized +0.50 USDT');await expect(table).toContainText('Realized +12.34 USDT');await expect(table).toContainText('+0.50% ROI');
 await expect(table).not.toContainText('Armed');await expect(table).not.toContainText('No positive threshold');
 await expect(table.locator('tbody td').nth(4).getByRole('button',{name:'Set TP/SL'})).toBeVisible();
 await expect(table.locator('tbody td').last()).toHaveText('Market Close');
 await page.screenshot({path:'../artifacts/positions-refined-desktop.png',fullPage:true});
});

test('PnL history shows net accounting, paginates and filters without page overflow',async({page})=>{
 await fixture(page);
 const row={id:'2',instrumentId:'BTC-PERP',kind:'Perpetual',direction:'Long',quantity:'0.1',averageEntry:'50000',averageExit:'51000',grossPnl:'100',openingFee:'2.75',closingFee:'2.805',funding:'-2.5',netPnl:'91.945',openedAt:'2026-11-03T12:00:00Z',closedAt:'2026-11-03T13:01:02Z',durationSeconds:'3662'};
 await page.route('**/api/pnl-history?*',r=>{const q=new URL(r.request().url()).searchParams;return r.fulfill({json:q.has('from')?{items:[],nextCursor:null}:q.has('cursor')?{items:[{...row,id:'1',netPnl:'-5'}],nextCursor:null}:{items:[row],nextCursor:'2'}});});
 await page.getByRole('button',{name:'PnL history',exact:true}).click();await expect(page.locator('.pnl-table')).toContainText('+91.94500000');
 await expect(page.locator('.pnl-table')).toContainText('0d 1h 1m 2s');await page.getByText('Breakdown',{exact:true}).click();await expect(page.locator('.pnl-table details')).toContainText('Funding -2.50000000');
 await page.getByRole('button',{name:'Load more',exact:true}).click();await expect(page.locator('.pnl-table tbody tr')).toHaveCount(2);
 await page.screenshot({path:'../artifacts/pnl-history-desktop.png',fullPage:true});
 await page.getByLabel('Closed from · UTC').fill('2026-11-04');await expect(page.getByText('No verified closed positions for these filters.')).toBeVisible();
 await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
});

test('history refresh preserves filters, expanded details and scroll after an in-flight filter change',async({page})=>{
 await fixture(page);
 let release!:()=>void;const gate=new Promise<void>(r=>release=r);let reads=0;
 const row={id:'1',instrumentId:'BTC-20261225-50000-C',kind:'Option',direction:'Long',quantity:'0.1',averageEntry:'1000',averageExit:'1100',grossPnl:'10',openingFee:'1',closingFee:'1',funding:'0',netPnl:'8',openedAt:'2026-11-03T12:00:00Z',closedAt:'2026-11-03T13:00:00Z',durationSeconds:'3600'};
 await page.route('**/api/pnl-history?*',async r=>{reads++;const product=new URL(r.request().url()).searchParams.get('product');if(reads===1)await gate;await r.fulfill({json:{items:[{...row,instrumentId:product==='Option'?row.instrumentId:'OLD-RESPONSE'}],nextCursor:null}});});
 await page.getByRole('button',{name:'PnL history',exact:true}).click();await expect.poll(()=>reads).toBeGreaterThan(0);
 await page.getByRole('combobox',{name:'Product',exact:true}).selectOption('Option');release();
 await expect(page.locator('.pnl-table')).not.toContainText('OLD-RESPONSE');await expect(page.locator('.pnl-table tbody tr')).toHaveCount(1);
 await page.getByText('Breakdown',{exact:true}).click();const scroll=page.locator('.history-panel .table-scroll');await scroll.evaluate(e=>{e.scrollLeft=250;});
 const before=reads;await expect.poll(()=>reads,{timeout:10000}).toBeGreaterThan(before);
 await expect(page.getByRole('combobox',{name:'Product',exact:true})).toHaveValue('Option');await expect(page.locator('.pnl-table details')).toHaveAttribute('open','');
 expect(await scroll.evaluate(e=>e.scrollLeft)).toBe(250);
});
