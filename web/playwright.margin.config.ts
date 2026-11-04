import {defineConfig} from '@playwright/test';
export default defineConfig({testDir:'./e2e',testMatch:'margin-balances.spec.ts',workers:1,timeout:30000,
  use:{baseURL:process.env.BASE_URL||'http://127.0.0.1:5175',channel:'msedge',viewport:{width:1440,height:900},screenshot:'only-on-failure'},
  webServer:process.env.BASE_URL?undefined:{command:'npm run dev -- --port 5175 --strictPort',url:'http://127.0.0.1:5175',reuseExistingServer:true}});
