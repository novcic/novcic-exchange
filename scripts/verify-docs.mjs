// Read-only contract smoke test plus a temporary Read API key on a dedicated local test account.
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
const spec=JSON.parse(await readFile(new URL('../../docs/api/openapi.json',import.meta.url),'utf8'));
const base='http://127.0.0.1:8081';
function validate(schema,value,path){
  if(schema.$ref)return validate(spec.components.schemas[schema.$ref.split('/').pop()],value,path);
  if(value===null){assert(schema.nullable,`${path} unexpectedly null`);return;}
  if(schema.allOf){for(const item of schema.allOf)validate(item,value,path);return;}
  if(schema.type==='array'){assert(Array.isArray(value),path);value.forEach((v,i)=>validate(schema.items,v,`${path}[${i}]`));return;}
  assert.equal(typeof value,schema.type,path);
  if(schema.type==='object'){
    for(const key of schema.required||[])assert(key in value,`${path}.${key} missing`);
    for(const [key,v] of Object.entries(value)){assert(schema.properties?.[key],`${path}.${key} undocumented`);validate(schema.properties[key],v,path+'.'+key);}
  }
  if(schema.pattern)assert(new RegExp(schema.pattern).test(value),path);
}
let token;
async function api(path,body){const response=await fetch(base+path,{method:body===undefined?'GET':'POST',headers:{'Content-Type':'application/json',...(token?{Authorization:'Bearer '+token}:{})},body:body===undefined?undefined:JSON.stringify(body)});assert(response.ok,`${path}: ${response.status}`);return response.json();}
for(const path of ['/api/health','/api/config','/api/network','/api/products','/api/market','/api/candles'])validate(spec.paths[path].get.responses['200'].content['application/json'].schema,await api(path),path);
const actors=JSON.parse(await readFile(new URL('../artifacts/platform-actors.json',import.meta.url),'utf8'));
token=(await api('/api/auth/login',actors[0])).accessToken;
for(const path of ['/api/auth/me','/api/balances','/api/orders','/api/trades','/api/ledger','/api/portfolio','/api/pnl-history','/api/custody/history','/api/keys'])validate(spec.paths[path].get.responses['200'].content['application/json'].schema,await api(path),path);
const key=await api('/api/keys',{name:'Documentation example verification',scope:'Read'});
try{
  const env={...process.env,NOVCIC_URL:base,NOVCIC_KEY:key.id,NOVCIC_SECRET:key.secret};
  const exec=promisify(execFile);
  const js=await exec(process.execPath,['../docs/examples/signed-request.mjs','GET','/api/balances'],{env,timeout:20000});
  const py=await exec('python',['../docs/examples/signed_request.py','GET','/api/balances'],{env,timeout:20000});
  assert.deepEqual(JSON.parse(js.stdout),JSON.parse(py.stdout));
}finally{await api('/api/keys/'+key.id+'/revoke',{});}
console.log('PASS: 15 live response schemas and both documentation signing clients; temporary key revoked.');
