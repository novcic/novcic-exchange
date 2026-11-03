import { readFileSync, writeFileSync, existsSync, mkdirSync } from 'node:fs';
import { randomBytes, randomUUID } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { JsonRpcProvider, Wallet, ContractFactory, Contract, parseUnits } from 'ethers';
import solc from 'solc';
const root=resolve(dirname(fileURLToPath(import.meta.url)),'..');
const state=resolve(root,'chain/state'); mkdirSync(state,{recursive:true});
const manifestPath=resolve(state,'deployment.json'), envPath=resolve(root,'.env.next');
// Anvil's published local-development key. Never used for an external network.
const treasury='0xac0974bec39a17e36ba4a6b4d238ff944bacb478cbed5efcae784d7bf4f2ff80';
const compose=(...args)=>execFileSync('docker',['compose','--env-file',envPath,'-f','compose.next.yaml','-p','novcic-next',...args],{cwd:root,stdio:'inherit'});
const mode=process.argv[2] ?? 'setup';
if(mode==='reset') {
  if(!process.argv.includes('--confirm-reset-next')) throw Error('Reset requires --confirm-reset-next; only the novcic-next project is reset.');
  compose('down','-v');
  if(existsSync(manifestPath)) writeFileSync(manifestPath+'.previous',readFileSync(manifestPath));
}
let env=existsSync(envPath)?readFileSync(envPath,'utf8'):'';
if(!env) {
  env=`CHAIN_GENERATION=${randomUUID()}\nCUSTODY_KEY=${randomBytes(32).toString('base64')}\nINTERNAL_KEY=${randomBytes(32).toString('hex')}\nJWT_KEY=${randomBytes(48).toString('hex')}\nTREASURY_KEY=${treasury}\n`;
  writeFileSync(envPath,env,{mode:0o600});
}
else if(mode==='reset'){
  // Keep encryption/signing credentials in their existing protected config;
  // a reset changes the chain generation, not the deployment's secret storage.
  env=env.replace(/^CHAIN_GENERATION=.+$/m,`CHAIN_GENERATION=${randomUUID()}`);
  writeFileSync(envPath,env,{mode:0o600});
}
const generation=env.match(/^CHAIN_GENERATION=(.+)$/m)[1];
// Anvil creates a genesis header before loading persisted blocks. Reuse its
// original timestamp so restarts cannot introduce competing empty genesis headers.
if(mode==='reset') env=env.replace(/^CHAIN_GENESIS_TIMESTAMP=.+\r?\n?/m,'');
if(!/^CHAIN_GENESIS_TIMESTAMP=/m.test(env)) {
  let timestamp;
  if(existsSync(manifestPath)&&mode!=='reset') {
    const previous=JSON.parse(readFileSync(manifestPath,'utf8'));
    timestamp=previous.genesisTimestamp;
    if(timestamp===undefined) {
      const response=await fetch('http://127.0.0.1:8545',{method:'POST',headers:{'Content-Type':'application/json'},
        body:JSON.stringify({jsonrpc:'2.0',id:1,method:'eth_getBlockByNumber',params:['0x0',false]}),signal:AbortSignal.timeout(5000)});
      const block=(await response.json()).result;
      if(block?.hash!==previous.genesisHash) throw Error('Existing genesis differs from the manifest. Restore and audit saved state before setup; do not reset.');
      timestamp=Number(BigInt(block.timestamp));
    }
  } else timestamp=Math.floor(Date.now()/1000);
  if(!Number.isSafeInteger(timestamp)||timestamp<0) throw Error('Invalid genesis timestamp.');
  env+=`CHAIN_GENESIS_TIMESTAMP=${timestamp}\n`;
  writeFileSync(envPath,env,{mode:0o600});
}
compose('up','-d','chain','db');
const provider=new JsonRpcProvider('http://127.0.0.1:8545',31337,{staticNetwork:true});
let ready=false;
for(let i=0;i<60;i++){try{await provider.getBlockNumber();ready=true;break;}catch{await new Promise(r=>setTimeout(r,500));}}
if(!ready) throw Error('Local chain did not start.');
if(Number((await provider.getNetwork()).chainId)!==31337) throw Error('Refusing to deploy to a non-local chain.');
let manifest=existsSync(manifestPath)&&mode!=='reset'?JSON.parse(readFileSync(manifestPath,'utf8')):null;
if(manifest){
  if(manifest.generation!==generation) throw Error('Chain generation mismatch. Use explicit reset.');
  if((await provider.getBlock(0)).hash!==manifest.genesisHash) throw Error('Chain identity mismatch.');
  for(const t of Object.values(manifest.tokens)) if(await provider.getCode(t.address)==='0x') throw Error('Existing token deployment disappeared; explicit reset required.');
  console.log('Verified existing chain and token contracts. No redeployment.');
} else {
  const input={language:'Solidity',sources:{'Token.sol':{content:readFileSync(resolve(root,'chain/Token.sol'),'utf8')}},settings:{evmVersion:'paris',optimizer:{enabled:true,runs:200},outputSelection:{'*':{'*':['abi','evm.bytecode.object']}}}};
  const output=JSON.parse(solc.compile(JSON.stringify(input)));
  if(output.errors?.some(e=>e.severity==='error')) throw Error(JSON.stringify(output.errors));
  const artifact=output.contracts['Token.sol'].Token;
  const signer=new Wallet(treasury,provider); const tokens={};
  for(const [symbol,decimals] of [['BTC',8],['USDT',6]]){
    const token=await new ContractFactory(artifact.abi,artifact.evm.bytecode.object,signer).deploy(symbol+' ERC20',symbol,decimals,parseUnits('1000000000000',decimals));
    await token.waitForDeployment(); const receipt=await token.deploymentTransaction().wait();
    tokens[symbol]={address:await token.getAddress(),decimals,deploymentBlock:receipt.blockNumber};
  }
  const genesis=await provider.getBlock(0);
  manifest={generation,chainId:'31337',network:'Ethereum Local',confirmations:'3',genesisHash:genesis.hash,genesisTimestamp:genesis.timestamp,tokens};
  writeFileSync(manifestPath,JSON.stringify(manifest,null,2));
  console.log('Deployed local BTC-ERC20 and USDT-ERC20 contracts.');
}
for(const [asset,t] of Object.entries(manifest.tokens)) env=env.replace(new RegExp(`^${asset}_CONTRACT=.*\\n?`,'m'),'')+`${asset}_CONTRACT=${t.address}\n`;
writeFileSync(envPath,env,{mode:0o600});
compose('exec','-T','chain','sh','-c','test -w /state && test -s /state/anvil.json');
if(mode==='fund') {
  const [address,asset,amount]=process.argv.slice(3); if(!manifest.tokens[asset]) throw Error('Usage: node chain/setup.mjs fund ADDRESS BTC|USDT AMOUNT');
  compose('exec','-T','custody','dotnet','Novcic.Custody.dll','--local-fund',address,asset,amount,process.argv[6]||randomUUID());
} else { compose('up','-d','--build'); console.log('Novčić is available at http://localhost:8081'); }
provider.destroy();
