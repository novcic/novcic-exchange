import {execFileSync} from 'node:child_process';
import {resolve} from 'node:path';
export default function setup(){execFileSync('node',['chain/platform-verify.mjs'],{cwd:resolve('..'),stdio:'inherit',env:{...process.env,EXCHANGE_URL:process.env.BASE_URL||'http://127.0.0.1:8081'}});}
