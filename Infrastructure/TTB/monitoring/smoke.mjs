import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
const repository = resolve(fileURLToPath(new URL('../../../', import.meta.url)));
const require = createRequire(resolve(repository, 'WebCommerce/package.json'));
const { chromium } = require('@playwright/test');
const base = 'http://127.0.0.1:5294'; // Existing owned SSH-forward only; never send credentials or queries to a remote/public UI.
const evidence = resolve(repository, '.artifacts/monitoring-qa');
await mkdir(evidence, { recursive: true });
async function get(path) {
  const response = await fetch(base + path); assert.equal(response.status, 200);
  const result = await response.json(); assert.equal(result.status, 'success'); return result.data;
}
let targets = await get('/api/v1/targets');
// A Ready collector may still be awaiting its first scheduled scrape. Do not restart it or mask a known-down target.
const firstScrapeDeadline = Date.now() + 75000;
while (targets.activeTargets.some(target => target.health === 'unknown' && Date.parse(target.lastScrape) <= 0) && Date.now() < firstScrapeDeadline) {
  await new Promise(resolve => setTimeout(resolve, 1000));
  targets = await get('/api/v1/targets');
}
assert.equal(targets.activeTargets.length, 3);
assert(targets.activeTargets.every(target => target.health === 'up' && target.lastError === ''));
assert.deepEqual(targets.activeTargets.map(target => target.labels.job).sort(), ['ttb-api', 'ttb-backups', 'ttb-callback-tls']);
const apiTarget = targets.activeTargets.find(target => target.labels.job === 'ttb-api');
const rules = await get('/api/v1/rules?type=alert'); const allRules = rules.groups.flatMap(group => group.rules);
assert.equal(allRules.length, 32); assert(allRules.every(rule => rule.health === 'ok'));
const receivers = await get('/api/v1/alertmanagers'); assert.equal(receivers.activeAlertmanagers.length, 0);
const checks = {
  up: 1, ttb_database_up: 1, ttb_recovery_frozen: 0, ttb_bank_profile_ready: 0,
  ttb_reports_enabled: 0, ttb_inventory_invariant_failures: 0, ttb_build_reader_contract: 3,
  ttb_private_storage_capacity_probe_success: 1,
  ttb_bank_signing_key_expected: 0, ttb_bank_signing_key_probe_success: 0, ttb_bank_signing_key_rotation_scheduled: 0,
};
const values = await Promise.all(Object.entries(checks).map(async ([metric, expected]) => {
  const result = await get('/api/v1/query?query=' + encodeURIComponent(metric + '{job="ttb-api"}'));
  assert.equal(result.result.length, 1); const value = Number(result.result[0].value[1]); assert.equal(value, expected, metric); return [metric, value];
}));
const unconfiguredRotation = await get('/api/v1/query?query=' + encodeURIComponent('ttb_bank_signing_key_rotation_due_timestamp_seconds{job="ttb-api"}'));
assert.equal(unconfiguredRotation.result.length, 0, 'Do not invent a key expiry/rotation date before provisioning.');
const capacity = {};
for (const metric of ['ttb_private_storage_filesystem_size_bytes', 'ttb_private_storage_filesystem_available_bytes']) {
  const result = await get('/api/v1/query?query=' + encodeURIComponent(metric + '{job="ttb-api"}'));
  assert.equal(result.result.length, 1); capacity[metric] = Number(result.result[0].value[1]);
}
assert(capacity.ttb_private_storage_filesystem_size_bytes > 0);
assert(capacity.ttb_private_storage_filesystem_available_bytes >= 0);
assert(capacity.ttb_private_storage_filesystem_available_bytes <= capacity.ttb_private_storage_filesystem_size_bytes);
const circuits = await get('/api/v1/query?query=' + encodeURIComponent('ttb_bank_circuit_open{job="ttb-api"}'));
assert.equal(circuits.result.length, 4);
assert.deepEqual(circuits.result.map(row => row.metric.service).sort(), ['enquiry-qr-range', 'enquiry-void', 'merchant-void', 'verify-qr']);
assert(circuits.result.every(row => Number(row.value[1]) === 0));
const backups = {};
for (const metric of ['ttb_backup_snapshot_probe_success', 'ttb_backup_scheduling_expected', 'ttb_backup_last_success_timestamp_seconds', 'ttb_backup_completed_archives', 'ttb_backup_pool_used_bytes', 'ttb_backup_pool_budget_bytes', 'ttb_backup_filesystem_size_bytes', 'ttb_backup_filesystem_available_bytes', 'ttb_backup_recipient_probe_success', 'ttb_backup_recipient_not_after_timestamp_seconds']) {
  const result = await get('/api/v1/query?query=' + encodeURIComponent(metric + '{job="ttb-backups"}'));
  assert.equal(result.result.length, 1, metric); backups[metric] = Number(result.result[0].value[1]);
}
assert.equal(backups.ttb_backup_snapshot_probe_success, 1);
assert.equal(backups.ttb_backup_scheduling_expected, 1);
assert.equal(backups.ttb_backup_pool_budget_bytes, 20 * 1024 ** 3);
assert(backups.ttb_backup_completed_archives >= 1);
assert(backups.ttb_backup_pool_used_bytes > 0 && backups.ttb_backup_pool_used_bytes < backups.ttb_backup_pool_budget_bytes);
const backupAge = Date.now() / 1000 - backups.ttb_backup_last_success_timestamp_seconds;
assert(backupAge >= -60 && backupAge < 1800);
assert(backups.ttb_backup_filesystem_size_bytes > 0 && backups.ttb_backup_filesystem_available_bytes >= 0 && backups.ttb_backup_filesystem_available_bytes <= backups.ttb_backup_filesystem_size_bytes);
assert.equal(backups.ttb_backup_recipient_probe_success, 1);
assert(backups.ttb_backup_recipient_not_after_timestamp_seconds > Date.now() / 1000 + 2592000);
const tls = {};
for (const metric of ['probe_success', 'ttb_callback_certificate_probe_success', 'ttb_callback_public_serving_expected', 'ttb_callback_certificate_not_after_timestamp_seconds', 'ttb_callback_tls_leaf_not_after_timestamp_seconds']) {
  const result = await get('/api/v1/query?query=' + encodeURIComponent(metric + '{job="ttb-callback-tls"}'));
  assert.equal(result.result.length, 1, metric); tls[metric] = Number(result.result[0].value[1]);
}
assert.equal(tls.probe_success, 1);
assert.equal(tls.ttb_callback_certificate_probe_success, 1);
assert.equal(tls.ttb_callback_public_serving_expected, 0); // Health-only preflight never constitutes accepted bank callback serving.
assert(tls.ttb_callback_certificate_not_after_timestamp_seconds > Date.now() / 1000 + 1209600);
assert.equal(tls.ttb_callback_tls_leaf_not_after_timestamp_seconds, tls.ttb_callback_certificate_not_after_timestamp_seconds);
const browser = await chromium.launch({ headless: true });
try {
  const page = await browser.newPage({ viewport: { width: 1536, height: 1024 } });
  page.setDefaultTimeout(15000);
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  await page.goto(base + '/targets'); await page.getByRole('table').getByText(/^up$/i).first().waitFor();
  await page.screenshot({ path: resolve(evidence, 'targets-private-uat.png'), fullPage: true });
  await page.goto(base + '/alerts'); await page.getByText('TtbUnknownVoid', { exact: true }).waitFor();
  await page.screenshot({ path: resolve(evidence, 'alerts-private-uat.png'), fullPage: true });
  assert.deepEqual(errors, []);
} finally { await browser.close(); }
const proof = {
  verifiedAt: new Date().toISOString(), target: apiTarget.scrapeUrl, targetHealth: 'up', targets: targets.activeTargets.map(target => ({ job: target.labels.job, health: target.health })),
  rules: allRules.length, ruleHealth: 'ok', externalAlertReceivers: 0,
  bankKeyRotationDeadlineEmitted: unconfiguredRotation.result.length > 0,
  values: Object.fromEntries(values), privateStorageFilesystem: capacity, outgoingServiceCircuits: Object.fromEntries(circuits.result.map(row => [row.metric.service, Number(row.value[1])])), backupPool: backups, tls,
  // This probe verifies the collector only. Restore evidence is recorded separately by restore-drill.ps1.
};
await writeFile(resolve(evidence, 'live-monitoring.json'), JSON.stringify(proof, null, 2));
console.log(JSON.stringify(proof));
