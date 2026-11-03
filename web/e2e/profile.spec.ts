import {test,expect} from '@playwright/test';
import {createHmac,randomUUID} from 'node:crypto';
const password='ProfileVerification123!';
const names=['Spot','Perpetual','Future','Option'];
test('profiles, unique names, scoped HMAC keys and immediate security revocation',async({page,request})=>{
 const email=`profile-${randomUUID()}@example.com`;
 const registered=await request.post('/api/auth/register',{data:{email,password,country:'RS'}});expect(registered.ok()).toBeTruthy();const session=await registered.json();
 const otherResponse=await request.post('/api/auth/register',{data:{email:`other-${randomUUID()}@example.com`,password,country:'RS'}});const other=await otherResponse.json();expect(other.user.username).not.toBe(session.user.username);
 expect(session.user.username).toMatch(/^User[0-9]{8}$/);expect(session.user.avatar).toBe('default');
 const auth={Authorization:'Bearer '+session.accessToken};
 const username='Trader_'+randomUUID().slice(0,8);
 const saved=await request.post('/api/profile',{headers:auth,data:{username,avatar:'fox',dateOfBirth:'1995-06-12'}});expect(saved.ok()).toBeTruthy();expect((await saved.json()).dateOfBirth).toBe('1995-06-12');
 expect((await request.post('/api/profile',{headers:{Authorization:'Bearer '+other.accessToken},data:{username:username.toUpperCase(),avatar:'default',dateOfBirth:null}})).status()).toBe(400);
 expect((await request.post('/api/profile',{headers:auth,data:{username,avatar:'https://bad.example/image',dateOfBirth:null}})).status()).toBe(400);
 expect((await request.post('/api/profile',{headers:auth,data:{username,avatar:'fox',dateOfBirth:'2999-01-01'}})).status()).toBe(400);
 const products=await (await request.get('/api/products')).json();
 async function signed(key:{id:string;secret:string},method:string,path:string,data?:unknown){const timestamp=String(Date.now()),nonce=randomUUID(),intent=randomUUID(),body=data===undefined?'':JSON.stringify(data);const signature=createHmac('sha256',key.secret).update([timestamp,nonce,method,path,intent,body].join('\n')).digest('hex');return request.fetch(path,{method,headers:{'X-Api-Key':key.id,'X-Api-Timestamp':timestamp,'X-Api-Nonce':nonce,'X-Api-Signature':signature,'Idempotency-Key':intent,'Content-Type':'application/json'},...(body?{data:body}:{})});}
 let lastKey:{id:string;secret:string}|null=null;
 for(const scope of ['Read','Trade'])for(const product of names){
  const created=await request.post('/api/keys',{headers:auth,data:{name:scope+' '+product,scope,products:[product]}});expect(created.ok()).toBeTruthy();const key=await created.json();lastKey=key;expect(key.products).toEqual([product]);
  expect((await signed(key,'GET','/api/balances')).status()).toBe(200);
  const pnl=await signed(key,'GET','/api/pnl-history?limit=1');expect(pnl.status()).toBe(200);expect((await pnl.json()).items).toEqual([]);
  expect((await signed(key,'POST','/api/transfer',{asset:'USDT',from:'Funding',to:'Unified Trading',amount:'1'})).status()).toBe(403);
  expect((await signed(key,'POST','/api/leverage',{leverage:'2'})).status()).toBe(403);
  for(const other of names){const instrument=products.instruments.find((i:any)=>i.kind===other&&i.status==='Trading');expect(instrument).toBeTruthy();const response=await signed(key,'POST','/api/order-preview',{instrument:instrument.id,side:'Buy',type:'Market',price:null,quantity:'0'});expect(response.status()).toBe(other===product?200:403);}
  for(const target of names){const instrument=products.instruments.find((i:any)=>i.kind===target&&i.status==='Trading');for(const path of ['/api/position-protection','/api/stop-closing','/api/amend-order']){
   const payload={instrument:instrument.id,id:randomUUID(),lifecycleId:randomUUID(),version:path==='/api/amend-order'?'opaque':'1',positionVersion:'1',protectionVersion:'0',price:'50000',takeProfit:null,stopLoss:null};
   expect((await signed(key,'POST',path,payload)).status()).toBe(scope==='Trade'&&target===product?400:403);
  }}
  expect((await signed(key,'POST','/api/cancel-all',{})).status()).toBe(scope==='Read'?403:200);
 }
 const changed=await request.post('/api/profile/password',{headers:auth,data:{currentPassword:password,newPassword:'ChangedProfile123!'}});expect(changed.ok()).toBeTruthy();
 expect((await request.get('/api/profile',{headers:auth})).status()).toBe(401);expect((await request.post('/api/auth/refresh')).status()).toBe(401);expect((await signed(lastKey!,'GET','/api/balances')).status()).toBe(401);
 const login=await request.post('/api/auth/login',{data:{email,password:'ChangedProfile123!'}});expect(login.ok()).toBeTruthy();const fresh=await login.json();const nextEmail=`changed-${randomUUID()}@example.com`;
 expect((await request.post('/api/profile/email',{headers:{Authorization:'Bearer '+fresh.accessToken},data:{currentPassword:'wrong',newEmail:nextEmail}})).status()).toBe(400);
 expect((await request.post('/api/profile/email',{headers:{Authorization:'Bearer '+fresh.accessToken},data:{currentPassword:'ChangedProfile123!',newEmail:nextEmail}})).status()).toBe(200);
 expect((await request.get('/api/profile',{headers:{Authorization:'Bearer '+fresh.accessToken}})).status()).toBe(401);
 await page.goto('/login');await page.getByLabel('Email address').fill(nextEmail);await page.getByLabel('Password',{exact:true}).fill('ChangedProfile123!');await page.getByRole('button',{name:/^Sign in/}).click();await expect(page).toHaveURL(/assets/);
 await page.getByRole('link',{name:'My profile',exact:true}).hover();await expect(page.locator('.profile-card')).toContainText(username);await expect(page.locator('.profile-card')).toContainText(nextEmail);
 await page.getByRole('link',{name:'My API keys',exact:true}).click();await expect(page.getByRole('heading',{name:'My API keys'})).toBeVisible();await page.getByRole('button',{name:'Create a new API key'}).click();await expect(page.getByRole('radio',{name:'Read-Only',exact:true})).toBeChecked();await expect(page.getByRole('checkbox',{name:'Spot',exact:true})).toBeChecked();await expect(page.locator('.permission-options label').first()).toHaveCSS('flex-direction','row');await page.getByLabel('Key name',{exact:true}).fill('Browser integration');await page.getByRole('button',{name:'Create API key',exact:true}).click();await expect(page.getByRole('heading',{name:'Save your API credentials'})).toBeVisible();await expect(page.locator('.key-secret')).toHaveCount(2);await page.getByRole('button',{name:'I saved my credentials'}).click();const createdRow=page.getByRole('row').filter({hasText:'Browser integration'});await expect(createdRow).toBeVisible();await createdRow.getByRole('button',{name:'Revoke',exact:true}).click();await expect(createdRow).toContainText('Revoked');
 await page.getByRole('link',{name:'My profile',exact:true}).click();await expect(page.getByLabel('Username',{exact:true})).toHaveValue(username);await expect(page.getByLabel('Date of birth')).toHaveValue('1995-06-12');
 await page.setViewportSize({width:390,height:844});await page.getByRole('heading',{name:'My account'}).click();await page.getByRole('link',{name:'My profile',exact:true}).focus();await expect(page.locator('.profile-dropdown')).toBeVisible();await page.keyboard.press('Escape');await expect(page.locator('.profile-dropdown')).toHaveCount(0);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
});
