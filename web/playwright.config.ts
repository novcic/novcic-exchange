import { defineConfig } from '@playwright/test';
export default defineConfig({ testDir: './e2e', timeout: 90000, expect: { timeout: 15000 }, fullyParallel: false, workers: 1,
  reporter: [['list'], ['html', { open: 'never' }]], use: { baseURL: process.env.BASE_URL ?? 'http://localhost:8080', browserName: 'chromium', channel: 'msedge', trace: 'retain-on-failure', screenshot: 'only-on-failure' } });
