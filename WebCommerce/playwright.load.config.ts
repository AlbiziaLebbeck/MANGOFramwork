import { defineConfig } from '@playwright/test';
export default defineConfig({ testDir: './e2e-load', outputDir: './test-results/load', timeout: 180000, workers: 1, retries: 0,
  reporter: [['list'], ['json', { outputFile: '../.artifacts/acceptance/load-results.json' }]],
  use: { baseURL: 'http://127.0.0.1:5186', viewport: { width: 1280, height: 720 }, trace: 'retain-on-failure', screenshot: 'only-on-failure' } });
