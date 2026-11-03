import {createHmac,randomUUID} from 'node:crypto';
const key=process.env.NOVCIC_API_KEY,secret=process.env.NOVCIC_API_SECRET;
if(!key||!secret)throw Error('Set NOVCIC_API_KEY and NOVCIC_API_SECRET.');
const path='/api/balances',timestamp=Date.now().toString(),nonce=randomUUID();
const signature=createHmac('sha256',secret).update([timestamp,nonce,'GET',path,'',''].join('\n')).digest('hex');
const result=await fetch((process.env.NOVCIC_URL||'http://127.0.0.1:8081')+path,{headers:{'X-Api-Key':key,'X-Api-Timestamp':timestamp,'X-Api-Nonce':nonce,'X-Api-Signature':signature}});
if(!result.ok)throw Error('Signed request rejected: '+result.status);
console.log(JSON.stringify(await result.json(),null,2));
