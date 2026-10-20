import { defineConfig } from '@playwright/test';
import { resolve } from 'node:path';
import { tmpdir } from 'node:os';
const evidence =
  process.env.TTB_UI_EVIDENCE ??
  resolve(tmpdir(), 'mangos-ttb-session-i18n');
export default defineConfig({
  testDir: './e2e',
  testMatch: ['session-i18n.spec.ts', 'uat-bank-notice.spec.ts', 'header-layout.spec.ts'],
  workers: 1,
  retries: 0,
  timeout: 90000,
  outputDir: resolve(evidence, 'test-results'),
  reporter: [['list'], ['json', { outputFile: resolve(evidence, 'runner-results.json') }]],
  use: {
    actionTimeout: 15000,
    baseURL: 'http://127.0.0.1:5176',
    viewport: { width: 1360, height: 900 },
    trace: 'retain-on-failure',
  },
});
