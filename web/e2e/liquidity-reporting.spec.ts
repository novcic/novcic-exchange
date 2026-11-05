import {test,expect,type Page} from '@playwright/test';

async function fixture(page:Page,{stale=false,unavailable=false,loss=false,feeShare='0.8',fundingEmpty=false}={}){
  const requests:any[]=[];
  const history=[
    {id:'deposit',kind:'Deposit',sourceAccount:'Funding',asset:'USDT',btc:'0.02000000',usdt:'500.00000000',shares:'15',received:'0',status:'Completed',detail:'Contribution balanced and activated',createdAt:'2026-11-04T00:00:00Z',completedAt:'2026-11-04T00:01:00Z'},
    {id:'withdrawal',kind:'Withdrawal',asset:'USDT',btc:'0',usdt:'0',shares:'7.5',received:'750',status:'Completed',detail:'Redeemed at execution value',createdAt:'2026-11-04T01:00:00Z',completedAt:'2026-11-04T01:01:00Z'},
    {id:'pending',kind:'Withdrawal',asset:'BTC',btc:'0',usdt:'0',shares:'10',received:'0',status:'Pending',detail:'Waiting for counterparty fills and released collateral',createdAt:'2026-11-04T02:00:00Z',completedAt:null},
    {id:'cancelled',kind:'Deposit',asset:'USDT',btc:'0',usdt:'100',shares:'0',received:'0',status:'Cancelled',detail:'Unprocessed request cancelled',createdAt:'2026-11-04T03:00:00Z',completedAt:'2026-11-04T03:01:00Z'}
  ];
  await page.route('**/hubs/**',r=>r.abort());
  await page.route('**/api/**',async route=>{
    const path=new URL(route.request().url()).pathname;
    if(path==='/api/liquidity-cancel'||path==='/api/liquidity-deposit'){requests.push(route.request().postDataJSON());await route.fulfill({json:{}});return;}
    const data:Record<string,unknown>={
      '/api/auth/refresh':{accessToken:'fixture',user:{id:'fixture',email:'fixture@example.com',username:'User14978236',avatar:'default',country:'RS'}},
      '/api/config':{amm:{lpShare:feeShare}},
      '/api/products':{priceFresh:!stale,index:{price:'50000'},instruments:[],pools:[],tickers:[],optionAnalytics:[],amm:{btc:'0.1',usdt:'5000',shares:'100',nav:'10000',feesEarned:'1000',feesEarnedBtc:'0.05'}},
      '/api/portfolio':{liquidity:{shares:'20'},liquidityPerformance:{sharePriceUsd:stale?null:'100',currentValueUsd:stale?null:loss?'19511.47':'2000',totalDepositedUsd:unavailable?null:loss?'19750':'1500',totalWithdrawnUsd:unavailable?null:loss?'0':'750',pnlUsd:stale||unavailable?null:loss?'-238.53':'1250',returnPercent:stale||unavailable?null:loss?'-1.2077468354':'83.3333333333',completeHistory:!unavailable},liquidityFees:{btc:unavailable?null:'0.0002',usdt:unavailable?null:'12.5',since:'2026-11-04T00:01:00Z',completeHistory:!unavailable},liquidityRequests:history,loans:[],supplies:[],schedules:[]},
      '/api/balances':[{kind:'Unified Trading',asset:'BTC',available:'1'},{kind:'Unified Trading',asset:'USDT',available:'10000'},{kind:'Funding',asset:'BTC',available:fundingEmpty?'0':'0.125'},{kind:'Funding',asset:'USDT',available:fundingEmpty?'0':'1234.56789'}]
    };
    expect(path in data).toBeTruthy();await route.fulfill({json:data[path]});
  });
  await page.goto('/invest?tab=Liquidity');return requests;
}

test('liquidity deposit uses the chosen account balances and exact submission amounts',async({page})=>{
  const requests=await fixture(page);await page.getByRole('button',{name:'Add liquidity',exact:true}).click();
  const dialog=page.getByRole('dialog'),source=dialog.getByLabel('Source account'),controls=dialog.locator('.balance-control');
  await expect(source).toHaveValue('Unified Trading');await source.selectOption('Funding');
  await expect(dialog).toContainText('Available 0.12500000 BTC · 1,234.56789000 USDT');
  await controls.nth(0).getByRole('button',{name:'Max',exact:true}).click();await controls.nth(1).getByRole('button',{name:'50%',exact:true}).click();
  await expect(dialog.getByLabel('Amount',{exact:true})).toHaveValue('0.12500000');await expect(dialog.getByLabel('Secondary amount',{exact:true})).toHaveValue('617.28394500');
  await source.selectOption('Unified Trading');await expect(dialog).toContainText('Available 1.00000000 BTC · 10,000.00000000 USDT');
  await expect(dialog.getByLabel('Amount',{exact:true})).toHaveValue('0.12500000');await source.selectOption('Funding');
  await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
  await dialog.getByRole('button',{name:'Confirm add liquidity'}).scrollIntoViewIfNeeded();await page.screenshot({path:'../artifacts/liquidity-funding-mobile.png',fullPage:true});
  await dialog.getByRole('button',{name:'Confirm add liquidity'}).click();await expect(dialog).not.toBeVisible();
  expect(requests).toEqual([{btc:'0.12500000',usdt:'617.28394500',sourceAccount:'Funding'}]);
});

test('empty Funding cannot size from Unified Trading balances and both sources remain available',async({page})=>{
  await fixture(page,{fundingEmpty:true});await page.getByRole('button',{name:'Add liquidity',exact:true}).click();
  const dialog=page.getByRole('dialog');await dialog.getByLabel('Source account').selectOption('Funding');
  await expect(dialog.getByRole('button',{name:'Max',exact:true}).nth(0)).toBeDisabled();await expect(dialog.getByRole('button',{name:'Max',exact:true}).nth(1)).toBeDisabled();
  await dialog.getByLabel('Source account').selectOption('Unified Trading');await dialog.getByRole('button',{name:'Max',exact:true}).nth(0).click();
  await expect(dialog.getByLabel('Amount',{exact:true})).toHaveValue('1.00000000');
  await page.evaluate(()=>{document.documentElement.style.zoom='2';});await expect(dialog.getByLabel('Source account')).toBeVisible();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
});

test('liquidity history distinguishes submitted assets, received withdrawals and pending estimates',async({page})=>{
  const requests=await fixture(page);const rows=page.locator('.liquidity-requests tbody tr');
  await expect(rows).toHaveCount(4);await expect(rows.nth(0)).toContainText('Add liquidity');await expect(rows.nth(0)).toContainText('0.02000000 BTC');await expect(rows.nth(0)).toContainText('500.00000000 USDT');await expect(rows.nth(0)).toContainText('$1,500.00');await expect(rows.nth(0)).toContainText('Issued');
  await expect(rows.nth(0)).toContainText('From Funding');await expect(rows.nth(3)).toContainText('From Unified Trading');
  await expect(rows.nth(1)).toContainText('Remove liquidity');await expect(rows.nth(1)).toContainText('750.00000000 USDT');await expect(rows.nth(1)).toContainText('Received');await expect(rows.nth(1)).toContainText('Redeemed');
  await expect(rows.nth(2)).toContainText('0.02000000 BTC');await expect(rows.nth(2)).toContainText('Estimated receive');await expect(rows.nth(2)).toContainText('$1,000.00');
  await expect(page.getByRole('columnheader',{name:'Requested · UTC'})).toBeVisible();await expect(rows.nth(3)).toContainText('Closed 2026-11-04 03:01:00');
  await page.getByRole('button',{name:'Cancel request'}).click();expect(requests).toEqual([{id:'pending'}]);
});

test('personal earnings are separate from pool totals and explicitly compounded',async({page})=>{
  await fixture(page);const earnings=page.getByRole('article',{name:'Your fee earnings'});
  await expect(page.getByTestId('lp-fees-btc')).toHaveText('0.00020000 BTC');await expect(page.getByTestId('lp-fees-usdt')).toHaveText('12.50000000 USDT');await expect(page.getByTestId('lp-fees-usd')).toHaveText('$22.50');
  await expect(page.getByText('20.0000%',{exact:true})).toBeVisible();await expect(page.locator('.liquidity-cards .product-rate')).toContainText('$2,000.00');
  await expect(earnings).toContainText('Automatically reinvested');await page.getByText('How fees and withdrawals work').click();await expect(earnings).toContainText('16.00% of the customer fee');await expect(earnings).toContainText('There is no separate claim step');
  await page.getByRole('button',{name:'Remove liquidity',exact:true}).click();await expect(page.getByRole('dialog')).toContainText('Remove liquidity');await expect(page.getByRole('dialog').getByRole('combobox')).toHaveValue('USDT');
});

test('stale BTC valuation is unavailable while exact assets and USDT receipts remain visible',async({page})=>{
  await fixture(page,{stale:true});await expect(page.getByTestId('lp-fees-btc')).toHaveText('0.00020000 BTC');await expect(page.getByTestId('lp-fees-usd')).toHaveText('—');
  await expect(page.getByTestId('lp-pnl')).toHaveText('—');await expect(page.getByTestId('lp-share-price')).toHaveText('—');await expect(page.getByTestId('lp-invested')).toHaveText('1,500.00');
  const rows=page.locator('.liquidity-requests tbody tr');await expect(rows.nth(0).locator('td').nth(3)).toHaveText('—');await expect(rows.nth(1).locator('td').nth(3)).toHaveText('$750.00');await expect(rows.nth(2)).toContainText('— BTC');
});

test('missing attribution is not shown as zero or as claimable rewards',async({page})=>{
  await fixture(page,{unavailable:true});await expect(page.getByTestId('lp-fees-btc')).toHaveText('— BTC');await expect(page.getByText('Personal fee history is unavailable.',{exact:false})).toBeVisible();await expect(page.getByRole('button',{name:/claim/i})).toHaveCount(0);
  await expect(page.getByTestId('lp-pnl')).toHaveText('—');await expect(page.getByTestId('lp-invested')).toHaveText('—');await expect(page.getByText('Historical investment values are incomplete, so total P&L is unavailable.')).toBeVisible();
});

test('LP performance includes past withdrawals and explains changing share price',async({page})=>{
  await fixture(page);
  await expect(page.getByTestId('lp-pnl')).toHaveText('+$1,250.00 (+83.33%)');await expect(page.getByTestId('lp-pnl')).toHaveClass('positive');
  await expect(page.getByTestId('lp-invested')).toHaveText('1,500.00');await expect(page.getByTestId('lp-withdrawn')).toHaveText('750.00');await expect(page.getByTestId('lp-share-price')).toHaveText('100.0000');
  await page.getByText('How LP performance is calculated').click();const card=page.getByRole('article',{name:'Your liquidity',exact:true});
  await expect(card).toContainText('Share price can rise or fall');await expect(card).toContainText('current value + completed withdrawals − total invested');
  await expect(page.getByText('Choose USDT for a USDT payout;', {exact:false})).toHaveCount(0);
});

test('LP loss stays signed and visible even with positive fee earnings',async({page})=>{
  await fixture(page,{loss:true});await expect(page.getByTestId('lp-pnl')).toHaveText('−$238.53 (−1.21%)');await expect(page.getByTestId('lp-pnl')).toHaveClass('negative');
  await expect(page.getByRole('article',{name:'Your liquidity',exact:true})).toContainText('$19,511.47');
  await expect(page.getByTestId('lp-fees-usdt')).toHaveText('12.50000000 USDT');
});

test('two liquidity cards fill the row on wide screens without an empty third column',async({page})=>{
  await page.setViewportSize({width:2400,height:1300});await fixture(page);
  const reserves=page.locator('.liquidity-cards > article').nth(0),own=page.getByRole('article',{name:'Your liquidity',exact:true}),fees=page.getByRole('article',{name:'Your fee earnings'});
  await expect(own).toBeVisible();const a=(await reserves.boundingBox())!,b=(await own.boundingBox())!,c=(await fees.boundingBox())!;
  expect(Math.abs(a.y-b.y)).toBeLessThan(1);expect(Math.abs(a.width-b.width)).toBeLessThan(1);expect(Math.abs(b.x+b.width-c.x-c.width)).toBeLessThan(1);
  await page.screenshot({path:'../artifacts/liquidity-reporting-wide.png',fullPage:true});
});

test('fee explanations follow configured split and layout reflows on mobile',async({page})=>{
  await fixture(page,{feeShare:'0.6'});await expect(page.locator('.product-intro')).toContainText('60.00%');await page.getByText('How fees and withdrawals work').click();await expect(page.getByRole('article',{name:'Your fee earnings'})).toContainText('12.00% of the customer fee');
  await page.screenshot({path:'../artifacts/liquidity-reporting-desktop.png',fullPage:true});
  await page.evaluate(()=>{document.documentElement.style.zoom='2';});
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
  await expect(page.getByRole('button',{name:'Remove liquidity',exact:true})).toBeVisible();
  await page.screenshot({path:'../artifacts/lp-performance-zoom.png',fullPage:true});
  await page.evaluate(()=>{document.documentElement.style.zoom='1';});
  await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
  await page.getByRole('region',{name:'Liquidity request history'}).focus();await page.keyboard.press('ArrowRight');await expect(page.getByRole('button',{name:'Remove liquidity',exact:true})).toBeVisible();
  await page.screenshot({path:'../artifacts/liquidity-reporting-mobile.png',fullPage:true});
});
