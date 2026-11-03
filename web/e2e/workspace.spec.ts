import {test,expect} from '@playwright/test';

test('public API exposes aggregate depth only',async({request})=>{
  const response=await request.get('/api/market');expect(response.ok()).toBeTruthy();const book=await response.json();
  expect(book).not.toHaveProperty('orders');
  for(const row of [...book.bids,...book.asks])expect(Object.keys(row).sort()).toEqual(['price','quantity','total']);
});
for(const [width,height] of [[1366,768],[1440,900],[1920,1080]])test(`desktop terminal fits ${width}x${height}`,async({page})=>{
  await page.setViewportSize({width,height});await page.goto('/trade');await expect(page.getByRole('button',{name:'Choose market'})).toBeVisible();
  await expect(page.getByRole('button',{name:'Individual orders'})).toHaveCount(0);
  await expect(page.locator('.market-navigation')).toHaveCount(0);
  expect(await page.evaluate(()=>document.documentElement.scrollHeight<=innerHeight+1)).toBeTruthy();
  const history=await page.locator('.history-tabs').boundingBox();expect(history!.y+history!.height).toBeLessThan(height);
  await page.getByRole('button',{name:'Trade',exact:true}).click();await page.getByRole('link',{name:'Perpetual futures'}).click();
  await expect(page.getByRole('button',{name:'Choose market'})).toHaveText(/BTCUSDT/);await expect(page.locator('body')).not.toContainText('BTC-PERP');
  await page.getByRole('button',{name:'Trade',exact:true}).click();await page.getByRole('link',{name:'Options',exact:false}).click();await expect(page.locator('.options-chain')).toBeVisible();
  await expect(page.locator('.options-chain')).toContainText('Calls');await expect(page.locator('.options-chain')).toContainText('Puts');
  await page.screenshot({path:`../artifacts/ui/options-${width}.png`});
});
test('market picker searches and closes with Escape; mobile stays within viewport',async({page})=>{
  await page.setViewportSize({width:390,height:844});await page.goto('/trade');await page.getByRole('button',{name:'Choose market'}).click();
  await page.getByRole('textbox',{name:'Search markets'}).fill('bitcoin');await expect(page.getByRole('dialog')).toContainText('Featured');await page.keyboard.press('Escape');await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
});

test('option quote selection preserves side and a valid limit price',async({page})=>{
  await page.goto('/trade?product=Option');
  const quote=page.locator('.options-chain button.positive:not([disabled])').first();await expect(quote).toBeVisible();
  const price=(await quote.innerText()).replaceAll(',','');await quote.click();
  await expect(page.locator('.side-switch .active')).toContainText('Sell');await expect(page.getByLabel('Limit price',{exact:true})).toHaveValue(price);
  expect(await page.getByLabel('Limit price',{exact:true}).evaluate((e:HTMLInputElement)=>e.validity.valid)).toBeTruthy();
  await page.getByLabel('Limit price',{exact:true}).fill('123.45');
  await page.getByRole('button',{name:'5m',exact:true}).count();
  await expect(page.getByLabel('Limit price',{exact:true})).toHaveValue('123.45');
});
