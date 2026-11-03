import { defineConfig } from '@playwright/test';
export default defineConfig({ testDir: './e2e',testMatch:process.env.LEGACY_STACK?'exchange.spec.ts':'expanded.spec.ts',globalSetup:process.env.LEGACY_STACK?undefined:'./e2e/setup.ts', timeout: 90000, expect: { timeout: 15000 }, fullyParallel: false, workers: 1,
  reporter: [['list'], ['html', { open: 'never' }]], use: { baseURL: process.env.BASE_URL ?? 'http://127.0.0.1:8081', browserName: 'chromium', channel: 'msedge', trace: 'retain-on-failure', screenshot: 'only-on-failure' } });
