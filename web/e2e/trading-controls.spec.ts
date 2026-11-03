import {test,expect} from '@playwright/test';
test('padded tabs, execution average, cancel-all confirmation and accessible toast',async({page})=>{
 let cancelled=false;
 await page.route('**/api/**',async route=>{
  const path=new URL(route.request().url()).pathname;
  const order={id:'order',instrumentId:'BTCUSDT',side:'Buy',type:'Market',price:null,quantity:'1',remaining:'0.5',averageExecutionPrice:'123.45',filledNotional:'61.725',status:cancelled?'Cancelled':'PartiallyFilled',createdAt:'2026-11-03T12:00:00Z'};
  const data:Record<string,unknown>={
   '/api/auth/refresh':{accessToken:'fixture',user:{id:'fixture-user',email:'fixture@example.com',country:'RS'}},
   '/api/config':{amm:{spotFee:'0.001'}},'/api/market':{bids:[],asks:[],trades:[]},'/api/candles':[],
   '/api/products':{priceFresh:true,instruments:[{id:'BTCUSDT',kind:'Spot',status:'Trading'},{id:'BTC-PERP',kind:'Perpetual',status:'Trading'}],tickers:[],optionAnalytics:[],pools:[],amm:{}},
   '/api/balances':[], '/api/portfolio':{leverage:'5',risk:{initialMargin:'100',equity:'1000',maintenanceMargin:'10'},positions:[]},'/api/orders':[order],'/api/trades':[],
   '/api/order-preview':{maximumQuantity:'0',fee:'0',feeAsset:'USDT',netReceipt:'0',orderCost:'0'},
   '/api/leverage-preview':{leverage:'3',risk:{initialMargin:'160'}},'/api/leverage':{leverage:'3'}
  };
  if(path==='/api/cancel-all'){cancelled=true;await route.fulfill({json:{cancelled:'1'}});return;}
  await route.fulfill({json:data[path]||{}});
 });
 await page.goto('/trade');
 const tab=page.getByRole('button',{name:'Order history',exact:true});await tab.click();
 await expect(page.getByRole('columnheader',{name:'Avg. execution price · USDT'})).toBeVisible();await expect(page.locator('.history-panel')).toContainText('123.45');
 expect(await tab.evaluate(el=>parseInt(getComputedStyle(el).paddingLeft))).toBeGreaterThanOrEqual(16);
 await page.getByRole('button',{name:'Open orders'}).click();await page.getByRole('button',{name:'Cancel all',exact:true}).click();
 await expect(page.getByRole('dialog')).toContainText('across every market');await page.getByRole('button',{name:'Cancel all open orders',exact:true}).click();
 await expect(page.locator('.toast')).toContainText('1 orders cancelled.');await expect(page.getByRole('dialog')).toHaveCount(0);
 await page.getByRole('button',{name:'Dismiss notification'}).click();await expect(page.locator('.toast')).toHaveCount(0);
 await page.goto('/trade?instrument=BTC-PERP');await page.locator('.leverage-card button').click();
 await expect(page.getByRole('dialog')).toContainText('Proposed initial margin');await expect(page.getByRole('dialog')).toContainText('160.00 USDT');
 await page.getByRole('slider',{name:'Account leverage'}).fill('3');await expect(page.getByRole('button',{name:'Apply 3× leverage'})).toBeEnabled();await page.getByRole('button',{name:'Apply 3× leverage'}).click();await expect(page.locator('.toast')).toContainText('Account leverage updated');
 await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
});
