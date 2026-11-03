import {defineConfig} from '@playwright/test';
export default defineConfig({testDir:'./e2e',testMatch:'market-sizing.spec.ts',workers:1,timeout:30000,use:{baseURL:'http://127.0.0.1:8081',channel:'msedge',screenshot:'only-on-failure'}});
