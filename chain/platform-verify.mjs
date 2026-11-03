import assert from 'node:assert/strict';
import {randomUUID,createHmac} from 'node:crypto';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {writeFileSync,mkdirSync} from 'node:fs';
import Decimal from '../web/node_modules/decimal.js/decimal.mjs';
const exec=promisify(execFile),base=process.env.EXCHANGE_URL||'http://127.0.0.1:8081',run=Date.now();
const actors=[];
async function request(actor,path,body,key=randomUUID()){
  const response=await fetch(base+'/api'+path,{method:body===undefined?'GET':'POST',headers:{'Content-Type':'application/json',...(actor?.token?{Authorization:'Bearer '+actor.token}:{}),'Idempotency-Key':key},body:body===undefined?undefined:JSON.stringify(body)});
  const data=await response.json();if(!response.ok)throw Error(path+' '+response.status+' '+JSON.stringify(data));return data;
}
async function until(fn){for(let i=0;i<100;i++){if(await fn())return;await new Promise(r=>setTimeout(r,500));}throw Error('Timed out waiting for expected state');}
async function fund(address,asset,amount){const result=await exec('docker',['compose','--env-file','.env.next','-f','compose.next.yaml','-p','novcic-next','exec','-T','custody','dotnet','Novcic.Custody.dll','--local-fund',address,asset,amount,randomUUID()],{timeout:65000});assert.match(result.stdout,/Funded/);}
for(const role of ['maker','taker']){
  const credentials={email:`${role}-${run}@example.com`,password:'PlatformVerification123!',country:'RS'};
  const auth=await request(null,'/auth/register',credentials);const actor={token:auth.accessToken,credentials,id:auth.user.id};actor.address=(await request(actor,'/custody/address',{})).address;actors.push(actor);
}
await Promise.all(actors.flatMap(a=>[fund(a.address,'BTC','1'),fund(a.address,'USDT','50000')]));
for(const a of actors){
  await until(async()=>{const w=await request(a,'/balances');return w.find(x=>x.kind==='Funding'&&x.asset==='USDT')?.available==='50000.00000000'&&w.find(x=>x.kind==='Funding'&&x.asset==='BTC')?.available==='1.00000000';});
  await request(a,'/transfer',{asset:'BTC',from:'Funding',to:'Unified Trading',amount:'1'});await request(a,'/transfer',{asset:'USDT',from:'Funding',to:'Unified Trading',amount:'45000'});
}
console.log('PASS concurrent local funding, both tokens, confirmed deposits and account transfers.');
const[a,b]=actors;
async function order(actor,side,quantity,price,instrument='BTCUSDT',reduceOnly=false){return request(actor,'/orders',{side,type:price===null?'Market':'Limit',quantity,price,instrument,leverage:instrument==='BTCUSDT'?'1':'2',reduceOnly});}
await order(a,'Sell','0.02','77000');const spot=await order(b,'Buy','0.02',null);assert.equal(spot.status,'Filled');
assert.ok(new Decimal((await request(a,'/market')).lastPrice).gt(0));assert.ok((await request(a,'/candles')).length);
const products=await request(a,'/products');assert.equal(products.priceFresh,true);assert.ok(products.index.price);
const price=new Decimal(products.index.price).toFixed(2);
for(const instrument of ['BTC-PERP',products.instruments.find(x=>x.kind==='Future'&&x.status==='Trading').id,products.instruments.find(x=>x.kind==='Option'&&x.optionSide==='Call'&&x.status==='Trading').id]){
  const quote=instrument.endsWith('-C')?'1000':price;
  await order(a,'Sell','0.001',quote,instrument);assert.equal((await order(b,'Buy','0.001',null,instrument)).status,'Filled');
  await order(a,'Buy','0.001',quote,instrument,true);assert.equal((await order(b,'Sell','0.001',null,instrument,true)).status,'Filled');
}
console.log('PASS spot, perpetual, expiry future and European-option matching, positions and reduce-only closes.');
await request(a,'/supply',{asset:'USDT',amount:'1000'});
const loan=await request(b,'/borrow',{asset:'USDT',amount:'100',collateralAsset:'BTC',collateral:'0.01'});
await request(b,'/add-collateral',{loanId:loan.id,amount:'0.001'});
assert.equal((await request(b,'/repay',{loanId:loan.id,amount:'101'})).status,'Repaid');
await request(a,'/redeem',{asset:'USDT',amount:(await request(a,'/portfolio')).supplies.find(s=>s.asset==='USDT').shares});
const deposit=await request(a,'/liquidity-deposit',{btc:'0.01',usdt:new Decimal((await request(a,'/products')).index.price).mul('0.01').toFixed(8)});
await until(async()=>{const p=await request(a,'/portfolio');const op=p.liquidityRequests.find(x=>x.id===deposit.id);if(op.status==='Completed')return true;
  const orders=await request(a,'/market');const bid=orders.bids[0],ask=orders.asks[0];
  // Conversion orders are filled through the same public market, never a special swap endpoint.
  if(bid&&new Decimal(bid.quantity).lt('0.001'))await order(b,'Sell',bid.quantity,bid.price);
  else if(ask&&new Decimal(ask.quantity).lt('0.001'))await order(b,'Buy',ask.quantity,ask.price);
  return false;
});
const book=await request(b,'/market');assert.equal('orders' in book,false);for(const level of [...book.bids,...book.asks])assert.deepEqual(Object.keys(level).sort(),['price','quantity','total']);
const exit=await request(a,'/liquidity-withdraw',{shares:'0.01',asset:'USDT'});
await until(async()=>(await request(a,'/portfolio')).liquidityRequests.find(x=>x.id===exit.id).status==='Completed');
const dca=await request(b,'/dca',{amount:'10',hours:'24',budget:'100'});assert.equal((await request(b,'/toggle-dca',{id:dca.id})).paused,true);
console.log('PASS Earn, borrowing, additional collateral, repayment, redemption, queued liquidity provision/removal and DCA controls.');
const key=await request(b,'/keys',{name:'Verification key',scope:'Trade'});
function signed(path,body,{nonce=randomUUID(),timestamp=Date.now().toString(),idem=body===undefined?'':randomUUID()}={}){
  const method=body===undefined?'GET':'POST',raw=body===undefined?'':JSON.stringify(body),canonical=[timestamp,nonce,method,'/api'+path,idem,raw].join('\n');
  const headers={'Content-Type':'application/json','X-Api-Key':key.id,'X-Api-Timestamp':timestamp,'X-Api-Nonce':nonce,'X-Api-Signature':createHmac('sha256',key.secret).update(canonical).digest('hex'),'Idempotency-Key':idem};
  return()=>fetch(base+'/api'+path,{method,headers,body:raw||undefined});
}
const read=signed('/balances');assert.equal((await read()).status,200);assert.equal((await read()).status,401);
assert.equal((await signed('/balances',undefined,{timestamp:'1'})()).status,401);
assert.equal((await signed('/withdraw',{asset:'USDT',amount:'1',address:a.address})()).status,401);
const body={side:'Buy',type:'Limit',quantity:'0.000001',price:'1',instrument:'BTCUSDT'},idem=randomUUID();
const first=await signed('/orders',body,{idem})();assert.equal(first.status,200);const placed=await first.json();
const replay=await signed('/orders',body,{idem})();assert.equal((await replay.json()).id,placed.id);
assert.equal((await signed('/orders/'+placed.id+'/cancel',{})()).status,200);
await request(b,'/keys/'+key.id+'/revoke',{});assert.equal((await signed('/balances')()).status,401);
console.log('PASS HMAC authentication, stale/replayed nonce rejection, withdrawal scope denial, idempotent algorithmic order and revocation.');
const market=await request(a,'/market');assert.ok(BigInt(market.revision)>0n);
const samples=[];await Promise.all(Array.from({length:5},async()=>{for(let i=0;i<10;i++){const start=performance.now();await request(a,'/market');samples.push(performance.now()-start);}}));samples.sort((a,b)=>a-b);
const measurement={requests:50,concurrency:5,p50Ms:samples[24].toFixed(2),p95Ms:samples[47].toFixed(2),maxMs:samples[49].toFixed(2)};
mkdirSync('artifacts',{recursive:true});writeFileSync('artifacts/platform-actors.json',JSON.stringify(actors.map(a=>a.credentials),null,2));writeFileSync('artifacts/platform-verification.json',JSON.stringify({time:new Date().toISOString(),measurement,checks:'All passed'},null,2));
console.log('PASS market snapshot revisions and bounded read load:',JSON.stringify(measurement));
