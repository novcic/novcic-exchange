import { JsonRpcProvider, Wallet, Contract, parseUnits } from 'ethers';
import { readFileSync } from 'node:fs';
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
const manifest=JSON.parse(readFileSync(new URL('./state/deployment.json',import.meta.url)));
const provider=new JsonRpcProvider('http://127.0.0.1:8545',31337,{staticNetwork:true});
const treasury=new Wallet('0xac0974bec39a17e36ba4a6b4d238ff944bacb478cbed5efcae784d7bf4f2ff80',provider);
let access;
async function api(path,body,key){const r=await fetch('http://localhost:8081/api'+path,{method:body===undefined?'GET':'POST',headers:{'Content-Type':'application/json',...(access?{Authorization:'Bearer '+access}:{}),...(key?{'Idempotency-Key':key}:{})},body:body===undefined?undefined:JSON.stringify(body)});if(!r.ok)throw Error(path+': '+r.status+' '+await r.text());return r.json();}
const credentials={email:`chain-${Date.now()}@example.com`,password:'ChainVerification123!',country:'RS'};
const auth=await api('/auth/register',credentials);access=auth.accessToken;
const address=(await api('/custody/address',{})).address;
assert.equal((await api('/custody/address',{})).address,address);
const token=new Contract(manifest.tokens.USDT.address,['function transfer(address,uint256) returns(bool)','function balanceOf(address) view returns(uint256)'],treasury);
execFileSync('docker',['compose','--env-file','.env.next','-f','compose.next.yaml','-p','novcic-next','exec','-T','custody','dotnet','Novcic.Custody.dll','--local-fund',address,'USDT','1234.567891',crypto.randomUUID()],{stdio:'inherit'});
async function until(fn){for(let i=0;i<100;i++){const r=await fn();if(r)return r;await new Promise(r=>setTimeout(r,500));}throw Error('Timed out waiting for chain reconciliation');}
await until(async()=>{const w=(await api('/balances')).find(x=>x.kind==='Funding'&&x.asset==='USDT');return w.available==='1234.56789100';});
console.log('PASS: address idempotency and confirmed exact-precision token deposit.');
await api('/transfer',{asset:'USDT',from:'Funding',to:'Unified Trading',amount:'100'},crypto.randomUUID());
assert.equal((await api('/balances')).find(x=>x.kind==='Unified Trading'&&x.asset==='USDT').available,'100.00000000');
const destination=Wallet.createRandom().address;
const request={asset:'USDT',amount:'34.567891',address:destination};const key=crypto.randomUUID();
const withdrawal=await api('/withdraw',request,key);assert.equal((await api('/withdraw',request,key)).id,withdrawal.id);
await until(async()=>{const h=await api('/custody/history');return h.find(x=>x.id===withdrawal.id)?.status==='Completed';});
assert.equal(await token.balanceOf(destination),parseUnits('34.567891',6));
const funding=(await api('/balances')).find(x=>x.kind==='Funding'&&x.asset==='USDT');assert.equal(funding.available,'1100.00000000');assert.equal(funding.locked,'0.00000000');
console.log('PASS: wallet transfer, reserved withdrawal, duplicate request, on-chain payout and exact final balance.');
// Store only test login credentials for browser validation, never custody keys.
const {writeFileSync,mkdirSync}=await import('node:fs');mkdirSync('artifacts',{recursive:true});writeFileSync('artifacts/chain-review.json',JSON.stringify(credentials));
provider.destroy();
