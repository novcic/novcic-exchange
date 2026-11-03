import assert from 'node:assert/strict';
import {readFileSync,writeFileSync} from 'node:fs';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {randomUUID} from 'node:crypto';
import {JsonRpcProvider,Contract,Wallet,parseUnits} from 'ethers';
import Decimal from '../web/node_modules/decimal.js/decimal.mjs';
const exec=promisify(execFile),base='http://127.0.0.1:8081',credentials=JSON.parse(readFileSync('artifacts/platform-actors.json'))[1];
const manifest=JSON.parse(readFileSync('chain/state/deployment.json'));
const rpc=new JsonRpcProvider('http://127.0.0.1:8545',31337,{staticNetwork:true});
const compose=(...args)=>exec('docker',['compose','--env-file','.env.next','-f','compose.next.yaml','-p','novcic-next',...args],{timeout:60000});
let token;
async function api(path,body,key=randomUUID()){
  const result=await fetch(base+'/api'+path,{method:body===undefined?'GET':'POST',headers:{'Content-Type':'application/json',...(token?{Authorization:'Bearer '+token}:{}),'Idempotency-Key':key},body:body===undefined?undefined:JSON.stringify(body)});
  if(!result.ok)throw Error(path+': '+result.status+' '+await result.text());return result.json();
}
async function until(fn){for(let i=0;i<100;i++){try{const value=await fn();if(value)return value;}catch{}await new Promise(r=>setTimeout(r,500));}throw Error('Recovery state timed out');}
token=(await api('/auth/login',credentials)).accessToken;
const before=(await api('/balances')).find(w=>w.kind==='Funding'&&w.asset==='USDT');
const target=Wallet.createRandom().address,amount='1.234567',key=randomUUID();
const request={asset:'USDT',address:target,amount};
let withdrawal,hash;
try{
  await rpc.send('evm_setAutomine',[false]);await rpc.send('evm_setIntervalMining',[0]);
  withdrawal=await api('/withdraw',request,key);
  const pending=await until(async()=>{const item=(await api('/custody/history')).find(x=>x.id===withdrawal.id);return item?.status==='Broadcast'&&item;});hash=pending.hash;
  assert.ok(hash);assert.equal((await api('/balances')).find(w=>w.kind==='Funding'&&w.asset==='USDT').locked,'1.23456700');
  await compose('restart','custody','matching');
  await until(async()=>{const r=await fetch(base+'/api/health');return r.ok;});
  assert.equal((await api('/withdraw',request,key)).id,withdrawal.id);
  const pendingAgain=await until(async()=>{const item=(await api('/custody/history')).find(x=>x.id===withdrawal.id);return item?.hash===hash&&item;});assert.equal(pendingAgain.hash,hash);
}finally{await rpc.send('evm_setIntervalMining',[1]);}
await until(async()=>(await api('/custody/history')).find(x=>x.id===withdrawal.id)?.status==='Completed');
const contract=new Contract(manifest.tokens.USDT.address,['function balanceOf(address) view returns(uint256)'],rpc);
assert.equal(await contract.balanceOf(target),parseUnits(amount,6));
const after=(await api('/balances')).find(w=>w.kind==='Funding'&&w.asset==='USDT');assert.equal(after.available,new Decimal(before.available).minus(amount).toFixed(8));assert.equal(after.locked,'0.00000000');
console.log('PASS pending broadcast survived custody/matching restart with the same transaction hash and exactly one payout.');
const checkpoint=await rpc.getBlockNumber();const generation=(await api('/network')).generation;
await compose('restart','chain');await until(async()=>Number(await rpc.send('eth_blockNumber',[]))>=checkpoint);
assert.equal((await api('/network')).generation,generation);assert.equal(await contract.balanceOf(target),parseUnits(amount,6));
await until(async()=>{const r=await fetch(base+'/api/health');return r.ok;});
const history=await api('/custody/history');assert.equal(history.filter(x=>x.id===withdrawal.id).length,1);
assert.equal((await api('/balances')).find(w=>w.kind==='Funding'&&w.asset==='USDT').available,after.available);
writeFileSync('artifacts/recovery-verification.json',JSON.stringify({time:new Date().toISOString(),transactionHash:hash,generation,checks:'Broadcast recovery, exact single payout and persisted chain verified'},null,2));
console.log('PASS persisted chain restart retained token state and balances without duplicate reconciliation.');rpc.destroy();
