import {test,expect,type Page} from '@playwright/test';

async function fixture(page:Page,stale=false,bankrupt=false){
  const risk={equity:bankrupt?'-10':'500',marginBalance:bankrupt?'-10':'500',totalEquity:stale?null:bankrupt?'90':'600',totalWalletBalance:stale?null:'1000',availableBalance:stale?null:bankrupt?'-610':'-100',initialMargin:'600',maintenanceMargin:'30',initialMarginRate:stale||bankrupt?null:'120',maintenanceMarginRate:stale||bankrupt?null:'6',transferableUsdt:'0',transferableBtc:'0',unrealizedPnl:'-400',realizedCash:'0',indexPrice:'50000',priceFresh:!stale,restricted:bankrupt};
  const assets=[
    {kind:'Funding',asset:'BTC',walletBalance:'0',locked:'0',unrealizedPnl:'0',realizedCash:'0',equity:'0',transferable:'0',usdValue:'0'},
    {kind:'Funding',asset:'USDT',walletBalance:'10000',locked:'0',unrealizedPnl:'0',realizedCash:'0',equity:'10000',transferable:'10000',usdValue:'10000'},
    {kind:'Unified Trading',asset:'BTC',walletBalance:'0.02',locked:'0',unrealizedPnl:'0',realizedCash:'0',equity:'0.02',transferable:'0',usdValue:stale?null:'1000'},
    {kind:'Unified Trading',asset:'USDT',walletBalance:'0',locked:'0',unrealizedPnl:stale?null:'-400',realizedCash:'0',equity:stale?null:'-400',transferable:'0',usdValue:stale?null:'-400'}
  ];
  await page.route('**/hubs/**',r=>r.abort());
  await page.route('**/api/**',async route=>{
    const data:Record<string,unknown>={
      '/api/auth/refresh':{accessToken:'fixture',user:{id:'fixture',email:'fixture@example.com',username:'User14978236',avatar:'default',country:'RS'}},
      '/api/config':{amm:{spotFee:'0.001',futuresFee:'0.00055',optionFee:'0.0003',optionPremiumCap:'0.07'}},
      '/api/products':{index:{price:'50000'},priceFresh:!stale,instruments:[{id:'BTC-PERP',kind:'Perpetual',status:'Trading'}],tickers:[],optionAnalytics:[],pools:[],amm:{}},
      '/api/market':{markPrice:stale?null:'50000',lastPrice:'54000',bids:[],asks:[],trades:[],high:'54000',low:'50000',volume:'0.12',change:'8'},
      '/api/candles':[], '/api/orders':[], '/api/trades':[], '/api/ledger':[], '/api/custody/history':[],
      '/api/network':{network:'Local Ethereum',chainId:'31337',confirmations:'3',tokens:{}},
      '/api/balances':assets.map(a=>({kind:a.kind,asset:a.asset,available:a.walletBalance,locked:a.locked})),
      '/api/portfolio':{leverage:'10',risk,assets,positions:[],events:[]}
    };
    const path=new URL(route.request().url()).pathname;
    await route.fulfill({json:data[path]??{}});
  });
}

test('last and mark prices are distinct, IMR above 100% does not mean liquidation',async({page})=>{
  await fixture(page);await page.goto('/trade?product=Perpetual');
  await expect(page.getByTestId('market-mark')).toHaveText('50,000.00');
  await expect(page.locator('.last-price>strong')).toHaveText('54,000.00');
  const summary=page.getByRole('region',{name:'Cross-margin account'});
  await expect(summary.getByTestId('account-equity')).toHaveText('$600.00');
  await expect(summary.getByTestId('margin-balance')).toHaveText('$500.00');
  await expect(summary.getByTestId('available-margin')).toHaveText('−$100.00');
  await expect(summary).toContainText('IMR 120.00%');await expect(summary).toContainText('MMR 6.00%');
  await expect(summary.locator('p')).toHaveCount(0);
  await expect(summary).not.toContainText('For new orders');
  await expect(summary.locator('dl>div').last()).toContainText('Available balance');
  await expect(summary.getByTestId('available-margin')).toHaveClass('negative');
  await expect(page.getByRole('button',{name:'Max',exact:true})).toBeDisabled();
  await expect(page.getByRole('slider',{name:'Percentage of available balance'})).toBeDisabled();
  expect(await page.getByTestId('market-mark').evaluate(e=>parseFloat(getComputedStyle(e).fontSize))).toBeLessThan(await page.locator('.last-price>strong').evaluate(e=>parseFloat(getComputedStyle(e).fontSize)));
  await page.screenshot({path:'../artifacts/margin-terminal.png',fullPage:true});
  await summary.scrollIntoViewIfNeeded();await page.screenshot({path:'../artifacts/margin-terminal-account.png',fullPage:true});
});

test('asset equity includes losses, wallet coins and transferable stay distinct',async({page})=>{
  await fixture(page);await page.goto('/assets');await page.getByRole('tab',{name:'Unified Trading'}).click();
  const usdt=page.getByTestId('Unified Trading-USDT');
  await expect(usdt.getByTestId('equity')).toHaveText('-400.00000000');
  await expect(usdt.getByTestId('total')).toContainText('0.00000000');
  await expect(usdt.getByTestId('available')).toHaveText('0.00000000');
  await expect(page.locator('.balance-amount')).toContainText('$10,600.00');
  await page.getByRole('checkbox',{name:'Hide zero balances'}).check();await expect(usdt).toBeVisible();
  await expect(page.getByTestId('Unified Trading-BTC').getByTestId('available')).toHaveText('0.00000000');
  await page.screenshot({path:'../artifacts/margin-assets.png',fullPage:true});
  await usdt.getByRole('button',{name:'Transfer',exact:true}).click();
  await expect(page.getByRole('dialog')).toContainText('Available to transfer: 0.00000000 USDT');
});

test('stale prices do not display old equity or rates as live',async({page})=>{
  await fixture(page,true);await page.goto('/trade?product=Perpetual');
  await expect(page.getByTestId('market-mark')).toHaveText('—');
  await expect(page.getByTestId('available-margin')).toHaveText('—');
  await expect(page.getByRole('region',{name:'Cross-margin account'})).toContainText('MMR —');
  await page.goto('/assets');await page.getByRole('tab',{name:'Unified Trading'}).click();
  await expect(page.getByTestId('Unified Trading-USDT').getByTestId('equity')).toHaveText('—');
  await expect(page.locator('.balance-amount')).not.toContainText('$');
});

test('nonpositive margin displays breached rates rather than negative percentages',async({page})=>{
  await fixture(page,false,true);await page.goto('/trade?product=Perpetual');
  const summary=page.getByRole('region',{name:'Cross-margin account'});
  await expect(summary).toContainText('MMR ≥100%');await expect(summary).toContainText('IMR ≥100%');
  await expect(summary.getByTestId('available-margin')).toHaveText('−$610.00');
});

test('margin and asset controls reflow on mobile and at 200 percent desktop scale',async({page})=>{
  await fixture(page);
  for(const width of [390,720]){
    await page.setViewportSize({width,height:900});await page.goto('/assets');
    await page.getByRole('tab',{name:'Unified Trading'}).click();
    await expect(page.getByTestId('Unified Trading-USDT').getByTestId('equity')).toBeVisible();
    expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBeTruthy();
    await page.screenshot({path:`../artifacts/margin-assets-${width}.png`,fullPage:true});
    await page.goto('/trade?product=Perpetual');await expect(page.getByTestId('market-mark')).toBeVisible();
    expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBeTruthy();
  }
});
