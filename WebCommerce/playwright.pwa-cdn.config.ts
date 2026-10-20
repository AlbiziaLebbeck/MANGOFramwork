import { defineConfig } from '@playwright/test';
import { resolve } from 'node:path';
import { tmpdir } from 'node:os';
const evidence = process.env.TTB_UI_EVIDENCE ?? resolve(tmpdir(), 'mangos-ttb-cdn-pwa');
export default defineConfig({
  testDir: './e2e-pwa', testMatch: 'cdn-update.spec.ts', workers: 1, retries: 0, timeout: 60000,
  outputDir: resolve(evidence, 'test-results'), reporter: [['list'], ['json', { outputFile: resolve(evidence, 'results.json') }]],
  webServer: { command: 'node ../Tools/TTB/tests/pwa-server.mjs', url: 'http://127.0.0.1:5197', reuseExistingServer: false,
    env: { TTB_PWA_QA_PORT: '5197', TTB_PWA_QA_EDGE: '1' } },
  use: { baseURL: 'http://127.0.0.1:5197', viewport: { width: 1360, height: 900 }, trace: 'retain-on-failure' },
});
