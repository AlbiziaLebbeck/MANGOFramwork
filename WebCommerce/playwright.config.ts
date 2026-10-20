import { defineConfig } from '@playwright/test';
export default defineConfig({
  globalSetup: './e2e/setup.ts',
  outputDir: './test-results/shopper',
  testDir: './e2e',
  timeout: 120000,
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list'], ['html', { open: 'never' }], ['json', { outputFile: '../.artifacts/acceptance/browser-results.json' }]],
  use: {
    baseURL: 'http://127.0.0.1:5176',
    viewport: { width: 1536, height: 1024 },
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
});
