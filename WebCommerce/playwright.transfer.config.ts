import { defineConfig } from '@playwright/test';
import { resolve } from 'node:path';
import { tmpdir } from 'node:os';
const evidence = process.env.TTB_UI_EVIDENCE ?? resolve(tmpdir(), 'mangos-ttb-native-transfer');
export default defineConfig({
  testDir: './e2e-load', testMatch: 'transfer.spec.ts', workers: 1, retries: 0, timeout: 150000,
  outputDir: resolve(evidence, 'test-results'), reporter: [['list'], ['json', { outputFile: resolve(evidence, 'results.json') }]],
  webServer: { command: 'node ../Tools/TTB/tests/webgl-server.mjs', url: 'http://127.0.0.1:5198', timeout: 120000, reuseExistingServer: false },
  use: { baseURL: 'http://127.0.0.1:5198', viewport: { width: 1360, height: 900 }, trace: 'retain-on-failure' },
});
