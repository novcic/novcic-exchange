import {defineConfig} from '@playwright/test';
export default defineConfig({testDir:'./e2e',testMatch:'positions.spec.ts',workers:1,timeout:30000,use:{baseURL:process.env.BASE_URL||'http://127.0.0.1:5173',channel:'msedge',viewport:{width:1440,height:900},screenshot:'only-on-failure'}});
