import {test,expect,type Page} from '@playwright/test';
import {readFileSync} from 'node:fs';
import Decimal from 'decimal.js';
const accounts=()=>JSON.parse(readFileSync('../artifacts/platform-actors.json','utf8'));
async function login(page:Page){const account=accounts()[1];await page.goto('/login');await page.getByLabel('Email address').fill(account.email);await page.getByLabel('Password',{exact:true}).fill(account.password);await page.getByRole('button',{name:/^Sign in/}).click();await expect(page.getByRole('heading',{name:'Your assets'})).toBeVisible();await expect(page.locator('.balance-amount')).not.toContainText('…');}

test('registration, country picker, session restoration and branding',async({page})=>{
  await page.goto('/register');await page.getByLabel('Email address').fill(`browser-${Date.now()}@example.com`);await page.getByLabel('Password',{exact:true}).fill('BrowserVerification123!');
  await page.getByRole('button',{name:'Country of residence'}).click();await page.getByRole('textbox',{name:'Search countries'}).fill('Serbia');await page.getByRole('option',{name:'Serbia RS'}).click();await page.getByRole('button',{name:/^Create account/}).click();
  await expect(page.getByRole('heading',{name:'Your assets'})).toBeVisible();await page.reload();await expect(page.getByRole('heading',{name:'Your assets'})).toBeVisible();await expect(page).toHaveTitle('Novčić — Digital Asset Exchange');await expect(page.locator('body')).not.toContainText('Demo Exchange');
});
test('asset tabs, contextual actions and transfer dialog',async({page})=>{
  await login(page);await page.getByRole('button',{name:'⇄ Transfer',exact:true}).click();await page.getByLabel('Transfer amount').fill('1');await page.getByRole('button',{name:'Transfer funds',exact:true}).click();await expect(page.getByRole('dialog')).toHaveCount(0);
  await page.getByRole('tab',{name:/Unified Trading/}).click();await expect(page.getByRole('tab',{name:/Unified Trading/})).toHaveAttribute('aria-selected','true');
  await page.getByRole('tab',{name:/Funding/}).click();await page.locator('.asset-table tr').filter({hasText:'USDT'}).getByRole('button',{name:'Deposit',exact:true}).click();await expect(page.getByLabel('Deposit address')).toHaveValue(/^0x[0-9a-f]{40}$/);await page.getByRole('button',{name:'Close dialog'}).click();
  await page.locator('.asset-table tr').filter({hasText:'BTC'}).getByRole('link',{name:/Trade/}).click();await expect(page.getByRole('button',{name:'Choose market'})).toContainText('BTCUSDT');
});
test('spot order lifecycle and product navigation',async({page})=>{
  await login(page);await page.getByRole('button',{name:'Trade',exact:true}).click();await page.getByRole('link',{name:/^Spot/}).click();await page.getByLabel('Limit price',{exact:true}).fill('1');await page.getByLabel('Order quantity').fill('0.000001');await page.getByRole('button',{name:'Buy BTC',exact:true}).click();await expect(page.locator('.order-panel [role=status]')).toContainText('Order accepted');
  const row=page.locator('.history-panel tbody tr').filter({hasText:'0.000001'});await row.getByRole('button',{name:'Cancel',exact:true}).click();await expect(page.locator('.history-panel')).toContainText('Order cancelled.');
  for(const [tab,heading] of [['Perpetual futures','BTCUSDT'],['Expiry futures','BTC-'],['Options','BTC-']]){await page.getByRole('button',{name:'Trade',exact:true}).click();await page.getByRole('link',{name:new RegExp('^'+tab)}).click();await expect(page.locator('.market-picker-trigger')).toContainText(heading);await expect(page.getByLabel('Reduce only')).toBeVisible();}
});
test('Earn subscription and redemption, DCA pause, portfolio',async({page})=>{
  await login(page);await page.getByRole('link',{name:'Earn & invest',exact:true}).click();const card=page.locator('article').filter({has:page.getByRole('heading',{name:'USDT Earn'})});await card.getByRole('button',{name:'Supply',exact:true}).click();await page.getByLabel('Amount',{exact:true}).fill('5');await page.getByRole('button',{name:'Confirm supply'}).click();await expect(page.getByRole('dialog')).toHaveCount(0);
  await card.getByRole('button',{name:'Redeem',exact:true}).click();await page.getByLabel('Amount',{exact:true}).fill('1');await page.getByRole('button',{name:'Confirm redeem'}).click();await expect(page.getByRole('dialog')).toHaveCount(0);
  await page.getByRole('tab',{name:'Recurring buys'}).click();await page.getByRole('button',{name:'Create plan',exact:true}).click();await page.getByLabel('Amount',{exact:true}).fill('10');await page.getByLabel('Secondary amount').fill('20');await page.getByRole('button',{name:'Confirm create plan'}).click();await expect(page.getByRole('dialog')).toHaveCount(0);await page.getByRole('button',{name:'Pause',exact:true}).click();await expect(page.getByRole('button',{name:'Pause',exact:true})).toHaveCount(0);
  await page.getByRole('link',{name:'Portfolio',exact:true}).click();await expect(page.getByRole('heading',{name:'Margin health'})).toBeVisible();await expect(page.locator('body')).not.toContainText('NaN');
});
test('API key creation, one-time display and revocation',async({page})=>{
  const keyName='Browser check '+Date.now();await login(page);await page.getByRole('link',{name:'API',exact:true}).click();await page.getByLabel('Key name').fill(keyName);await page.getByRole('button',{name:'Create API key'}).click();await expect(page.getByRole('heading',{name:'Save your API secret'})).toBeVisible();await page.getByRole('button',{name:'I saved it — close'}).click();await expect(page.locator('.secret-panel')).toHaveCount(0);await page.locator('tbody tr').filter({hasText:keyName}).getByRole('button',{name:'Revoke'}).click();await expect(page.locator('tbody tr').filter({hasText:keyName})).toContainText('Revoked');
});
test('mobile navigation and modal keyboard dismissal',async({page})=>{
  await page.setViewportSize({width:390,height:844});await login(page);await page.getByRole('button',{name:'Trade',exact:true}).click();await page.getByRole('link',{name:/^Spot/}).click();await page.getByRole('button',{name:'Trade',exact:true}).click();await page.getByRole('link',{name:/^Perpetual futures/}).click();await page.getByRole('link',{name:'Assets',exact:true}).click();await page.getByRole('button',{name:'⇄ Transfer',exact:true}).click();await expect(page.getByRole('dialog')).toBeVisible();await page.keyboard.press('Escape');await expect(page.getByRole('dialog')).toHaveCount(0);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:'../artifacts/ui/assets-mobile-verified.png',fullPage:true});
});
test('unknown transfer response survives reload and retries exactly once',async({page})=>{
  await login(page);
  const auth=await(await page.request.post('/api/auth/login',{data:accounts()[1]})).json();
  const balances=async()=>await(await page.request.get('/api/balances',{headers:{Authorization:'Bearer '+auth.accessToken}})).json();
  const before=(await balances()).find((w:any)=>w.kind==='Unified Trading'&&w.asset==='USDT').available;
  await page.route('**/api/transfer',async route=>{await route.fetch();await route.abort('failed');});
  await page.getByRole('button',{name:'⇄ Transfer',exact:true}).click();await page.getByLabel('Transfer amount').fill('1');await page.getByRole('button',{name:'Transfer funds',exact:true}).click();
  await expect(page.getByRole('button',{name:'Retry original request'}).first()).toBeVisible();
  await page.unroute('**/api/transfer');await page.reload();await expect(page.getByRole('button',{name:'Retry original request'})).toBeVisible();await page.getByRole('button',{name:'Retry original request'}).click();
  await expect(page.getByRole('button',{name:'Retry original request'})).toHaveCount(0);
  const after=(await balances()).find((w:any)=>w.kind==='Unified Trading'&&w.asset==='USDT').available;
  expect(new Decimal(after).minus(before).toString()).toBe('1');
});
